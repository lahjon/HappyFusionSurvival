# Arena Clash — Full Implementation Prompt

> **Purpose:** This document is a complete specification and build prompt for an AI coding assistant (Claude) to implement the game "Arena Clash" from start to finish using **Unity MCP** (Model Context Protocol). Claude has direct access to the Unity Editor and must create all scripts, prefabs, scenes, ScriptableObject assets, UI canvases, materials, and placeholder meshes in-engine. Every system described in this document must have a tangible, runnable in-editor representation — not just code files.

---

## Critical Build Philosophy: Everything Must Exist In-Engine

**This project uses Unity MCP.** Claude has direct access to the Unity Editor via MCP tools. This means:

1. **Every script must be created as a .cs file** in the correct project folder AND attached to the appropriate GameObject or prefab.
2. **Every prefab must be physically created** in the Unity Editor — player prefab, every weapon prefab, every coin prefab, every hazard prefab, every UI panel. Do not just write code that "assumes" a prefab exists. Create it.
3. **Every ScriptableObject asset must be instantiated** — create the actual .asset files for every weapon, every upgrade, every level config. Populate their fields with the values specified in this document.
4. **Every scene must be created** — MainMenu, Lobby, Shop, all 10 arena scenes. Build them with actual GameObjects, even if geometry is blockout (cubes, cylinders, planes with colored materials).
5. **Every UI element must be built** — create Canvas objects, panels, text elements, buttons, sliders, and layout groups. Use placeholder styling but make them functional and wired up to code.
6. **Materials must be created** for visual distinction — simple colored materials for blockout geometry (red for hazards, gold for coins, blue for players, gray for platforms, etc.).
7. **Prefab references must be wired up** — if a script has a `public GameObject prefab` field, the actual prefab must be dragged/assigned in the Inspector. Do not leave serialized fields empty.
8. **Systems for audio and VFX must be architected and wired** — create the AudioManager, SFXLibrary ScriptableObject, VFXManager, and particle system prefabs with empty/placeholder clips and effects. The systems and hooks must exist so real assets can be dropped in later. Skip creating actual sound files and particle effects for now, but the infrastructure, references, and trigger points must all be in place.
9. **Layer and Tag setup** — create all required layers (Player, Weapon, Coin, Hazard, KillZone, Ground) and tags in the Unity project settings.
10. **Physics settings** — configure collision matrix (e.g., coins don't collide with each other, weapons don't collide with the player who threw them).
11. **Logging** Create a log in ./Phase_Log.log where log every Phase and every step of the phase thats completed. This is used so we can restart the prompt again and know where we are in the process.
12. **Animations and Feedback** Prefer DOTween when doing feedback rather than enumarator

**The deliverable for each phase is a playable, testable build** — not a collection of scripts. After each phase, someone should be able to press Play in the Unity Editor and interact with the systems built in that phase.


### Blockout & Placeholder Standards

When creating geometry and visuals that will be replaced by art later:

- **Platforms/floors:** Scaled cubes or planes with a gray or brown material.
- **Walls/obstacles:** Scaled cubes with a darker gray material.
- **Hazard zones:** Use bright red or orange materials. Fire vents = red cubes. Lava = red planes. Saws = red cylinders.
- **Weapons:** Simple primitive shapes with a distinct color per type. Throwing knife = thin white cube. Hatchet = T-shaped grouped cubes. Revolver = small dark gray L-shape. Etc.
- **Coins:** Yellow (gold) and silver (silver coin) spheres or cylinders with a slight emissive material.
- **Players:** Capsule with a colored material (Player 1 = blue, Player 2 = red, Player 3 = green, Player 4 = yellow). Camera child object at eye level.
- **Kill zones:** Large invisible trigger collider below platforms, with a red debug gizmo for editor visibility.
- **Spawn points:** Empty GameObjects with a custom gizmo icon or a small colored sphere (editor only).
- **UI:** Use Unity's default UI styling (white panels, default font) but with correct layout, anchoring, and functionality. Every button must have an onClick wired up. Every text field must be updating from game state.

### Asset Naming Conventions

```
Prefabs:     PFB_PlayerCharacter, PFB_Weapon_ThrowingKnife, PFB_Coin_Gold, PFB_Hazard_FireVent
Materials:   MAT_Blockout_Gray, MAT_Blockout_Red, MAT_Player_Blue, MAT_Coin_Gold
ScriptableObjects: WPN_ThrowingKnife, WPN_Hatchet, UPG_IronSkin, UPG_QuickHands, LVL_TheCircle
Scenes:      SCN_MainMenu, SCN_Lobby, SCN_Shop, SCN_Arena_01_Circle, SCN_Arena_10_GrandFinale
Audio:       SFX_Throw_Knife, SFX_Impact_Metal, MUS_Round_Early (placeholder empty AudioClips)
```

---

## Project Overview

**Arena Clash** is a fast-paced, physics-driven multiplayer FPS where 2–4 players battle through 10 rapid-fire rounds in small, hazardous micro-arenas. Each round lasts 5–60 seconds and features unique environments, hazards, and weapon spawns. Players earn coins based on performance and spend them at an Upgrade Terminal (shop) after every 3 rounds. Round 10 is a winner-takes-all deathmatch where all accumulated upgrades are active.

The game blends the chaotic, physics-driven energy of **Stick Fight: The Game** with first-person shooter mechanics featuring intentionally wonky, skill-expressive weapons. Weapons are hard to aim on purpose — bullets spread, thrown weapons arc unpredictably, and melee has exaggerated wind-ups. This rewards prediction and timing over raw pixel-perfect aim.

### Core Design Pillars

1. **Accessible Chaos** — Easy to pick up, hard to master. Wonky weapon physics create hilarious moments while rewarding skilled play.
2. **Strategic Progression** — Coin economy and upgrade shop add meaningful decision-making between rounds.
3. **Quick Sessions** — A full 10-round match takes ~10–15 minutes. Individual rounds are 5–60 seconds.
4. **Competitive Climax** — Every round feeds into the Round 10 deathmatch. Early rounds build advantages for the finale.

### Target Audience

Casual-to-mid-core multiplayer gamers (ages 13+) who enjoy party games, arena shooters, and competitive couch/online play. Fans of Stick Fight, Gang Beasts, Shellshock Live, and classic arena FPS titles.

---

## Technology Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Engine | Unity (latest LTS, URP) | 3D first-person perspective |
| Networking | Photon Fusion 2 | **Host Mode** — using the Fusion Starter Multiplayer Template |
| AI Tooling | Unity MCP | Claude has direct Unity Editor access — must create all assets in-engine |
| Language | C# | Unity standard |
| Input | Unity New Input System | Required for rebinding and cross-platform |
| Build Targets | PC (Steam) primary | Console ports planned post-launch |
| Version Control | Git | GitFlow branching recommended |

---

## Networking Model: Photon Fusion 2 — Host Mode

