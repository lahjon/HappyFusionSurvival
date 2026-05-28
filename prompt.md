# AUTONOMOUS IMPLEMENTATION GUIDE
## Round-based PvPvE pivot — Purge × Stardew Valley

**PURPOSE:** Drive overnight implementation of the new game concept on top of the existing Fusion 2 codebase. Read `CLAUDE.md` first — this guide assumes you know the project's Fusion-first conventions, the `Interactions/` contract, `ActionInvoker`, `Inventory`, and the `Player.cs` patterns in `Assets/03_Shooter/`.

---

## 🎮 GAME CONCEPT (CANONICAL)

**One sentence:** A multiplayer FPS where you spend 15 cheerful minutes in a small town buying gear, running NPC errands, and crafting — then the lights flicker, the music turns, and the town becomes a PvP last-team-standing arena for another ~15 minutes.

### Format
- **Up to 18 players per match.**
- **Team sizes:** Solo (1) / Duo (2) / Trio (3). Picked at the lobby. One size per match; no mixed teams.
- Practical caps: up to 18 solo / 9 duo / 6 trio — all divide cleanly.
- **Friendly fire:** off within a team.
- **One match = one round.** No carry-over of coins or items between matches.

### Phases (state-authority driven, single `MatchManager`)
| Phase          | Duration            | PvP | Tone                                                   |
|----------------|---------------------|-----|--------------------------------------------------------|
| `Lobby`        | until host starts   | no  | menu music                                             |
| `Day`          | 15 min              | no  | bright pastel, cheerful tracks, vendors open           |
| `DuskWarning`  | 30 s                | no  | lights flicker, music distorts, vendors close, sirens  |
| `Night`        | up to ~15 min       | YES | saturated happy-horror palette, eerie music, fog       |
| `MatchOver`    | 20 s                | no  | scoreboard, winning team highlighted                   |

`Night` ends early when only one team has living members. If timer expires with multiple teams alive, tiebreak by total team kills, then by total damage dealt.

### Day-phase activities
- **NPC quests:** small fetch / kill / deliver tasks from town NPCs — pay coins + occasionally items/recipes.
- **Vendors:** buy weapons, ammo, healing, placeables, ingredients. Sell loot.
- **Crafting benches:** already exist — recipes filtered per bench (see `Assets/Common/Crafting/`).
- **Resource pickup:** existing `LootContainer` / `PickupableProp` systems.
- **Vehicles:** drive around town to fetch hauls / reach distant vendors.
- **Combat practice:** training dummies (already exist) — no real PvE threat.

### Night-phase activities
- **PvP only.** No PvE enemies.
- Vendors and quest-giver NPCs despawn or shutter; their interactable prompts disable.
- Town stays the same — same layout, same buildings — but lighting / audio / particles flip to happy-horror.
- Optional v2: shrinking play zone to force engagement near the end.

### Aesthetic flip
- **Day:** warm directional light, soft pastel skybox, ambient bird chirps + cosy music.
- **Night:** desaturated-but-still-colourful palette (cyan/magenta neon over fog), tilted/wrong-pitched cover of the day track, distant screams, flickering streetlights.
- Think Cult of the Lamb / Hello Neighbor — cheerful shapes, ominous lighting.

---

## 🚨 BEFORE YOU TOUCH ANY CODE

### Already exists — extend, don't recreate
| System              | Where                                                 |
|---------------------|-------------------------------------------------------|
| Player + SimpleKCC  | `Assets/03_Shooter/Scripts/Player.cs`                 |
| Networked input     | `Assets/03_Shooter/Scripts/PlayerInput.cs`            |
| Mode bootstrap      | `Assets/03_Shooter/Scripts/GameManager.cs`            |
| Hotbar inventory    | `Assets/03_Shooter/Scripts/Inventory/Inventory.cs`    |
| Combat              | `Assets/03_Shooter/Actions/` + `ActionInvoker`        |
| Vehicles            | `Assets/03_Shooter/Scripts/Vehicles/`                 |
| Interaction system  | `Assets/Common/Interactions/`                         |
| Loot containers     | `Assets/Common/Inventory/LootContainer.cs`            |
| Crafting            | `Assets/Common/Crafting/` + `CraftingBench`           |
| Input actions       | `Assets/Common/Input/GameInputActions`                |
| Lobby / connection  | `Assets/Common/UIGameMenu.cs`                         |

