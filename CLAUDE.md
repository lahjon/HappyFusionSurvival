# CLAUDE.md

Guidance for Claude Code working in this repo.

## Project

Unity 6 (`6000.4.8f1`, URP) multiplayer FPS on the **Photon Fusion 2 Starter Kit**. **Round-based PvPvE, The Purge × Stardew Valley** — co-op town by day, last-team-standing PvP by night. Active scene: `HappyTown` (`Assets/Scenes/HappyTown.unity`) — the main gameplay scene, successor to the legacy `03_Shooter` scene.

> Repo folder name `HappyFusionSurvival` is legacy git history. **Not** a survival game — hunger / day-7 escape / scavenge-to-survive vocabulary is being removed. See `prompt.md` for the migration plan.

### Match shape

- **Up to 18 players.** Team size picked at lobby: solo (1) / duo (2) / trio (3), one size per match.
- **Day (~15 min) "Town":** PvP off, bright pastel. Quests, vendors, crafting, gathering, prep.
- **Night (~15 min, ends early on last-team-standing) "The Purge":** PvP on, same town flipped to happy-horror. Vendors gone, doors shut. Ends when one team remains, or timer expires (tiebreaker: team kills/score).
- Friendly fire off within a team. Currency is per-match only (no carry-over).

Anything that changes between phases (vendors, damage rules, music, lighting, AI) reads phase from the networked match controller — never local `Time.time`.

### Stays vs. out

- **Stays:** 8-slot hotbar, crafting benches, vehicles, climbing/mantling, ragdoll, combat actions, stamina.
- **Out:** hunger, food-as-survival, "day N of 7" framing, PvE night enemies (night is pure PvP). Legacy hunger code in `Player.cs`/`UIShooter.cs`/`FoodConsumable.cs`/`Food.asset` — leave alone unless explicitly doing the pivot.

### Core principles

- **Networked-first.** Default to Fusion 2 (`NetworkBehaviour`, `[Networked]`, RPCs, `INetworkInput`, `TickTimer`) for any gameplay state, player action, or spawned object. Plain `MonoBehaviour` only for local visuals/UI/input — and call it out.
- **Plan replication up front.** For each new gameplay system state: (a) consistent across peers? (b) who's authority? (c) which Fusion primitive? Say so explicitly if local-only. Retrofitting later is a rewrite.
- **Phase-aware.** Day-vs-night behavior reads phase from the single networked `MatchManager` (planned, see `prompt.md`), not per-system timers.
- **Reuse before adding.** Search for an existing system before writing new. Shared primitives are deliberate (interaction system, `ActionInvoker`/`CombatAction`, `ItemCapability` facets, SimpleKCC `Player.cs`, inventory ops) — plug in, don't reimplement. If you add parallel code, justify why reuse failed.
- **Debug commands use Quantum Console** (`Assets/Plugins/QFSW/`). Annotate static methods `[Command("name","description")]` (see `Assets/Scripts/Debug/DebugCommands.cs`). No ad-hoc debug keys, IMGUI consoles, or `Debug.Log` cheats. Commands touching networked state route via RPCs (pattern in `DebugCommands.cs`).

## Build / run

No CLI build. Open in Unity 6000.4.8f1.

- **Build settings:** `00_MainMenu` (index 0, entry) + `HappyTown` (index 1, main gameplay scene).
- **Multi-client:** Fusion Multiplay/ParrelSync. `UIGameMenu.ForceSinglePlayer` flips `GameMode.Single` ↔ `GameMode.AutoHostOrClient`.
- **Tests:** none configured. Don't claim tests pass; verify via the Editor.
- `run-loop.ps1` pipes `prompt.md` into the Claude CLI for overnight generation — not a build script.

## Architecture

### Starter Kit pattern

Each mode is self-contained: `NN_Mode/`, namespace `Starter.<Mode>`, three core scripts:
```
Player.cs       — NetworkBehaviour, SimpleKCC movement + Render/FixedUpdateNetwork
PlayerInput.cs  — NetworkBehaviour, accumulates input in Update(), pushes via OnInput
GameManager.cs  — NetworkBehaviour + IPlayerJoined/IPlayerLeft, spawns Player prefabs
```