We are using the **Photon Fusion 2 Starter Multiplayer Template** with **Host Mode**. One player acts as the host and runs the authoritative simulation. All other players are clients who send input to the host. The host processes all game logic and replicates state back to clients.

### Authority Rules

| System | Authority | Details |
|--------|----------|---------|
| Player Movement | Host-authoritative with client prediction | Clients send input via `GetInput()`. Host processes movement. Fusion handles prediction and reconciliation. |
| Weapon Pickups | Host-authoritative | Client sends pickup request. Host validates (first request wins). Clients predict locally with rollback. |
| Projectiles (Thrown/Bullets) | Host-authoritative with lag compensation | Host spawns and simulates all projectiles. Use Fusion's `HitboxManager` and lag-compensated raycasts. |
| Damage & Elimination | Host-authoritative | Host calculates all damage. Clients display results. |
| Coin Collection | Host-authoritative with local prediction | Client enters trigger → visual pickup instant → host confirms credit. |
| Shop Transactions | Host-authoritative | Client sends purchase request. Host validates funds, deducts, applies upgrade. |
| Round/Match State | Host-authoritative | Host controls round transitions, timers, scene loading, match flow. |
| Hazards | Host-authoritative | Host runs hazard logic. State synced via `[Networked]` properties. |

### Key Fusion Concepts

- **`[Networked]`** — Synced properties on `NetworkBehaviour`. Use for health, coins, round state, weapon ownership, hazard states.
- **`NetworkObject`** — Every synced entity needs this. Attach to all player, weapon, coin, and hazard prefabs.
- **`NetworkRunner`** — Core Fusion component. One per scene.
- **`TickTimer`** — Tick-accurate timer for round countdowns, hazard cycles, fuse timers, shop countdown.
- **`GetInput<T>()`** — How the host reads client input each tick.
- **`HasInputAuthority`** / **`HasStateAuthority`** — In Host Mode, the host has StateAuthority on everything. Clients have InputAuthority on their own player.
- **`Runner.Spawn()`** — Spawns networked objects. Always call on the host.
- **`Rpc`** — Use sparingly for cosmetic events (audio, particles). Prefer `[Networked]` state for gameplay.
- **`Runner.LagCompensation.Raycast()`** — Lag-compensated hit detection for hitscan weapons.
- **`NetworkSceneManagerDefault`** — Scene loading synced across all clients.

### Input Data Struct

```csharp
public struct NetworkInputData : INetworkInput
{
    public Vector2 MoveDirection;
    public Vector2 LookDelta;
    public NetworkBool JumpPressed;
    public NetworkBool FirePressed;
    public NetworkBool InteractPressed;
    public NetworkBool DropPressed;
}
```

### Input Action Map (Unity New Input System)

Create an `InputActionAsset` named `PlayerInputActions` with these bindings:

```
Move:       WASD / Left Stick        (Vector2)
Look:       Mouse Delta / Right Stick (Vector2)
Jump:       Space / South Button      (Button)
Throw/Fire: Left Mouse / Right Trigger (Button)
Interact:   E / West Button           (Button)
Drop:       Q / North Button          (Button)
```

> **MCP Action:** Create this InputActionAsset in `Assets/_Project/Input/PlayerInputActions.inputactions` and configure all bindings.

---

## Project Folder Structure

> **MCP Action:** Create this entire folder hierarchy in the Unity project at the start of Phase 1.

```
Assets/
  _Project/
    Scripts/
      Core/           # GameManager, MatchController, RoundManager, LevelSelector
      Networking/     # FusionBootstrap, PlayerSpawner, NetworkCallbacks
      Player/         # PlayerController, PlayerHealth, PlayerInventory, PlayerStats, PlayerEconomy
      Weapons/        # WeaponBase, ThrowableWeapon, FirearmWeapon, MeleeWeapon, WeaponSpawner
      Economy/        # CoinManager, CoinPickup, ShopManager
      Hazards/        # HazardBase, FireVent, ConveyorBelt, CrumblingPlatform, VoidKillZone, etc.
      UI/             # HUDManager, ShopUI, ResultsScreen, MainMenuUI, LobbyUI, SpectatorUI
      Audio/          # AudioManager, SFXLibrary (ScriptableObject), MusicManager
      VFX/            # VFXManager, VFXLibrary (ScriptableObject)
      Camera/         # FirstPersonCamera, ScreenShake, SpectatorCamera
      Data/           # ScriptableObject class definitions (WeaponData, UpgradeData, LevelData)
    Prefabs/
      Player/         # PFB_PlayerCharacter
      Weapons/        # PFB_Weapon_ThrowingKnife, PFB_Weapon_Hatchet, etc.
      Coins/          # PFB_Coin_Gold, PFB_Coin_Silver
      Hazards/        # PFB_Hazard_FireVent, PFB_Hazard_Conveyor, etc.
      UI/             # PFB_HUD, PFB_ShopUI, PFB_ResultsPanel, PFB_KillFeedEntry
      VFX/            # PFB_VFX_HitSpark, PFB_VFX_Explosion, PFB_VFX_CoinCollect (empty placeholders)
      Audio/          # (empty AudioClip placeholders if needed)
    Scenes/
      SCN_MainMenu
      SCN_Lobby
      SCN_Shop
      SCN_Arena_01_Circle
      SCN_Arena_02_Bridge
      SCN_Arena_03_Furnace
      SCN_Arena_04_Pit
      SCN_Arena_05_ZeroGLab
      SCN_Arena_06_Carousel
      SCN_Arena_07_Freezer
      SCN_Arena_08_Gauntlet
      SCN_Arena_09_Volcano
      SCN_Arena_10_GrandFinale
    ScriptableObjects/
      Weapons/        # WPN_ThrowingKnife.asset, WPN_Hatchet.asset, etc.
      Upgrades/       # UPG_IronSkin.asset, UPG_QuickHands.asset, etc.
      Levels/         # LVL_TheCircle.asset, LVL_TheBridge.asset, etc.
      Audio/          # SFXLib_Master.asset
      VFX/            # VFXLib_Master.asset
    Input/
      PlayerInputActions.inputactions
    Art/
      Materials/      # MAT_Blockout_Gray, MAT_Blockout_Red, MAT_Player_Blue, etc.
      Models/         # (empty, for future art)
    Audio/
      SFX/            # (placeholder empty clips or folders)
      Music/          # (placeholder empty clips or folders)
    Animations/
      Player/
      Weapons/
```

---

## Data Architecture (ScriptableObjects)

All game data is driven by ScriptableObjects. Define these script classes first, then **immediately create all .asset instances** with populated values from the tables in this document.

### WeaponData.cs

