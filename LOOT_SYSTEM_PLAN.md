# Loot System — Implementation Plan

Common, networked lootable containers (chests, bookcases) with a mouse-driven 6-slot UI, drag-drop / right-click cross-swap, Take All, and single-player exclusive access.

## Status

| Phase | Title | Status |
|---|---|---|
| 0 | Input System migration | ✅ Code complete — manual Editor steps pending |
| 1 | Promote inventory types to `Assets/Common/Inventory/` | ✅ Done |
| 2 | `LootContainer : NetworkBehaviour` | ✅ Done |
| 3 | `LootSession` on the player (local controller) | ✅ Done |
| 4 | Interaction discovery (E to open) | ✅ Done |
| 5 | `LootUI` (procedural drag-drop / right-click / Take All) | ✅ Done |
| 6 | Authoring helpers (Chest + Bookcase prefabs) | ✅ Done |
| 7 | Verification pass (Debug.Log `[VERIFY]` checks) | ⏳ Needs Play-mode test |

## Locked-in decisions

| Decision | Choice | Notes |
|---|---|---|
| Input | Full Input System migration | Add `Inventory` action map; switch via `SwitchCurrentActionMap`. |
| Code location | `Assets/Common/Inventory/` | Namespace `Starter.Common.Inventory`. Both Shooter and Survival can consume. |
| Player slots | 8 (unchanged); container = 6 | UI shows container 3×2 above player 8×1. |
| Container contents | Hand-authored per-instance | `[SerializeField] InitialSlot[] InitialContents` on each prefab/instance. LootTable SO is a possible later add-on. |

---

## Phase 0 — Input System migration ✅

Done in code. Manual Editor steps remain — see the bottom of this section.

### Code changes (committed)

| File | Change |
|---|---|
| `Assets/InputSystem_Actions.inputactions` | Added Player actions `Drop`, `HotbarScroll`, `Hotbar1`..`Hotbar8` + bindings. Removed colliding `<Keyboard>/1` `<Keyboard>/2` from `Previous`/`Next`. Removed `Hold` interaction from `Interact`. Added new `Inventory` map: `Close`, `TakeAll`, `Point`, `LeftClick`, `RightClick`. |
| `Assets/Common/Input/GameInputActions.cs` *(new)* | MonoBehaviour wrapper holding a cloned `InputActionAsset`. Sits on the player root. Exposes strongly-typed accessors; only initializes for the local input authority via `EnableForLocalPlayer()`. |
| `Assets/Common/Input/InputContextController.cs` *(new)* | Toggles Player ↔ Inventory map + cursor lock. Invoked by `LootSession` in Phase 3. |
| `Assets/03_Shooter/Scripts/PlayerInput.cs` | Reads via Input System actions in `BeforeUpdate`. Cursor-lock gate preserved. |
| `Assets/03_Shooter/Scripts/Inventory/Inventory.cs` | Hotbar keys, scroll, E (Interact), Q (Drop) read via Input System. |
| `Assets/Common/UIGameMenu.cs` | Enter/Esc uses `Keyboard.current`. |
| `Assets/03_Shooter/Scripts/Inventory/InventoryTooltip.cs` | `Input.mousePosition` → `Mouse.current.position.ReadValue()`. |

### Manual Editor steps

**1. `Assets/03_Shooter/Prefabs/Player.prefab`**
- Open in Prefab Mode. Select root.
- Unity auto-adds **GameInputActions** (due to `[RequireComponent]` on `PlayerInput`); if not, **Add Component → Game Input Actions**.
- Drag `Assets/InputSystem_Actions.inputactions` into **Reference Asset**.
- **Add Component → Input Context Controller**.
- Save (Ctrl+S).

**2. `Assets/00_MainMenu/00_MainMenu.unity` and `Assets/03_Shooter/03_Shooter.unity`**
- Select **EventSystem**. Click **Replace with InputSystemUIInputModule** on the legacy `StandaloneInputModule`'s info box (or remove + re-add manually).
- Verify **Actions Asset** = `InputSystem_Actions`; leave UI action map fields at defaults.
- Save.

