# CLAUDE.md

Guidance for Claude Code working in this repo.

## Project

Unity 6 (`6000.4.8f1`, URP) multiplayer game on the **Photon Fusion 2 Starter Kit**. Co-op FPS with hotbar inventory, crafting, vehicles, climbing, ragdoll, hunger/stamina. Active scene: `03_Shooter`.

**Networked-first.** Default to Fusion 2 patterns (`NetworkBehaviour`, `[Networked]`, RPCs, `INetworkInput`, `TickTimer`) for anything touching gameplay state, player actions, or spawned objects. Plain `MonoBehaviour` only for local-only visuals/UI/input — call it out when you do.

**State replication is part of every feature.** Before writing a new gameplay system, state in your plan: (a) does this state need to be consistent across peers? (b) who is the authority? (c) which Fusion primitive carries it? If local-only, say so explicitly. Retrofitting replication onto a `MonoBehaviour` later is a rewrite.

## Build / run

No CLI build. Open in Unity 6000.4.8f1.

- **Build settings:** `00_MainMenu` (index 0) + `03_Shooter`. Entry scene is the menu.
- **Multi-client testing:** Fusion Multiplay/ParrelSync. `UIGameMenu.ForceSinglePlayer` flips `GameMode.Single` ↔ `GameMode.AutoHostOrClient`.
- **Tests:** none configured. Don't claim tests pass; verify via the Editor.
- `run-loop.ps1` pipes `prompt.md` into the Claude CLI for overnight generation — not a build/test script.

## Architecture

### Starter Kit pattern

Each mode is self-contained: `NN_Mode/` with its own scene, namespace `Starter.<Mode>`, and three core scripts:

```
Player.cs         — NetworkBehaviour, SimpleKCC movement + Render/FixedUpdateNetwork
PlayerInput.cs    — NetworkBehaviour, accumulates input in Update(), pushes via OnInput
GameManager.cs    — NetworkBehaviour + IPlayerJoined/IPlayerLeft, spawns Player prefabs
```

### `Assets/Common/` (shared)

- `Runner.prefab` — `NetworkRunner` + `NetworkEvents` + `NetworkSceneManagerDefault`. Instantiated by `UIGameMenu.StartGame()`.
- `UIGameMenu.cs` — connect/disconnect, nickname (`PlayerPrefs["PlayerName"]`), cursor lock, scene reload on shutdown. **`GameModeIdentifier` is critical** — set as `SessionProperty` so modes don't share rooms.
- `UINameplate.cs` (`Starter` namespace — needs `using Starter;`).
- `Input/` — `GameInputActions` (local `InputActionAsset` wrapper, lives on player root) and `InputContextController` (toggles Player/Inventory action maps + cursor). **Don't use legacy `Input.*`** — read actions via `GetComponent<GameInputActions>()`.
- `Interactions/` — see below.
- `Inventory/` — definitions, ops, loot containers, pickup, placement. `ItemDefinition` ScriptableObjects with optional `ConsumableDefinition` subclass (e.g. food/healing/recipe-unlock).
- `Crafting/` — `RecipeDefinition` SOs, `RecipeDatabase`, `IRecipeBook`.

### `Assets/03_Shooter/` (active gameplay)

- **Player systems** on `Player.cs`: SimpleKCC movement, stamina (drains on sprint/jump/climb), hunger (caps stamina), climbing/mantling with wall-leap, ragdoll on heavy knockback, head-bob, sprint FOV, camera collision sweep.
- **Combat** via `Actions/` ScriptableObjects (`CombatAction` base → `HitscanAction`, `OverlapAction`). Resolved by per-actor `ActionInvoker` (cooldown + charge tick are `[Networked]`). Same primitive serves players, training dummies, fists.
- **Inventory** — 8-slot networked hotbar (`Inventory.cs`, `NetworkArray<InventorySlot>`). Weight over `WeightLimit` slows movement. Large items only exist while equipped. `PlacementController` (local-only) drives ghost-preview for `PlaceableDefinition` items.
- **Crafting** — `CraftingBench` (IInteractable) hands off to local `CraftingSession` UI; recipes filtered per-bench.
- **Vehicles** — `Vehicle` + `Seat` + per-player `VehicleSession`. Driver gets input authority transferred via `Object.AssignInputAuthority()`; arcade-style tank turning, `NetworkTransform`-replicated, host-only dynamic Rigidbody (clients kinematic).
- **AI / world** — `Chicken` + `ChickenSpawner`, `TrainingDummy` (uses ActionInvoker to swing back), `LootContainer`.