```csharp
public enum WeaponType { Throwing, Firearm, Melee }

[CreateAssetMenu(fileName = "NewWeapon", menuName = "ArenaClash/WeaponData")]
public class WeaponData : ScriptableObject
{
    public string weaponName;
    public WeaponType type;
    public GameObject prefab;             // MUST be assigned to the actual weapon prefab
    public float damage;
    public float throwForce;
    public float throwArcRandomness;      // Wonkiness 0–1 (0 = straight, 1 = ±15°)
    public float windUpDuration;
    public int ammoCount;                 // Firearms only (0 = N/A)
    public float recoilStrength;
    public float spreadAngle;
    public float knockbackForce;
    public bool canBounce;
    public int maxBounces;
    public float fireRate;
    public AudioClip throwSFX;            // Wire to placeholder clip
    public AudioClip impactSFX;           // Wire to placeholder clip
    public AudioClip fireSFX;             // Wire to placeholder clip
}
```

> **MCP Action:** After creating this script, create these ScriptableObject assets in `Assets/_Project/ScriptableObjects/Weapons/`:

| Asset Name | weaponName | type | damage | throwForce | wonkiness | windUp | ammo | recoil | spread | knockback | bounce | maxBounces |
|-----------|-----------|------|--------|-----------|-----------|--------|------|--------|--------|-----------|--------|------------|
| WPN_ThrowingKnife | Throwing Knife | Throwing | 100 | 30 | 0.15 | 0.2 | 0 | 0 | 0 | 5 | false | 0 |
| WPN_Hatchet | Hatchet | Throwing | 100 | 22 | 0.25 | 0.4 | 0 | 0 | 0 | 8 | false | 0 |
| WPN_Shuriken | Shuriken | Throwing | 50 | 28 | 0.3 | 0.15 | 0 | 0 | 0 | 3 | false | 0 |
| WPN_Javelin | Javelin | Throwing | 120 | 18 | 0.2 | 0.7 | 0 | 0 | 0 | 15 | false | 0 |
| WPN_Bolas | Bolas | Throwing | 0 | 20 | 0.35 | 0.3 | 0 | 0 | 0 | 0 | false | 0 |
| WPN_Dynamite | Dynamite | Throwing | 80 | 20 | 0.4 | 0.3 | 0 | 0 | 0 | 12 | false | 0 |
| WPN_RubberBall | Rubber Ball | Throwing | 35 | 25 | 0.5 | 0.1 | 0 | 0 | 0 | 2 | true | 3 |
| WPN_Revolver | Revolver | Firearm | 80 | 0 | 0 | 0 | 3 | 8 | 3 | 5 | false | 0 |
| WPN_SawedOff | Sawed-Off Shotgun | Firearm | 40 | 0 | 0 | 0 | 2 | 12 | 15 | 10 | false | 0 |
| WPN_FlareGun | Flare Gun | Firearm | 30 | 15 | 0.3 | 0 | 1 | 6 | 2 | 3 | false | 0 |
| WPN_Bat | Bat | Melee | 60 | 0 | 0 | 0.3 | 0 | 0 | 0 | 15 | false | 0 |
| WPN_FryingPan | Frying Pan | Melee | 50 | 0 | 0 | 0.5 | 0 | 0 | 0 | 10 | false | 0 |
| WPN_BoxingGlove | Boxing Glove | Melee | 30 | 0 | 0 | 0.15 | 0 | 0 | 0 | 25 | false | 0 |

**For each weapon, also create a blockout prefab** in `Assets/_Project/Prefabs/Weapons/` with:
- A primitive mesh (scaled cube, cylinder, or grouped primitives) with a distinct colored material
- A `NetworkObject` component
- A `NetworkRigidbody3D` component (for throwables)
- The appropriate weapon script component (`ThrowableWeapon`, `FirearmWeapon`, or `MeleeWeapon`)
- A `Collider` (BoxCollider or CapsuleCollider)
- The `WeaponData` reference wired to the matching ScriptableObject

Then go back and assign each prefab to its `WeaponData.prefab` field.

### UpgradeData.cs

```csharp
public enum UpgradeCategory { Passive, Consumable, WeaponMod }
public enum StatType { MaxHealth, MoveSpeed, ThrowSpeed, DamageDealt, KnockbackResist, WeaponSway, CoinBonus, PickupSpeed, BurnDamage, SlowEffect }

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ArenaClash/UpgradeData")]
public class UpgradeData : ScriptableObject
{
    public int upgradeId;
    public string upgradeName;
    [TextArea] public string description;
    public UpgradeCategory category;
    public Sprite icon;                   // Assign placeholder sprite
    public int cost;
    public bool isStackable;
    public UpgradeEffect[] effects;
}

[System.Serializable]
public class UpgradeEffect
{
    public StatType stat;
    public float value;
    public bool isMultiplier;
}
```

> **MCP Action:** Create all upgrade .asset files in `Assets/_Project/ScriptableObjects/Upgrades/`:

**Passive Upgrades:**

| Asset | ID | Name | Effect | Cost | Stackable |
|-------|-----|------|--------|------|-----------|
| UPG_IronSkin | 1 | Iron Skin | MaxHealth +0.15 (mult) | 8 | No |
| UPG_QuickHands | 2 | Quick Hands | ThrowSpeed +0.20 (mult) | 6 | No |
| UPG_Scavenger | 3 | Scavenger | CoinBonus +1 (flat) | 5 | No |
| UPG_SteadyAim | 4 | Steady Aim | WeaponSway -0.50 (mult) | 7 | No |
| UPG_ThickBoots | 5 | Thick Boots | KnockbackResist +0.40 (mult) | 6 | No |
| UPG_Vulture | 6 | Vulture | CoinBonus +2 on elimination (special logic) | 10 | No |
| UPG_LuckyStart | 7 | Lucky Start | Spawn closer to weapon (special logic) | 4 | No |
| UPG_GlassCannon | 8 | Glass Cannon | DamageDealt +0.40 (mult), MaxHealth -0.20 (mult) | 9 | No |
| UPG_MarathonRunner | 9 | Marathon Runner | MoveSpeed +0.15 (mult) | 7 | No |
| UPG_StickyFingers | 10 | Sticky Fingers | PickupSpeed +0.50 (mult) | 5 | No |

**Consumables:**

| Asset | ID | Name | Effect | Cost | Stackable |
|-------|-----|------|--------|------|-----------|
| UPG_ShieldCharge | 11 | Shield Charge | Block first hit next round | 3 | Yes |
| UPG_SpeedBurst | 12 | Speed Burst | MoveSpeed +0.40 for 5s at round start | 2 | Yes |
| UPG_MagnetAura | 13 | Magnet Aura | Auto-collect coins 5m radius, 1 round | 4 | Yes |
| UPG_Decoy | 14 | Decoy | Spawn fake player at random spawn | 3 | Yes |
| UPG_ArmorPlating | 15 | Armor Plating | +50 temp HP next round | 5 | Yes |

**Weapon Modifiers:**