### Doesn't exist — build it
- `MatchManager` (networked phase + timer)
- `TeamManager` (team assignment + friendly-fire checks)
- Currency (`Coins` field on `Player`)
- Vendor NPC + buy/sell session
- Quest giver NPC + `QuestManager`
- PvP damage gate (modify existing `Health.ApplyDamage`)
- Day/Night aesthetic controller (lighting, post, audio)
- Win condition + match-over UI
- Lobby team-size selector (extend `UIGameMenu`)

### Hard rules
- **Do not** add Day-N-of-7 logic, hunger ticks, escape conditions, night PvE enemies. Those belong to the old survival concept.
- **Do not** spin up new movement controllers or input systems. Extend the existing ones.
- **Do not** mutate `[Networked]` from anywhere but state authority.
- **Do not** read `Time.time` for match logic — go through `MatchManager.Phase` and its `TickTimer`s.
- **Do not** use legacy `Input.*` — route through `GameInputActions` (project rule, see memory).

### Ground rules for each phase below
1. Read `CLAUDE.md`'s "Picking the primitive" cheatsheet before adding networked state.
2. State (authority / Fusion primitive / why) in a one-line comment above each new `[Networked]` field — keeps replication intent explicit.
3. After every script change, check `read_console` for compile errors before continuing.
4. UI mutations and Editor-state changes go through MCP **only after the user approves them** (project rule). Pure script + asset work can proceed.

---

## 📋 PHASE 0 — Strip the survival framing

**Goal:** Remove hunger and any "survive day N" framing so new code isn't competing with dead concepts.

### Touchpoints
- `Assets/03_Shooter/Scripts/Player.cs` — remove `Hunger` field, hunger tick in `FixedUpdateNetwork`, and the hunger→stamina cap.
- `Assets/03_Shooter/Scripts/UIShooter.cs` — remove the hunger bar reference + its update call.
- `Assets/03_Shooter/Scripts/Inventory/FoodConsumable.cs` — keep the consumable, but its effect is now **heal-only** (no hunger refill).
- `Assets/03_Shooter/Items/Food.asset` — repurpose as a healing item (set its consumable effect to health, not hunger).

### Verify
- Project compiles, no `Hunger` references left (`grep -ri "hunger" Assets/`).
- Eating a Food item still heals; UI no longer shows a hunger bar.
- Note: do **not** rename the repo folder — git history isn't worth the churn.

---

## 📋 PHASE 1 — `MatchManager` (the single source of truth for phase)

**Goal:** One networked object drives `Lobby → Day → DuskWarning → Night → MatchOver`. Everything that changes between phases reads from it.

### Picking the primitives
- **`Phase`** — per-match value all peers must see → `[Networked] MatchPhase Phase` on `MatchManager` (state authority writes).
- **`PhaseTimer`** — countdown to next transition → `[Networked] TickTimer PhaseTimer` (state authority sets on transition).
- **`RoundIndex`** — `[Networked] int` (only used for telemetry / rematch counter).