**3. Sanity test in Play mode**
- WASD, mouse look, LMB fire, Space jump, 1–8 hotbar, scroll cycle, E pickup, Q drop, Enter/Esc menu.
- If aim feels ~10× too fast: add `ScaleVector2(x=0.1,y=0.1)` processor to the **Look** action.

---

## Phase 1 — Promote shared inventory types

Move and re-namespace to `Starter.Common.Inventory`:
- `InventorySlot.cs`
- `ItemDefinition.cs`, `ItemDatabase.cs`
- `PickupableItem.cs`, `ItemView.cs`
- `InventorySlotHover.cs`, `InventoryTooltip.cs` *(UI primitives reused by `LootUI`)*

Add new `Assets/Common/Inventory/InventoryOps.cs` (static helper):
```csharp
static short TryAdd(ref NetworkArray<InventorySlot> arr, short itemId, short count);
static bool TryMove(ref NetworkArray<InventorySlot> from, int fromIdx,
                    ref NetworkArray<InventorySlot> to,   int toIdx);
static bool TryAutoMergeOrPlace(ref NetworkArray<InventorySlot> from, int fromIdx,
                                ref NetworkArray<InventorySlot> to);
```

`TryMove` handles merge-into-same-item, swap-into-different-item, swap-into-empty. `TryAutoMergeOrPlace` is for right-click swap and Take All.

Update `Starter.Shooter.Inventory` to delegate slot mutations through `InventoryOps`. Keep `Inventory.SlotCount = 8`.

---

## Phase 2 — `LootContainer : NetworkBehaviour`

`Assets/Common/Inventory/LootContainer.cs`, namespace `Starter.Common.Inventory`:

```csharp
[Networked, Capacity(6)]
NetworkArray<InventorySlot> Slots => default;

[Networked, OnChangedRender(nameof(OnUserChanged))]
PlayerRef CurrentUser { get; set; }

[Networked]
TickTimer LockHeartbeat { get; set; }

[SerializeField] string DisplayName = "Container";
[SerializeField] float InteractRange = 2.5f;

[Serializable] struct InitialSlot { public ItemDefinition Item; public short Count; }
[SerializeField] InitialSlot[] InitialContents;
```

**Lifecycle**
- `Spawned()` on state authority: populate `Slots` from `InitialContents`.
- `FixedUpdateNetwork()` on SA: when `LockHeartbeat.Expired(Runner)`, re-validate the opener (still in range, still connected). Clear `CurrentUser` if not. Reset heartbeat ~1 s.
- On player despawn / disconnect: SA clears `CurrentUser` if the leaving player was the opener.

**RPCs** (`InputAuthority → StateAuthority`, all validate `info.Source == CurrentUser` for mutations):
- `RPC_RequestOpen()` — assigns `CurrentUser = info.Source` only if currently `default` and caller in range. Client observes via `OnUserChanged`.
- `RPC_RequestClose()` — clears if caller matches.
- `RPC_RequestMove(byte fromInv, byte fromSlot, byte toInv, byte toSlot)` — `fromInv/toInv ∈ {0=Player, 1=Container}`. SA resolves the player's `Inventory` via `CurrentUser`, calls `InventoryOps.TryMove`.
- `RPC_RequestSwapToOther(byte fromInv, byte fromSlot)` — right-click; SA calls `InventoryOps.TryAutoMergeOrPlace` into the opposite inventory.
- `RPC_RequestTakeAll()` — SA loops container slots, auto-merges/places each into the player inventory, writes leftovers back.

---

## Phase 3 — `LootSession` on the player

`Assets/Common/Inventory/LootSession.cs`, MonoBehaviour on the Player prefab (input-authority only):

- Tracks the currently-open `LootContainer Current`.
- Observes `Current.OnUserChanged`:
  - If `CurrentUser == localPlayerRef` → call `InputContextController.EnterInventoryMode()`, open the UI.
  - If cleared → close UI, call `EnterPlayerMode()`.
- Subscribes to `_actions.Close.performed` → `Current.RPC_RequestClose()`.
- Subscribes to `_actions.TakeAll.performed` → `Current.RPC_RequestTakeAll()`.
- Public helpers `RequestMove(...)`, `RequestSwap(...)`, `RequestTakeAll()` that the UI slot widgets call.

---

## Phase 4 — Interaction discovery

