using System;
using Fusion;
using Starter.Common.Interactions;
using UnityEngine;

namespace Starter.Common.Inventory
{
	/// <summary>
	/// Networked 6-slot lootable container (chest, bookcase, ...).
	/// Single-player exclusive access enforced via <see cref="CurrentUser"/>.
	/// State authority owns all slot mutations; clients drive interactions via RPCs.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class LootContainer : NetworkBehaviour, IPlayerLeft, IInteractable
	{
		public const int Capacity = 6;
		public const byte InvPlayer = 0;
		public const byte InvContainer = 1;

		[Header("Authoring")]
		public string DisplayName = "Container";
		public float InteractRange = 2.5f;

		[Tooltip("Initial inventory populated by state authority on Spawned().")]
		[SerializeField] private InitialSlot[] _initialContents;

		[Networked, Capacity(Capacity), OnChangedRender(nameof(OnSlotsChangedRender))]
		public NetworkArray<InventorySlot> Slots => default;

		[Networked, OnChangedRender(nameof(OnUserChangedRender))]
		public PlayerRef CurrentUser { get; set; }

		[Networked] private TickTimer LockHeartbeat { get; set; }

		public event Action SlotsChanged;
		public event Action<LootContainer, PlayerRef, PlayerRef> UserChanged;

		private PlayerRef _lastUser;

		[Serializable]
		public struct InitialSlot
		{
			public ItemDefinition Item;
			[Min(1)] public short Count;
		}

		public override void Spawned()
		{
			_lastUser = CurrentUser;

			if (Object.HasStateAuthority)
			{
				if (_initialContents != null)
				{
					for (int i = 0; i < _initialContents.Length; i++)
					{
						var seed = _initialContents[i];
						if (seed.Item == null || seed.Count <= 0) continue;
						InventoryOps.TryAdd(Slots, seed.Item.Id, seed.Count);
					}
				}

				LockHeartbeat = TickTimer.CreateFromSeconds(Runner, 1f);
			}
		}

		public override void FixedUpdateNetwork()
		{
			if (Object.HasStateAuthority == false) return;
			if (LockHeartbeat.ExpiredOrNotRunning(Runner) == false) return;

			LockHeartbeat = TickTimer.CreateFromSeconds(Runner, 1f);

			if (CurrentUser == PlayerRef.None) return;

			if (TryGetOpenerTransform(CurrentUser, out var t) == false)
			{
				CurrentUser = PlayerRef.None;
				return;
			}

			float distSq = (t.position - transform.position).sqrMagnitude;
			float allowed = InteractRange * 1.5f;
			if (distSq > allowed * allowed)
			{
				CurrentUser = PlayerRef.None;
			}
		}

		public void PlayerLeft(PlayerRef player)
		{
			if (Object.HasStateAuthority == false) return;
			if (CurrentUser == player)
			{
				CurrentUser = PlayerRef.None;
			}
		}

		// --- IInteractable ---

		float IInteractable.InteractRange => InteractRange;
		bool IInteractable.CanInteract => CurrentUser == PlayerRef.None;
		Vector3 IInteractable.InteractionPoint => transform.position;
		string IInteractable.LockedReason => $"{DisplayName} is in use";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			var session = scanner.GetComponent<LootSession>();
			if (session != null) session.TryOpen(this);
		}

