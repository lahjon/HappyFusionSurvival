using System.Collections.Generic;
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

		[Header("Weight")]
		[Tooltip("Total inventory weight that can be carried with no movement penalty. Over the limit by 0-25% halves move speed; >25% quarters it.")]
		[Min(0f)]
		public float WeightLimit = 100f;

		[Header("Throw")]
		[Tooltip("Forward velocity applied to dropped items (m/s along the player's forward).")]
		public float ThrowForwardSpeed = 6f;
		[Tooltip("Upward velocity applied to dropped items (m/s).")]
		public float ThrowUpSpeed = 3f;
		[Tooltip("How much of the player's current velocity is added to the throw (0 = none, 1 = full). Lets sprinting throws carry further.")]
		public float PlayerVelocityContribution = 1f;
		[Tooltip("Seconds the dropped item is uninteractable so the thrower can't immediately re-grab it.")]
		public float ThrowInteractionLock = 1f;

		[Header("References")]
		[Tooltip("Local-only anchor where the held HandPrefab is parented. Typically under the camera handle.")]
		public Transform HandAnchor;

		[Tooltip("Item seeded into slot 0 the first time the inventory spawns (state authority only). Usually the starting weapon.")]
		[SerializeField] private ItemData _startingItem;

		[Tooltip("Local-only hand prefab shown when the selected slot is empty (e.g. fists for the punch attack).")]
		[SerializeField] private GameObject _fallbackHandPrefab;

		[Tooltip("Item supplying the unarmed (empty-hand) WeaponCapability — i.e. the fist punch action. " +
		         "Read by Player.GetActiveAction when no item is selected. Not placed in the hotbar.")]
		[SerializeField] private ItemData _unarmedItem;

		[Tooltip("Optional ammo item seeded into the inventory on first spawn (state authority only), so a " +
		         "magazine-fed starting weapon isn't bricked before the shop/crafting loop exists. Leave null for none.")]
		[SerializeField] private ItemData _startingAmmo;
		[Tooltip("How many _startingAmmo units to seed on first spawn.")]
		[SerializeField, Min(0)] private int _startingAmmoCount = 0;

		[Tooltip("Shared world-pickup prefab (NetworkObject + PickupableItem) spawned for any dropped item whose " +
		         "ItemData has no bespoke WorldPrefab override. Builds its visual from the item's ItemVisual.")]
		[SerializeField] private GameObject _genericWorldPrefab;

		[Tooltip("Shared in-hand rig (HeldWeapon + Mesh + MuzzleAnchor) used for any held item whose ItemData " +
		         "has no bespoke HandPrefab override. Configured from the item's Visual + WeaponCapability at equip.")]
		[SerializeField] private GameObject _genericHandPrefab;

		[Networked, Capacity(SlotCount), OnChangedRender(nameof(OnSlotsChanged))]
		public NetworkArray<InventorySlot> Slots => default;

		[Networked, OnChangedRender(nameof(OnSelectedChanged))]
		public int SelectedSlot { get; set; }

		/// <summary>
		/// Id of the placeable currently held in the player's hands. 0 = nothing carried.
		/// Lives outside the 8-slot hotbar — placeables are never deposited into slots.
		/// While non-zero, the carry visual takes priority over the selected slot's HandPrefab
		/// and the selected slot's fire/use input is suppressed (see Player.ProcessFireInput).
		/// </summary>
		[Networked, OnChangedRender(nameof(OnCarriedChanged))]
		public short CarriedPlaceableId { get; set; }

		[Networked]
		private TickTimer _useCooldownTimer { get; set; }

		/// <summary>Counts down a reload of the selected magazine-fed weapon. Started on state authority by
		/// <see cref="UpdateAmmoCycle"/> when the magazine runs dry and reserve ammo exists; on expiry the
		/// magazine is refilled. Networked so clients see the reload in progress.</summary>
		[Networked]
		public TickTimer ReloadTimer { get; private set; }
		/// <summary>True while a reload is in flight. Blocks firing and drives the HUD "Reloading…" state.</summary>
		[Networked]
		public NetworkBool IsReloading { get; private set; }

		public event System.Action SlotsChanged;
		public event System.Action SelectedChanged;
		public event System.Action CarriedChanged;

		private GameObject _heldInstance;
		private short _heldItemId;
		private GameInputActions _actions;
		private Player _player;

		public GameObject HeldInstance => _heldInstance;
		public short SelectedItemId => Slots[SelectedSlot].ItemId;

		/// <summary>
		/// Local-only flag. While true, RefreshHeldItem leaves no held visual in the hand —
		/// used by PlacementController so the ghost preview is the only thing the player sees.
		/// </summary>
		public bool SuppressHeldVisual
		{
			get => _suppressHeldVisual;
			set
			{
				if (_suppressHeldVisual == value) return;
				_suppressHeldVisual = value;
				RefreshHeldItem();
			}
		}
		private bool _suppressHeldVisual;

		public ItemData SelectedDefinition
		{
			get
			{
				if (ItemDatabase.Instance == null) return null;
				var s = Slots[SelectedSlot];
				return s.IsEmpty ? null : ItemDatabase.Instance.GetById(s.ItemId);
			}
		}

		/// <summary>Weapon facet used when the hand is empty (the fists), or null if no unarmed item is wired.</summary>
		public WeaponCapability UnarmedWeapon => _unarmedItem != null ? _unarmedItem.GetCapability<WeaponCapability>() : null;

		/// <summary>
		/// The CombatAction the held item would fire: the selected item's WeaponCapability action,
		/// or the unarmed (fists) action when the hand is empty. Null if the held item isn't a weapon.
		/// Single source of truth for both Player (fire/feedback) and PlayerInput (sway/aim-recoil).
		/// </summary>
		/// <summary>The weapon facet of the held item, or the unarmed (fists) weapon when the hand is
		/// empty. Null if the held item isn't a weapon. Source for ActiveAction plus the secondary
		/// (RMB) mode/action and aim tuning read by Player/PlayerInput.</summary>
		public WeaponCapability ActiveWeapon
		{
			get
			{
				var def = SelectedDefinition;
				return def != null ? def.GetCapability<WeaponCapability>() : UnarmedWeapon;
			}
		}

		public CombatAction ActiveAction
		{
			get
			{
				var weapon = ActiveWeapon;
				if (weapon == null || weapon.Actions == null || weapon.Actions.Count == 0) return null;
				return weapon.Actions[0];
			}
		}

		/// <summary>True if the held weapon is magazine-fed (drives the ammo HUD + reload cycle).</summary>
		public bool ActiveUsesMagazine
		{
			get
			{
				var w = ActiveWeapon;
				return w != null && w.UsesMagazine;
			}
		}

		/// <summary>Rounds in the held weapon's magazine (the selected slot's Loaded). 0 if nothing magazine-fed is held.</summary>
		public int ActiveLoaded => ActiveUsesMagazine ? Slots[SelectedSlot].Loaded : 0;

		/// <summary>Gadget facet of the held item, or null. Source for the charge accessors below.</summary>
		public GadgetCapability ActiveGadget => SelectedDefinition != null ? SelectedDefinition.GetCapability<GadgetCapability>() : null;

		/// <summary>True if the held gadget is charge-limited (drives the charge HUD + the fire gate).</summary>
		public bool ActiveUsesCharges
		{
			get
			{
				var g = ActiveGadget;
				return g != null && g.MaxCharges > 0;
			}
		}

		/// <summary>Charges left in the held gadget (the selected slot's Loaded). 0 if no charge gadget is held.</summary>
		public int ActiveCharges => ActiveUsesCharges ? Slots[SelectedSlot].Loaded : 0;

		/// <summary>Reserve rounds for the held weapon's ammo type, summed across all hotbar slots.</summary>
		public int ActiveReserve
		{
			get
			{
				var w = ActiveWeapon;
				if (w == null || w.UsesMagazine == false) return 0;
				return AmmoReserve(w.AmmoItem.Id);
			}
		}

		/// <summary>Item currently carried in-hand, or null. Independent of any hotbar slot.</summary>
		public ItemData CarriedDefinition
		{
			get
			{
				if (CarriedPlaceableId == 0 || ItemDatabase.Instance == null) return null;
				return ItemDatabase.Instance.GetById(CarriedPlaceableId);
			}
		}

		/// <summary>Placement rules for the carried item, or null if nothing placeable is carried.</summary>
		public PlaceableCapability CarriedPlaceable => CarriedDefinition?.GetCapability<PlaceableCapability>();

		/// <summary>Sum of every slot's (Weight * Count). 0 when no ItemDatabase.</summary>
		public float CurrentWeight
		{
			get
			{
				if (ItemDatabase.Instance == null) return 0f;
				float total = 0f;
				for (int i = 0; i < Slots.Length; i++)
				{
					var s = Slots[i];
					if (s.IsEmpty) continue;
					var def = ItemDatabase.Instance.GetById(s.ItemId);
					if (def != null) total += def.Weight * s.Count;
				}
				return total;
			}
		}

		/// <summary>1.0 at or under WeightLimit, 0.5 from 0-25% over, 0.25 beyond 25% over. Read by Player to scale walk/sprint speed.</summary>
		public float SpeedMultiplier
		{
			get
			{
				if (WeightLimit <= 0f) return 1f;
				float w = CurrentWeight;
				if (w <= WeightLimit) return 1f;
				float overFraction = (w - WeightLimit) / WeightLimit;
				return overFraction <= 0.25f ? 0.5f : 0.25f;
			}
		}

		public override void Spawned()
		{
			_player = GetComponent<Player>();

			if (HasStateAuthority && _startingItem != null && _startingItem.Id != 0 && AllSlotsEmpty())
			{
				// Seed the slot's Loaded: a magazine-fed weapon starts with a full magazine; a charge gadget
				// (grapple) starts with its full charges (InitialLoaded). Ordinary items stay 0.
				var startWeapon = _startingItem.GetCapability<WeaponCapability>();
				short loaded = startWeapon != null && startWeapon.UsesMagazine
					? (short)startWeapon.MagazineSize
					: _startingItem.InitialLoaded();
				Slots.Set(0, new InventorySlot { ItemId = _startingItem.Id, Count = 1, Loaded = loaded });
				SelectedSlot = 0;

				// Seed reserve ammo so the starting weapon can reload before the shop/crafting loop exists.
				if (_startingAmmo != null && _startingAmmo.Id != 0 && _startingAmmoCount > 0)
				{
					TryAdd(_startingAmmo.Id, (short)_startingAmmoCount);
				}
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
			// Don't strand the carried object on disconnect / death — spawn it loose so the
			// world keeps a copy. Guarded on hasState: networked accessors throw once the
			// state has been released (e.g. application quit / runner shutdown).
			if (hasState && HasStateAuthority && CarriedPlaceableId != 0)
			{
				DropCarried();
			}

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
				if (CarriedPlaceableId != 0)
					RPC_RequestDropCarried();
				else
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
			if (idx == SelectedSlot) return;
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

			ItemDatabase.TryGet(pickup.ItemId, out var def);
			if (def != null && def.HasCapability<PlaceableCapability>())
			{
				// Placeables route into the carry channel, not the hotbar. Any existing carry
				// is dropped first inside AuthorityStartCarry.
				if (AuthorityStartCarry(pickup.ItemId) == false) return;

				if (pickup.Count <= 1)
					Runner.Despawn(obj);
				else
					pickup.Count = (short)(pickup.Count - 1);
				return;
			}

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
			DropAt(slot, 1);
		}

		public void RequestDropSlot(int slot)
		{
			if (slot < 0 || slot >= SlotCount) return;
			RPC_RequestDropAt(slot);
		}

		void IPlayerInventory.AuthorityDropSlot(int slot) => DropAt(slot);
		bool IPlayerInventory.AuthorityStartCarry(short itemId) => AuthorityStartCarry(itemId);

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestUseSlot(int slot)
		{
			TryUseAt(slot);
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestPlaceCarried(Vector3 position, Quaternion rotation)
		{
			TryPlaceCarried(position, rotation);
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_RequestDropCarried()
		{
			DropCarried();
		}

		/// <summary>
		/// Local-side entry for "place the currently-carried item at the given pose".
		/// Validation runs on the state authority via the RPC.
		/// </summary>
		public void RequestPlaceCarried(Vector3 position, Quaternion rotation)
		{
			RPC_RequestPlaceCarried(position, rotation);
		}

		private bool TryPlaceCarried(Vector3 position, Quaternion rotation)
		{
			if (HasStateAuthority == false) return false;
			var cap = CarriedPlaceable;
			if (cap == null || cap.PlacedPrefab == null) return false;
			if (cap.PlacedPrefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[Inventory] Placeable '{CarriedDefinition?.DisplayName}' has PlacedPrefab '{cap.PlacedPrefab.name}' without a NetworkObject component — place ignored.");
				return false;
			}

			// Host re-validates the client-supplied pose — never trust the local placement raycast. Allow the
			// authored range plus a proportional slack (matches the InteractRange * 1.25f host-range convention)
			// and a fixed camera-height term so placing on the ground from eye level isn't falsely rejected.
			const float CameraHeightSlack = 2f;
			float maxDist = cap.PlacementRange * 1.25f + CameraHeightSlack;
			if ((position - transform.position).sqrMagnitude > maxDist * maxDist) return false;

			if (cap.Footprint > 0f && IsObstructed(position, cap.Footprint, cap.PlacementMask)) return false;

			Runner.Spawn(cap.PlacedPrefab, position, rotation);
			CarriedPlaceableId = 0;
			return true;
		}

		/// <summary>
		/// State-authority entry point used by world pickups (PickupableItem, PickupableProp,
		/// InteractableStation, LootContainer) to put a placeable into the player's hands.
		/// If the player is already carrying something it's dropped as a loose physics object
		/// first. Auto-selects an empty hotbar slot so the previously-equipped HandPrefab visual
		/// goes away; if no empty slot exists, leaves selection alone (carry visual overrides).
		/// </summary>
		public bool AuthorityStartCarry(short itemId)
		{
			if (HasStateAuthority == false) return false;
			if (itemId == 0 || ItemDatabase.Instance == null) return false;
			var carryDef = ItemDatabase.Instance.GetById(itemId);
			if (carryDef == null || carryDef.HasCapability<PlaceableCapability>() == false) return false;

			// Already carrying something — drop the old one loose before swapping.
			if (CarriedPlaceableId != 0)
			{
				DropCarried();
			}

			int empty = FindFirstEmptySlot();
			if (empty >= 0 && empty != SelectedSlot)
			{
				SelectedSlot = empty;
			}

			CarriedPlaceableId = itemId;
			return true;
		}

		/// <summary>
		/// State-authority entry point: spawn the carried placeable's loose WorldPrefab in front
		/// of the player with a throw arc, then clear the carry slot. Used both by the explicit
		/// drop input and by callers that need to evict the carry (e.g. on despawn).
		/// </summary>
		public void DropCarried()
		{
			if (HasStateAuthority == false) return;
			if (CarriedPlaceableId == 0 || ItemDatabase.Instance == null) return;

			var def = ItemDatabase.Instance.GetById(CarriedPlaceableId);
			short carried = CarriedPlaceableId;
			CarriedPlaceableId = 0;

			var worldPrefab = ResolveWorldPrefab(def);
			if (worldPrefab == null) return;
			if (worldPrefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[Inventory] Placeable '{def?.DisplayName}' world prefab '{worldPrefab.name}' has no NetworkObject component — drop ignored.");
				return;
			}

			Vector3 pos = HandAnchor != null
				? HandAnchor.position + transform.forward * DropForwardOffset
				: transform.position + transform.forward * DropForwardOffset + Vector3.up * DropUpOffset;

			var spawned = Runner.Spawn(worldPrefab, pos, Quaternion.identity);
			if (spawned != null && spawned.TryGetComponent<PickupableItem>(out var pi))
			{
				pi.Initialize(carried, 1);
				Vector3 inherited = _player != null && _player.KCC != null
					? _player.KCC.RealVelocity * PlayerVelocityContribution
					: Vector3.zero;
				var velocity = transform.forward * ThrowForwardSpeed + Vector3.up * ThrowUpSpeed + inherited;
				pi.Throw(velocity, ThrowInteractionLock);
			}
		}

		private static readonly Collider[] s_overlapBuffer = new Collider[16];

		private bool IsObstructed(Vector3 position, float radius, int layerMask)
		{
			// Find the surface directly below the ghost position. We use it both to exclude that
			// collider AND to lift the overlap sphere so its lower edge sits just above the surface
			// — adjacent floor tiles (e.g. PolygonTown's road/sidewalk grid) aren't excluded by the
			// single-collider mask, so the sphere must not intersect floor level at all.
			Collider surface = null;
			Vector3 surfacePoint = position;
			Vector3 upAxis = Vector3.up;
			if (Physics.Raycast(position + Vector3.up * 0.05f, Vector3.down, out RaycastHit surfaceHit, radius + 0.5f, layerMask, QueryTriggerInteraction.Ignore))
			{
				surface = surfaceHit.collider;
				surfacePoint = surfaceHit.point;
				upAxis = surfaceHit.normal;
			}

			Vector3 center = surfacePoint + upAxis * (radius + 0.02f);
			int count = Physics.OverlapSphereNonAlloc(center, radius, s_overlapBuffer, layerMask, QueryTriggerInteraction.Ignore);
			Transform self = transform.root;
			for (int i = 0; i < count; i++)
			{
				var col = s_overlapBuffer[i];
				if (col == null) continue;
				if (col == surface) continue;
				if (col.transform.root == self) continue;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Local-side entry for "use this slot's item" (e.g. right-click in the HUD).
		/// Routes to state authority via RPC; no-op if the slot isn't a consumable.
		/// </summary>
		public void RequestUseSlot(int slot)
		{
			if (slot < 0 || slot >= SlotCount) return;
			RPC_RequestUseSlot(slot);
		}

		/// <summary>
		/// Called by Player when LMB is pressed while a consumable is in hand.
		/// Runs on both state and input authority during FixedUpdateNetwork so prediction
		/// and authoritative state agree on the cooldown.
		/// </summary>
		public bool TryUseSelectedConsumable() => TryUseAt(SelectedSlot);

		private bool TryUseAt(int slot)
		{
			if (slot < 0 || slot >= SlotCount) return false;
			if (_useCooldownTimer.ExpiredOrNotRunning(Runner) == false) return false;

			var s = Slots[slot];
			if (s.IsEmpty || ItemDatabase.Instance == null) return false;

			var def = ItemDatabase.Instance.GetById(s.ItemId);
			if (def == null || def.TryGetCapability<ConsumableCapability>(out var consumable) == false) return false;

			_useCooldownTimer = TickTimer.CreateFromSeconds(Runner, consumable.UseCooldownSeconds);

			consumable.Apply(gameObject, Runner);

			s.Count -= 1;
			Slots.Set(slot, s.Count <= 0 ? InventorySlot.Empty : s);
			return true;
		}

		public short TryAdd(short itemId, short count)
		{
			return InventoryOps.TryAdd(Slots, itemId, count);
		}

		private int FindFirstEmptySlot()
		{
			for (int i = 0; i < Slots.Length; i++)
			{
				if (Slots[i].IsEmpty) return i;
			}
			return -1;
		}

		public bool RemoveAt(int slot, short count)
		{
			if (slot < 0 || slot >= SlotCount || count <= 0) return false;

			var s = Slots[slot];
			if (s.IsEmpty || s.Count < count) return false;

			s.Count -= count;
			// Preserve Loaded only while the stack survives; a fully-removed slot resets to Empty.
			Slots.Set(slot, s.Count <= 0 ? InventorySlot.Empty : s);
			return true;
		}

		// ---- Ammo / magazine ----

		/// <summary>First slot holding <paramref name="ammoId"/>, or -1. Mirrors ProjectileAction.FindAmmoSlot.</summary>
		public int FindAmmoSlot(short ammoId)
		{
			if (ammoId == 0) return -1;
			for (int i = 0; i < Slots.Length; i++)
			{
				var s = Slots[i];
				if (s.IsEmpty) continue;
				if (s.ItemId == ammoId) return i;
			}
			return -1;
		}

		/// <summary>Total reserve rounds of <paramref name="ammoId"/> across the hotbar (not counting loaded magazines).</summary>
		public int AmmoReserve(short ammoId)
		{
			if (ammoId == 0) return 0;
			int total = 0;
			for (int i = 0; i < Slots.Length; i++)
			{
				var s = Slots[i];
				if (s.IsEmpty || s.ItemId != ammoId) continue;
				total += s.Count;
			}
			return total;
		}

		/// <summary>
		/// Consume one round from the selected weapon's magazine. Predicted on every predicting peer
		/// (no HasStateAuthority gate — Slots is networked so IA's write reconciles to SA, exactly like
		/// <see cref="RemoveAt"/>). Returns false when the magazine is empty.
		/// </summary>
		public bool TryConsumeChamberedRound()
		{
			int slot = SelectedSlot;
			if (slot < 0 || slot >= SlotCount) return false;
			var s = Slots[slot];
			if (s.IsEmpty || s.Loaded <= 0) return false;
			s.Loaded -= 1;
			Slots.Set(slot, s);
			return true;
		}

		/// <summary>
		/// Spend one charge from the held gadget (grapple). Predicted on every peer via the networked Loaded
		/// write, exactly like <see cref="TryConsumeChamberedRound"/> — no recharge, so at zero the gadget is
		/// spent. Returns false when no charge gadget is held or it is already empty.
		/// </summary>
		public bool TryConsumeCharge()
		{
			if (ActiveUsesCharges == false) return false;
			int slot = SelectedSlot;
			if (slot < 0 || slot >= SlotCount) return false;
			var s = Slots[slot];
			if (s.IsEmpty || s.Loaded <= 0) return false;
			s.Loaded -= 1;
			Slots.Set(slot, s);
			return true;
		}

		/// <summary>
		/// Fire the local-only <see cref="HeldGadget.OnUsePressed"/> hook of the currently-held gadget (active
		/// radar ping, flashlight toggle, …). Call on the owning client only — the effect is local. Networked
		/// gadgets (the grapple) do NOT route through here; they are handled on the player tick.
		/// </summary>
		public void UseSelectedGadgetLocal()
		{
			if (_heldInstance != null && _heldInstance.TryGetComponent<HeldGadget>(out var gadget))
				gadget.OnUsePressed();
		}

		/// <summary>
		/// Drives the auto-reload cycle for the held magazine-fed weapon. Called every tick from
		/// <see cref="Player.ProcessInput"/> on state + input authority; all mutations gate on state
		/// authority (the [Networked] ReloadTimer / IsReloading replicate to clients for the HUD).
		/// When the magazine runs dry and reserve ammo exists, starts a reload; on timer expiry, moves
		/// rounds from the reserve stacks into the selected slot's magazine.
		/// </summary>
		public void UpdateAmmoCycle()
		{
			var weapon = ActiveWeapon;
			if (weapon == null || weapon.UsesMagazine == false)
			{
				// Not holding a magazine weapon — cancel any stale reload (e.g. switched off mid-reload).
				if (HasStateAuthority && IsReloading)
				{
					IsReloading = false;
					ReloadTimer = default;
				}
				return;
			}

			if (HasStateAuthority == false) return;

			int slot = SelectedSlot;
			if (slot < 0 || slot >= SlotCount) return;
			var s = Slots[slot];
			// The selected slot must actually be the weapon (it is — ActiveWeapon reads SelectedDefinition).
			if (s.IsEmpty) return;

			short ammoId = weapon.AmmoItem.Id;

			if (IsReloading)
			{
				if (ReloadTimer.ExpiredOrNotRunning(Runner))
				{
					// Refill: pull rounds from reserve stacks into the magazine.
					int need = weapon.MagazineSize - s.Loaded;
					int moved = ConsumeReserve(ammoId, need);
					if (moved > 0)
					{
						// Re-read the slot — ConsumeReserve may have touched it if the weapon shared
						// an index path (it won't, weapons aren't ammo, but stay correct under change).
						s = Slots[slot];
						s.Loaded = (short)(s.Loaded + moved);
						Slots.Set(slot, s);
					}
					IsReloading = false;
					ReloadTimer = default;
				}
				return;
			}

			// Auto-start a reload when the magazine is empty and reserve exists.
			if (s.Loaded <= 0 && AmmoReserve(ammoId) > 0)
			{
				ReloadTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, weapon.ReloadSeconds));
				IsReloading = true;
			}
		}

		/// <summary>State-authority: remove up to <paramref name="amount"/> rounds of <paramref name="ammoId"/>
		/// from reserve stacks (lowest slot first). Returns how many were actually removed.</summary>
		private int ConsumeReserve(short ammoId, int amount)
		{
			if (ammoId == 0 || amount <= 0) return 0;
			int remaining = amount;
			for (int i = 0; i < Slots.Length && remaining > 0; i++)
			{
				var s = Slots[i];
				if (s.IsEmpty || s.ItemId != ammoId) continue;
				short take = (short)Mathf.Min(remaining, s.Count);
				s.Count -= take;
				Slots.Set(i, s.Count <= 0 ? InventorySlot.Empty : s);
				remaining -= take;
			}
			return amount - remaining;
		}

		/// <summary>Bespoke WorldPrefab override if the item sets one, else the shared generic pickup. Null if neither exists.</summary>
		private GameObject ResolveWorldPrefab(ItemData def)
		{
			if (def == null) return null;
			return def.WorldPrefab != null ? def.WorldPrefab : _genericWorldPrefab;
		}

		public void DropSelected()
		{
			DropAt(SelectedSlot, 1);
		}

		/// <param name="dropCount">Number of items to drop; <= 0 means the entire stack.</param>
		public void DropAt(int slot, short dropCount = 0)
		{
			if (slot < 0 || slot >= SlotCount) return;

			var s = Slots[slot];
			if (s.IsEmpty) return;

			short amount = (dropCount <= 0 || dropCount >= s.Count) ? s.Count : dropCount;

			// Spawn the loose pickup first; if no world prefab resolves, bail without touching the slot.
			if (SpawnLooseInFront(s.ItemId, amount) == null) return;

			if (amount >= s.Count)
			{
				Slots.Set(slot, InventorySlot.Empty);
			}
			else
			{
				s.Count -= amount;
				Slots.Set(slot, s);
			}
		}

		/// <summary>
		/// State-authority: spawn <paramref name="amount"/> of <paramref name="itemId"/> as a single loose
		/// world pickup in front of the player, thrown out of the hand exactly like a manual drop. Returns the
		/// spawned <see cref="PickupableItem"/>, or null if the item has no resolvable (NetworkObject) world
		/// prefab. Shared by <see cref="DropAt"/> and the debug item-give path.
		/// </summary>
		private PickupableItem SpawnLooseInFront(short itemId, short amount)
		{
			if (HasStateAuthority == false || ItemDatabase.Instance == null) return null;

			var def = ItemDatabase.Instance.GetById(itemId);
			var worldPrefab = ResolveWorldPrefab(def);
			if (worldPrefab == null) return null;
			if (worldPrefab.GetComponent<NetworkObject>() == null)
			{
				Debug.LogWarning($"[Inventory] Item '{def?.DisplayName}' world prefab '{worldPrefab.name}' has no NetworkObject component — drop ignored.");
				return null;
			}

			// Spawn at the player's hand (where the held item visually lives) so the drop
			// appears to fall out of the hand. Fall back to a chest-height offset on the
			// player root if HandAnchor isn't wired up.
			Vector3 pos = HandAnchor != null
				? HandAnchor.position + transform.forward * DropForwardOffset
				: transform.position + transform.forward * DropForwardOffset + Vector3.up * DropUpOffset;

			var spawned = Runner.Spawn(worldPrefab, pos, Quaternion.identity);
			if (spawned == null || spawned.TryGetComponent<PickupableItem>(out var pi) == false) return null;

			pi.Initialize(itemId, amount);
			Vector3 inherited = _player != null && _player.KCC != null
				? _player.KCC.RealVelocity * PlayerVelocityContribution
				: Vector3.zero;
			var velocity = transform.forward * ThrowForwardSpeed + Vector3.up * ThrowUpSpeed + inherited;
			pi.Throw(velocity, ThrowInteractionLock);
			return pi;
		}

		/// <summary>
		/// State-authority debug entry: give <paramref name="count"/> of <paramref name="itemId"/> to this
		/// player. Stackable/hotbar items fill the inventory first (<see cref="TryAdd"/>); anything that doesn't
		/// fit — and every placeable, which is never stored in the hotbar — is spawned as a loose pickup in front
		/// of the player. Routed through <see cref="RPC_DebugGiveItem"/> so the add replicates off-host.
		/// </summary>
		public void AuthorityGiveItem(short itemId, short count)
		{
			if (HasStateAuthority == false) return;
			if (itemId == 0 || count <= 0 || ItemDatabase.Instance == null) return;

			var def = ItemDatabase.Instance.GetById(itemId);
			if (def == null) return;

			// Placeables can't live in the hotbar — drop the whole lot loose. Everything else tries the
			// inventory first and only spills the remainder into the world.
			short leftover = def.HasCapability<PlaceableCapability>() ? count : TryAdd(itemId, count);

			// Spill leftover into loose pickups, one stack (MaxStack) at a time. The safety counter guards
			// against a misconfigured MaxStack (<=1 is handled by Mathf.Max) looping forever.
			int safety = SlotCount + count;
			while (leftover > 0 && safety-- > 0)
			{
				short chunk = (short)Mathf.Min((int)leftover, Mathf.Max(1, def.MaxStack));
				if (SpawnLooseInFront(itemId, chunk) == null) break;
				leftover -= chunk;
			}
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_DebugGiveItem(short itemId, short count)
		{
			AuthorityGiveItem(itemId, count);
		}

		/// <summary>
		/// State-authority debug entry: refill ammo for every weapon currently in the hotbar. For each
		/// weapon found, gathers the ammo type(s) it draws from — the magazine <see cref="WeaponCapability.AmmoItem"/>
		/// plus the ammo of any projectile/grenade actions it owns — and tops that ammo's reserve up to the
		/// ammo item's MaxStack (a full stack). Finally tops every magazine-fed weapon's loaded magazine off
		/// to its full MagazineSize. Returns the number of distinct ammo types refilled.
		/// </summary>
		public int AuthorityRefillAmmo()
		{
			if (HasStateAuthority == false || ItemDatabase.Instance == null) return 0;

			var seen = new HashSet<short>();
			int refilled = 0;

			// Pass 1: top each weapon's ammo type(s) up to a full reserve stack.
			for (int i = 0; i < Slots.Length; i++)
			{
				var s = Slots[i];
				if (s.IsEmpty) continue;
				var def = ItemDatabase.Instance.GetById(s.ItemId);
				var weapon = def != null ? def.GetCapability<WeaponCapability>() : null;
				if (weapon == null) continue;

				if (TopUpAmmoReserve(weapon.AmmoItem, seen)) refilled++;
				if (weapon.Actions != null)
					for (int a = 0; a < weapon.Actions.Count; a++)
						if (TopUpAmmoReserve(AmmoOf(weapon.Actions[a]), seen)) refilled++;
				if (TopUpAmmoReserve(AmmoOf(weapon.SecondaryAction), seen)) refilled++;
			}

			// Pass 2: refill loaded magazines now that reserves are full (debug grant — magazine fill
			// is direct, it doesn't draw down the reserve we just topped up).
			for (int i = 0; i < Slots.Length; i++)
			{
				var s = Slots[i];
				if (s.IsEmpty) continue;
				var def = ItemDatabase.Instance.GetById(s.ItemId);
				var weapon = def != null ? def.GetCapability<WeaponCapability>() : null;
				if (weapon == null || weapon.UsesMagazine == false) continue;
				if (s.Loaded < weapon.MagazineSize)
				{
					s.Loaded = (short)weapon.MagazineSize;
					Slots.Set(i, s);
				}
			}

			return refilled;
		}

		/// <summary>Ammo item a combat action draws from (projectile/grenade actions), or null.</summary>
		private static ItemData AmmoOf(CombatAction action)
		{
			return action switch
			{
				ProjectileAction p => p.AmmoItem,
				GrenadeAction g    => g.AmmoItem,
				_                  => null,
			};
		}

		/// <summary>
		/// State-authority: bring the reserve of <paramref name="ammo"/> up to its MaxStack, adding the
		/// shortfall via <see cref="TryAdd"/>. No-op (returns false) for null ammo or a type already handled
		/// this refill — <paramref name="seen"/> dedupes weapons that share an ammo type.
		/// </summary>
		private bool TopUpAmmoReserve(ItemData ammo, HashSet<short> seen)
		{
			if (ammo == null || ammo.Id == 0) return false;
			if (seen.Add(ammo.Id) == false) return false;

			int max  = Mathf.Max(1, ammo.MaxStack);
			int need = max - AmmoReserve(ammo.Id);
			if (need > 0) TryAdd(ammo.Id, (short)need);
			return true;
		}

		[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
		public void RPC_DebugRefillAmmo()
		{
			AuthorityRefillAmmo();
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
			// Held visual could be either the carried def (priority) or the selected slot's item.
			// Carry takes priority so a slot mutation while carrying shouldn't force a refresh
			// unless the carry is empty.
			if (CarriedPlaceableId == 0)
			{
				var s = Slots[SelectedSlot];
				short id = s.IsEmpty ? (short)0 : s.ItemId;
				if (id != _heldItemId)
				{
					RefreshHeldItem();
				}
			}
			SlotsChanged?.Invoke();
		}

		private void OnCarriedChanged()
		{
			RefreshHeldItem();
			CarriedChanged?.Invoke();
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
			if (_suppressHeldVisual) return;

			// Skip the refresh outside the Spawned…Despawned window — the networked accessors
			// below (CarriedPlaceableId, Slots[…]) throw on pre-spawn or post-shutdown reads.
			// Triggered e.g. when PlacementController.OnDisable runs during runner shutdown.
			if (Object == null || Object.IsValid == false) return;

			GameObject prefab = null;
			ItemData heldDef = null;

			// Carried placeable wins over the selected slot's visual.
			if (CarriedPlaceableId != 0 && ItemDatabase.Instance != null)
			{
				heldDef = ItemDatabase.Instance.GetById(CarriedPlaceableId);
				if (heldDef != null) _heldItemId = heldDef.Id;
			}
			else
			{
				var s = Slots[SelectedSlot];
				if (s.IsEmpty)
				{
					prefab = _fallbackHandPrefab; // fists — bespoke rig (FistPunchAnimator), not configured
				}
				else if (ItemDatabase.Instance != null)
				{
					heldDef = ItemDatabase.Instance.GetById(s.ItemId);
					if (heldDef != null) _heldItemId = heldDef.Id;
				}
			}

			// Ordinary items: bespoke HandPrefab override if set, else the shared generic hand rig.
			if (heldDef != null)
				prefab = heldDef.HandPrefab != null ? heldDef.HandPrefab : _genericHandPrefab;

			if (prefab == null) return;

			_heldInstance = Instantiate(prefab, HandAnchor);

			// Generic rig: build the mesh + copy weapon tuning from the held item's asset BEFORE the
			// rest-pose reset and overlay-layer sweep so the attached mesh inherits the overlay layer.
			if (heldDef != null && _heldInstance.TryGetComponent<HeldWeapon>(out var rig))
				rig.Configure(heldDef.Visual, heldDef.GetCapability<WeaponCapability>());

			// Gadget items (radar/scanner, …) attach their HeldGadget module to the hand instance from the
			// item's GadgetCapability — no bespoke HandPrefab needed. Done before the overlay-layer sweep so
			// the gadget's world-space canvas inherits the FirstPersonOverlay layer like the rest of the rig.
			if (heldDef != null && heldDef.TryGetCapability<GadgetCapability>(out var gadget))
				gadget.CreateRuntime(_heldInstance);

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
