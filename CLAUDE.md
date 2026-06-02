# CLAUDE.md

Guidance for Claude Code working in this repo.

## Project

Unity 6 (`6000.4.8f1`, URP) multiplayer FPS on the **Photon Fusion 2 Starter Kit**. **Round-based PvPvE inspired by The Purge × Stardew Valley** — cheerful co-op town life by day, last-team-standing PvP by night. Active scene: `03_Shooter`.

> The repo folder is still named `HappyFusionSurvival` for git history reasons. The project is **not** a survival game anymore — survival vocabulary (hunger, day-7 escape, scavenging-to-survive) is legacy and being removed. See `prompt.md` for the active migration plan.

### Match shape

- **Up to 18 players** per match (divides cleanly: 18 solo / 9 duo / 6 trio). Team size chosen at the lobby: **solo (1) / duo (2) / trio (3)** — one team size per match (no mixed sizes).
- **Day phase (~15 min) — "Town."** PvP off. Bright, happy-go-lucky pastel tone. Players run NPC quests, gather/buy/sell at vendors, craft loadouts at workbenches, fetch resources, prep for the night.
- **Night phase (~15 min, ends early on last-team-standing) — "The Purge."** PvP on. Same town, happy-horror flip: saturated colours, ominous lighting, vendors gone, doors shut. Match ends when only one team is alive, or timer expires (tiebreaker by team kills/score).
- **Friendly fire:** off within a team. **Currency:** earned and spent within the match only (no carry-over between matches).

Build new gameplay around this two-phase structure. Anything that changes between phases (vendor availability, damage rules, ambient music, lighting, AI behaviour) must read the phase from the networked match controller — never from local `Time.time`.

### What stays vs. what's out

- **Stays:** 8-slot hotbar inventory, crafting benches, vehicles, climbing/mantling, ragdoll, combat actions, stamina (movement mechanic).
- **Out (being removed):** hunger, food-as-survival-resource, any "day N of 7" escape framing, PvE night enemies (night is pure PvP). Existing hunger code in `Player.cs`/`UIShooter.cs`/`FoodConsumable.cs`/`Food.asset` is legacy — leave it alone unless explicitly working the pivot.

**Networked-first.** Default to Fusion 2 patterns (`NetworkBehaviour`, `[Networked]`, RPCs, `INetworkInput`, `TickTimer`) for anything touching gameplay state, player actions, or spawned objects. Plain `MonoBehaviour` only for local-only visuals/UI/input — call it out when you do.

**State replication is part of every feature.** Before writing a new gameplay system, state in your plan: (a) does this state need to be consistent across peers? (b) who is the authority? (c) which Fusion primitive carries it? If local-only, say so explicitly. Retrofitting replication onto a `MonoBehaviour` later is a rewrite.