| Asset | ID | Name | Effect | Cost | Stackable |
|-------|-----|------|--------|------|-----------|
| UPG_FlamingEdge | 16 | Flaming Edge | BurnDamage +20 over 3s on throw hit | 6 | No |
| UPG_Boomerang | 17 | Boomerang | Thrown weapons return on miss (special) | 8 | No |
| UPG_HeavyImpact | 18 | Heavy Impact | Melee knockback +60% | 5 | No |
| UPG_RapidReload | 19 | Rapid Reload | Firearm fire rate +30% | 7 | No |
| UPG_ToxicTips | 20 | Toxic Tips | SlowEffect 3s (−30% speed) on hit | 8 | No |

### LevelData.cs

```csharp
[CreateAssetMenu(fileName = "NewLevel", menuName = "ArenaClash/LevelData")]
public class LevelData : ScriptableObject
{
    public string levelName;
    public string sceneName;              // Must match actual scene asset name
    public float roundDuration;           // 0 = no limit (Round 10)
    public int difficulty;                // 1–10
    public Vector3[] spawnPoints;         // Player spawn positions
    public WeaponSpawnEntry[] weaponSpawns;
    public CoinSpawnEntry[] coinSpawns;
    [TextArea] public string designNotes;
}

[System.Serializable]
public class WeaponSpawnEntry
{
    public WeaponData weapon;             // MUST reference the actual ScriptableObject asset
    public Vector3 position;
    public Quaternion rotation;
}

[System.Serializable]
public class CoinSpawnEntry
{
    public int value;                     // 1 = gold, 3 = silver
    public Vector3 position;
}
```

> **MCP Action:** Create LevelData assets for all 10 levels (details in the Levels section below). Populate spawn points, weapon spawns, and coin spawns with actual Vector3 positions matching the blockout geometry in each scene.

### Audio & VFX Libraries (ScriptableObjects)

```csharp
[CreateAssetMenu(fileName = "SFXLibrary", menuName = "ArenaClash/SFXLibrary")]
public class SFXLibrary : ScriptableObject
{
    [Header("Weapons")]
    public AudioClip throwKnife;
    public AudioClip throwHatchet;
    public AudioClip throwGeneric;
    public AudioClip fireRevolver;
    public AudioClip fireShotgun;
    public AudioClip fireFlare;
    public AudioClip meleeSwing;
    public AudioClip impactFlesh;
    public AudioClip impactMetal;
    public AudioClip impactWood;
    public AudioClip impactStone;
    public AudioClip explosion;

    [Header("Player")]
    public AudioClip jump;
    public AudioClip land;
    public AudioClip takeDamage;
    public AudioClip eliminate;
    public AudioClip footstep;

    [Header("Economy")]
    public AudioClip coinPickup;
    public AudioClip shopPurchase;
    public AudioClip shopDeny;

    [Header("Match")]
    public AudioClip countdownBeep;
    public AudioClip countdownGo;
    public AudioClip roundWin;
    public AudioClip matchVictory;

    [Header("Hazards")]
    public AudioClip fireVentWarn;
    public AudioClip fireVentActive;
    public AudioClip crumbleWarn;
    public AudioClip crumbleCollapse;
    public AudioClip gravityShift;
    public AudioClip airlockOpen;
    public AudioClip sawBlade;
    public AudioClip bouncePad;
}
```

```csharp
[CreateAssetMenu(fileName = "VFXLibrary", menuName = "ArenaClash/VFXLibrary")]
public class VFXLibrary : ScriptableObject
{
    [Header("Combat")]
    public GameObject hitSpark;           // Particle system prefab
    public GameObject hitFlash;
    public GameObject explosion;
    public GameObject fireTrail;
    public GameObject burnEffect;

    [Header("Economy")]
    public GameObject coinCollect;
    public GameObject purchaseConfirm;

    [Header("Environment")]
    public GameObject dustCloud;
    public GameObject fireVentFlames;
    public GameObject crumbleDust;
    public GameObject gravityDistortion;

    [Header("Player")]
    public GameObject spawnEffect;
    public GameObject eliminationEffect;
    public GameObject shieldEffect;
    public GameObject speedTrail;
}
```

> **MCP Action:** Create `SFXLib_Master.asset` and `VFXLib_Master.asset`. For SFX, leave AudioClip fields empty (null) — the system will null-check before playing. For VFX, create empty placeholder particle system prefabs (just a ParticleSystem GameObject with default settings, saved as a prefab) for each entry and assign them. The VFXManager will instantiate these at trigger points.

### AudioManager.cs

```csharp
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }
    public SFXLibrary sfxLibrary;
    
    [Header("Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;         // For non-positional UI sounds

    public void PlaySFX(AudioClip clip, Vector3 position, float volume = 1f)
    {
        if (clip == null) return;         // CRITICAL: null-check so placeholder empties don't crash
        AudioSource.PlayClipAtPoint(clip, position, volume);
    }

    public void PlaySFXUI(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, volume);
    }

    public void PlayMusic(AudioClip clip, bool loop = true) { /* ... */ }
    public void StopMusic() { /* ... */ }
}
```

### VFXManager.cs

```csharp
public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }
    public VFXLibrary vfxLibrary;

    public void SpawnEffect(GameObject effectPrefab, Vector3 position, Quaternion rotation, float lifetime = 2f)
    {
        if (effectPrefab == null) return; // CRITICAL: null-check for placeholders
        var instance = Instantiate(effectPrefab, position, rotation);
        Destroy(instance, lifetime);
    }
}
```

> **MCP Action:** Create a persistent `_GameManagers` GameObject in each scene (or a DontDestroyOnLoad bootstrap scene) with `AudioManager` and `VFXManager` attached. Wire the SFXLibrary and VFXLibrary assets. Add an `AudioSource` child for music and one for UI SFX.

---

## Match Flow & Game Structure

### Match Overview

A match consists of **10 rounds** in three phases:

| Rounds | Phase | Description |
|--------|-------|-------------|
| 1–3 | Early | Learn arenas, collect coins. Shop after Round 3. |
| 4–6 | Mid | More dangerous arenas, higher coin stakes. Shop after Round 6. |
| 7–9 | Late | Most hazardous arenas, highest coin stakes. Shop after Round 9. |
| 10 | Grand Finale | Winner-takes-all deathmatch. All upgrades active. No time limit. |

### Round Flow (per round)

1. **Countdown** (3 seconds) — Players see arena and weapon positions. Movement locked. Camera free-look enabled.
2. **Round Active** (5–60 seconds) — Full gameplay. Fight, collect coins, use weapons.
3. **Round End** — Timer expires OR 1 player remains.
4. **Results Screen** (5 seconds) — Placements, coins awarded, running totals.
5. **Next** — Round 3/6/9 → Shop Phase. Otherwise → next round.

### Shop Phase (after rounds 3, 6, 9)

1. Load Shop scene. Players teleported to shop space.
2. Interact with terminal → shop UI opens.
3. **35-second countdown** always visible.
4. Purchase upgrades. Host validates transactions.
5. Timer expires or all ready → next round.

