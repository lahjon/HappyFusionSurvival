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
	public sealed class Inventory : NetworkBehaviour, IPlayerInventory, IPickupTarget
	{
		public const int SlotCount = 8;

		[Header("Tuning")]
		public float PickupRange = 2f;
		public float DropForwardOffset = 1.0f;
		public float DropUpOffset = 1.0f;

		[Header("Throw")]
		[Tooltip("Forward velocity applied to dropped items (m/s along the player's forward).")]
		public float ThrowForwardSpeed = 1f;
		[Tooltip("Upward velocity applied to dropped items (m/s).")]
		public float ThrowUpSpeed = 1f;
		[Tooltip("Seconds the dropped item is uninteractable so the thrower can't immediately re-grab it.")]
		public float ThrowInteractionLock = 1f;

		[Header("References")]
		[Tooltip("Local-only anchor where the held HandPrefab is parented. Typically under the camera handle.")]
		public Transform HandAnchor;

		[Tooltip("Item seeded into slot 0 the first time the inventory spawns (state authority only). Usually the starting weapon.")]
		[SerializeField] private ItemDefinition _startingItem;

		[Tooltip("Local-only hand prefab shown when the selected slot is empty (e.g. fists for the punch attack).")]
		[SerializeField] private GameObject _fallbackHandPrefab;

		[Networked, Capacity(SlotCount), OnChangedRender(nameof(OnSlotsChanged))]
		public NetworkArray<InventorySlot> Slots => default;

		[Networked, OnChangedRender(nameof(OnSelectedChanged))]
		public int SelectedSlot { get; set; }

		public event System.Action SlotsChanged;
		public event System.Action SelectedChanged;

		private GameObject _heldInstance;
		private short _heldItemId;
		private GameInputActions _actions;

		public GameObject HeldInstance => _heldInstance;
		public short SelectedItemId => Slots[SelectedSlot].ItemId;

		public override void Spawned()
		{
			if (HasStateAuthority && _startingItem != null && _startingItem.Id != 0 && AllSlotsEmpty())
			{
				Slots.Set(0, new InventorySlot { ItemId = _startingItem.Id, Count = 1 });
				SelectedSlot = 0;
			}

			if (HasInputAuthority)
			{
				_actions = GetComponent<GameInputActions>();
			}

			RefreshHeldItem();
		}

		private bool AllSlotsEmpty()
		{
			for (int i = 0; i < Slots.Length; i++)
			{
				if (Slots[i].IsEmpty == false) return false;
			}
			return true;
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

		void IPickupTarget.OnPickupRequested(PickupableItem pickup)
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

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestDropAt(int slot)
		{
			DropAt(slot);
		}

		public void RequestDropSlot(int slot)
		{
			if (slot < 0 || slot >= SlotCount) return;
			RPC_RequestDropAt(slot);
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
			DropAt(SelectedSlot);
		}

		public void DropAt(int slot)
		{
			if (slot < 0 || slot >= SlotCount) return;

			var s = Slots[slot];
			if (s.IsEmpty) return;
			if (ItemDatabase.Instance == null) return;

			var def = ItemDatabase.Instance.GetById(s.ItemId);
			if (def == null || def.WorldPrefab == null) return;
			if (def.WorldPrefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[Inventory] Item '{def.DisplayName}' has WorldPrefab '{def.WorldPrefab.name}' without a NetworkObject component — drop ignored.");
				return;
			}

			// Spawn at the player's hand (where the held item visually lives) so the drop
			// appears to fall out of the hand. Fall back to a chest-height offset on the
			// player root if HandAnchor isn't wired up.
			Vector3 pos = HandAnchor != null
				? HandAnchor.position + transform.forward * DropForwardOffset
				: transform.position + transform.forward * DropForwardOffset + Vector3.up * DropUpOffset;

			var spawned = Runner.Spawn(def.WorldPrefab, pos, Quaternion.identity);
			if (spawned != null && spawned.TryGetComponent<PickupableItem>(out var pi))
			{
				pi.Initialize(s.ItemId, s.Count);
				var velocity = transform.forward * ThrowForwardSpeed + Vector3.up * ThrowUpSpeed;
				pi.Throw(velocity, ThrowInteractionLock);
			}

			Slots.Set(slot, InventorySlot.Empty);
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

			var s = Slots[SelectedSlot];
			GameObject prefab = null;

			if (s.IsEmpty)
			{
				prefab = _fallbackHandPrefab;
			}
			else if (ItemDatabase.Instance != null)
			{
				var def = ItemDatabase.Instance.GetById(s.ItemId);
				if (def != null)
				{
					prefab = def.HandPrefab;
					_heldItemId = def.Id;
				}
			}

			if (prefab == null) return;

			_heldInstance = Instantiate(prefab, HandAnchor);
			_heldInstance.transform.localPosition = Vector3.zero;
			_heldInstance.transform.localRotation = Quaternion.identity;

			if (HasInputAuthority)
			{
				int overlayLayer = LayerMask.NameToLayer("FirstPersonOverlay");
				if (overlayLayer >= 0)
				{
					SetLayerRecursively(_heldInstance, overlayLayer);
				}
			}
		}

		private static void SetLayerRecursively(GameObject go, int layer)
		{
			go.layer = layer;
			var t = go.transform;
			for (int i = 0; i < t.childCount; i++)
			{
				SetLayerRecursively(t.GetChild(i).gameObject, layer);
			}
		}
	}
}