		/// <summary>
		/// Fusion sets <c>info.Source = PlayerRef.None</c> when an RPC is invoked locally on
		/// the state authority (host calling its own SA). In that case the actual caller is
		/// the local player. For true remote calls Fusion populates <c>info.Source</c>
		/// from the server-verified connection.
		/// </summary>
		private PlayerRef ResolveSource(RpcInfo info)
		{
			return info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestOpen(RpcInfo info = default)
		{
			var source = ResolveSource(info);
			Debug.Log($"[LootContainer:{DisplayName}] RPC_RequestOpen from {source} (CurrentUser={CurrentUser})");

			if (CurrentUser != PlayerRef.None) { Debug.Log("  rejected: already in use"); return; }
			if (TryGetOpenerTransform(source, out var t) == false) { Debug.Log($"  rejected: no transform for {source}"); return; }

			float distSq = (t.position - transform.position).sqrMagnitude;
			float allowed = InteractRange * 1.25f;
			if (distSq > allowed * allowed) { Debug.Log($"  rejected: out of range dsq={distSq:F2} allowedSq={allowed * allowed:F2}"); return; }

			CurrentUser = source;
			LockHeartbeat = TickTimer.CreateFromSeconds(Runner, 1f);
			Debug.Log($"  accepted: CurrentUser = {source}");
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestClose(RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			CurrentUser = PlayerRef.None;
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestMove(byte fromInv, byte fromSlot, byte toInv, byte toSlot, RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			if (TryResolvePlayerInventory(source, out var playerInv) == false) return;

			if (fromInv == toInv && fromSlot == toSlot) return;

			var playerSlots = playerInv.Slots;
			var from = (fromInv == InvContainer) ? Slots : playerSlots;

			// Large-item rules: any swap-within-player drops the Large item; container→player must auto-equip.
			if (IsLargeAt(from, fromSlot))
			{
				if (fromInv == InvPlayer && toInv == InvPlayer)
				{
					playerInv.AuthorityDropSlot(fromSlot);
					return;
				}
				if (fromInv == InvContainer && toInv == InvPlayer)
				{
					AcquireLargeFromContainer(playerInv, fromSlot);
					return;
				}
			}

			var to = (toInv == InvContainer) ? Slots : playerSlots;
			InventoryOps.TryMove(from, fromSlot, to, toSlot);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestSwapToOther(byte fromInv, byte fromSlot, RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			if (TryResolvePlayerInventory(source, out var playerInv) == false) return;

			var playerSlots = playerInv.Slots;
			var from = (fromInv == InvContainer) ? Slots : playerSlots;

			// Container → player for a Large item: auto-equip instead of stacking-or-placing.
			if (fromInv == InvContainer && IsLargeAt(from, fromSlot))
			{
				AcquireLargeFromContainer(playerInv, fromSlot);
				return;
			}

			var to = (fromInv == InvContainer) ? playerSlots : Slots;
			InventoryOps.TryAutoMergeOrPlace(from, fromSlot, to);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestTakeAll(RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			if (TryResolvePlayerInventory(source, out var playerInv) == false) return;

			var playerSlots = playerInv.Slots;
			bool acquiredLarge = false;
			for (int i = 0; i < Slots.Length; i++)
			{
				if (IsLargeAt(Slots, i))
				{
					// Only one Large can be carried at a time — leave the rest in the container.
					if (acquiredLarge) continue;
					if (AcquireLargeFromContainer(playerInv, i)) acquiredLarge = true;
					continue;
				}
				InventoryOps.TryAutoMergeOrPlace(Slots, i, playerSlots);
			}
		}

		private static bool IsLargeAt(NetworkArray<InventorySlot> arr, int idx)
		{
			if (idx < 0 || idx >= arr.Length) return false;
			var s = arr[idx];
			if (s.IsEmpty || ItemDatabase.Instance == null) return false;
			var def = ItemDatabase.Instance.GetById(s.ItemId);
			return def != null && def.Size == EItemSize.Large;
		}

		private bool AcquireLargeFromContainer(IPlayerInventory playerInv, int containerSlot)
		{
			if (containerSlot < 0 || containerSlot >= Slots.Length) return false;
			var src = Slots[containerSlot];
			if (src.IsEmpty) return false;

			if (playerInv.AuthorityAcquireLarge(src.ItemId) == false) return false;

			// Consume exactly one large from the container slot.
			src.Count -= 1;
			Slots.Set(containerSlot, src.Count <= 0 ? InventorySlot.Empty : src);
			return true;
		}

		private bool TryGetOpenerTransform(PlayerRef player, out Transform t)
		{
			t = null;
			if (Runner == null) return false;

			var obj = Runner.GetPlayerObject(player);
			if (obj == null) return false;

			t = obj.transform;
			return true;
		}

		private bool TryResolvePlayerInventory(PlayerRef player, out IPlayerInventory inventory)
		{
			inventory = null;
			if (Runner == null) return false;

			var obj = Runner.GetPlayerObject(player);
			if (obj == null) return false;

			inventory = obj.GetComponentInChildren<IPlayerInventory>();
			return inventory != null;
		}

		private void OnSlotsChangedRender()
		{
			SlotsChanged?.Invoke();
		}

		private void OnUserChangedRender()
		{
			var prev = _lastUser;
			_lastUser = CurrentUser;
			Debug.Log($"[LootContainer:{DisplayName}] OnUserChangedRender prev={prev} cur={CurrentUser}");
			UserChanged?.Invoke(this, prev, CurrentUser);
		}
	}

	/// <summary>
	/// Implemented by per-mode networked inventories (e.g. Starter.Shooter.Inventory).
	/// Lets <see cref="LootContainer"/> resolve the opener's player-side slot array
	/// without taking a hard dependency on the Shooter assembly.
	/// </summary>
	public interface IPlayerInventory
	{
		NetworkArray<InventorySlot> Slots { get; }
		event Action SlotsChanged;

		/// <summary>Local-side request to drop the contents of <paramref name="slot"/> into the world.</summary>
		void RequestDropSlot(int slot);

		/// <summary>State-authority-only: spawn the slot's contents as a world pickup at the player's hand pose.</summary>
		void AuthorityDropSlot(int slot);

		/// <summary>
		/// State-authority-only: place one Large item into the inventory, ending in the selected slot.
		/// Returns false if the item is not registered. Used by LootContainer to enforce auto-equip.
		/// </summary>
		bool AuthorityAcquireLarge(short itemId);
	}
}