### Round 10: Grand Finale

- Large arena, all weapon types, no time limit.
- All upgrades active. Arena shrinks at 60 seconds.
- Last player standing wins the match.

### Match State Machine

```csharp
public enum MatchState
{
    WaitingForPlayers,
    Countdown,         // 3s
    RoundActive,
    RoundResults,      // 5s
    ShopPhase,         // 35s after rounds 3, 6, 9
    GrandFinale,       // Round 10
    MatchOver
}
```

```csharp
// MatchController.cs (NetworkBehaviour on persistent scene)
[Networked] public MatchState CurrentState { get; set; }
[Networked] public int CurrentRound { get; set; }         // 1–10
[Networked] public TickTimer RoundTimer { get; set; }
[Networked] public TickTimer CountdownTimer { get; set; }
[Networked] public TickTimer ShopTimer { get; set; }
```

**Transitions:**
```
WaitingForPlayers → Countdown        (2–4 players ready)
Countdown (3s)    → RoundActive      (players unlocked)
RoundActive       → RoundResults     (timer expires OR 1 alive)
RoundResults (5s) → ShopPhase        (if round 3, 6, or 9)
                  → Countdown        (otherwise)
ShopPhase (35s)   → Countdown
Round 10 ends     → MatchOver
```

### Scene Management

- `MatchController` lives in a persistent scene (never unloaded).
- Arena scenes loaded/unloaded additively via `NetworkSceneManagerDefault`.
- Round 1 = always "The Circle". Round 10 = always "Grand Finale". Rounds 2–9 shuffled by difficulty.

> **MCP Action:** Create the MatchController as a prefab with all networked properties. Place it in a bootstrap/persistent scene. Create all 10 arena scenes + Shop scene with blockout geometry.

---

## Coin Economy

### Earning Coins

**Placement Rewards:**

| Placement | 2 Players | 3 Players | 4 Players |
|-----------|-----------|-----------|-----------|
| 1st | 5 | 8 | 10 |
| 2nd | 2 | 4 | 6 |
| 3rd | — | 2 | 3 |
| 4th | — | — | 1 |

**In-Level Pickups:**

| Type | Value | Visual | Location |
|------|-------|--------|----------|
| Gold Coin | 1 | Small yellow sphere, bob + spin | Accessible areas |
| Silver Coin | 3 | Larger silver sphere, bright glow | Near hazards (high risk) |

> **MCP Action:** Create `PFB_Coin_Gold` and `PFB_Coin_Silver` prefabs. Each needs: a sphere mesh with emissive material (yellow/silver), a `NetworkObject`, a `SphereCollider` set to Trigger, a `CoinPickup` NetworkBehaviour script, and a simple bob/spin animation (script-driven or Animation component). Create the actual prefab GameObjects with all components attached and configured.

### PlayerEconomy.cs

```csharp
public class PlayerEconomy : NetworkBehaviour
{
    [Networked] public int TotalCoins { get; set; }
    [Networked, Capacity(30)] public NetworkArray<int> OwnedUpgradeIds => default;

    public void AwardPlacementCoins(int placement, int playerCount) { /* Host only */ }
    public void AddCoins(int amount) { /* Host only */ }
    public bool SpendCoins(int amount) { /* Host only */ }
    public bool OwnsUpgrade(int upgradeId) { /* ... */ }
    public void AddUpgrade(int upgradeId) { /* ... */ }
}
```

---

## Weapons & Combat

### Design Philosophy

Weapons are **intentionally wonky**. Bullets spread, thrown weapons arc unpredictably, melee has exaggerated wind-ups. Skill ceiling = prediction and timing, not pixel aim.

### Weapon Class Hierarchy

```csharp
WeaponBase : NetworkBehaviour              // Pickup, drop, ownership
  ├── ThrowableWeapon : WeaponBase         // Knives, hatchets, shurikens, etc.
  ├── FirearmWeapon : WeaponBase           // Revolver, shotgun, flare gun
  └── MeleeWeapon : WeaponBase             // Bat, pan, boxing glove
```

### WeaponBase

```csharp
public abstract class WeaponBase : NetworkBehaviour
{
    public WeaponData data;
    [Networked] public PlayerRef Owner { get; set; }
    [Networked] public NetworkBool IsHeld { get; set; }

    public void RequestPickup(PlayerRef player) { /* Host validates */ }
    public abstract void Use(PlayerRef user);
    public void Drop() { /* Detach, re-enable physics */ }
}
```

### Throwing Weapons

**Mechanics:**
1. **Wind-Up:** Fire pressed → animation for `windUpDuration`. Cannot cancel. Visible to opponents.
2. **Release:** Detach, launch as physics projectile. Direction = camera forward + wonkiness offset.
3. **Wonkiness:** Random offset up to ±15° × `throwArcRandomness`.
4. **Gravity:** All thrown weapons affected. Heavy = more arc.
5. **Hit player:** Deal damage, destroy projectile, knockback.
6. **Hit environment:** Embed (knife in wall) or bounce (rubber ball).

**Networking:** Host spawns projectile via `Runner.Spawn()`, simulates physics, detects hits.

### Firearms

**Mechanics:**
- **Recoil:** Camera kicks up `recoilStrength` degrees per shot. 0.3s recovery. Stacks.
- **Spread:** Random deviation within `spreadAngle` cone.
- **Ammo:** Finite, no refills. Auto-drop when empty.
- **Hit detection:** `Runner.LagCompensation.Raycast()` on host.

### Melee

**Mechanics:**
- Swing animation with hitbox active window.
- `OverlapSphere`/`OverlapBox` during active frames.
- All hits apply knockback.
- **Frying Pan special:** Reflects projectiles during active frames.

### Weapon Spawn System

- Defined per-level in `LevelData.weaponSpawns`.
- Host spawns weapons on round start via `Runner.Spawn()`.
- Visible during countdown, pickupable when round starts.
- Glow + float animation for visibility.

> **MCP Action:** For each of the 13 weapons, create a prefab with: blockout mesh, colored material, NetworkObject, NetworkRigidbody3D (throwables), appropriate weapon script, collider. Wire the WeaponData SO reference. Create a WeaponSpawner component that reads LevelData and instantiates weapon prefabs at specified positions.

---

## Player System

### Player Prefab (PFB_PlayerCharacter)

> **MCP Action:** Create this prefab with ALL of the following components and children:

```
PFB_PlayerCharacter (GameObject)
├── Components:
│   ├── NetworkObject
│   ├── NetworkCharacterController (or NetworkRigidbody3D + CharacterController)
│   ├── PlayerController.cs
│   ├── PlayerHealth.cs
│   ├── PlayerStats.cs
│   ├── PlayerEconomy.cs
│   ├── PlayerInventory.cs (manages held weapon)
│   ├── HitboxRoot (Fusion lag compensation)
│   └── AudioSource (for positional player sounds)
├── Body (child)
│   ├── Capsule mesh (height 1.8, radius 0.3)
│   ├── CapsuleCollider
│   └── Material: MAT_Player_Blue (default, changed per player index)
├── CameraMount (child, at y=1.6 eye level)
│   ├── Camera
│   ├── FirstPersonCamera.cs
│   └── ScreenShake.cs
├── WeaponHold (child of CameraMount, offset forward/right)
│   └── (weapon prefab is parented here when held)
├── GroundCheck (child, small sphere at feet)
│   └── Used by PlayerController for ground detection
└── InteractTrigger (child)
    └── SphereCollider (trigger, radius 2) for weapon/terminal interaction
```

Create 4 player materials: `MAT_Player_Blue`, `MAT_Player_Red`, `MAT_Player_Green`, `MAT_Player_Yellow`.

### PlayerHealth.cs

```csharp
public class PlayerHealth : NetworkBehaviour
{
    [Networked] public float CurrentHealth { get; set; } = 100f;
    [Networked] public float MaxHealth { get; set; } = 100f;
    [Networked] public NetworkBool IsAlive { get; set; } = true;
    [Networked] public PlayerRef LastDamagedBy { get; set; }

    public void TakeDamage(float amount, PlayerRef source)
    {
        if (!Object.HasStateAuthority) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        LastDamagedBy = source;
        // Trigger hit VFX/SFX via [Networked] change callback or RPC
        if (CurrentHealth <= 0f) Eliminate(source);
    }

    private void Eliminate(PlayerRef eliminatedBy)
    {
        IsAlive = false;
        // Notify MatchController
        // Trigger ragdoll / elimination VFX
        // Switch to spectator camera
    }
}
```

### Elimination Rules

- Health ≤ 0 → eliminated. Killer credited.
- Falls below kill plane → eliminated. `LastDamagedBy` gets credit.
- Eliminated → ragdoll, despawn, spectator mode.
- Round ends: 1 alive = winner, or timer expires = highest health wins (tiebreak: coins).

### PlayerStats.cs

**Every system reads from PlayerStats, never hardcoded.** This is how upgrades affect gameplay.

```csharp
public class PlayerStats : NetworkBehaviour
{
    public const float BASE_HEALTH = 100f;
    public const float BASE_MOVE_SPEED = 8f;
    public const float BASE_THROW_SPEED = 1f;
    public const float BASE_DAMAGE_MULT = 1f;

    [Networked] public float MaxHealth { get; set; }
    [Networked] public float MoveSpeed { get; set; }
    [Networked] public float ThrowSpeedMultiplier { get; set; }
    [Networked] public float DamageMultiplier { get; set; }
    [Networked] public float KnockbackResistance { get; set; }
    [Networked] public float WeaponSwayMultiplier { get; set; }
    [Networked] public float CoinBonusFlat { get; set; }
    [Networked] public float PickupSpeedMultiplier { get; set; }

    public void RecalculateStats(List<UpgradeData> upgrades) { /* reset to base, apply all */ }
}
```

---

## Upgrade Shop

### ShopManager.cs

```csharp
public class ShopManager : NetworkBehaviour
{
    public UpgradeData[] allUpgrades;     // MUST be populated with all 20 SO assets

    public void RequestPurchase(PlayerRef buyer, int upgradeId)
    {
        if (!Object.HasStateAuthority) return;
        var upgrade = GetUpgradeById(upgradeId);
        var economy = GetPlayerEconomy(buyer);
        if (economy.TotalCoins < upgrade.cost) return;
        if (!upgrade.isStackable && economy.OwnsUpgrade(upgradeId)) return;
        economy.SpendCoins(upgrade.cost);
        economy.AddUpgrade(upgradeId);
    }
}
```

### Shop Scene

> **MCP Action:** Create `SCN_Shop` with:
> - A room (4 walls, floor, ceiling — blockout cubes with gray materials)
> - 4 computer terminal objects (cube + plane "screen") at designated positions
> - Each terminal has an `InteractableTerminal.cs` trigger that opens the shop UI
> - A `ShopManager` NetworkObject in the scene
> - 4 spawn points for players
> - A Canvas with the full shop UI (see layout below)

### Shop UI Layout

> **MCP Action:** Create a Canvas prefab or in-scene Canvas with:

```
Canvas (Screen Space - Overlay)
├── ShopPanel (full screen, semi-transparent dark background)
│   ├── Header
│   │   ├── TitleText ("UPGRADE TERMINAL")
│   │   ├── CoinDisplay (coin icon + balance text, top-right)
│   │   └── TimerText (countdown, top-center)
│   ├── TabBar (horizontal layout group)
│   │   ├── Tab_Passive (Button)
│   │   ├── Tab_Consumable (Button)
│   │   └── Tab_WeaponMod (Button)
│   ├── ItemGrid (GridLayoutGroup, scrollable)
│   │   └── ItemCard (prefab, instantiated per item)
│   │       ├── IconImage
│   │       ├── NameText
│   │       ├── CostBadge (coin icon + cost text)
│   │       ├── DescriptionText
│   │       └── OwnedOverlay (hidden by default)
│   ├── ReadyButton (bottom-center)
│   └── CloseButton (top-right corner)
```

Create `PFB_ShopItemCard` prefab with all UI elements. The `ShopUI.cs` script instantiates cards from the upgrade catalog.

---

## Levels & Arenas

### Level Design Philosophy

Arenas are small, tight, readable at a glance. Players understand the layout, hazards, and weapons within the 3-second countdown. Every arena forces conflict by driving players toward contested resources.

> **MCP Action for EVERY level:** Create the Unity scene with blockout geometry (cubes, planes, cylinders). Place spawn point empty GameObjects. Place weapon spawn point markers. Place coin spawn point markers. Add hazard GameObjects with their scripts. Add a VoidKillZone trigger below the play area. Add a directional light. Create the matching LevelData ScriptableObject asset with all positions populated.

### Level 1: The Circle (Tutorial Round)

| Property | Value |
|----------|-------|
| Duration | 15 seconds |
| Difficulty | 1 |
| Geometry | Circular platform (radius ~8m) floating in void. Flat, no cover. |
| Weapons | 4 throwing knives piled at center |
| Coins | 6 gold coins in ring around knife pile at ~3m radius |
| Hazards | Void kill zone below platform |
| Spawns | 4 equidistant points on platform edge, facing center |
| Design | Teaches core loop: rush center, grab knife, fight. |

> **MCP Action:** Create a large cylinder (scale 16, 0.5, 16) as the platform. Gray material. Place 4 knife spawn markers at center. Place 6 coin spawns in a circle. Place 4 player spawns at edges. Add a box collider kill zone below (y = -10, trigger, tagged "KillZone"). Skybox: default or dark.

### Level 2: The Bridge

