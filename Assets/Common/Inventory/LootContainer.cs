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
			if (TryResolvePlayerSlots(source, out var playerSlots) == false) return;

			if (fromInv == toInv && fromSlot == toSlot) return;

			var from = (fromInv == InvContainer) ? Slots : playerSlots;
			var to   = (toInv   == InvContainer) ? Slots : playerSlots;
			InventoryOps.TryMove(from, fromSlot, to, toSlot);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestSwapToOther(byte fromInv, byte fromSlot, RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			if (TryResolvePlayerSlots(source, out var playerSlots) == false) return;

			var from = (fromInv == InvContainer) ? Slots : playerSlots;
			var to   = (fromInv == InvContainer) ? playerSlots : Slots;
			InventoryOps.TryAutoMergeOrPlace(from, fromSlot, to);
		}

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		public void RPC_RequestTakeAll(RpcInfo info = default)
		{
			var source = ResolveSource(info);
			if (CurrentUser != source) return;
			if (TryResolvePlayerSlots(source, out var playerSlots) == false) return;

			for (int i = 0; i < Slots.Length; i++)
			{
				InventoryOps.TryAutoMergeOrPlace(Slots, i, playerSlots);
			}
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

		private bool TryResolvePlayerSlots(PlayerRef player, out NetworkArray<InventorySlot> slots)
		{
			slots = default;
			if (Runner == null) return false;

			var obj = Runner.GetPlayerObject(player);
			if (obj == null) return false;

			var inventory = obj.GetComponentInChildren<IPlayerInventory>();
			if (inventory == null) return false;

			slots = inventory.Slots;
			return true;
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
	}
}
