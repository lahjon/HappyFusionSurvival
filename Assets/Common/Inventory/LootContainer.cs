using System;
using System.Collections.Generic;
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
	public sealed class LootContainer : NetworkBehaviour, IPlayerLeft, IInteractable, IPickupableStation
	{
		public const int Capacity = 6;
		public const byte InvPlayer = 0;
		public const byte InvContainer = 1;

		[Header("Authoring")]
		public string DisplayName = "Container";
		public float InteractRange = 2.5f;

		[Header("Pickup")]
		[Tooltip("If true, the player can hold Interact to pick up this container (only when empty and not in use).")]
		public bool IsPickupable = false;
		[Tooltip("Item granted to the player when pickup completes. Must carry a PlaceableCapability. Required when IsPickupable is on.")]
		public ItemData PickupItem;
		[Tooltip("Seconds the player must hold Interact to pick this up.")]
		[Min(0.1f)] public float PickupHoldSeconds = 2f;

		[Tooltip("Initial inventory populated by state authority on Spawned().")]
		[SerializeField] private InitialSlot[] _initialContents;

		[Header("Procedural Loot")]
		[Tooltip("Optional pool of possible items. If ItemCount > 0, slots are filled from this list at spawn.")]
		[SerializeField] private List<LootEntry> _lootTable;
		[Tooltip("ON: pick ItemCount weighted-random entries. OFF: take the first ItemCount entries (wrapping if shorter).")]
		[SerializeField] private bool _randomize = true;
		[Tooltip("How many slots to seed from the loot table. 0 disables procedural seeding (use _initialContents instead).")]
		[SerializeField, Min(0)] private int _itemCount;

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
			public ItemData Item;
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

				SeedProceduralLoot();

				LockHeartbeat = TickTimer.CreateFromSeconds(Runner, 1f);
			}
		}

		private void SeedProceduralLoot()
		{
			if (_lootTable == null || _lootTable.Count == 0 || _itemCount <= 0) return;

			var rng = WorldGen.RngFor(transform.position);
			int n = Mathf.Min(_itemCount, Slots.Length);
			for (int i = 0; i < n; i++)
			{
				LootEntry entry;
				if (_randomize)
				{
					if (!LootRoll.TryPickWeighted(_lootTable, rng, out entry)) continue;
				}
				else
				{
					entry = _lootTable[i % _lootTable.Count];
				}
				if (entry.Item == null) continue;
				short count = entry.Count > 0 ? entry.Count : (short)1;
				InventoryOps.TryAdd(Slots, entry.Item.Id, count);
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
		string IInteractable.InteractLabel => $"Open {DisplayName}";

		void IInteractable.OnInteract(InteractionScanner scanner)
		{
			var session = scanner.GetComponent<LootSession>();
			if (session != null) session.TryOpen(this);
		}

		// --- IPickupableStation ---

		bool IPickupableStation.IsPickupable => IsPickupable && PickupItem != null;
		ItemData IPickupableStation.AsItem => PickupItem;
		float IPickupableStation.PickupHoldSeconds => PickupHoldSeconds;

		string IPickupableStation.PickupBlockedReason
		{
			get
			{
				if (CurrentUser != PlayerRef.None) return $"{DisplayName} is in use";
				for (int i = 0; i < Slots.Length; i++)
				{
					if (!Slots[i].IsEmpty) return "Empty first";
				}
				return string.Empty;
			}
		}

		void IPickupableStation.LocalRequestPickup() => RPC_RequestPickup();

		[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
		private void RPC_RequestPickup(RpcInfo info = default)
		{
			if (Runner == null) return;
			if (!IsPickupable || PickupItem == null) return;
			if (CurrentUser != PlayerRef.None) return;

			for (int i = 0; i < Slots.Length; i++)
			{
				if (!Slots[i].IsEmpty) return;
			}

			var source = ResolveSource(info);
			if (!TryGetOpenerTransform(source, out var t)) return;

			float distSq = (t.position - transform.position).sqrMagnitude;
			float allowed = InteractRange * 1.25f;
			if (distSq > allowed * allowed) return;

			if (!TryResolvePlayerInventory(source, out var inv)) return;

			if (!inv.AuthorityStartCarry(PickupItem.Id)) return;

			Runner.Despawn(Object);
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
		/// State-authority-only: put a placeable into the player's hands (carry channel — not a
		/// hotbar slot). Drops any currently-carried placeable loose first, auto-selects an empty
		/// hotbar slot so the previously-held visual goes away. Returns false if the id's item
		/// doesn't carry a <see cref="PlaceableCapability"/>.
		/// </summary>
		bool AuthorityStartCarry(short itemId);
	}
}