### `Assets/Common/` (shared)

- `Runner.prefab` — `NetworkRunner` + `NetworkEvents` + `NetworkSceneManagerDefault`. Instantiated by `UIGameMenu.StartGame()`.
- `UIGameMenu.cs` — connect/disconnect, nickname (`PlayerPrefs["PlayerName"]`), cursor lock, scene reload on shutdown. **`GameModeIdentifier`** set as `SessionProperty` so modes don't share rooms.
- `UINameplate.cs` (`Starter` namespace).
- `Input/` — `GameInputActions` (local `InputActionAsset` wrapper on player root) + `InputContextController` (toggles Player/Inventory maps + cursor). **No legacy `Input.*`** — read via `GetComponent<GameInputActions>()`.
- `Inventory/` — An `ItemDefinition` is one ScriptableObject composing behavior from `[SerializeReference] List<ItemCapability>` — add `WeaponCapability` / `PlaceableCapability` / `ConsumableCapability` (→ `HealingCapability`/`FoodCapability`/`RecipeUnlockCapability`). Facets compose freely; query via `GetCapability<T>()`/`TryGetCapability`/`HasCapability`. **Items have no per-item world/hand prefab — two shared prefabs render them all** (`Pickup_Generic`, `Hand_Generic`). An item's look is a single visual-only `Prefab` on `ItemDefinition.Visual` (`ItemVisual`: a `Prefab` + world/held scale & offset fields); weapon feel lives in `WeaponCapability.Rig` (`HeldRigTuning`). `Pickup_Generic` (`PickupableItem`, keyed off networked `ItemId`) and `Hand_Generic` (`HeldWeapon.Configure(visual, weapon)` at equip) **instantiate that `Prefab`** as the model, sized/offset by the `ItemVisual` fields. If `Visual.Prefab` is null the generic rig falls back to a placeholder cube. New ordinary item = **one item asset + one visual-only prefab** (generated visual prefabs live in `Assets/Items/Visuals/`; they're plain `MeshFilter`+`MeshRenderer`, no gameplay/network components). `WorldPrefab`/`HandPrefab` are a different thing — optional bespoke prefabs that **replace** the generic prefab entirely when an item needs real components in that context (Wrench, Crate, Pot, Money, fists), not just a model. Gadget devices (Scanner) use the generic hand path and attach their behavior via `GadgetCapability.CreateRuntime` at equip — no bespoke `HandPrefab`. Item ids are a stable `short` network key — validate/auto-assign via `Tools/Inventory/Validate Item Database`. **Every new `ItemDefinition` asset MUST be added to `ItemDatabase.asset`'s `_items` list** — the database is the id→item lookup the network spawn path uses, so an unregistered item can't be resolved by `ItemId` and won't spawn/equip. After adding an item, register it there (and run the validator).
- `Crafting/` — `RecipeDefinition` SOs, `RecipeDatabase`, `IRecipeBook`.

### `Assets/03_Shooter/` (active gameplay)

- **Player** (`Assets/Scripts/Player.cs`): SimpleKCC movement, stamina (sprint/jump/climb drain), climbing/mantling + wall-leap, ragdoll on heavy knockback, head-bob, sprint FOV, camera collision sweep. (Hunger fields present but legacy.) Prefab: `Assets/Scenes/03_Shooter/Prefabs/Player.prefab`.
- **Combat** — `Actions/` SOs (`CombatAction` → `HitscanAction`/`OverlapAction`/`ProjectileAction`), resolved by per-actor `ActionInvoker` (cooldown + charge tick `[Networked]`). Held item's actions come from its `WeaponCapability` (fists from `Inventory._unarmedItem`); `Inventory.ActiveAction` is the single source the fire path reads. Same primitive for players, dummies, fists.
- **Inventory** — 8-slot networked hotbar (`Inventory.cs`, `NetworkArray<InventorySlot>`). Weight over `WeightLimit` slows movement. Large items exist only while equipped. `PlacementController` (local) drives ghost-preview for `PlaceableCapability` items.
- **Crafting** — `CraftingBench` (IInteractable) → local `CraftingSession` UI; recipes filtered per-bench.
- **Vehicles** — `Vehicle` + `Seat` + per-player `VehicleSession`. Driver gets input authority via `Object.AssignInputAuthority()`; arcade tank turning, `NetworkTransform`-replicated, host-only dynamic Rigidbody (clients kinematic).
- **AI / world** — `Chicken`+`ChickenSpawner`, `TrainingDummy` (ActionInvoker to swing back), `LootContainer`. Day-phase ambient only; no PvE during the Purge.