### Interaction system (shared, mandatory)

Anything the player can "use" (chests, pickups, doors, benches, vehicles, NPCs) **must** plug into `Assets/Common/Interactions/`.

- `IInteractable` — `InteractRange`, `CanInteract`, `InteractionPoint`, `LockedReason`, `OnInteract(InteractionScanner)`. Implement on the `NetworkBehaviour`.
- `InteractionScanner` (local, on player) — picks best in-range, in-view-cone candidate per frame; routes Interact action to its `OnInteract`. Reads `IInteractionGate` siblings (e.g. `LootSession`) to suppress scanning while a UI panel is open.
- `InteractionPrompt` (local, on every interactable's prefab) — camera-facing world-space indicator. Standardized size/colors — don't tweak per-prefab.
- Authority pattern: `OnInteract` routes into a per-player networked session via `scanner.GetComponent<TSession>()`. Canonical example: `LootContainer.OnInteract` → `LootSession.TryOpen`.
- Re-validate range on the host in any RPC the interaction kicks off (convention: `InteractRange * 1.25f`) — never trust the client scan alone.

### Movement

All modes use **SimpleKCC** (`Fusion.Addons.SimpleKCC`). Don't write controllers from scratch — extend the mode's `Player.cs`. Canonical patterns in `03_Shooter/Player.cs`:
- Hitscan + lag compensation (`Runner.LagCompensation.Raycast`)
- Predicted look rotation (`KCC.Settings.ForcePredictedLookRotation = true`)
- Layer swap to `FirstPersonOverlay` for weapon-camera anti-clip

## Fusion 2 conventions

- Only **state authority** mutates `[Networked]` / `NetworkArray` / `NetworkDictionary`. Input flows via `INetworkInput` + `GetInput()` in `FixedUpdateNetwork()`.
- Local input (`InputAction.WasPressedThisFrame()`) is read in `Update()` only; bridge to state authority via RPCs or input-struct buttons.
- `TickTimer`, not `Time.time`, for cooldowns/durations.
- `OnChangedRender` over polling `[Networked]` values (see `Player.Nickname`, `_isJumping`, `_fireCount`).
- `Object.AssignInputAuthority()` / `RemoveInputAuthority()` for vehicle-style possession.
- Unity 6 API: `Rigidbody.linearVelocity`/`linearDamping` (not `velocity`/`drag`); `FindFirstObjectByType<T>()` / `FindObjectsByType<T>(FindObjectsSortMode.None)`.

### Picking the primitive

- Per-player gameplay value (health, stamina, ammo): `[Networked]` on player NB, mutate in `FixedUpdateNetwork` on state auth.
- Per-player cooldown/duration: `[Networked] TickTimer`.
- Per-player input intent: field in the `INetworkInput` struct + `NetworkButtons` flag.
- One-shot cross-authority effect (damage, request): `[Rpc]` with explicit `RpcSources` / `RpcTargets`.
- One-shot visual all peers must see synced (muzzle flash): `[Networked]` counter + `OnChangedRender` — tolerates lost ticks better than RPC-to-All.
- Shared world object (loot, chicken, vehicle): `NetworkObject` spawned via `Runner.Spawn`/`Despawn`. Never `Instantiate`/`Destroy` directly.
- Synced variable-length list: `NetworkArray<T>` (`[Capacity(N)]`) or `NetworkDictionary<K,V>`. Only state auth writes.
- Cosmetic local view (camera shake, bob, footstep): plain `MonoBehaviour`, `Time.deltaTime`.
- Local UX state (cursor, menu open, scroll position): plain `MonoBehaviour`.

If UI reflects networked state (stamina bar, hotbar, ammo), the data field is `[Networked]` on the gameplay component; the UI is a `MonoBehaviour` that reads it each frame. Don't put `[Networked]` on UI scripts.

## Unity Editor via MCP

Project uses `com.coplaydev.unity-mcp`. Use MCP (and the `unity-mcp-skill`) when a task requires Editor state — GameObjects, scenes, prefabs, Editor tests, live console. Prefer Read/Edit/Grep for pure script work.

**Ask before mutating Editor state.** Read-only calls (hierarchy, components, asset lists, console tail) are fine. Anything that creates/deletes/modifies GameObjects/components/prefabs/scenes/settings, enters Play mode, or triggers imports needs explicit approval. Describe the change and wait. One approval = one change.

- UnityMCP can hang during compilation/domain reload — wait, don't retry.
- Editor log: `C:/Users/fredr/AppData/Local/Unity/Editor/Editor.log`.
