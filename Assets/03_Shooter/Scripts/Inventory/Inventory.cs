using Fusion;
using Starter.Common.Input;
using Starter.Common.Inventory;
using UnityEngine;

namespace Starter.Shooter
{
	/// <summary>
	/// Networked 8-slot hotbar inventory. Sibling of Player on the same NetworkObject.
	/// State authority owns all slot mutations; clients request actions via RPC.
	/// </summary>
	[RequireComponent(typeof(NetworkObject))]
	public sealed class Inventory : NetworkBehaviour, IPlayerInventory, IInteractionTarget
	{
		public const int SlotCount = 8;

		[Header("Tuning")]
		public float PickupRange = 2f;
		public float DropForwardOffset = 1.5f;
		public float DropUpOffset = 0.5f;

		[Header("References")]
		[Tooltip("Local-only anchor where the held HandPrefab is parented. Typically under the camera handle.")]
		public Transform HandAnchor;

		[Networked, Capacity(SlotCount), OnChangedRender(nameof(OnSlotsChanged))]
		public NetworkArray<InventorySlot> Slots => default;

		[Networked, OnChangedRender(nameof(OnSelectedChanged))]
		public int SelectedSlot { get; set; }

		public event System.Action SlotsChanged;
		public event System.Action SelectedChanged;

		private GameObject _heldInstance;
		private short _heldItemId;
		private GameInputActions _actions;

		public override void Spawned()
		{
			if (HasInputAuthority)
			{
				_actions = GetComponent<GameInputActions>();
			}

			RefreshHeldItem();
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			if (_heldInstance != null)
			{
				Destroy(_heldInstance);
				_heldInstance = null;
			}
		}

		private void Update()
		{
			if (HasInputAuthority == false)
				return;
			if (Cursor.lockState != CursorLockMode.Locked)
				return;
			if (_actions == null || _actions.IsInitialized == false)
				return;

			var slots = _actions.HotbarSlots;
			for (int i = 0; i < SlotCount && i < slots.Length; i++)
			{
				if (slots[i].WasPressedThisFrame() && SelectedSlot != i)
				{
					RPC_RequestSelect(i);
				}
			}

			float scroll = _actions.HotbarScroll.ReadValue<float>();
			if (scroll != 0f)
			{
				int step = scroll > 0f ? -1 : 1;
				int next = ((SelectedSlot + step) % SlotCount + SlotCount) % SlotCount;
				RPC_RequestSelect(next);
			}

			if (_actions.Drop.WasPressedThisFrame())
			{
				RPC_RequestDrop();
			}
		}

		float IInteractionTarget.PickupRange => PickupRange;

		void IInteractionTarget.OnPickupRequested(PickupableItem pickup)
		{
			if (pickup == null || pickup.Object == null) return;
			RPC_RequestPickup(pickup.Object.Id);
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestSelect(int idx)
		{
			if (idx < 0 || idx >= SlotCount) return;
			SelectedSlot = idx;
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestPickup(NetworkId worldItemId)
		{
			if (!Runner.TryFindObject(worldItemId, out var obj)) return;
			if (!obj.TryGetComponent<PickupableItem>(out var pickup)) return;

			float distSq = (pickup.transform.position - transform.position).sqrMagnitude;
			float allowed = PickupRange * 1.5f;
			if (distSq > allowed * allowed) return;

			short leftover = TryAdd(pickup.ItemId, pickup.Count);
			if (leftover >= pickup.Count) return;

			if (leftover <= 0)
			{
				Runner.Despawn(obj);
			}
			else
			{
				pickup.Count = leftover;
			}
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestDrop()
		{
			DropSelected();
		}

		public short TryAdd(short itemId, short count)
		{
			return InventoryOps.TryAdd(Slots, itemId, count);
		}

		public bool RemoveAt(int slot, short count)
		{
			if (slot < 0 || slot >= SlotCount || count <= 0) return false;

			var s = Slots[slot];
			if (s.IsEmpty || s.Count < count) return false;

			s.Count -= count;
			Slots.Set(slot, s.Count <= 0 ? InventorySlot.Empty : s);
			return true;
		}

		public void DropSelected()
		{
			var s = Slots[SelectedSlot];
			if (s.IsEmpty) return;
			if (ItemDatabase.Instance == null) return;

			var def = ItemDatabase.Instance.GetById(s.ItemId);
			if (def == null || def.WorldPrefab == null) return;

			var pos = transform.position + transform.forward * DropForwardOffset + Vector3.up * DropUpOffset;
			var spawned = Runner.Spawn(def.WorldPrefab, pos, Quaternion.identity);
			if (spawned != null && spawned.TryGetComponent<PickupableItem>(out var pi))
			{
				pi.Initialize(s.ItemId, s.Count);
			}

			Slots.Set(SelectedSlot, InventorySlot.Empty);
		}

		public void SelectSlot(int idx)
		{
			if (idx < 0 || idx >= SlotCount) return;
			SelectedSlot = idx;
		}

		private void OnSelectedChanged()
		{
			RefreshHeldItem();
			SelectedChanged?.Invoke();
		}

		private void OnSlotsChanged()
		{
			var s = Slots[SelectedSlot];
			short id = s.IsEmpty ? (short)0 : s.ItemId;
			if (id != _heldItemId)
			{
				RefreshHeldItem();
			}
			SlotsChanged?.Invoke();
		}

		private void RefreshHeldItem()
		{
			if (_heldInstance != null)
			{
				Destroy(_heldInstance);
				_heldInstance = null;
			}
			_heldItemId = 0;

			if (HandAnchor == null) return;
			if (ItemDatabase.Instance == null) return;

			var s = Slots[SelectedSlot];
			if (s.IsEmpty) return;

			var def = ItemDatabase.Instance.GetById(s.ItemId);
			if (def == null || def.HandPrefab == null) return;

			_heldInstance = Instantiate(def.HandPrefab, HandAnchor);
			_heldInstance.transform.localPosition = Vector3.zero;
			_heldInstance.transform.localRotation = Quaternion.identity;
			_heldItemId = def.Id;
		}
	}
}
