# HorrorTown — procedural city + run randomizer

Branch: `HorrorTown`. This is the pivot away from the Purge/PvPvP round game to a **1–4 player co-op** run-based
game in a procedurally generated 1×1 km metropolitan city at night.

## The loop

Four PCs somewhere in the city must be brought online. Finding them is the game; activating them is one button
and a loud consequence. Between the players and each terminal sit **blocks** — a locked front door, a keypad, a
chained fire exit, a mag-lock with no power, a facade that can only be climbed. Which blocks, on which building,
and where the keys are is re-rolled every run and **proven solvable before the run starts**.

Fighting is not an option. Monsters kill on contact and cannot be hurt. Streets are wide, lit and lethal;
alleys, courtyards, interiors and rooftops are the real road network.

## Architecture at a glance

```
seed ──► CityLayoutGenerator ──► CityLayout      (districts / blocks / lots / roads — pure data)
             │                        │
             │                        ├─► BuildingPlanGenerator ──► floors, rooms, portals (lazy, per building)
             │                        │
             │                        └─► CityStreamer ──► CityBuilder ──► geometry (shells always, interiors near players)
             │
             └──► RunPlanner ──► RunPlanData     (objectives, barriers, keys, clues, start area)
                       │
                       └──► RunSolver.Verify     (reject + re-roll until provably completable)
```

**Nothing generated is ever replicated.** The host picks one integer, writes it to `RunState.RunSeed`, and every
peer builds an identical city and an identical plan locally. The only networked data is what players *change*:
which barriers are open, which terminals are live, what the team has learned. That is a few hundred bytes.

## Files

| Area | Path | What it does |
|---|---|---|
| RNG | `Scripts/City/RunRng.cs` | Deterministic named-stream RNG. Re-rolling `Content` reshuffles loot without moving a wall. |
| Layout | `Scripts/City/CityLayoutGenerator.cs` | BSP arterials → districts → street grid → blocks → alleys → lots. |
| Layout data | `Scripts/City/CityLayout.cs`, `CityTypes.cs`, `CitySettings.cs`, `CityNames.cs` | Data model, tuning, address generation. |
| Interiors | `Scripts/City/BuildingPlan.cs`, `BuildingPlanGenerator.cs` | Floors, stair cores, corridors, rooms, doors, windows, fire escapes, roof. |
| Geometry | `Scripts/City/CityBuilder.cs`, `MeshBuilder.cs`, `BuildingKit.cs`, `CityStreamer.cs` | Kit-driven realization with a primitive fallback; shell ↔ interior swapping. |
| Lighting | `Scripts/City/StreetLightGrid.cs` | Thousands of lamps placed by district density, only the few near a player kept live. Alleys unlit by design. |
| Nav | `Scripts/City/CityNavGraph.cs` | Street graph sampled straight from the road layout + A*. |
| Logic | `Scripts/Run/RunLogicTypes.cs`, `RunPlanData.cs`, `RunPlanner.cs`, `RunSolver.cs` | The randomizer: sites, entry routes, barriers, forward-fill key placement, verification. |
| State | `Scripts/Run/RunState.cs` | The only networked run data. Barrier flags, objective flags, shared knowledge. |
| Blocks | `Scripts/Run/Barrier.cs` | One component for every kind of block. |
| Content | `Scripts/Run/ObjectiveTerminal.cs`, `NavigationTerminal.cs`, `ClueNote.cs`, `PowerBreaker.cs`, `IntelVendor.cs` | Interactables the plan spawns. |
| Orchestration | `Scripts/Run/RunDirector.cs`, `RunItemCatalog.cs` | Boots the run, spawns content, answers "can this player open that". |
| Monsters | `Scripts/Monsters/Monster.cs`, `MonsterDirector.cs`, `MonsterProfile.cs` | Sparse hazards, cover-aware senses, escalation on terminal activation. |

## Why some decisions went the way they did

**Forward-fill, not random scatter.** Keys are placed one at a time into slots that are *already reachable* with
the keys placed so far (`RunPlanner.PlaceRequiredItems`). That makes every run solvable by construction. A
separate pass (`RunSolver.Verify`) then re-proves it from scratch, and a plan that fails is thrown away and
re-rolled rather than patched — an unwinnable run is the worst thing this system can produce.

**Every site has several ways in.** A site is enterable if *any one* entry route is open. That is what the brief's
"one run the front door is shut so climb the outside, another run pick the lock" needs, and it is the pressure
valve that stops a harsh barrier roll from being a dead end.

**Barriers attach to portals, not to buildings.** The building generator emits a portal graph; the planner decides
which portals get blocked and how. Adding a new kind of block is an enum value and a branch — not a new door
class, prefab or interaction path.

**Two representations per building.** Every building is always a cheap instanced box (skyline, sightlines,
walkable roof). Only buildings near a player become walk-in. A square kilometre of interiors cannot all exist.

**Monster senses read the layout, not raycasts.** Sight range is scaled by *what ground the player is standing
on* — road, plaza, interior. "Stay off the street" is then a rule players can learn and rely on, rather than an
accident of where walls happen to be.

