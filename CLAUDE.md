# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity 6 (`6000.4.8f1`) multiplayer game built on the **Photon Fusion 2 Starter Kit**. The active development goal is a 1–4 player co-op survival roguelite (`04_Survival`) — see `prompt.md` for the full design + phased implementation spec the project was originally bootstrapped against.

There is no README. Treat `prompt.md` and `MEMORY.md` as the authoritative design docs for the Survival mode.

**This is a networked game.** Every gameplay system must be designed multiplayer-first. Default to Fusion 2 networked patterns (`NetworkBehaviour`, `[Networked]` state, RPCs, `INetworkInput`, `TickTimer`) whenever a feature touches gameplay state, player actions, spawned objects, or anything other clients need to see. Only fall back to plain `MonoBehaviour` / local-only logic for purely client-side visuals (camera, UI, cosmetic FX) — and call that out explicitly when you do. See the "Networking conventions (Fusion 2)" section below for the specific patterns to follow.

## Build / run

This is a Unity project — there is no CLI build. Open in Unity 6000.4.8f1 (URP) and use the Editor:

- **Entry scene:** `Assets/00_MainMenu/00_MainMenu.unity` (build index 0)
- **Active gameplay scenes** in build settings: `00_MainMenu` (enabled) and `03_Shooter` (enabled). `01_ThirdPersonCharacter` and `02_Platformer` are registered but disabled. There is no `04_Survival` scene in build settings yet.
- **Multi-client testing:** the project relies on Fusion's Multiplay/ParrelSync workflow. `UIGameMenu.ForceSinglePlayer` (Inspector) flips between `GameMode.Single` (fast Editor iteration) and `GameMode.AutoHostOrClient`.
- **Tests:** no test framework configured — there's no `Tests~` folder and nothing under `com.unity.test-framework` is wired up. Don't claim tests pass; verify via the Editor.

`run-loop.ps1` is **not** a build/test script — it pipes `prompt.md` into the Claude CLI repeatedly for unattended overnight code generation. Don't run it as part of normal development.

## Architecture

### Starter Kit pattern — one folder per game mode

Each gameplay mode is self-contained: `NN_Mode/` with its own scene, namespace, and three core scripts:

```
Player.cs         — NetworkBehaviour, owns SimpleKCC movement + Render/FixedUpdateNetwork
PlayerInput.cs    — NetworkBehaviour, accumulates input in Update(), pushes via OnInput callback
GameManager.cs    — NetworkBehaviour + IPlayerJoined/IPlayerLeft, spawns/despawns Player prefabs
```

Namespaces follow `Starter.<ModeName>` (e.g. `Starter.Shooter`, `Starter.ThirdPersonCharacter`). The `04_Survival` mode uses `Starter.Survival`.

### Shared `Assets/Common/`

- `Runner.prefab` — has `NetworkRunner` + `NetworkEvents` + `NetworkSceneManagerDefault`. Instantiated by `UIGameMenu.StartGame()`.
- `UIGameMenu.cs` (namespace `Starter`) — handles connect/disconnect, nickname persistence (`PlayerPrefs["PlayerName"]`), cursor lock, and reloads the current scene on shutdown. The **`GameModeIdentifier` field is critical for matchmaking** — it's set as a `SessionProperty` so players from different modes don't end up in the same room.
- `UINameplate.cs` (namespace `Starter`) — billboard-style nickname above players. When referencing it from a mode, add `using Starter;`.

### Networking conventions (Fusion 2)

- Only **State Authority** mutates `[Networked]` fields and `NetworkArray`/`NetworkDictionary` entries. Input flows via `INetworkInput` structs and `GetInput()` inside `FixedUpdateNetwork()`.
- Use `Input.GetKeyDown` in `Update()`, never in `FixedUpdateNetwork()` — bridge to state authority via RPCs.
- Use `TickTimer`, not `Time.time`, for network-safe cooldowns.
- Use `OnChangedRender` (e.g. `Player.Nickname`, `Player._isJumping` in `03_Shooter`) instead of polling `[Networked]` values for visual reactions.
- Unity 6 API: `Rigidbody.linearVelocity`/`linearDamping` (not `velocity`/`drag`); `FindFirstObjectByType<T>()` / `FindObjectsByType<T>(FindObjectsSortMode.None)` (not the deprecated `FindObjectOfType`).
- Use `Object.AssignInputAuthority()` / `Object.RemoveInputAuthority()` for vehicle-style possession.

### Movement

All modes use **SimpleKCC** (`Fusion.Addons.SimpleKCC`). Don't write character controllers from scratch — extend the existing `Player.cs` of the mode you're in. Look at `Assets/03_Shooter/Scripts/Player.cs` for the canonical pattern of:
- Hitscan + lag compensation (`Runner.LagCompensation.Raycast`)
- Predicted look rotation for the local player (`KCC.Settings.ForcePredictedLookRotation = true`)
- Dummy chest-bone IK
- Layer swap to `FirstPersonOverlay` for weapon-camera anti-clip

### Current working-tree state

`git status` shows large deletions under `Assets/01_ThirdPersonCharacter/`, `Assets/02_Platformer/`, and `Assets/04_Survival/` (the Survival mode is being rebuilt from scratch — `04_Survival/` contains only empty folders). Before assuming a mode "exists," check the disk, not just `git ls-files`. `00_MainMenu/` and `03_Shooter/` are intact.

## Working in the Unity Editor via MCP

The project depends on `com.coplaydev.unity-mcp` (MCP for Unity). Use the Unity MCP tools (and the `unity-mcp-skill`) when a task genuinely requires Editor state — inspecting/creating GameObjects, editing scenes, managing prefabs, running Editor tests, reading the live console. Prefer file-level Read/Edit/Grep for pure script work; reach for MCP only when the Editor is the source of truth.

**Always ask before making changes in Unity.** Read-only MCP calls (querying scene hierarchy, reading components, listing assets, tailing the console) are fine without confirmation. Any call that **mutates Editor state** — creating/deleting GameObjects, modifying components or prefabs, editing scenes, changing project settings, entering/exiting Play mode, triggering imports — requires explicit user approval first. Describe what you're about to do, then wait. A single approval covers the specific change discussed, not follow-ups.

Other notes:
- UnityMCP can become unresponsive during compilation/domain reload — wait, don't retry.
- `coplay-mcp` requires `set_unity_project_root` before any other call.
- Editor log: `C:/Users/fredr/AppData/Local/Unity/Editor/Editor.log`.

## Survival mode notes

The Survival mode (`Starter.Survival`, `Assets/04_Survival/`) is the active build target. `prompt.md` defines 13 phases (0–12) with primitive visuals (cubes/spheres) and **`Debug.Log("[VERIFY] ... PASS ✓")`-style** self-checks instead of automated tests. When extending player systems for Survival, **extend** the existing `Player.cs` of the mode — do not create a new player prefab or movement script.