### Script outline
```
Assets/03_Shooter/Scripts/Match/MatchManager.cs

public enum MatchPhase : byte { Lobby, Day, DuskWarning, Night, MatchOver }

public sealed class MatchManager : NetworkBehaviour
{
    public static MatchManager Instance { get; private set; }

    [SerializeField] float dayDurationSeconds       = 15f * 60f;
    [SerializeField] float duskWarningSeconds       = 30f;
    [SerializeField] float nightMaxSeconds          = 15f * 60f;
    [SerializeField] float matchOverSeconds         = 20f;

    [Networked, OnChangedRender(nameof(OnPhaseChangedRender))]
    public MatchPhase Phase { get; private set; }

    [Networked] public TickTimer PhaseTimer { get; private set; }
    [Networked] public int       RoundIndex { get; private set; }

    public override void Spawned()
    {
        Instance = this;
        if (Object.HasStateAuthority) EnterPhase(MatchPhase.Lobby);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        if (Phase == MatchPhase.Lobby || Phase == MatchPhase.MatchOver) return;
        if (!PhaseTimer.Expired(Runner)) return;
        AdvancePhase();
    }

    void AdvancePhase() { /* Day → DuskWarning → Night → MatchOver */ }

    void EnterPhase(MatchPhase next) { /* set Phase, set PhaseTimer, fire OnPhaseChanged */ }

    void OnPhaseChangedRender() { /* local: swap music, lighting profile, fire UI event */ }

    // Called by win-condition watcher (Phase 8)
    public void TryEndNightEarly(int winningTeamId) { ... }
}
```

### Hookup
- Spawn one `MatchManager` `NetworkObject` in `03_Shooter.unity` (or have `GameManager.PlayerJoined` `Runner.Spawn` it on the first joiner if missing).
- Authority: state authority of the runner (host in AutoHostOrClient).
- Drive day-start when host hits "Begin Match" in lobby — call `Rpc_BeginMatch` (`RpcSources.InputAuthority`, `RpcTargets.StateAuthority`).

### Verify
- 2 clients connect, sit in `Lobby`. Host hits Begin → both transition into `Day` simultaneously (Fusion eventual-consistent, but next tick).
- Force-shorten `dayDurationSeconds = 30f` for testing. Watch console: phase advances cleanly through all 5 states on both clients.
- `OnPhaseChangedRender` fires on every peer (state-replication sanity check).

---

## 📋 PHASE 2 — `TeamManager` + lobby team-size selection

**Goal:** Assign each `PlayerRef` to a team id based on chosen team size. Make `SameTeam(a, b)` cheap.

### Primitives
- **`TeamSize`** — set once at lobby by host, locked at `Day` start → `[Networked] int TeamSize` on `TeamManager`.
- **`TeamByPlayer`** — `[Networked] NetworkDictionary<PlayerRef, int> TeamByPlayer` (state authority writes).
- **`TeamColor[]`** — table of team colors (local SO, not networked).

### Behaviour
- `MatchManager` calls `TeamManager.AssignTeams(TeamSize)` at the `Lobby → Day` transition.
- Assignment: take all `PlayerRef`s sorted by join order, group into chunks of `TeamSize`.
- Late joiners during `Day`: assigned to the smallest existing team, or new team if all full. **No team changes after `DuskWarning`** — late join during night = spectate.
- Expose `bool SameTeam(PlayerRef a, PlayerRef b)`, `int TeamOf(PlayerRef p)`.

### Lobby UI (extend `UIGameMenu.cs`)
- Add a dropdown: "Team size: Solo / Duo / Trio".
- Host-only — write the chosen value through `Rpc_SetTeamSize` (`RpcSources.InputAuthority`, host has it).
- Lobby panel shows current team assignments live (read from `TeamByPlayer`).

### Verify
- Host picks Duo, three clients in lobby → console shows {P1,P2}=team 0, {P3}=team 1 (will fill).
- Add a 4th client → team 1 fills.
- Begin match → assignments freeze, nameplate color picks up team color.

---

## 📋 PHASE 3 — Currency (`Coins`)

**Goal:** A networked coin balance per player that vendors and quests read/write.

### Primitives
- `[Networked] int Coins` on `Player.cs` — state authority on the player owns it.
- Mutations via methods on `Player`: `AddCoins(int)`, `TrySpendCoins(int)`. Both check `Object.HasStateAuthority`.
- Vendors/quests on clients call into the player by sending RPCs to **state authority** of the player — never write `Coins` directly from another script.