### Match flow (planned, not yet built)

- `MatchManager` (`NetworkBehaviour`, scene singleton): `[Networked] MatchPhase Phase`, `[Networked] TickTimer PhaseTimer`, `[Networked] int RoundIndex`. State auth advances `Lobby → Day → DuskWarning (~30s) → Night → MatchOver`.
- `TeamManager` (`NetworkBehaviour`): per-`PlayerRef` team id from lobby team size; drives friendly-fire + win-condition scan.
- Damage gate: `Health.ApplyDamage` consults `MatchManager.Phase` + `TeamManager.SameTeam(attacker, victim)`. Day + same team → blocked.
- Vendors/quest givers/shops (`IInteractable` `NetworkBehaviour`) self-disable (`CanInteract = false`) when `Phase == Night`.
- Win: state auth watches teams-with-living-members; reaching 1 (or 0) → `Phase = MatchOver`. Night timer expiry is tiebreaker.

### Menu / UI systems convention

Every menu-driving component (shop, quest board, dialogue, crafting) **must expose public `OpenMenu()`** — zero args, callable from UnityEvent / trigger / animator event without a typed session reference. Pattern (`Shopkeeper`, `QuestGiver`):
```csharp
public void OpenMenu()
{
    var playerObj = Runner != null ? Runner.GetPlayerObject(Runner.LocalPlayer) : null;
    var session   = playerObj != null
        ? playerObj.GetComponent<TSession>()
        : FindAnyObjectByType<TSession>();
    session?.TryOpen(this);
}
```
Resolves local player's session via Fusion, falls back to `FindAnyObjectByType` offline (`FindFirstObjectByType` is deprecated in Unity 6). Session component (`ShopSession`, `QuestSession`) lives on the Player prefab, initialized by `PlayerInput.cs` on spawn. `RequestClose()`/`CloseMenu()` follow the same pattern.

### Interaction system (shared, mandatory)

Anything usable (chests, pickups, doors, benches, vehicles, NPCs) **must** plug into `Assets/Common/Interactions/`.

- `IInteractable` — `InteractRange`, `CanInteract`, `InteractionPoint`, `LockedReason`, `OnInteract(InteractionScanner)`. On the `NetworkBehaviour`.
- `InteractionScanner` (local, player) — picks best in-range, in-cone candidate; routes Interact to `OnInteract`. Reads `IInteractionGate` siblings (e.g. `LootSession`) to suppress scanning while a panel is open.
- `InteractionPrompt` (local, every interactable prefab) — camera-facing indicator, standardized size/colors, don't tweak per-prefab.
- Authority: `OnInteract` routes into a per-player networked session via `scanner.GetComponent<TSession>()` (e.g. `LootContainer.OnInteract` → `LootSession.TryOpen`).
- Re-validate range on host in any RPC (convention `InteractRange * 1.25f`) — never trust client scan alone.

### Menu / Escape system (shared, mandatory)

Every openable local UI (pause, loot/crafting/quest/shop/computer, sleep, lobby/result, new ones) **must** register on the global menu stack in `Assets/Common/Menu/` — the single owner of Escape. No script reads `Keyboard.escapeKey` itself.