| Property | Value |
|----------|-------|
| Duration | 20 seconds |
| Geometry | Narrow bridge (2m wide, 20m long) + two islands (5m radius each) |
| Weapons | 1 hatchet per island, 2 shurikens at bridge center |
| Coins | 1 silver at bridge center, 3 gold per island |
| Hazards | Bridge crumbles from edges inward every 5s. Void below. |
| Spawns | 2 per island |

### Level 3: The Furnace

| Property | Value |
|----------|-------|
| Duration | 30 seconds |
| Geometry | Industrial room (15m × 10m). Two parallel conveyor belts pushing east. |
| Weapons | 2 dynamite near vents, 2 bats on conveyors |
| Coins | Gold coins on conveyors (moving), silver on fire vent grates |
| Hazards | Fire vents (3s on/off), conveyors (constant push) |
| Spawns | 4 along west wall |

### Level 4: The Pit

| Property | Value |
|----------|-------|
| Duration | 25 seconds |
| Geometry | Colosseum pit. Center floor (8m diameter) + 3 tiers of platforms |
| Weapons | 2 javelins (top tier), 3 rubber balls (floor) |
| Coins | Silver on floor (risky), gold on mid-tier |
| Hazards | Rising spikes from floor starting at 5s. Lethal by 20s. |
| Spawns | 4 on mid-tier |

### Level 5: Zero-G Lab

| Property | Value |
|----------|-------|
| Duration | 40 seconds |
| Geometry | Space station (20m × 15m × 10m). Floating platforms, corridors. |
| Weapons | 1 revolver (floating zero-G zone), 3 shurikens on platforms |
| Coins | Float freely in zero-G zones |
| Hazards | Gravity shifts every 10s. Airlock opens at 30s. |
| Spawns | 4 on different platforms |

### Levels 6–9

| Level | Name | Duration | Key Mechanic |
|-------|------|----------|-------------|
| 6 | The Carousel | 30s | Rotating platform + edge saw blades + center bounce pads |
| 7 | The Freezer | 25s | Slippery ice floor + falling icicles + fog (limited visibility) |
| 8 | The Gauntlet | 45s | Linear obstacle course/race. Last to finish = eliminated. |
| 9 | The Volcano | 35s | Platforms over lava. Random chunks fall. Lava geysers. |

### Level 10: Grand Finale

| Property | Value |
|----------|-------|
| Duration | No limit (last standing wins) |
| Geometry | Large multi-level (30m × 30m). Open center, perimeter platforms, tunnels, central tower. |
| Weapons | All types. Throwables abundant. Firearms at high-risk spots. Melee at center. |
| Coins | None |
| Hazards | Saw blades in tunnels, fire vents on platforms. Arena shrinks at 60s (battle royale ring). |
| Spawns | 4 corners, max distance apart |

---

## Environmental Hazards

### Hazard Base Class

```csharp
public abstract class HazardBase : NetworkBehaviour
{
    [Networked] public NetworkBool IsActive { get; set; }
    public float damage;
    public float knockbackForce;
    public float warningDuration;

    public abstract void Activate();
    public abstract void Deactivate();
    protected abstract void OnPlayerContact(PlayerHealth player);
}
```

### Hazard Roster

| Hazard | Script | Behavior | Sync |
|--------|--------|----------|------|
| Void Kill Zone | VoidKillZone.cs | Trigger below platforms. Instant elimination. | Host detects, eliminates. |
| Fire Vent | FireVent.cs | Cycles 3s on/3s off. Damage + knockback when active. | [Networked] IsActive via TickTimer. |
| Conveyor Belt | ConveyorBelt.cs | Constant directional force on players/items. | [Networked] direction/speed. |
| Crumbling Platform | CrumblingPlatform.cs | Sections break off over time. | [Networked] array of section states. |
| Rising Spikes | RisingSpikes.cs | Floor rises as damage zone. | [Networked] spike height. |
| Gravity Zone | GravityZone.cs | Alters gravity in volume. | [Networked] gravity vector. |
| Airlock | Airlock.cs | Opens after delay, suction toward void. | [Networked] isOpen. |
| Bounce Pad | BouncePad.cs | Launches players/projectiles up. | Deterministic, no sync needed. |
| Saw Blade | SawBlade.cs | Patrols path, damage on contact. | NetworkTransform on path. |

> **MCP Action:** For each hazard type, create a prefab with: blockout mesh (red/orange material for dangerous parts), the hazard script, a collider (trigger where appropriate), and a NetworkObject. Place instances in the correct arena scenes with positions matching the level design.

---

## UI System

> **MCP Action for ALL UI:** Build every UI element as actual Unity UI (Canvas + RectTransform). Use default fonts and white/dark panels. Every button must have onClick wired to the correct method. Every text field must be bound to update from game state. Create prefabs for reusable elements.

### HUD (In-Round)

```
Canvas_HUD (Screen Space - Overlay)
├── HealthBar (top-left, Image fill + text)
├── CoinCounter (top-right, coin icon + text)
├── RoundInfo (top-center, "Round X/10" + timer)
├── WeaponIndicator (bottom-center, icon + name)
├── KillFeed (right side, vertical layout, 3 entries max)
│   └── PFB_KillFeedEntry (icon + text, fades after 3s)
├── ConsumableIcons (below health, horizontal layout)
└── SpectatorOverlay (hidden, shown on elimination)
    ├── WatchingText ("Spectating: PlayerName")
    ├── SwitchPlayerHint ("[←] [→] to switch")
    └── PlayersAliveText
```

### Results Screen

```
Canvas_Results (Screen Space - Overlay)
├── ResultsPanel (center, dark bg)
│   ├── TitleText ("ROUND X COMPLETE")
│   ├── PodiumLayout (1st/2nd/3rd/4th player cards)
│   │   └── PlayerResultCard (avatar color, name, placement, coins)
│   ├── ShopNextIndicator (visible if round 3/6/9: "SHOP PHASE NEXT")
│   └── ContinueTimer ("Next round in Xs")
```

### Main Menu

```
SCN_MainMenu:
├── Canvas_MainMenu
│   ├── TitleText ("ARENA CLASH")
│   ├── PlayButton → navigates to Lobby
│   ├── CustomizeButton → placeholder panel ("Coming Soon")
│   ├── SettingsButton → SettingsPanel
│   │   ├── VolumeSlider (Master, SFX, Music)
│   │   ├── SensitivitySlider
│   │   └── BackButton
│   └── QuitButton
├── Camera
└── Directional Light
```

### Lobby

```
SCN_Lobby (or panel within main menu scene):
├── Canvas_Lobby
│   ├── RoomCodeDisplay / RoomCodeInput
│   ├── PlayerSlots (4 slots showing connected players)
│   │   └── PlayerSlotCard (color, name, ready status icon)
│   ├── ReadyButton (toggle)
│   ├── StartCountdown (appears when all ready, "Starting in 3...")
│   └── BackButton
```

---

## Game Feel & Polish Systems

### Screen Shake (ScreenShake.cs)