### UI
- Add a small coin counter to `UIShooter`. Plain `MonoBehaviour`, reads `Player.Coins` each frame. Don't `[Networked]` the UI.

### Reset
- `MatchManager` on `Lobby → Day` transition calls `Player.SetCoinsForNewMatch(startingCoins)` for every spawned player (e.g. 100 starting coins).

### Verify
- Add `[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)] Rpc_DebugAddCoins(int)` and a keybind to call it. Press it on client 2 → host's `Coins` field for that player ticks up → both clients' UI shows the new balance.

---

## 📋 PHASE 4 — Vendor NPC (buy + sell)

**Goal:** An `IInteractable` NPC the player can talk to during `Day`. Opens a buy/sell panel. Vanishes during `Night`.

### Existing patterns to follow
- `LootContainer.OnInteract → LootSession.TryOpen` (see `Assets/Common/Inventory/LootContainer.cs`). Mirror this exactly: per-player networked session, host re-validates range on every RPC.

### New components
- `VendorNpc : NetworkBehaviour, IInteractable`
  - `[SerializeField] VendorInventory inventory;` — ScriptableObject listing items + buy price + sell price.
  - `InteractRange = 2.5f`; `LockedReason = MatchManager.Instance.Phase != MatchPhase.Day ? "Closed for the night" : null`.
  - `CanInteract` returns false when not `Day` (or `DuskWarning`).
  - `OnInteract` → `scanner.GetComponent<VendorSession>().TryOpen(this)`.
- `VendorSession : NetworkBehaviour` (one per player, sibling of `Inventory`)
  - `[Networked] NetworkBehaviourId OpenVendor`
  - `[Networked] bool IsOpen`
  - RPCs: `Rpc_RequestBuy(int slotIndex)`, `Rpc_RequestSell(int slotIndex)`. State authority validates: range to vendor (`InteractRange * 1.25f`), phase is `Day` or `DuskWarning`, player has enough coins / item.
  - Implements `IInteractionGate` so the scanner stops scanning while the panel is open.
- `VendorInventory : ScriptableObject` — list of `{ ItemDefinition item, int buyPrice, int sellPrice, int stock }`.
- `UIVendorPanel : MonoBehaviour` — local panel, reads from networked session.

### Vendor catalog seed (Day-phase shopping list)
| Slot | Item                         | Buy | Sell |
|------|------------------------------|-----|------|
| 1    | Crowbar (Workbench combat)   | 40  | 10   |
| 2    | Pistol Ammo (15 rounds)      | 20  | —    |
| 3    | Bandage (heal 25 hp)         | 15  | —    |
| 4    | Crate (placeable)            | 25  | 5    |
| 5    | Food (heal-only after Phase0)| 30  | 8    |

(Numbers are placeholders — tune after a couple of test matches.)

### Verify
- Vendor in scene, prompt visible during Day. Approach, press Interact → panel opens.
- Buy an item with insufficient coins → state-authority rejects, UI shows error.
- Buy with enough coins → coins go down, item appears in hotbar slot, both clients see new balance.
- Force `MatchManager.Phase = Night` → vendor prompt disappears, panel auto-closes for anyone with it open.

---

## 📋 PHASE 5 — Quest system

**Goal:** Tiny Stardew-style fetch/kill/deliver loop. NPCs hand out quests. Completing pays coins.

### Scope for v1 — keep it tight
- **Fetch quest only.** "Bring me 5 Wood." Stretch goal: deliver / kill.
- One global `QuestManager` (`NetworkBehaviour`, singleton). Per-player quest log.
- Each `QuestGiverNpc` exposes 1–3 quests via the same `IInteractable` pattern as the vendor.