- Generalize `Inventory.TryFindNearestPickup` into a player-side `InteractionScanner` that finds the nearest `PickupableItem` OR `LootContainer` within range each `Update` (input-authority only).
- `Interact` action (`E`) decides:
  - `PickupableItem` → existing `RPC_RequestPickup`.
  - `LootContainer` with `CurrentUser == default` → `container.RPC_RequestOpen()`.
  - `LootContainer` with `CurrentUser != default` → brief "Already in use" toast.
- World prompt: billboard text above the container — `[E] Open` / `[E] (In use)` driven by `CurrentUser`. Reuse `UINameplate` styling.

---

## Phase 5 — `LootUI` (procedural)

New `Assets/Common/Inventory/LootUI.cs`:

```
LootCanvas
└─ LootPanel (centered, dimmed background)
   ├─ Header: <DisplayName>   [Take All]   [X]
   ├─ ContainerGrid  (3×2, 6 slots ← LootContainer.Slots)
   └─ PlayerGrid     (4×2 or 8×1, 8 slots ← Inventory.Slots)
└─ DragGhostLayer (raycast off, follows pointer with held icon)
```

- Factor `InventoryHUD.BuildSlotWidget(...)` into a static `SlotWidgetFactory` in `Common/Inventory/UI/` so `InventoryHUD` and `LootUI` reuse it. Reuses `InventorySlotHover` and `InventoryTooltip`.
- New per-slot components:
  - `DraggableSlot : IBeginDragHandler, IDragHandler, IEndDragHandler` — `(InventoryKind kind, int index)`. On begin: snapshot, lift icon to ghost layer, disable raycast on source. On end: ghost cleanup.
  - `DroppableSlot : IDropHandler, IPointerClickHandler` — on drop: `LootSession.RequestMove(src, self)`. On `eventData.button == Right`: `LootSession.RequestSwapToOther(self)`.
- Bottom hotbar `InventoryHUD` hides while looting (`LootSession` toggles it) to avoid double-rendering player slots.
- All UI re-renders via `OnChangedRender` (`Inventory.SlotsChanged`, `LootContainer.Slots` change). No client prediction.

---

## Phase 6 — Authoring helpers

`Assets/Common/Prefabs/Loot/`:
- `Chest.prefab` — primitive cube + `NetworkObject` + `LootContainer` + `BoxCollider` + billboard prompt child.
- `Bookcase.prefab` — tall cube, same components.

Register both with `NetworkProjectConfig`. Drop a couple of test instances in `03_Shooter` scene with hand-authored `InitialContents`.

---

## Phase 7 — Verification

Per CLAUDE.md ("primitive visuals and `Debug.Log("[VERIFY] ... PASS ✓")`"):

- `[VERIFY] Open: P1 opens chest → CurrentUser = P1, UI shown only for P1`
- `[VERIFY] Lock: P2 tries to open → request rejected, prompt says (In use)`
- `[VERIFY] Move: drag container slot → player slot → both update on both clients`
- `[VERIFY] Swap (right-click): item moves to opposite inventory, stacks merge`
- `[VERIFY] TakeAll: container empties (modulo leftovers from full player inv)`
- `[VERIFY] Close: Esc / TakeAll-button / walk out of range / disconnect → CurrentUser cleared`
- `[VERIFY] Input: while looting, WASD / mouse-look / fire all suppressed`

---

## Risk / gotchas

- **Lock liveness**: don't rely only on client `Close`. SA must clear `CurrentUser` on opener despawn (`PlayerLeft`) or out-of-range. `LockHeartbeat` handles that.
- **Drag-drop while a packet is in flight**: client may briefly see "both slots showing the item." Render UI off `OnChangedRender` so it always tracks authoritative state.
- **`InventoryOps` correctness**: right-click & Take All must respect `MaxStack` per item and write back leftovers. One shared implementation, hit hard with edge cases.
- **Race-opening**: only the first `RPC_RequestOpen` SA processes wins; surface rejection with a brief "Already in use" toast on the losing client.
- **Hotbar HUD interactivity**: while looting, the bottom hotbar swaps from "display only" to a drop target for the player-side grid. The simplest path is hiding the bottom HUD and rendering the player grid inside `LootUI`.