```csharp
public class ScreenShake : MonoBehaviour
{
    public void Shake(float duration, float intensity)
    {
        // Offset camera local position by random vector within intensity
        // Lerp back to zero over duration
    }
}

// Triggers:
// Take damage:     Shake(0.1f, damage * 0.01f)
// Deal damage:     Shake(0.05f, 0.02f)
// Explosion:       Shake(0.3f, 0.15f)
// Elimination:     Shake(0.2f, 0.08f)
```

### Hit Effects

- **Hit flash:** Brief white overlay on damaged player mesh (swap material for 0.1s, swap back).
- **Damage numbers:** Optional floating TextMeshPro at hit point (float up, fade, destroy). Create `PFB_DamageNumber` prefab.
- **Impact particles:** Spawn VFX from VFXLibrary at collision point.

### Weapon Feel

- **Throw wind-up:** Camera FOV lerps 100 → 95 during wind-up, snaps to 100 on release.
- **Firearm recoil:** Camera pitch kicks up by `recoilStrength`, smooth 0.3s recovery.
- **Melee hit confirm:** Brief FOV pulse outward.

### Spectator Mode

- Triggered on elimination. Camera detaches, orbits arena.
- Left/right input cycles through surviving players.
- Spectator HUD overlay shows watched player and players remaining.

> **MCP Action:** Create SpectatorCamera.cs and attach to an empty in each arena scene. Create the spectator UI overlay as part of Canvas_HUD (hidden by default, shown on elimination).

---

## Phases Summary

### Phase 1: Foundation
- Create project structure, all folders, all placeholder materials
- Install/configure Photon Fusion from starter template (Host Mode)
- Create InputActionAsset with all bindings
- Build player prefab with all components
- Implement PlayerController with networked first-person movement
- Build MatchController state machine (all states, transitions, timers)
- Create 2 test arena scenes (The Circle + one other) with blockout geometry
- Create scene loading/unloading system
- Set up Layers, Tags, Physics matrix
- Set up AudioManager + VFXManager singletons (empty libraries)
- **Deliverable:** 2 players can connect, spawn, move in first person, and the match cycles through countdown → round → results → next round.

### Phase 2: Weapons & Combat
- Create all WeaponData ScriptableObject assets (13 weapons)
- Create all weapon prefabs with blockout meshes and components
- Implement WeaponBase, ThrowableWeapon, FirearmWeapon, MeleeWeapon
- Implement weapon pickup system (host-authoritative)
- Implement throw mechanics with wonkiness physics
- Implement firearm hitscan with recoil and spread
- Implement melee swing with hitbox timing
- Implement PlayerHealth and elimination
- Implement weapon spawn system per LevelData
- Wire all AudioManager/VFXManager trigger points (null-safe)
- Create coin prefabs (gold + silver) with pickup scripts
- **Deliverable:** Players can pick up weapons, throw/fire/swing them, take damage, get eliminated, and collect coins. Weapons feel wonky and fun.

### Phase 3: Economy & Shop
- Create all UpgradeData ScriptableObject assets (20 upgrades)
- Implement PlayerEconomy (coin tracking, upgrade ownership)
- Implement PlayerStats (stat aggregation from upgrades)
- Implement placement coin rewards in MatchController
- Build Shop scene with blockout geometry and terminals
- Build complete Shop UI with all panels, tabs, cards, and purchase flow
- Implement ShopManager with host-authoritative transactions
- Implement consumable activation system
- Wire upgrade effects into all gameplay systems
- **Deliverable:** Full economic loop works. Players earn coins, shop after rounds 3/6/9, buy upgrades that affect gameplay.

### Phase 4: All Levels & Hazards
- Implement all hazard scripts (9 types)
- Create all hazard prefabs with blockout meshes
- Build all 10 arena scenes with complete blockout geometry
- Place all weapon spawns, coin spawns, player spawns, hazards per level spec
- Create all LevelData ScriptableObject assets with populated data
- Implement level selection logic (Round 1 = Circle, Round 10 = Finale, 2–9 shuffled)
- Implement arena shrink mechanic for Round 10
- **Deliverable:** All 10 levels playable with unique layouts, hazards, and weapon/coin configurations.

### Phase 5: UI & Game Feel
- Build complete HUD (health, coins, timer, weapon, kill feed, consumables)
- Build Results Screen with animated coin counters
- Build Main Menu scene with navigation
- Build Lobby with player slots and ready-up
- Implement spectator mode and camera
- Implement screen shake on all triggers
- Implement hit flash and damage feedback
- Implement weapon feel (FOV zoom, recoil visuals)
- Populate AudioManager trigger points throughout all systems
- Populate VFXManager trigger points throughout all systems
- **Deliverable:** Complete UI shell. Game feels polished with feedback on every action. All audio/VFX hooks in place (silent/invisible until real assets added).

### Phase 6: Integration & Polish
- Full 10-round match integration testing
- All edge case handling (simultaneous deaths, disconnects, ties, 0 coins at shop)
- Economy balance pass
- Weapon balance pass
- Round duration tuning
- Network stress testing (simulated latency)
- Build checklist verification
- **Deliverable:** Complete, shippable game. Press Play → full match from lobby to victory.

---

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| All players die simultaneously | Highest health wins. Tie → most coins. Tie → random. |
| Player disconnects mid-round | Treated as elimination. Match continues. |
| Player disconnects during shop | Unspent coins carry. No purchases. |
| Only 1 player in lobby | Cannot start. "Need more players" message. |
| 0 coins at shop | Shop opens, can browse, can't buy. Ready button available. |
| Timer expires, multiple alive | Highest health = 1st. Tied → most coins. |
| Thrown weapon hits 2 players at once | First collision in physics tick wins. |
| Player falls off holding weapon | Weapon drops at last valid position. |
| Player buys same consumable twice | Allowed (stackable). Both activate next round. |
| All players ready before shop timer | Shop ends early, next round loads. |

---

## Layer & Tag Setup

> **MCP Action:** Configure these in Unity's Project Settings before Phase 1 work begins.

**Layers:**
- Default (0)
- Player (6)
- Weapon (7)
- Coin (8)
- Hazard (9)
- KillZone (10)
- Ground (11)
- WeaponPickup (12)

**Tags:**
- Player
- Weapon
- Coin
- Hazard
- KillZone
- SpawnPoint
- WeaponSpawn
- CoinSpawn
- ShopTerminal

**Physics Collision Matrix (disable):**
- Coin ↔ Coin (coins don't collide with each other)
- Weapon ↔ Weapon (thrown weapons pass through each other)
- Player ↔ Player (optional: disable or handle separately)

---

*This document contains everything needed to build Arena Clash from an empty Fusion starter template to a complete, playable multiplayer game. Each phase produces a testable deliverable. Every asset, prefab, scene, UI element, and ScriptableObject described here must be created as a real in-engine artifact via Unity MCP — not left as theoretical code.*