### Primitives
- `QuestDefinition : ScriptableObject` — `{ string title; ItemDefinition required; int amount; int rewardCoins; ItemDefinition rewardItem; }`
- `QuestSession : NetworkBehaviour` (per player) — `[Networked, Capacity(4)] NetworkArray<NetworkBehaviourId> ActiveQuests`. Each entry points at a `QuestState` networked object owned by the player.
- Alternative if `NetworkBehaviourId` lookups are awkward: use a `NetworkDictionary<int, byte>` where key = quest hash and value = progress count.
- Quest progress ticks happen on the player's state authority when `Inventory.Add` runs — `QuestSession.OnItemAcquired(ItemDefinition, int)`.

### Turn-in
- Player walks back to giver, interacts. If they have the required items + count, `Rpc_TurnIn(questHash)` → state authority deducts items, adds coins, marks quest complete.
- Re-validate range and phase on the host RPC.

### UI
- Simple list in top-right: "Bring 5 Wood (3/5)".

### Verify
- Accept quest → log shows it. Pick up matching items → counter ticks. Turn in → coins go up.
- Verify quest gives reward only once.

---

## 📋 PHASE 6 — Damage gate (the PvP toggle)

**Goal:** Wire `MatchManager.Phase` and `TeamManager.SameTeam` into `Health.ApplyDamage`. This is the actual "Day = peace, Night = murder" switch.

### Modify `Assets/03_Shooter/Scripts/Health.cs`
```csharp
public void ApplyDamage(PlayerRef instigator, float amount, ...)
{
    // Day: no player-to-player damage at all
    if (MatchManager.Instance != null &&
        MatchManager.Instance.Phase != MatchPhase.Night &&
        TryGetComponent<Player>(out _))
    {
        return; // training dummies, props still take damage
    }

    // Night: friendly fire off
    if (MatchManager.Instance != null &&
        MatchManager.Instance.Phase == MatchPhase.Night &&
        instigator.IsRealPlayer &&
        TryGetComponent<Player>(out _) &&
        TeamManager.Instance.SameTeam(instigator, Object.InputAuthority))
    {
        return;
    }

    // ... existing damage logic
}
```

### Edge cases
- Player damages a non-player (chicken, crate, dummy) — always allowed.
- Environmental damage (fall, vehicle collision) — instigator may be `PlayerRef.None`. Allow during day for self only? Decision: only block player-attributed damage to other players; falls and explosions still hurt.
- Heal/revive RPCs unaffected.

### Verify
- During `Day`, shoot another player → no damage, no hit indicator. Punch a training dummy → still works.
- Force `Night`, shoot teammate → no damage. Shoot enemy team → damage applies.

---

## 📋 PHASE 7 — Day/Night aesthetic flip

**Goal:** When `Phase` changes, swap directional light, skybox tint, post-process volume, ambient audio, and music. Each peer does this locally based on the networked `Phase`.

### Components
- `MatchAestheticController : MonoBehaviour` (in scene, **not** networked)
  - Subscribes to `MatchManager` phase-changed event (raise a static event from `OnPhaseChangedRender`).
  - Holds: `[SerializeField] Light sun;` `[SerializeField] Volume dayVolume, duskVolume, nightVolume;` `[SerializeField] AudioSource musicSource;` `[SerializeField] AudioClip dayMusic, duskStinger, nightMusic;`
  - On `Day`: sun tilt to noon, warm color, day volume weight = 1, others = 0, crossfade to day music.
  - On `DuskWarning`: kick off a 30s lerp — sun drops toward horizon, palette desaturates, fog rises, music distorts. Flicker the streetlights (existing point lights tagged `StreetLight`).
  - On `Night`: cool moonlight, neon cyan/magenta accent, fog dense, night music.
  - On `MatchOver`: freeze whatever was current.

### Audio
- Use existing `AudioMixer` if one exists; else add `Master / Music / SFX / Ambient` groups.
- DSP filter on music during `DuskWarning` for the "wrong pitch" cover effect.

### Verify
- Open the scene with 2 clients, fast-forward through phases — each sees the same flip at the same time (within a tick).
- Streetlight flicker happens during `DuskWarning` for everyone.