**Knowledge is shared, tools are not.** A door code learned by one player is known by the team (someone would
just read it out loud anyway). A crowbar in someone else's bag three streets away does not open your shutter.

## Static city + baked NavMesh (2026-07-28)

The environment is no longer re-rolled per run. **`StaticCity`** (scene component, `Scripts/City/StaticCity.cs`)
holds one serialized `CitySeed`; the whole city — ground, roads, every building with full interiors — is built
deterministically from it. The editor menu **Tools/HorrorTown/Generate City + Bake NavMesh** builds the city
(preview geometry is DontSave — never serialized into the scene), bakes the scene `NavMeshSurface` from physics
colliders and saves the data to `Assets/Scenes/HorrorTown/NavMesh-NavMesh.asset`. At runtime `StaticCity.Awake`
rebuilds the identical geometry, so the baked NavMesh always matches the world. **Change the seed → rebake.**

What still randomizes per run (from networked `RunState.RunSeed`): objectives, barriers, key/clue placement,
stashes, monsters. `RunDirector` uses `StaticCity.Layout` when present and spawns all barriers up front; the
`CityStreamer` path remains only as a fallback for scenes without a `StaticCity`.

Monsters path over the baked NavMesh (`NavMesh.CalculatePath`; street-graph fallback). Closed barriers carve it
via a runtime `NavMeshObstacle` on their blocker, so sealed doors block routes and opened doors become routes.
City geometry without a `BuildingKit` uses flat URP-Lit tints (`CityMaterials`) instead of magenta.

## Editor wiring (done)

Everything the code needs is wired; the city builds from untextured primitives without any authored kit.

- **Scene:** `Assets/Scenes/HorrorTown.unity` (build index 2). Hosts `RunState`, `RunDirector` and
  `MonsterDirector` alongside the usual `GameManager` stack. The lobby (`MainMenu.unity` →
  `LobbyManager.GameSceneIndex = 2`) loads it when the host presses Start; `MatchOver` returns to index 0.
- **Prefabs:** `Assets/Prefabs/Run/` — Barrier (plain GameObject), ObjectiveTerminal, NavigationTerminal,
  ClueNote, PowerBreaker, IntelVendor, Monster (all `NetworkObject`s carry the `FusionPrefab` label). Loot
  container and pickup reuse the existing shared prefabs.
- **Items:** `RunItemCatalog` at `Assets/Data/Run/RunItemCatalog.asset`. Lockpick, BoltCutters, DoorKey and
  Keycard1–3 are new assets (ids 29–34, registered in `ItemDatabase.asset`); Crowbar→Wrench, Rope→GrappleHook
  and Planks→Wood map onto existing items that already carry the right verbs (`HeldGrapple`,
  `PlaceableCapability`).
- **Optional:** a `CitySettings` asset and `BuildingKit` assets (menu `HorrorTown/…`) to swap primitives for
  Synty modules. Not wired — defaults are built in.

## Known gaps

- **Rope/plank traversal is unverified in-game.** The solver places GrappleHook/Wood as requirements; the
  existing grapple and placement verbs should cover the climb/bridge actions but this hasn't been play-tested
  against real rooftop gaps.
- **Elevators are in the portal graph but not built.** `PortalKind.Elevator` exists and gates on power; no
  geometry or ride behaviour yet.
- **Not yet play-tested in the Editor.** Everything here is new code on a new branch.

## Fixed since first draft

- **Buildings v2 (2026-07-28):** stairs are a compact two-flight switchback of solid box steps (~32° pitch,
  working collision) with the stairwell opening actually carved through the slab above — the old full-room ramp
  climbed into a ceiling. Facades pull from a 10-tint muted palette keyed by lot index (`CityMaterials.WallVariant`).
  Interiors are furnished per room kind by `RoomFurnisher` (desks/racks/shelving/beds/counters; merged meshes,
  colliders, doorway clearance, seed-deterministic so the NavMesh bake stays valid). `NightLighting`
  (`ExecuteAlways`, on the StaticCity object) owns the permanent night — flat near-black ambient, Exp2 fog, one
  moon directional — and disables the legacy `TimeManager` day cycle. `StreetLightGrid` now builds procedural
  sodium lamps (emissive head + culled spot) along every non-alley road with no art prefab needed, as part of
  `StaticCity.Build`. NavMesh rebaked against all of it (65 MB, same asset guid).

- Window and fire-escape openings are now window-shaped — spandrel up to a 1 m sill, header above 2.4 m
  (`CityConstants.WindowSillHeight`/`WindowHeadHeight`), so entering one is a mantle, not a walk-through.
  Doors, shutters and open thresholds still punch full doorways.
- Monsters navigate interiors via room-portal pathing (`Scripts/City/BuildingNav.cs`). A route composes up to
  three legs — out of the current building, street-graph A* across town, in through the target building's best
  entrance and up its stairwells. Monsters never pass barred portals (`RunState.IsBarrierOpen`) and never use
  windows, fire escapes or elevators; when every entrance is sealed they head for the nearest door and wait,
  which is exactly as unsettling as it sounds.
