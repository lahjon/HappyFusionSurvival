# CLAUDE.md

Guidance for Claude Code working in this repo.

## Project

Unity 6 (`6000.4.8f1`, URP) multiplayer game on the **Photon Fusion 2 Starter Kit**. Active goal: 1–4 player co-op survival roguelite (`04_Survival`) — see `prompt.md` for the phased design spec.

No README. `prompt.md` and `MEMORY.md` are the authoritative Survival design docs.

**Networked-first.** Default to Fusion 2 patterns (`NetworkBehaviour`, `[Networked]`, RPCs, `INetworkInput`, `TickTimer`) for anything touching gameplay state, player actions, or spawned objects. Plain `MonoBehaviour` only for local-only visuals (camera, UI, cosmetic FX) — call it out when you do. See "Networking conventions" below.

## Build / run

No CLI build. Open in Unity 6000.4.8f1:

- **Entry scene:** `Assets/00_MainMenu/00_MainMenu.unity` (build index 0)
- **Build settings:** `00_MainMenu` + `03_Shooter` enabled; `01_ThirdPersonCharacter` + `02_Platformer` registered but disabled; no `04_Survival` scene yet.
- **Multi-client testing:** Fusion Multiplay/ParrelSync. `UIGameMenu.ForceSinglePlayer` flips between `GameMode.Single` and `GameMode.AutoHostOrClient`.
- **Tests:** none configured. Don't claim tests pass; verify via the Editor.

`run-loop.ps1` is **not** a build/test script — it pipes `prompt.md` into the Claude CLI for overnight generation. Don't run it during normal dev.

## Architecture

### Starter Kit pattern — one folder per mode

Each mode is self-contained: `NN_Mode/` with its own scene, namespace, and three scripts:

```
Player.cs         — NetworkBehaviour, SimpleKCC movement + Render/FixedUpdateNetwork
PlayerInput.cs    — NetworkBehaviour, accumulates input in Update(), pushes via OnInput
GameManager.cs    — NetworkBehaviour + IPlayerJoined/IPlayerLeft, spawns Player prefabs
```

Namespaces: `Starter.<ModeName>` (e.g. `Starter.Shooter`, `Starter.Survival`).

### Shared `Assets/Common/`

- `Runner.prefab` — `NetworkRunner` + `NetworkEvents` + `NetworkSceneManagerDefault`. Instantiated by `UIGameMenu.StartGame()`.
- `UIGameMenu.cs` (`Starter`) — connect/disconnect, nickname (`PlayerPrefs["PlayerName"]`), cursor lock, scene reload on shutdown. **`GameModeIdentifier` is critical** — set as `SessionProperty` so modes don't share rooms.
- `UINameplate.cs` (`Starter`) — billboard nickname. Needs `using Starter;`.

### Networking conventions (Fusion 2)

- Only **State Authority** mutates `[Networked]` / `NetworkArray` / `NetworkDictionary`. Input flows via `INetworkInput` + `GetInput()` in `FixedUpdateNetwork()`.
- `Input.GetKeyDown` in `Update()` only; bridge to state authority via RPCs.
- `TickTimer`, not `Time.time`, for cooldowns.
- `OnChangedRender` instead of polling `[Networked]` values (see `Player.Nickname`, `Player._isJumping` in `03_Shooter`).
- Unity 6 API: `Rigidbody.linearVelocity`/`linearDamping` (not `velocity`/`drag`); `FindFirstObjectByType<T>()` / `FindObjectsByType<T>(FindObjectsSortMode.None)`.
- `Object.AssignInputAuthority()` / `RemoveInputAuthority()` for vehicle-style possession.

### Movement

All modes use **SimpleKCC** (`Fusion.Addons.SimpleKCC`). Don't write controllers from scratch — extend the mode's `Player.cs`. `Assets/03_Shooter/Scripts/Player.cs` is the canonical pattern:
- Hitscan + lag compensation (`Runner.LagCompensation.Raycast`)
- Predicted look rotation (`KCC.Settings.ForcePredictedLookRotation = true`)
- Dummy chest-bone IK
- Layer swap to `FirstPersonOverlay` for weapon-camera anti-clip

### Working-tree state

`git status` shows large deletions under `01_ThirdPersonCharacter/`, `02_Platformer/`, `04_Survival/` (Survival is being rebuilt — folder is empty). Check the disk, not `git ls-files`. `00_MainMenu/` and `03_Shooter/` are intact.

## Unity Editor via MCP

Project uses `com.coplaydev.unity-mcp`. Use MCP (and `unity-mcp-skill`) when a task requires Editor state — GameObjects, scenes, prefabs, Editor tests, live console. Prefer Read/Edit/Grep for pure script work.

**Ask before mutating Editor state.** Read-only calls (hierarchy, components, asset lists, console tail) are fine. Anything that creates/deletes/modifies GameObjects/components/prefabs/scenes/settings, enters Play mode, or triggers imports needs explicit approval first. Describe the change and wait. One approval = one change.

Other notes:
- UnityMCP can hang during compilation/domain reload — wait, don't retry.
- `coplay-mcp` requires `set_unity_project_root` first.
- Editor log: `C:/Users/fredr/AppData/Local/Unity/Editor/Editor.log`.

## Survival mode

`Starter.Survival` / `Assets/04_Survival/` — active build target. `prompt.md` defines 13 phases (0–12) with primitive visuals and `Debug.Log("[VERIFY] ... PASS ✓")` self-checks instead of tests. **Extend** the mode's `Player.cs` — don't create a new player prefab or movement script.