**Phase-aware gameplay.** New systems that behave differently in day vs. night must read the phase from a single networked `MatchManager` (planned — see `prompt.md`), not from per-system timers. Damage-to-players, vendor visibility, and music/lighting all gate on the same source of truth.

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
- `Inventory/` — definitions, ops, loot containers, pickup, placement. An `ItemDefinition` is a single ScriptableObject that composes behavior from a `[SerializeReference] List<ItemCapability>` — add a `WeaponCapability`, `PlaceableCapability`, or `ConsumableCapability` (→ `HealingCapability`/`FoodCapability`/`RecipeUnlockCapability`) to give the item that facet. Facets compose freely (a weapon can also be a consumable); query with `def.GetCapability<T>()` / `TryGetCapability` / `HasCapability`. **Items are zero-prefab:** an item's look lives in `ItemDefinition.Visual` (`ItemVisual`: mesh/material/scale for world + in-hand) and a weapon's rig feel in `WeaponCapability.Rig` (`HeldRigTuning`: swing/recoil/sway/muzzle). Two shared prefabs render them — `Pickup_Generic` (its `PickupableItem` builds the Visual child from the item's `ItemVisual` at spawn, keyed off the networked `ItemId`) and `Hand_Generic` (its `HeldWeapon` rig calls `Configure(visual, weapon)` at equip). So a new ordinary item = **one asset, no prefabs**. The `WorldPrefab`/`HandPrefab` fields are optional bespoke overrides for real-art or special items (Wrench, Crate furniture, Scanner radar device, Money, fists) and an `ItemVisual.VisualOverride` swaps in a model prefab instead of a primitive. Item ids are a stable network key (`short`); validate/auto-assign them with the `Tools/Inventory/Validate Item Database` menu.
- `Crafting/` — `RecipeDefinition` SOs, `RecipeDatabase`, `IRecipeBook`.

### `Assets/03_Shooter/` (active gameplay)

- **Player systems** on `Player.cs`: SimpleKCC movement, stamina (drains on sprint/jump/climb), climbing/mantling with wall-leap, ragdoll on heavy knockback, head-bob, sprint FOV, camera collision sweep. (Hunger fields are still present in code but marked legacy — see top of file.)
- **Combat** via `Actions/` ScriptableObjects (`CombatAction` base → `HitscanAction`, `OverlapAction`, `ProjectileAction`). Resolved by per-actor `ActionInvoker` (cooldown + charge tick are `[Networked]`). A held item's actions come from its `WeaponCapability` (empty-hand/fists from `Inventory._unarmedItem`); `Inventory.ActiveAction` is the single source the fire path reads. Same primitive serves players, training dummies, fists.
- **Inventory** — 8-slot networked hotbar (`Inventory.cs`, `NetworkArray<InventorySlot>`). Weight over `WeightLimit` slows movement. Large items only exist while equipped. `PlacementController` (local-only) drives ghost-preview for items carrying a `PlaceableCapability`.
- **Crafting** — `CraftingBench` (IInteractable) hands off to local `CraftingSession` UI; recipes filtered per-bench.
- **Vehicles** — `Vehicle` + `Seat` + per-player `VehicleSession`. Driver gets input authority transferred via `Object.AssignInputAuthority()`; arcade-style tank turning, `NetworkTransform`-replicated, host-only dynamic Rigidbody (clients kinematic).
- **AI / world** — `Chicken` + `ChickenSpawner`, `TrainingDummy` (uses ActionInvoker to swing back), `LootContainer`. Day-phase ambient only — there are no PvE enemies during the Purge.

### Match flow (planned)

Not yet implemented — track here so new systems plug into the right contract:

- `MatchManager` (`NetworkBehaviour`, singleton on a scene object): `[Networked] MatchPhase Phase`, `[Networked] TickTimer PhaseTimer`, `[Networked] int RoundIndex`. State authority advances `Lobby → Day → DuskWarning (~30s) → Night → MatchOver`.
- `TeamManager` (`NetworkBehaviour`): per-`PlayerRef` team id assigned at lobby based on chosen team size; drives friendly-fire checks and win-condition scan.
- Damage gate: `Health.ApplyDamage` consults `MatchManager.Phase` and `TeamManager.SameTeam(attacker, victim)`. Day phase + same team → blocked.
- Vendors / quest givers / shops are `IInteractable` `NetworkBehaviour`s that self-disable (`CanInteract = false`, despawn prompt) when `Phase == Night`.
- Win condition: state authority watches teams-with-living-members; reaching 1 (or 0) flips `Phase = MatchOver` and shows result. Night timer expiry is the tiebreaker.

### Menu / UI systems convention

Every menu-driving component (shop, quest board, dialogue, crafting, etc.) **must expose a public `OpenMenu()` method** — zero arguments, callable from a UnityEvent, trigger collider, animator event, or any external script without needing a direct typed reference to the session.

The pattern used by `Shopkeeper` and `QuestGiver`:
```csharp
public void OpenMenu()
{
    var playerObj = Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;
    var session   = playerObj != null
        ? playerObj.GetComponent<TSession>()
        : FindFirstObjectByType<TSession>();
    session?.TryOpen(this);
}
```
- Resolves the local player's session via Fusion when networked; falls back to `FindFirstObjectByType` for offline/editor use.
- The matching session component (`ShopSession`, `QuestSession`, etc.) lives on the Player prefab and is initialized by `PlayerInput.cs` on spawn.
- `RequestClose()` / `CloseMenu()` should follow the same pattern so external triggers can close menus too.

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
- Unity 6 API: `Rigidbody.linearVelocity`/`linearDamping` (not `velocity`/`drag`); `FindAnyObjectByType<T>()` / `FindObjectsByType<T>(FindObjectsSortMode.None)` (`FindFirstObjectByType` is deprecated — relies on instance-id ordering).

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