---

## 📋 PHASE 8 — Win condition

**Goal:** Detect last-team-standing and end the match.

### Implementation
- `MatchManager` runs `CheckWinCondition()` on state authority each `FixedUpdateNetwork` while `Phase == Night`:
  - Scan all `Player` components, group by `TeamManager.TeamOf`.
  - Count teams with at least one living member.
  - If `== 1` → call `EnterPhase(MatchPhase.MatchOver)` with that team as winner.
  - If `== 0` (everyone dies same tick, unlikely) → declare draw.
- Timer expiry path: at `PhaseTimer.Expired` while in `Night`, run tiebreaker (team kills then damage), enter `MatchOver`.
- Track team kills as a `[Networked, Capacity(18)] NetworkArray<int> TeamKills` on `TeamManager`; `Health.ApplyDamage` calls `TeamManager.RegisterKill(attackerTeam)` when killing blow lands during `Night`.

### Match over UI
- `UIMatchResult : MonoBehaviour` — reads winning team id + roster + scores from `MatchManager` + `TeamManager`. Shows for 20s, then `MatchManager` returns to `Lobby`.

### Verify
- 4 players, Duo. Force `Night`. Kill the opposing team's members → match ends immediately with your team's color highlighted.
- Let timer expire mid-fight with both teams alive → tiebreak by kills.

---

## 📋 PHASE 9 — Lobby flow + new-match reset

**Goal:** After `MatchOver`, return cleanly to `Lobby` so the same room can run another round.

### `MatchManager.Reset()` on `MatchOver → Lobby` transition
- Reset every player: HP full, inventory cleared (or reset to lobby loadout), coins zeroed, quest sessions cleared, ragdoll cancelled, teleport to spawn.
- Despawn round-only objects: dropped pickups, placed crates, vendor spawned loot.
- Re-spawn vendors and quest givers if they were despawned for night.
- `TeamManager` clears assignments; host can change team size again in lobby.

### `UIGameMenu` additions
- "Begin Match" button (host only, lobby phase only).
- Team-size dropdown (host only).
- Player roster with team colors.
- Disconnect button always available.

### Verify
- Finish a match, hit Begin again, full round runs cleanly with the same room.
- Change team size between rounds — assignments reflect the new size.

---

## 📋 PHASE 10 — Smoke-test pass

Before declaring done, run through one full match with 4 ParrelSync clients (Duo, 2v2):

1. Lobby: host picks Duo, hits Begin.
2. Day: each team explores, sells junk, accepts a quest, buys a weapon, completes the quest, turns it in.
3. DuskWarning: streetlights flicker, music distorts, vendors close mid-interaction (panels auto-close).
4. Night: PvP enabled, teams fight, last team standing wins.
5. MatchOver: scoreboard shows correct winner.
6. Lobby: rematch cleanly.

Console must be free of `[Networked]`-written-without-authority warnings and missing-reference errors.

---

## 🧭 ARCHITECTURE NOTES

### Where new code lives
```
Assets/03_Shooter/Scripts/Match/
    MatchManager.cs
    MatchPhase.cs              (enum)
    MatchAestheticController.cs
    TeamManager.cs

Assets/03_Shooter/Scripts/Economy/
    VendorNpc.cs
    VendorSession.cs
    VendorInventory.cs        (ScriptableObject)
    UIVendorPanel.cs

Assets/03_Shooter/Scripts/Quests/
    QuestManager.cs
    QuestDefinition.cs        (ScriptableObject)
    QuestGiverNpc.cs
    QuestSession.cs
    UIQuestLog.cs

Assets/03_Shooter/UI/
    UIMatchResult.cs
    UICoinCounter.cs

Assets/03_Shooter/Match/                (assets)
    DayVolume.asset
    DuskVolume.asset
    NightVolume.asset
    VendorInventory_General.asset
    QuestDef_FiveWood.asset
    ...
```

