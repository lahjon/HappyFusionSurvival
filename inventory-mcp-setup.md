# Wire hotbar inventory into 03_Shooter (Unity-MCP session)

Use this prompt in a fresh Claude Code session with the Unity-MCP server connected.
Open the project in Unity first so the MCP bridge is live, then paste the entire
section below as a single message.

---

## Task

Hotbar inventory scripts are already written and committed in `Assets/03_Shooter/Scripts/Inventory/`. Your job is to wire them into the Editor: create the supporting ScriptableObjects, build the world/hand prefabs, modify the Player prefab, hook the database into GameManager, and scatter test pickups in the Shooter scene.

Use the `unity-mcp-skill` for tool/workflow patterns. Use `batch_execute` aggressively. After Editor mutations, run a `read_console(types=["error","warning"])` and a `manage_camera(action="screenshot", capture_source="scene_view", include_image=True)` so we can sanity-check visually.

**Authority rule (from project CLAUDE.md):** ask the user before any batch of mutations. Present the full plan below for a single approval, then execute it end-to-end. Do **not** ask per-step.

## Background — existing code you should not re-create

All under namespace `Starter.Shooter`:

| File | Type | Key API |
|---|---|---|
| `ItemDefinition.cs` | `ScriptableObject`, menu `Shooter/Item Definition` | `short Id`, `string DisplayName`, `Sprite Icon`, `GameObject WorldPrefab`, `GameObject HandPrefab`, `short MaxStack` |
| `ItemDatabase.cs` | `ScriptableObject`, menu `Shooter/Item Database` | `List<ItemDefinition> _items`, `static Instance`, `Bind()`, `GetById(short)` |
| `InventorySlot.cs` | `INetworkStruct` | `short ItemId; short Count;` (Id 0 = empty) |
| `Inventory.cs` | `NetworkBehaviour`, sibling of `Player` | `NetworkArray<InventorySlot> Slots` capacity 8, `int SelectedSlot`. Public field `Transform HandAnchor`. Input: 1–8 / scroll / E (pickup) / Q (drop). RPCs to state authority. |
| `PickupableItem.cs` | `NetworkBehaviour` | `[Networked] short ItemId/Count` + `[SerializeField] ItemDefinition _initialItem`, `[SerializeField, Min(1)] short _initialCount = 1`. Auto-applies `_initialItem` on `Spawned` for scene-placed pickups. |
| `ItemView.cs` | local-only `MonoBehaviour` | Optional `Bob`/`Rotate` flair |

`Assets/03_Shooter/Scripts/GameManager.cs` already has `public ItemDatabase ItemDatabase;` and an `Awake()` that calls `ItemDatabase.Bind()` — just needs the asset dragged in.

## Plan to present for approval (then execute)

### 1. Folders
- `Assets/03_Shooter/Items/`
- `Assets/03_Shooter/Prefabs/` (skip if already exists)

### 2. ScriptableObject assets in `Assets/03_Shooter/Items/`

| Asset | Type | Id | DisplayName | MaxStack |
|---|---|---|---|---|
| `ItemDatabase.asset` | `ItemDatabase` | — | — | — |
| `Wood.asset`   | `ItemDefinition` | 1 | Wood   | 99 |
| `Scrap.asset`  | `ItemDefinition` | 2 | Scrap  | 99 |
| `Medkit.asset` | `ItemDefinition` | 3 | Medkit | 5  |

(`Icon` left empty for now — UI is out of scope for this pass.)

### 3. World pickup prefabs in `Assets/03_Shooter/Prefabs/`

Build a template, then duplicate three times.

**`Pickup_Template.prefab`** (root structure):
- Root: empty GameObject
  - `NetworkObject` (Fusion)
  - `PickupableItem` (our component)
  - `BoxCollider`, size `(0.3, 0.3, 0.3)`, **not** a trigger