- `MenuManager` (local singleton, auto-bootstrap `AfterSceneLoad`, `DontDestroyOnLoad`, no wiring) holds a `List<IMenuScreen>` stack. `Open`/`Close` push/pop; `IsAnyOpen`/`Top` query.
- **Escape (only handler):** ① if `QuantumConsole.Instance.IsActive`, `Deactivate()` it; ② else top screen handles — closes if `DismissOnEscape`, else swallows; ③ else raise `OpenPauseRequested`. **Enter is never read.**
- `IMenuScreen`: `MenuName`, `DismissOnEscape` (`false` = modal: Escape swallowed, dismisses via own logic), `CloseFromMenu()` (idempotent).
- Screen calls `MenuManager.Instance?.Open(this)`/`Close(this)` (and in `OnDestroy`/`OnDisable` if still open). Sessions still set `IsAny*` flags — those gate gameplay (camera, `Player.LateUpdate`), separate from the stack.
- `UIGameMenu` is the **root screen**: opens only via `OpenPauseRequested`, owns gameplay cursor-lock baseline gated on `MenuManager.IsAnyOpen`.

### Movement

All modes use **SimpleKCC** (`Fusion.Addons.SimpleKCC`). Extend the mode's `Player.cs`, don't write controllers from scratch. Canonical patterns in `Assets/Scripts/Player.cs`: hitscan + lag comp (`Runner.LagCompensation.Raycast`); predicted look (`KCC.Settings.ForcePredictedLookRotation = true`); layer swap to `FirstPersonOverlay` for weapon anti-clip.

## Fusion 2 conventions

- Only **state authority** mutates `[Networked]`/`NetworkArray`/`NetworkDictionary`. Input flows via `INetworkInput` + `GetInput()` in `FixedUpdateNetwork()`.
- Local input (`WasPressedThisFrame()`) read in `Update()` only; bridge to state auth via RPCs or input-struct buttons.
- `TickTimer` not `Time.time` for cooldowns/durations.
- `OnChangedRender` over polling `[Networked]` (see `Player.Nickname`, `_isJumping`, `_fireCount`).
- `Object.AssignInputAuthority()`/`RemoveInputAuthority()` for vehicle-style possession.
- Unity 6 API: `Rigidbody.linearVelocity`/`linearDamping`; `FindAnyObjectByType<T>()`/`FindObjectsByType<T>(FindObjectsSortMode.None)` (`FindFirstObjectByType` deprecated).

### Picking the primitive

- Per-player value (health, stamina, ammo): `[Networked]` on player NB, mutate in `FixedUpdateNetwork` on state auth.
- Per-player cooldown/duration: `[Networked] TickTimer`.
- Per-player input intent: field in `INetworkInput` struct + `NetworkButtons` flag.
- One-shot cross-authority effect (damage, request): `[Rpc]` with explicit `RpcSources`/`RpcTargets`.
- One-shot synced visual (muzzle flash): `[Networked]` counter + `OnChangedRender` (tolerates lost ticks better than RPC-to-All).
- Shared world object (loot, chicken, vehicle): `NetworkObject` via `Runner.Spawn`/`Despawn`. Never `Instantiate`/`Destroy`.
- Synced list: `NetworkArray<T>` (`[Capacity(N)]`) or `NetworkDictionary<K,V>`, state-auth writes only.
- Cosmetic local view (shake, bob, footstep): plain `MonoBehaviour`, `Time.deltaTime`.
- Local UX state (cursor, menu open, scroll): plain `MonoBehaviour`.

UI reflecting networked state (stamina bar, hotbar, ammo): data field is `[Networked]` on the gameplay component; UI is a `MonoBehaviour` reading it each frame. Never `[Networked]` on UI scripts.

## Unity Editor via MCP

Project uses `com.coplaydev.unity-mcp`. Use MCP (and `unity-mcp-skill`) when a task needs Editor state — GameObjects, scenes, prefabs, Editor tests, live console. Prefer Read/Edit/Grep for pure script work.

**Ask before mutating Editor state.** Read-only calls (hierarchy, components, asset lists, console tail) are fine. Creating/deleting/modifying GameObjects/components/prefabs/scenes/settings, entering Play mode, or triggering imports needs explicit approval — describe the change and wait. One approval = one change.

- UnityMCP can hang during compilation/domain reload — wait, don't retry.
- Editor log: `C:/Users/fredr/AppData/Local/Unity/Editor/Editor.log`.