### Authority cheatsheet for this pivot
| Concept                  | Authority                          | Primitive                                  |
|--------------------------|------------------------------------|---------------------------------------------|
| Match phase + timer      | State auth on `MatchManager`       | `[Networked] enum` + `TickTimer`           |
| Team assignment          | State auth on `TeamManager`        | `NetworkDictionary<PlayerRef,int>`         |
| Player coins             | State auth on each `Player`        | `[Networked] int`                          |
| Open vendor / open quest | State auth on each `*Session`      | `[Networked] bool` + RPCs in               |
| Vendor open/closed visual| Local read of phase                | `MonoBehaviour` reading `MatchManager`     |
| Lighting/audio flip      | Local                              | `MonoBehaviour` reacting to phase change   |
| Kill counts              | State auth on `TeamManager`        | `NetworkArray<int>`                        |
| Quest progress           | State auth on the player           | `NetworkArray` or `NetworkDictionary`      |

### Networked-vs-not gut check before adding a field
1. Does another peer need to see this value to render or react? If yes → `[Networked]`.
2. Will it be mutated by anyone other than state authority? If yes → either (a) use an RPC to route to state authority, or (b) you've picked the wrong primitive.
3. Is it a duration? `TickTimer`, not `Time.time`.
4. Is it a list whose length changes? `NetworkArray<T>` with explicit `[Capacity]`.
5. Is it a one-shot visible event (kill feed pop, fanfare)? `[Networked] int Counter` + `OnChangedRender`.

---

## ✅ DEFINITION OF DONE

A phase is done when:
- Compiles cleanly (`read_console` clean after each save).
- Works in a 2-client Fusion test (host + client, AutoHostOrClient mode).
- Authority is documented inline with a one-line comment above each `[Networked]`.
- The state-replication intent matches "Picking the primitive" in `CLAUDE.md`.
- UI and aesthetics react to networked state, never duplicate it.

The pivot is done when:
- A full Duo (or any team size) match runs end-to-end with 4 clients.
- No survival vocabulary (`hunger`, `Day 7`, `escape`) remains in code or UI.
- Day = vendors + quests + crafting work; PvP is blocked. Night = PvP works, vendors gone, teams isolated.
- A winning team is identified and a rematch starts cleanly.

---

## 📝 REPORT TEMPLATE (overnight run)

```markdown
# Pivot implementation report — <date>

## Phases complete
- [x] 0 Strip survival
- [x] 1 MatchManager
- [ ] 2 TeamManager  (in progress — late-join behaviour TODO)
- [ ] 3 Coins
- [ ] 4 Vendor NPC
- [ ] 5 Quests
- [ ] 6 Damage gate
- [ ] 7 Aesthetic flip
- [ ] 8 Win condition
- [ ] 9 Lobby reset
- [ ] 10 Smoke test

## Notable decisions
- Late-join during Day → smallest team; during Night → spectator.
- Coins reset every match (no carry-over) — confirmed with user.

## Open questions for review
- Should crafting recipes be unlockable as quest rewards?
- Tiebreaker order: kills → damage → first to last-stand? (Currently kills → damage.)

## Verified
- 4-client smoke test: <link to recording or screenshots>
- No `[Networked] write without authority` warnings.

## Outstanding
- Quest UI list needs a scroll view (multiple quests overflow).
- Music DSP filter on DuskWarning needs polish.
```

---

## 🚀 START HERE

1. Read `CLAUDE.md` end-to-end.
2. Do **Phase 0** (strip hunger) first — it's the smallest and clears the path.
3. Build **Phase 1 (MatchManager)** next — every subsequent phase reads from it.
4. Then 2, 3, 4… in order. Don't skip; later phases assume earlier ones.
5. Before mutating Editor state via MCP (creating prefabs, editing scenes, spawning `NetworkObject`s in the scene), describe the change and wait for user approval. Pure script + asset work can proceed.

Good night. Build the town.