- Child: primitive Cube, local scale `(0.3, 0.3, 0.3)`
  - `ItemView` (Bob/Rotate left off)
  - Remove the cube's own collider to avoid duplicate hits

Duplicate into `Pickup_Wood`, `Pickup_Scrap`, `Pickup_Medkit`. On each, set the child cube material color (brown / grey / white) and assign the matching `ItemDefinition` to `PickupableItem._initialItem` (leave `_initialCount = 1`).

### 4. Hand-held prefabs in `Assets/03_Shooter/Prefabs/` (local-only — **no NetworkObject**)

Tiny non-networked variants for the held-item visual:
- `Hand_Wood.prefab`   — primitive Cube, scale `0.15`, brown
- `Hand_Scrap.prefab`  — primitive Cube, scale `0.15`, grey
- `Hand_Medkit.prefab` — primitive Cube, scale `0.15`, white

Remove their colliders.

### 5. Wire WorldPrefab + HandPrefab on each ItemDefinition

| ItemDefinition | WorldPrefab | HandPrefab |
|---|---|---|
| Wood   | `Pickup_Wood`   | `Hand_Wood`   |
| Scrap  | `Pickup_Scrap`  | `Hand_Scrap`  |
| Medkit | `Pickup_Medkit` | `Hand_Medkit` |

### 6. Register all 3 in `ItemDatabase._items`

### 7. Modify the Shooter Player prefab

Find it: it's the prefab assigned to `GameManager.PlayerPrefab` in the `03_Shooter` scene, or search for prefabs containing a `Starter.Shooter.Player` component.

- Add `Inventory` component to the prefab root (sibling of `Player`).
- Locate the `CameraHandle` transform (already referenced by the existing `Player` component — read its inspector to find which child it points to).
- Create a child empty `HandAnchor` **under that camera handle**, local position `(0.3, -0.2, 0.5)`.
- Drag `HandAnchor` into the new `Inventory.HandAnchor` field.

### 8. Hook `ItemDatabase` into `GameManager`

Open the Shooter scene (look it up via build settings — index 1, or search `*.unity` under `Assets/03_Shooter/`). Find the GameManager GameObject. Drag `Assets/03_Shooter/Items/ItemDatabase.asset` into its `ItemDatabase` field. Save the scene.

### 9. Scatter test pickups

Place one instance each of `Pickup_Wood`, `Pickup_Scrap`, `Pickup_Medkit` in the scene near a `SpawnPoint`. Spread them ~2m apart so `OverlapSphere(2m)` picks one at a time. Save the scene.

## Tooling order of operations

1. Read `mcpforunity://editor/state` and confirm `ready_for_tools` and no compile errors.
2. Read `mcpforunity://project/info` to check Render Pipeline (URP) so material creation uses the right shader.
3. Present the plan above to the user, get approval.
4. Execute mutations in batches (folders → assets → prefabs → wiring → scene). Use `batch_execute(parallel=True)` where the ops are independent.
5. Wait for any compilation triggered by serialization changes (`is_compiling == false`).
6. `read_console(types=["error","warning"], count=20)` — fix anything that surfaced.
7. `manage_camera(action="screenshot", capture_source="scene_view", view_target="<SpawnPoint name>", include_image=True, max_resolution=512)` to confirm the three pickup cubes are visible.

## Verification (manual, after this prompt completes)

User will press Play with `UIGameMenu.ForceSinglePlayer = true` and check:
1. Walking onto a cube + pressing `E` despawns the cube and no errors appear.
2. Pressing `1`/`2`/`3` swaps the small cube visible under the camera (HandAnchor).
3. Scroll wheel cycles the selected slot.
4. Pressing `Q` drops the current stack as a new world pickup in front of the player.
5. In the Fusion Network State window on the player's `Inventory`, `Slots[]` and `SelectedSlot` reflect actions.

If any step fails (missing field, compile error, null reference, prefab structure mismatch), report back with the exact console message and the prefab/scene path involved.
