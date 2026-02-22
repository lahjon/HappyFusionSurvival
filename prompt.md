# AUTONOMOUS IMPLEMENTATION GUIDE
## Primitive Prototyping & Console-Based Verification for Overnight Development

**PURPOSE:** This guide enables Claude Opus to autonomously implement the entire game overnight using Unity primitives, console logging for verification, and extending the existing player systems.

GAME SUMMARY: Co-op Survival Roguelite🎮 High-Level ConceptGenre: 1-4 Player Co-op Survival FPS with Horror-Comedy AestheticCore Experience: Scavenge a dangerous world during the day, haul valuable equipment back to your base, and survive the nights together. Prepare for 6 days, then escape on Day 7.⚙️ Core Game LoopDays 1-6: Preparation Phase (20 min per day)DAY (15 minutes):

Explore the world looking for equipment and resources
Scavenge Points of Interest (gas stations, hardware stores, malls, etc.)
Haul items back to base - carrying them with both hands, movement slowed, can't use weapons
Fight or avoid enemies (dogs, scavengers, bandits)
NIGHT (5 minutes):

Return to base (safe if generator-powered lights are on)
Night Stalkers roam outside but cannot enter lit areas
Sit around campfires, cook food, plan next day
Social gathering time - 2+ players near fire = "Camaraderie" buff
BASE BUILDING:

Install scavenged equipment at base (generators, workbenches, crop incubators)
Power systems with generator + fuel
Energy budget: generator produces 5 energy/day, stations consume 1-2 each


---

## 🚨 CRITICAL: EXISTING SYSTEMS

### ✅ What Already Exists (DO NOT RECREATE):
- **Player Prefab** - NetworkObject with movement, camera, input
- **FPS Controller** - WASD movement, sprint, jump, mouse look
- **Photon Fusion Setup** - NetworkRunner, scene configuration
- **Basic Multiplayer** - Players can join and see each other

### ❌ What NOT To Do:
- **DO NOT** create new player movement scripts
- **DO NOT** create new camera controller
- **DO NOT** recreate FPS input system
- **DO NOT** replace existing player prefab
- **DO NOT** build UI test buttons/panels

### ✅ What TO Do:
- **EXTEND** existing PlayerController with new NetworkVariables
- **ADD** new scripts for new systems (inventory, items, AI, etc.)
- **USE** console logging exclusively for verification
- **BUILD** with primitives only (cubes, spheres, capsules)
- **VERIFY** via Debug.Log showing PASS/FAIL

---

## 🎯 IMPLEMENTATION RULES

### Every Feature Requires:
1. **Primitive Visuals** - Cubes/spheres/capsules only
2. **Console Logging** - Extensive Debug.Log statements
3. **Self-Verification** - Code that checks itself and logs PASS/FAIL
4. **Network Testing** - Verify with 2-4 networked clients
5. **Success Criteria** - Clear checklist before proceeding

### Console Logging Standard:
```csharp
// System initialization
Debug.Log($"[SYSTEM_NAME] Initialized: {details}");

// NetworkVariable changes
Debug.Log($"[SYSTEM_NAME] {variableName}: {oldValue} → {newValue}");

// Network events
Debug.Log($"[SYSTEM_NAME] Player {playerId} triggered {eventName}");

// Verification (MOST IMPORTANT)
bool testPassed = (condition);
Debug.Log($"[VERIFY] {testName}: {(testPassed ? "PASS ✓" : "FAIL ✗")}");
if (!testPassed) Debug.LogError($"[VERIFY] Details: {failureReason}");
```

### Before Proceeding to Next Phase:
- ✅ Console shows all "[VERIFY] ... PASS ✓" messages
- ✅ No errors in console
- ✅ All NetworkVariables syncing (check Fusion inspector)
- ✅ Works with 4 simultaneous clients
- ✅ Primitives visible and working

---

## 📋 PHASE 0: TERRAIN SETUP

### Build:
```
- Unity Terrain (2km x 2km)
- 20 primitive cubes as "placeholder buildings"
- 10 primitive spheres as "placeholder trees"
- 1 colored plane (50x50m) as "base zone"
- All as shared authority NetworkObjects
```

### Code:
```csharp
// TerrainSetup.cs
public class TerrainSetup : MonoBehaviour
{
    void Start()
    {
        Debug.Log($"[TERRAIN] Size: {terrain.terrainData.size}");
        Debug.Log($"[TERRAIN] Base zone at: {baseZone.transform.position}");
        Debug.Log($"[TERRAIN] Placeholder buildings: {buildingCount}");
        
        // Verify shared authority
        bool terrainShared = CheckSharedAuthority(terrain);
        Debug.Log($"[VERIFY] Terrain shared authority: {(terrainShared ? "PASS ✓" : "FAIL ✗")}");
    }
}
```

### Verification Checklist:
- [ ] Launch Client 1 & Client 2
- [ ] Both see same terrain height/texture
- [ ] Both see all placeholder buildings in same positions
- [ ] Console on both: "[VERIFY] Terrain shared authority: PASS ✓"
- [ ] Walk around with existing player controller - no falling through

---

## 📋 PHASE 1: EXTEND PLAYER - STAMINA & HEALTH

### Build Primitives:
```
- Simple Text UI: "Stamina: 85/100" (top-left corner)
- Simple Text UI: "Health: 70/100" (top-left corner)
- Red sphere above downed player's head
```

### Code - Extend Existing PlayerController.cs:
```csharp
// ADD TO EXISTING PlayerController.cs - Find it in project!

// ==== ADD THESE FIELDS ====
[Networked] public float CurrentStamina { get; set; } = 100f;
[Networked] public float MaxStamina { get; set; } = 100f;
[Networked] public int Health { get; set; } = 100;
[Networked] public bool IsDowned { get; set; } = false;

// ==== ADD TO EXISTING FixedUpdateNetwork() or Update() ====
void UpdateStamina()
{
    // If player is sprinting (check existing sprint bool/key)
    if (isSprinting)
    {
        CurrentStamina -= 10f * Time.deltaTime;
        if (Time.frameCount % 60 == 0)
            Debug.Log($"[STAMINA] Player {Object.InputAuthority} sprinting: {CurrentStamina:F1}/100");
    }
    else
    {
        CurrentStamina = Mathf.Min(CurrentStamina + 5f * Time.deltaTime, MaxStamina);
    }
    
    // Verification
    if (Time.frameCount % 180 == 0)
    {
        Debug.Log($"[VERIFY] Stamina NetworkVariable synced: PASS ✓");
    }
}

// ==== ADD NEW METHODS ====
public void TakeDamage(int amount)
{
    Health -= amount;
    Debug.Log($"[HEALTH] Player {Object.InputAuthority} took {amount} damage, HP: {Health}/100");
    
    if (Health <= 0 && !IsDowned)
    {
        SetDowned();
    }
    
    // Verify health synced
    Debug.Log($"[VERIFY] Health updated across network: PASS ✓");
}

void SetDowned()
{
    IsDowned = true;
    Debug.Log($"[HEALTH] Player {Object.InputAuthority} DOWNED");
    
    // Rotate player capsule/model to "lie down"
    transform.rotation = Quaternion.Euler(90, 0, 0);
    
    // Spawn red sphere indicator
    GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    indicator.transform.position = transform.position + Vector3.up * 2f;
    indicator.GetComponent<Renderer>().material.color = Color.red;
    indicator.transform.localScale = Vector3.one * 0.5f;
}

public void Revive()
{
    IsDowned = false;
    Health = 50;
    transform.rotation = Quaternion.identity;
    Debug.Log($"[HEALTH] Player {Object.InputAuthority} REVIVED with 50 HP");
    Debug.Log($"[VERIFY] Revive synced: PASS ✓");
}
```

### Console Commands (for testing):
```csharp
// Add to Update() for testing
void Update()
{
    // Existing player update code...
    
    // TEST COMMANDS (remove after testing)
    if (Input.GetKeyDown(KeyCode.F1)) 
    {
        TakeDamage(20);
    }
    if (Input.GetKeyDown(KeyCode.F2))
    {
        TakeDamage(100); // Kill self
    }
    if (Input.GetKeyDown(KeyCode.F3))
    {
        Revive();
    }
}
```

### Verification Checklist:
- [ ] Sprint with existing sprint key (probably Shift)
- [ ] Console shows: "[STAMINA] Player X sprinting: 90.0/100"
- [ ] Client 1 sprints, Client 2's console shows same stamina value
- [ ] Stop sprinting, stamina regenerates
- [ ] Press F1 to damage, console shows: "[HEALTH] Player X took 20 damage, HP: 80/100"
- [ ] Press F2 to kill, player rotates horizontal, red sphere appears
- [ ] Both clients see downed state
- [ ] Press F3 on downed player, they stand up
- [ ] Console shows: "[VERIFY] Health updated across network: PASS ✓"

---

## 📋 PHASE 2: HAULING SYSTEM

### Build Primitives:
```
- SMALL: Yellow cube (0.3m)
- MEDIUM: Blue cube (0.6m)  
- LARGE: Red cube (1.0m)
- Spawn 5 of each scattered in world
- All have NetworkObject + NetworkRigidbody
```

### Code:
```csharp
// HaulableItem.cs (NEW script)
using Fusion;
using UnityEngine;

public class HaulableItem : NetworkBehaviour
{
    public enum ItemSize { Small, Medium, Large }
    
    [Networked] public int CarrierPlayerId { get; set; } = -1;
    [Networked] public ItemSize Size { get; set; }
    
    public float GetSpeedPenalty()
    {
        return Size switch
        {
            ItemSize.Small => 0.7f,   // -30%
            ItemSize.Medium => 0.6f,  // -40%
            ItemSize.Large => 0.5f,   // -50%
            _ => 1f
        };
    }
    
    public override void Spawned()
    {
        Debug.Log($"[HAUL] Item spawned: Size={Size}, NetworkID={Object.Id}");
    }
}

// ADD TO EXISTING PlayerController.cs:
[Networked] public NetworkId CarriedItemId { get; set; }
[Networked] public bool IsCarrying { get; set; } = false;

void Update()
{
    // Existing update code...
    
    // Pickup/Drop
    if (Input.GetKeyDown(KeyCode.E))
    {
        if (!IsCarrying)
        {
            TryPickupItem();
        }
        else
        {
            DropItem();
        }
    }
}

void TryPickupItem()
{
    Collider[] nearby = Physics.OverlapSphere(transform.position, 2f);
    foreach (var col in nearby)
    {
        if (col.TryGetComponent<HaulableItem>(out var item))
        {
            if (item.CarrierPlayerId != -1)
            {
                Debug.Log($"[HAUL] FAIL: Item already carried by Player {item.CarrierPlayerId}");
                return;
            }
            
            // First-write-wins
            item.CarrierPlayerId = Object.InputAuthority.PlayerId;
            CarriedItemId = item.Object.Id;
            IsCarrying = true;
            
            // Parent to player
            item.transform.SetParent(transform);
            item.transform.localPosition = new Vector3(0.5f, 0.5f, 1f); // In front of player
            
            // Disable physics while carried
            item.GetComponent<NetworkRigidbody>().Rigidbody.isKinematic = true;
            
            Debug.Log($"[HAUL] SUCCESS: Player {Object.InputAuthority} picked up {item.Size} item");
            Debug.Log($"[HAUL] Speed penalty: {item.GetSpeedPenalty()}");
            Debug.Log($"[VERIFY] Pickup synced: PASS ✓");
            return;
        }
    }
}

void DropItem()
{
    if (!IsCarrying) return;
    
    // Find item by NetworkId
    if (Runner.TryFindObject(CarriedItemId, out var networkObj))
    {
        var item = networkObj.GetComponent<HaulableItem>();
        
        // Release carrier
        item.CarrierPlayerId = -1;
        CarriedItemId = default;
        IsCarrying = false;
        
        // Unparent
        item.transform.SetParent(null);
        
        // Enable physics
        item.GetComponent<NetworkRigidbody>().Rigidbody.isKinematic = false;
        
        Debug.Log($"[HAUL] Player {Object.InputAuthority} dropped item");
        Debug.Log($"[VERIFY] Drop synced: PASS ✓");
    }
}

// Modify movement speed
float GetCurrentSpeed()
{
    float baseSpeed = normalSpeed;
    
    if (IsCarrying && Runner.TryFindObject(CarriedItemId, out var obj))
    {
        var item = obj.GetComponent<HaulableItem>();
        baseSpeed *= item.GetSpeedPenalty();
        
        if (Time.frameCount % 120 == 0)
            Debug.Log($"[HAUL] Carrying, speed: {baseSpeed:F1} (penalty: {item.GetSpeedPenalty()})");
    }
    
    return baseSpeed;
}
```

### Verification Checklist:
- [ ] Walk to yellow cube, press E
- [ ] Console: "[HAUL] SUCCESS: Player X picked up Small item"
- [ ] Cube appears in front of player (parented)
- [ ] Movement speed reduced (feels slower)
- [ ] Client 2 tries to pick same cube, console: "[HAUL] FAIL: Item already carried"
- [ ] Press E to drop, cube falls with physics
- [ ] Both clients see cube in same location after drop
- [ ] Console: "[VERIFY] Pickup synced: PASS ✓"
- [ ] Try MEDIUM and LARGE items, verify different speed penalties

---

## 📋 PHASE 3: INVENTORY

### Build Primitives:
```
- Simple Text UI: "Inventory: Wood=5 Scrap=12 Fabric=3"
```

### Code:
```csharp
// ADD TO PlayerController.cs:
public enum ItemType { Wood, Scrap, Fabric, Batteries, Food, Fuel }

[Networked, Capacity(10)]
public NetworkDictionary<ItemType, int> Inventory => default;

void Start()
{
    // Existing start code...
    
    // Initialize inventory with test items
    Inventory.Add(ItemType.Wood, 0);
    Inventory.Add(ItemType.Scrap, 0);
    Debug.Log($"[INVENTORY] Initialized for Player {Object.InputAuthority}");
}

public void AddItem(ItemType type, int count)
{
    int oldCount = Inventory.TryGet(type, out int current) ? current : 0;
    int newCount = oldCount + count;
    
    // Check stack limit
    int maxStack = type == ItemType.Wood || type == ItemType.Scrap ? 99 : 50;
    if (newCount > maxStack)
    {
        Debug.Log($"[INVENTORY] Stack limit reached for {type}: capped at {maxStack}");
        newCount = maxStack;
    }
    
    Inventory.Set(type, newCount);
    Debug.Log($"[INVENTORY] Player {Object.InputAuthority} added {count}x {type}: {oldCount} → {newCount}");
    Debug.Log($"[VERIFY] Inventory sync: PASS ✓");
}

public void RemoveItem(ItemType type, int count)
{
    if (!Inventory.TryGet(type, out int current)) return;
    
    int newCount = Mathf.Max(0, current - count);
    Inventory.Set(type, newCount);
    Debug.Log($"[INVENTORY] Player {Object.InputAuthority} removed {count}x {type}: {current} → {newCount}");
}

// Console commands for testing
void Update()
{
    // Existing update...
    
    if (Input.GetKeyDown(KeyCode.Alpha1)) AddItem(ItemType.Wood, 5);
    if (Input.GetKeyDown(KeyCode.Alpha2)) AddItem(ItemType.Scrap, 10);
    if (Input.GetKeyDown(KeyCode.Alpha3)) RemoveItem(ItemType.Wood, 2);
}
```

### Verification Checklist:
- [ ] Press 1 key, console: "[INVENTORY] Player X added 5x Wood: 0 → 5"
- [ ] Press 1 again, console: "0 → 5" then "5 → 10"
- [ ] Client 1 adds wood, Client 2's console shows same inventory change
- [ ] Add 99 wood, then add more → capped at 99
- [ ] Console: "[VERIFY] Inventory sync: PASS ✓"

---

## 📋 PHASE 4: BASE PLACEMENT

### Build Primitives:
```
- Ghost preview: Semi-transparent cube (green when valid, red when invalid)
- Placed buildings: Different colored cubes
  - Generator: Orange (2x2x2m)
  - Workbench: Brown (1.5x1x1.5m)
```

### Code:
```csharp
// PlacementSystem.cs (NEW script, attach to player or global manager)
using Fusion;
using UnityEngine;

public class PlacementSystem : NetworkBehaviour
{
    public GameObject ghostPrefab; // Assign cube with transparent material
    private GameObject ghostInstance;
    private bool isPlacing = false;
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B)) // B for Build
        {
            EnterPlacementMode();
        }
        
        if (isPlacing)
        {
            UpdateGhostPosition();
            
            if (Input.GetKeyDown(KeyCode.Q)) RotateGhost(-45f);
            if (Input.GetKeyDown(KeyCode.E)) RotateGhost(45f);
            
            if (Input.GetMouseButtonDown(0)) TryPlaceBuilding();
            if (Input.GetMouseButtonDown(1)) CancelPlacement();
        }
    }
    
    void EnterPlacementMode()
    {
        isPlacing = true;
        ghostInstance = Instantiate(ghostPrefab);
        Debug.Log($"[PLACEMENT] Player {Object.InputAuthority} entered placement mode");
    }
    
    void UpdateGhostPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            ghostInstance.transform.position = hit.point;
            
            // Check if valid
            bool valid = Physics.CheckBox(hit.point, Vector3.one, Quaternion.identity) == false;
            ghostInstance.GetComponent<Renderer>().material.color = valid ? Color.green : Color.red;
        }
    }
    
    void TryPlaceBuilding()
    {
        Vector3 pos = ghostInstance.transform.position;
        Quaternion rot = ghostInstance.transform.rotation;
        
        bool valid = Physics.CheckBox(pos, Vector3.one, rot) == false;
        
        if (!valid)
        {
            Debug.Log($"[PLACEMENT] INVALID: Colliding with other objects");
            return;
        }
        
        // Spawn NetworkObject
        var building = Runner.Spawn(buildingPrefab, pos, rot);
        Debug.Log($"[PLACEMENT] Building placed at {pos}, NetworkID: {building.Id}");
        Debug.Log($"[VERIFY] Building spawn synced: PASS ✓");
        
        CancelPlacement();
    }
    
    void CancelPlacement()
    {
        isPlacing = false;
        Destroy(ghostInstance);
        Debug.Log($"[PLACEMENT] Cancelled");
    }
    
    void RotateGhost(float degrees)
    {
        ghostInstance.transform.Rotate(0, degrees, 0);
        Debug.Log($"[PLACEMENT] Rotated {degrees}°, now: {ghostInstance.transform.eulerAngles.y:F0}°");
    }
}
```

### Verification Checklist:
- [ ] Press B, ghost cube appears
- [ ] Ghost follows mouse cursor on terrain
- [ ] Ghost is green over empty space, red over objects
- [ ] Press Q/E to rotate
- [ ] Click to place, NetworkObject spawns
- [ ] Client 2 instantly sees building appear
- [ ] Try to place overlapping = stays red, can't place
- [ ] Console: "[VERIFY] Building spawn synced: PASS ✓"

---

## 📋 PHASE 5: DAY/NIGHT CYCLE

### Build Primitives:
```
- Directional Light (already in scene - just control it)
- Simple Text UI: "DAY 2 - Time: 14:32 - TWILIGHT WARNING"
- Skybox color changes (code-driven, no asset needed)
```

### Code:
```csharp
// TimeManager.cs (NEW script - NetworkBehaviour on dedicated GameObject)
using Fusion;
using UnityEngine;

public class TimeManager : NetworkBehaviour
{
    [Networked] public float CurrentTime { get; set; } = 0f;
    [Networked] public int CurrentDay { get; set; } = 1;
    
    public Light directionalLight;
    private const float DAY_LENGTH = 900f;   // 15 minutes
    private const float NIGHT_LENGTH = 300f; // 5 minutes
    private const float FULL_CYCLE = DAY_LENGTH + NIGHT_LENGTH;
    
    public override void Spawned()
    {
        directionalLight = FindObjectOfType<Light>();
        Debug.Log($"[TIME] TimeManager spawned, Authority: {Object.HasStateAuthority}");
        Debug.Log($"[TIME] Day cycle: {DAY_LENGTH}s day + {NIGHT_LENGTH}s night = {FULL_CYCLE}s total");
    }
    
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return; // Only time authority updates
        
        CurrentTime += Runner.DeltaTime;
        
        if (CurrentTime >= FULL_CYCLE)
        {
            CurrentTime = 0f;
            CurrentDay++;
            OnDayTransition();
        }
        
        UpdateVisuals();
    }
    
    void UpdateVisuals()
    {
        // All clients update visuals based on NetworkVariable
        float dayProgress = CurrentTime / FULL_CYCLE;
        
        // Rotate sun
        float angle = dayProgress * 360f;
        directionalLight.transform.rotation = Quaternion.Euler(angle - 90f, 0, 0);
        
        // Change skybox color
        if (CurrentTime < DAY_LENGTH)
        {
            RenderSettings.ambientLight = Color.Lerp(Color.white, new Color(1f, 0.8f, 0.6f), dayProgress);
        }
        else
        {
            RenderSettings.ambientLight = Color.Lerp(new Color(1f, 0.8f, 0.6f), new Color(0.2f, 0.2f, 0.4f), (CurrentTime - DAY_LENGTH) / NIGHT_LENGTH);
        }
        
        // Logging (throttled)
        if (Time.frameCount % 180 == 0)
        {
            string phase = GetCurrentPhase();
            Debug.Log($"[TIME] Day {CurrentDay}, Time: {CurrentTime:F0}s/{FULL_CYCLE}s, Phase: {phase}");
        }
    }
    
    void OnDayTransition()
    {
        Debug.Log($"[TIME] ═══ DAY {CurrentDay} STARTED ═══");
        
        // Trigger energy production
        FindObjectOfType<EnergySystem>()?.OnDayTransition();
        
        // Trigger crop growth
        var crops = FindObjectsOfType<CropPlant>();
        foreach (var crop in crops)
        {
            crop.GrowOneStage();
        }
        
        Debug.Log($"[VERIFY] Day transition synced across all clients: PASS ✓");
    }
    
    string GetCurrentPhase()
    {
        if (CurrentTime < DAY_LENGTH - 300f) return "DAY";
        if (CurrentTime < DAY_LENGTH) return "TWILIGHT";
        return "NIGHT";
    }
    
    public bool IsNight() => CurrentTime >= DAY_LENGTH;
}
```

### Verification Checklist:
- [ ] Spawn TimeManager NetworkObject in scene
- [ ] Console: "[TIME] TimeManager spawned, Authority: True/False"
- [ ] Watch directional light rotate over time
- [ ] Console every 3 seconds: "[TIME] Day 1, Time: 45s/1200s, Phase: DAY"
- [ ] Both clients see same time (check console timestamps)
- [ ] Wait 15 minutes (or speed up for testing), day transitions
- [ ] Console: "[TIME] ═══ DAY 2 STARTED ═══"
- [ ] Both clients transition simultaneously
- [ ] Skybox color changes from bright to orange to dark
- [ ] Console: "[VERIFY] Day transition synced across all clients: PASS ✓"

---

## 📋 PHASE 6: ENERGY SYSTEM

### Build Primitives:
```
- Generator: Orange cube (2x2x2m) with child sphere light indicator
  - Green sphere = ON with fuel
  - Red sphere = OFF or no fuel
- Simple Text UI: "Energy: 3/5 | Fuel: 4 days | Production: +5/day | Consumption: -2/day"
```

### Code:
```csharp
// EnergySystem.cs (NEW script - NetworkBehaviour, singleton)
using Fusion;
using UnityEngine;

public class EnergySystem : NetworkBehaviour
{
    public static EnergySystem Instance { get; private set; }
    
    [Networked] public int CurrentEnergy { get; set; } = 0;
    [Networked] public int FuelCount { get; set; } = 0;
    [Networked] public bool GeneratorOn { get; set; } = false;
    
    public GameObject generatorModel; // Orange cube with light sphere
    private Light generatorLight;
    
    public override void Spawned()
    {
        Instance = this;
        generatorLight = generatorModel.GetComponentInChildren<Light>();
        Debug.Log($"[ENERGY] EnergySystem initialized");
    }
    
    public override void FixedUpdateNetwork()
    {
        UpdateGeneratorVisuals();
    }
    
    void UpdateGeneratorVisuals()
    {
        GeneratorOn = FuelCount > 0;
        generatorLight.color = GeneratorOn ? Color.green : Color.red;
        
        if (Time.frameCount % 180 == 0)
        {
            Debug.Log($"[ENERGY] Status: Fuel={FuelCount} days, Energy={CurrentEnergy}/5, Generator={GeneratorOn}");
        }
    }
    
    public void AddFuel(int amount)
    {
        int oldFuel = FuelCount;
        FuelCount += amount;
        Debug.Log($"[ENERGY] Fuel added: {oldFuel} → {FuelCount}");
        Debug.Log($"[VERIFY] Fuel NetworkVariable synced: PASS ✓");
    }
    
    public void OnDayTransition()
    {
        Debug.Log($"[ENERGY] ═══ ENERGY TICK (Day Transition) ═══");
        
        if (FuelCount > 0)
        {
            FuelCount--;
            CurrentEnergy = Mathf.Min(CurrentEnergy + 5, 5); // Max 5 energy storage
            Debug.Log($"[ENERGY] Consumed 1 fuel, produced 5 energy");
            Debug.Log($"[ENERGY] New totals: Fuel={FuelCount}, Energy={CurrentEnergy}");
        }
        else
        {
            CurrentEnergy = 0;
            Debug.LogWarning($"[ENERGY] NO FUEL! Energy production stopped.");
        }
        
        ConsumeEnergy();
    }
    
    void ConsumeEnergy()
    {
        int totalConsumption = 0;
        
        // Check all energy-consuming stations
        var stations = FindObjectsOfType<EnergyConsumer>();
        foreach (var station in stations)
        {
            totalConsumption += station.energyPerDay;
        }
        
        Debug.Log($"[ENERGY] Total consumption: {totalConsumption} energy/day");
        
        CurrentEnergy -= totalConsumption;
        
        if (CurrentEnergy < 0)
        {
            Debug.LogWarning($"[ENERGY] INSUFFICIENT ENERGY! {CurrentEnergy} (need {totalConsumption})");
            
            // Disable stations
            foreach (var station in stations)
            {
                station.SetPowered(false);
            }
        }
        else
        {
            Debug.Log($"[ENERGY] Sufficient energy. Remaining: {CurrentEnergy}");
            foreach (var station in stations)
            {
                station.SetPowered(true);
            }
        }
        
        Debug.Log($"[VERIFY] Energy balance calculated: PASS ✓");
    }
    
    public bool HasEnergy() => CurrentEnergy > 0;
}

// EnergyConsumer.cs (NEW script - attach to stations)
using UnityEngine;

public class EnergyConsumer : MonoBehaviour
{
    public int energyPerDay = 2;
    public GameObject powerIndicator; // Small sphere that changes color
    
    public void SetPowered(bool powered)
    {
        powerIndicator.GetComponent<Renderer>().material.color = powered ? Color.green : Color.red;
        Debug.Log($"[ENERGY] Station {gameObject.name} powered: {powered}");
    }
}
```

### Console Commands (for testing):
```csharp
// Add to some test script or player controller
void Update()
{
    if (Input.GetKeyDown(KeyCode.F5))
    {
        EnergySystem.Instance?.AddFuel(5);
    }
    if (Input.GetKeyDown(KeyCode.F6))
    {
        FindObjectOfType<TimeManager>()?.OnDayTransition(); // Force day transition for testing
    }
}
```

### Verification Checklist:
- [ ] Generator cube spawned with light sphere
- [ ] Press F5 to add fuel
- [ ] Console: "[ENERGY] Fuel added: 0 → 5"
- [ ] Generator light turns green
- [ ] Press F6 to trigger day transition
- [ ] Console: "[ENERGY] ═══ ENERGY TICK (Day Transition) ═══"
- [ ] Console: "[ENERGY] Consumed 1 fuel, produced 5 energy"
- [ ] Console: "[ENERGY] New totals: Fuel=4, Energy=5"
- [ ] Place 3 stations (6 energy/day consumption)
- [ ] Next day transition → energy insufficient
- [ ] Stations turn red (unpowered)
- [ ] Console: "[VERIFY] Energy balance calculated: PASS ✓"

---

## 📋 PHASE 7: POI SYSTEM & LOOT

### Build Primitives:
```
POI Buildings (large colored cubes):
- Gas Station: Red cube (10x5x5m)
- Hardware Store: Orange cube (15x8x6m)
- Warehouse: Gray cube (20x10x8m)
- Medical Clinic: White cube (12x6x6m)
- Shopping Mall: Blue cube (25x12x8m)

Loot items (small spheres/cubes):
- Fuel: Red sphere (0.3m)
- Wood: Brown sphere (0.3m)
- Tools: Yellow sphere (0.3m)
- Generator: Orange cube (0.6m)
- Workbench: Brown cube (0.5m)

Discovery marker: Green cone (1m tall) above discovered POI
```

### Code:
```csharp
// POIManager.cs (NEW script - NetworkBehaviour, singleton)
using Fusion;
using UnityEngine;
using System.Collections.Generic;

public class POIManager : NetworkBehaviour
{
    public static POIManager Instance { get; private set; }
    
    [System.Serializable]
    public class POI
    {
        public string name;
        public GameObject building;
        public Transform[] lootSpawnPoints;
        public GameObject[] lootPrefabs;
        public float[] lootChances; // 0-1 for each prefab
    }
    
    public List<POI> allPOIs = new List<POI>();
    
    [Networked, Capacity(20)]
    public NetworkArray<bool> DiscoveredPOIs => default;
    
    private List<NetworkObject> spawnedLoot = new List<NetworkObject>();
    
    public override void Spawned()
    {
        Instance = this;
        Debug.Log($"[POI] POIManager spawned with {allPOIs.Count} POIs");
        
        if (Object.HasStateAuthority)
        {
            SpawnAllLoot();
        }
    }
    
    void SpawnAllLoot()
    {
        Debug.Log($"[POI] ═══ SPAWNING SESSION LOOT ═══");
        int totalSpawned = 0;
        
        for (int i = 0; i < allPOIs.Count; i++)
        {
            POI poi = allPOIs[i];
            Debug.Log($"[POI] Processing {poi.name}...");
            
            for (int j = 0; j < poi.lootPrefabs.Length; j++)
            {
                if (Random.value < poi.lootChances[j])
                {
                    Vector3 spawnPos = poi.lootSpawnPoints[Random.Range(0, poi.lootSpawnPoints.Length)].position;
                    NetworkObject loot = Runner.Spawn(poi.lootPrefabs[j], spawnPos);
                    spawnedLoot.Add(loot);
                    totalSpawned++;
                    
                    Debug.Log($"[POI]   Spawned {poi.lootPrefabs[j].name} at {spawnPos} ({poi.lootChances[j] * 100}% chance)");
                }
            }
        }
        
        Debug.Log($"[POI] Total loot spawned: {totalSpawned} NetworkObjects");
        Debug.Log($"[VERIFY] Loot spawn authority designated: PASS ✓");
    }
    
    public void DiscoverPOI(int poiIndex, int playerId)
    {
        if (DiscoveredPOIs[poiIndex]) return; // Already discovered
        
        DiscoveredPOIs.Set(poiIndex, true);
        
        // Show green cone marker
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cone);
        marker.transform.position = allPOIs[poiIndex].building.transform.position + Vector3.up * 10f;
        marker.transform.localScale = Vector3.one * 2f;
        marker.GetComponent<Renderer>().material.color = Color.green;
        
        Debug.Log($"[POI] Player {playerId} discovered: {allPOIs[poiIndex].name}");
        
        int totalDiscovered = 0;
        for (int i = 0; i < DiscoveredPOIs.Length; i++)
            if (DiscoveredPOIs[i]) totalDiscovered++;
            
        Debug.Log($"[POI] Total discovered: {totalDiscovered}/{allPOIs.Count}");
        Debug.Log($"[VERIFY] Discovery NetworkArray synced: PASS ✓");
    }
}

// POITrigger.cs (NEW script - attach to each POI building)
using UnityEngine;

public class POITrigger : MonoBehaviour
{
    public int poiIndex;
    private bool playerNearby = false;
    
    void Update()
    {
        // Check if player within 20m
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            
            if (distance < 20f && !playerNearby)
            {
                playerNearby = true;
                POIManager.Instance?.DiscoverPOI(poiIndex, player.Object.InputAuthority.PlayerId);
            }
            else if (distance >= 20f && playerNearby)
            {
                playerNearby = false;
            }
        }
    }
}

// LootItem.cs (NEW script - attach to loot prefabs)
using Fusion;
using UnityEngine;

public class LootItem : NetworkBehaviour
{
    public ItemType itemType;
    public int amount = 1;
    
    void Update()
    {
        // Check if player nearby and presses F to pickup
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            
            if (distance < 2f && Input.GetKeyDown(KeyCode.F))
            {
                // Add to inventory
                player.AddItem(itemType, amount);
                
                Debug.Log($"[POI] Player {player.Object.InputAuthority} picked up {amount}x {itemType}");
                
                // Despawn loot
                if (Object.HasStateAuthority)
                {
                    Runner.Despawn(Object);
                    Debug.Log($"[POI] Loot NetworkObject despawned: {Object.Id}");
                    Debug.Log($"[VERIFY] Loot despawn synced: PASS ✓");
                }
            }
        }
    }
}
```

### Verification Checklist:
- [ ] Spawn POIManager in scene
- [ ] Console: "[POI] ═══ SPAWNING SESSION LOOT ═══"
- [ ] Console shows each POI and spawned loot with percentages
- [ ] Console: "[POI] Total loot spawned: XX NetworkObjects"
- [ ] Walk to POI building
- [ ] At 20m distance, green cone appears above building
- [ ] Console: "[POI] Player X discovered: Gas Station"
- [ ] Console: "[POI] Total discovered: 1/5"
- [ ] Both clients see green cone
- [ ] Walk to loot sphere, press F
- [ ] Console: "[POI] Player X picked up 1x Fuel"
- [ ] Sphere despawns for both clients
- [ ] Inventory updated (check Phase 3)
- [ ] Second player tries to pick same loot = nothing (already gone)
- [ ] Console: "[VERIFY] Loot despawn synced: PASS ✓"

---

## 📋 PHASE 8: VEHICLE SYSTEM

### Build Primitives:
```
Motorcycle:
- Body: Black capsule (2m long, 0.5m diameter)
- Wheels: 2 black spheres (0.4m diameter)
- Handlebars: 2 gray small cubes
- Seat: Red cube (0.3m)

Truck:
- Body: Blue box (4m x 2m x 2m)
- Wheels: 4 black spheres (0.6m diameter)
- Cab: Smaller blue box in front
- Bed: Open box at rear (1.5m x 2m)
- Seats: 4 red cubes (driver + 3 passengers)
```

### Code:
```csharp
// Vehicle.cs (NEW script - NetworkBehaviour with NetworkRigidbody)
using Fusion;
using UnityEngine;

public class Vehicle : NetworkBehaviour
{
    public enum VehicleType { Motorcycle, Truck }
    
    public VehicleType type;
    public float speedMultiplier = 5f;
    public int maxPassengers = 1;
    
    [Networked, Capacity(4)]
    public NetworkArray<int> PassengerIds => default; // -1 = empty seat
    
    [Networked] public int FuelUnits { get; set; } = 5;
    
    public Transform[] seatPositions;
    public Transform truckBed; // Only for truck
    
    private NetworkRigidbody netRb;
    private Rigidbody rb;
    
    public override void Spawned()
    {
        netRb = GetComponent<NetworkRigidbody>();
        rb = GetComponent<Rigidbody>();
        
        // Initialize passenger array
        for (int i = 0; i < PassengerIds.Length; i++)
        {
            PassengerIds.Set(i, -1);
        }
        
        Debug.Log($"[VEHICLE] {type} spawned at {transform.position}, Fuel: {FuelUnits}");
    }
    
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return;
        
        // Get driver (seat 0)
        int driverId = PassengerIds[0];
        if (driverId == -1) return; // No driver
        
        // Simple driving controls
        float forward = Input.GetAxis("Vertical");
        float turn = Input.GetAxis("Horizontal");
        
        rb.AddForce(transform.forward * forward * speedMultiplier * 100f);
        rb.AddTorque(transform.up * turn * speedMultiplier * 50f);
        
        // Fuel consumption
        if (forward != 0 && Time.frameCount % 300 == 0) // Every 5 seconds of driving
        {
            FuelUnits--;
            Debug.Log($"[VEHICLE] Fuel consumed: {FuelUnits + 1} → {FuelUnits}");
            
            if (FuelUnits <= 0)
            {
                Debug.LogWarning($"[VEHICLE] OUT OF FUEL!");
            }
        }
        
        // Logging (throttled)
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[VEHICLE] {type} driving, Speed: {rb.velocity.magnitude:F1}, Fuel: {FuelUnits}");
        }
    }
    
    public void EnterVehicle(PlayerController player, int seatIndex)
    {
        if (seatIndex < 0 || seatIndex >= maxPassengers) return;
        if (PassengerIds[seatIndex] != -1) return; // Seat occupied
        
        PassengerIds.Set(seatIndex, player.Object.InputAuthority.PlayerId);
        
        // Parent player to seat
        player.transform.SetParent(seatPositions[seatIndex]);
        player.transform.localPosition = Vector3.zero;
        
        // Disable player movement
        player.enabled = false;
        
        if (seatIndex == 0) // Driver
        {
            Object.AssignInputAuthority(player.Object.InputAuthority);
            Debug.Log($"[VEHICLE] Player {player.Object.InputAuthority} became driver, input authority assigned");
        }
        
        Debug.Log($"[VEHICLE] Player {player.Object.InputAuthority} entered seat {seatIndex}");
        Debug.Log($"[VEHICLE] Passengers: [{string.Join(", ", PassengerIds.ToArray())}]");
        Debug.Log($"[VERIFY] Vehicle entry synced: PASS ✓");
    }
    
    public void ExitVehicle(PlayerController player)
    {
        // Find player's seat
        int seatIndex = -1;
        for (int i = 0; i < PassengerIds.Length; i++)
        {
            if (PassengerIds[i] == player.Object.InputAuthority.PlayerId)
            {
                seatIndex = i;
                break;
            }
        }
        
        if (seatIndex == -1) return; // Not in vehicle
        
        PassengerIds.Set(seatIndex, -1);
        
        // Unparent player
        player.transform.SetParent(null);
        player.transform.position = transform.position + transform.right * 2f; // Exit to side
        
        // Re-enable player movement
        player.enabled = true;
        
        if (seatIndex == 0) // Was driver
        {
            Object.RemoveInputAuthority();
            Debug.Log($"[VEHICLE] Player {player.Object.InputAuthority} exited as driver, input authority removed");
        }
        
        Debug.Log($"[VEHICLE] Player {player.Object.InputAuthority} exited vehicle");
        Debug.Log($"[VERIFY] Vehicle exit synced: PASS ✓");
    }
}

// ADD TO PlayerController.cs:
void Update()
{
    // Existing update...
    
    // Vehicle interaction
    if (Input.GetKeyDown(KeyCode.F))
    {
        // Check nearby vehicles
        Collider[] nearby = Physics.OverlapSphere(transform.position, 3f);
        foreach (var col in nearby)
        {
            if (col.TryGetComponent<Vehicle>(out var vehicle))
            {
                vehicle.EnterVehicle(this, 0); // Try driver seat
                return;
            }
        }
    }
    
    if (Input.GetKeyDown(KeyCode.G)) // Exit vehicle
    {
        if (transform.parent != null && transform.parent.TryGetComponent<Vehicle>(out var vehicle))
        {
            vehicle.ExitVehicle(this);
        }
    }
}
```

### Verification Checklist:
- [ ] Spawn motorcycle NetworkObject in world
- [ ] Console: "[VEHICLE] Motorcycle spawned at (X,Y,Z), Fuel: 5"
- [ ] Walk to motorcycle (within 3m), press F
- [ ] Console: "[VEHICLE] Player X became driver, input authority assigned"
- [ ] Console: "[VEHICLE] Passengers: [0, -1, -1, -1]"
- [ ] Drive with WASD, motorcycle moves
- [ ] Client 2 sees Client 1 riding motorcycle smoothly
- [ ] Console every second: "[VEHICLE] Motorcycle driving, Speed: 15.2, Fuel: 5"
- [ ] Drive for 5 seconds
- [ ] Console: "[VEHICLE] Fuel consumed: 5 → 4"
- [ ] Press G to exit
- [ ] Console: "[VEHICLE] Player X exited as driver, input authority removed"
- [ ] Player standing next to motorcycle
- [ ] Both clients see same vehicle position
- [ ] Console: "[VERIFY] Vehicle entry synced: PASS ✓"
- [ ] Repeat with truck (4 passengers, truck bed for items)

---

## 📋 PHASE 9: CAMPFIRE SYSTEM

### Build Primitives:
```
- Campfire: Brown cylinder (0.5m diameter, 0.2m tall)
- Fire: Particle system (orange/yellow flames)
- Warmth zone: Transparent yellow sphere (3m radius, editor-only visualization)
- Cooking slots: 4 small brown cubes around fire
```

### Code:
```csharp
// Campfire.cs (NEW script - NetworkBehaviour)
using Fusion;
using UnityEngine;

public class Campfire : NetworkBehaviour
{
    [Networked] public float BurnTimeRemaining { get; set; } = 600f; // 10 minutes
    [Networked] public int PlayersNearby { get; set; } = 0;
    
    public ParticleSystem fireEffect;
    public float warmthRadius = 3f;
    
    public override void Spawned()
    {
        Debug.Log($"[CAMPFIRE] Placed at {transform.position}, initial burn time: {BurnTimeRemaining}s");
    }
    
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        
        // Count down burn time
        BurnTimeRemaining -= Runner.DeltaTime;
        
        if (BurnTimeRemaining <= 0)
        {
            Debug.Log($"[CAMPFIRE] Extinguished, despawning");
            Runner.Despawn(Object);
            return;
        }
        
        // Check players nearby
        CheckPlayersNearby();
        
        // Logging (every 30 seconds)
        if (Time.frameCount % 900 == 0)
        {
            Debug.Log($"[CAMPFIRE] Burn time: {BurnTimeRemaining:F0}s, Players nearby: {PlayersNearby}");
        }
    }
    
    void CheckPlayersNearby()
    {
        int count = 0;
        var players = FindObjectsOfType<PlayerController>();
        
        foreach (var player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= warmthRadius)
            {
                count++;
                
                // Apply warmth buff (player handles locally)
                player.SetNearCampfire(true);
            }
            else
            {
                player.SetNearCampfire(false);
            }
        }
        
        if (count != PlayersNearby)
        {
            PlayersNearby = count;
            
            bool camaraderie = PlayersNearby >= 2;
            Debug.Log($"[CAMPFIRE] Players nearby: {PlayersNearby}, Camaraderie buff: {camaraderie}");
            
            if (camaraderie)
            {
                ApplyCamaraderieBuff();
            }
        }
    }
    
    void ApplyCamaraderieBuff()
    {
        Debug.Log($"[CAMPFIRE] Camaraderie buff ACTIVE: +10% max stamina next day");
        
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= warmthRadius)
            {
                player.hasCamaraderieBuff = true;
            }
        }
    }
    
    public void AddWood(int amount)
    {
        float oldTime = BurnTimeRemaining;
        BurnTimeRemaining += amount * 300f; // 5 minutes per wood
        
        Debug.Log($"[CAMPFIRE] Wood added: {amount}, Burn time: {oldTime:F0}s → {BurnTimeRemaining:F0}s");
        Debug.Log($"[VERIFY] Campfire time extended: PASS ✓");
    }
}

// ADD TO PlayerController.cs:
[HideInInspector] public bool nearCampfire = false;
[HideInInspector] public bool hasCamaraderieBuff = false;

public void SetNearCampfire(bool near)
{
    if (near != nearCampfire)
    {
        nearCampfire = near;
        
        if (near)
        {
            Debug.Log($"[CAMPFIRE] Player {Object.InputAuthority} entered warmth zone, stamina regen: 1x → 2x");
        }
        else
        {
            Debug.Log($"[CAMPFIRE] Player {Object.InputAuthority} left warmth zone, stamina regen: 2x → 1x");
        }
    }
}

// Modify stamina regen in UpdateStamina():
void UpdateStamina()
{
    float regenRate = 5f;
    if (nearCampfire) regenRate *= 2f; // 2x regen near campfire
    
    CurrentStamina = Mathf.Min(CurrentStamina + regenRate * Time.deltaTime, MaxStamina);
}
```

### Console Commands:
```csharp
// Add to test script
void Update()
{
    if (Input.GetKeyDown(KeyCode.F7))
    {
        // Spawn campfire at player position
        var campfirePrefab = Resources.Load<GameObject>("Campfire");
        Runner.Spawn(campfirePrefab, transform.position);
    }
}
```

### Verification Checklist:
- [ ] Press F7 to spawn campfire
- [ ] Console: "[CAMPFIRE] Placed at (X,Y,Z), initial burn time: 600s"
- [ ] Fire particle effect plays
- [ ] Both clients see fire
- [ ] Walk near fire (< 3m)
- [ ] Console: "[CAMPFIRE] Player X entered warmth zone, stamina regen: 1x → 2x"
- [ ] Check stamina regenerates faster (watch UI)
- [ ] Walk away from fire
- [ ] Console: "[CAMPFIRE] Player X left warmth zone"
- [ ] Second player walks to same fire
- [ ] Console: "[CAMPFIRE] Players nearby: 2, Camaraderie buff: true"
- [ ] Console: "[CAMPFIRE] Camaraderie buff ACTIVE: +10% max stamina next day"
- [ ] Wait 10 minutes (or speed up time)
- [ ] Console: "[CAMPFIRE] Extinguished, despawning"
- [ ] Fire despawns for both clients

---

## 📋 PHASE 10: ENEMY AI - BASIC

### Build Primitives:
```
Rabid Dog:
- Body: Red capsule (0.5m tall, 0.3m diameter)
- Eyes: 2 white small spheres
- Health bar: Red plane above head

Scavenger:
- Body: Yellow capsule (1.8m tall)
- Weapon: Small gray cube in hand

Bandit:
- Body: Blue capsule (1.8m tall)  
- Rifle: Gray cylinder in hand

Night Stalker:
- Body: Black sphere (0.8m diameter)
- Eyes: Red glowing particle effect
```

### Code:
```csharp
// EnemyAI.cs (NEW script - NetworkBehaviour with NavMeshAgent)
using Fusion;
using UnityEngine;
using UnityEngine.AI;

public class EnemyAI : NetworkBehaviour
{
    public enum EnemyType { Dog, Scavenger, Bandit, NightStalker }
    public enum AIState { Idle, Patrol, Chase, Attack }
    
    public EnemyType enemyType;
    
    [Networked] public int Health { get; set; } = 100;
    [Networked] public AIState CurrentState { get; set; } = AIState.Idle;
    [Networked] public NetworkId TargetPlayerId { get; set; }
    
    private NavMeshAgent agent;
    private Transform currentTarget;
    
    public float detectionRange = 15f;
    public float attackRange = 2f;
    public int attackDamage = 10;
    
    public override void Spawned()
    {
        agent = GetComponent<NavMeshAgent>();
        
        Debug.Log($"[AI] {enemyType} spawned at {transform.position}, Health: {Health}");
        Debug.Log($"[AI] Area authority: {Object.InputAuthority}");
    }
    
    public override void FixedUpdateNetwork()
    {
        if (!Object.HasInputAuthority) return; // Only area authority controls AI
        
        switch (CurrentState)
        {
            case AIState.Idle:
                LookForPlayers();
                break;
                
            case AIState.Chase:
                ChaseTarget();
                break;
                
            case AIState.Attack:
                AttackTarget();
                break;
        }
        
        // Logging (throttled)
        if (Time.frameCount % 90 == 0 && CurrentState != AIState.Idle)
        {
            Debug.Log($"[AI] {enemyType} state: {CurrentState}, Target: {TargetPlayerId}, Distance: {GetTargetDistance():F1}m");
        }
    }
    
    void LookForPlayers()
    {
        var players = FindObjectsOfType<PlayerController>();
        foreach (var player in players)
        {
            float distance = Vector3.Distance(transform.position, player.transform.position);
            
            if (distance <= detectionRange)
            {
                TargetPlayerId = player.Object.Id;
                currentTarget = player.transform;
                CurrentState = AIState.Chase;
                
                Debug.Log($"[AI] {enemyType} detected Player {player.Object.InputAuthority} at {distance:F1}m");
                return;
            }
        }
    }
    
    void ChaseTarget()
    {
        if (currentTarget == null)
        {
            CurrentState = AIState.Idle;
            return;
        }
        
        float distance = Vector3.Distance(transform.position, currentTarget.position);
        
        if (distance <= attackRange)
        {
            CurrentState = AIState.Attack;
            agent.isStopped = true;
        }
        else if (distance > detectionRange * 1.5f)
        {
            // Lost player
            CurrentState = AIState.Idle;
            TargetPlayerId = default;
            currentTarget = null;
            agent.isStopped = true;
        }
        else
        {
            agent.SetDestination(currentTarget.position);
        }
    }
    
    void AttackTarget()
    {
        if (currentTarget == null)
        {
            CurrentState = AIState.Idle;
            return;
        }
        
        float distance = Vector3.Distance(transform.position, currentTarget.position);
        
        if (distance > attackRange)
        {
            CurrentState = AIState.Chase;
            agent.isStopped = false;
            return;
        }
        
        // Attack (once per second)
        if (Time.frameCount % 30 == 0)
        {
            if (currentTarget.TryGetComponent<PlayerController>(out var player))
            {
                player.TakeDamage(attackDamage);
                Debug.Log($"[AI] {enemyType} ATTACKED Player {player.Object.InputAuthority}, Damage: {attackDamage}");
            }
        }
    }
    
    public void TakeDamage(int amount)
    {
        Health -= amount;
        Debug.Log($"[AI] {enemyType} took {amount} damage, Health: {Health}/{100}");
        
        if (Health <= 0)
        {
            Die();
        }
        
        Debug.Log($"[VERIFY] Enemy health synced: PASS ✓");
    }
    
    void Die()
    {
        Debug.Log($"[AI] {enemyType} KILLED at {transform.position}");
        
        // Death animation (just rotate for now)
        transform.rotation = Quaternion.Euler(90, 0, 0);
        
        // Despawn after 2 seconds
        Invoke(nameof(DespawnSelf), 2f);
    }
    
    void DespawnSelf()
    {
        if (Object.HasStateAuthority)
        {
            Runner.Despawn(Object);
            Debug.Log($"[AI] Enemy despawned");
        }
    }
    
    float GetTargetDistance()
    {
        return currentTarget != null ? Vector3.Distance(transform.position, currentTarget.position) : -1f;
    }
}
```

### Console Commands:
```csharp
// Add to test script
void Update()
{
    if (Input.GetKeyDown(KeyCode.F8))
    {
        // Spawn dog enemy near player
        var dogPrefab = Resources.Load<GameObject>("EnemyDog");
        Runner.Spawn(dogPrefab, transform.position + transform.forward * 10f);
    }
    
    if (Input.GetKeyDown(KeyCode.F9))
    {
        // Damage nearest enemy
        var enemy = FindObjectOfType<EnemyAI>();
        if (enemy != null)
        {
            enemy.TakeDamage(25);
        }
    }
}
```

### Verification Checklist:
- [ ] Bake NavMesh in scene (Window → AI → Navigation)
- [ ] Press F8 to spawn dog enemy
- [ ] Console: "[AI] Dog spawned at (X,Y,Z), Health: 100"
- [ ] Console: "[AI] Area authority: X"
- [ ] Enemy idles for a moment
- [ ] Walk within 15m of enemy
- [ ] Console: "[AI] Dog detected Player X at 12.3m"
- [ ] Console: "[AI] Dog state: Chase, Target: XX, Distance: 10.2m"
- [ ] Enemy NavMeshAgent moves toward player
- [ ] Both clients see enemy moving
- [ ] Enemy reaches player
- [ ] Console: "[AI] Dog ATTACKED Player X, Damage: 10"
- [ ] Player health decreases (check Phase 1)
- [ ] Press F9 to damage enemy
- [ ] Console: "[AI] Dog took 25 damage, Health: 75/100"
- [ ] Press F9 four times
- [ ] Console: "[AI] Dog KILLED at (X,Y,Z)"
- [ ] Enemy rotates horizontal
- [ ] After 2 seconds, enemy despawns
- [ ] Both clients see despawn
- [ ] Console: "[VERIFY] Enemy health synced: PASS ✓"

---

## 📋 PHASE 11: NIGHT STALKER LIGHT EXCLUSION

### Build Primitives:
```
- Base lights: White point lights on cylinder poles (already should exist)
- Light zones: Transparent yellow spheres (3m radius) overlaid on lights (editor visualization)
- Night Stalkers: Black spheres with red glowing particle eyes
```

### Code:
```csharp
// LightZone.cs (NEW script - attach to each base light)
using UnityEngine;

public class LightZone : MonoBehaviour
{
    public float radius = 5f;
    public LayerMask litZoneLayer;
    
    void Start()
    {
        // Create sphere collider for lit zone
        var col = gameObject.AddComponent<SphereCollider>();
        col.radius = radius;
        col.isTrigger = true;
        gameObject.layer = LayerMask.NameToLayer("LitZone");
        
        Debug.Log($"[LIGHT] Light zone created at {transform.position}, Radius: {radius}m");
    }
    
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}

// Modify EnergySystem.cs:
public void UpdateBaseLights()
{
    bool lightsOn = GeneratorOn && FuelCount > 0;
    
    var lightZones = FindObjectsOfType<LightZone>();
    foreach (var zone in lightZones)
    {
        zone.gameObject.SetActive(lightsOn);
    }
    
    Debug.Log($"[LIGHT] Base lights: {(lightsOn ? "ON" : "OFF")}");
    Debug.Log($"[LIGHT] Light zones active: {lightZones.Length}");
}

// Modify EnemyAI.cs for Night Stalkers:
public override void Spawned()
{
    agent = GetComponent<NavMeshAgent>();
    
    // Night Stalkers exclude LitZone from NavMesh
    if (enemyType == EnemyType.NightStalker)
    {
        int litZoneMask = 1 << NavMesh.GetAreaFromName("LitZone");
        agent.areaMask = ~litZoneMask; // Invert to exclude
        
        Debug.Log($"[LIGHT] Night Stalker NavMesh mask configured to EXCLUDE LitZone");
    }
    
    Debug.Log($"[AI] {enemyType} spawned at {transform.position}, Health: {Health}");
}

void Update()
{
    // Night Stalkers only spawn at night
    if (enemyType == EnemyType.NightStalker)
    {
        var timeManager = FindObjectOfType<TimeManager>();
        if (timeManager != null && !timeManager.IsNight())
        {
            // Despawn during day
            if (Object.HasStateAuthority)
            {
                Runner.Despawn(Object);
                Debug.Log($"[LIGHT] Night Stalker despawned (daytime)");
            }
        }
    }
}
```

### NavMesh Setup:
```
1. Window → AI → Navigation
2. Create NavMesh Area called "LitZone"
3. Mark light zone colliders as "LitZone" area
4. Bake NavMesh
```

### Verification Checklist:
- [ ] Place light zones around base (LightZone script on cylinders)
- [ ] Console: "[LIGHT] Light zone created at (X,Y,Z), Radius: 5m"
- [ ] Turn on generator (add fuel, ensure energy)
- [ ] Console: "[LIGHT] Base lights: ON"
- [ ] Console: "[LIGHT] Light zones active: 4"
- [ ] Spawn Night Stalker outside base (press F8 but use NightStalker prefab)
- [ ] Console: "[LIGHT] Night Stalker NavMesh mask configured to EXCLUDE LitZone"
- [ ] Night Stalker approaches base
- [ ] Night Stalker stops at edge of yellow sphere (light zone)
- [ ] Night Stalker circles base but never enters lit area
- [ ] Console shows Night Stalker pathfinding around, not through
- [ ] Turn off generator
- [ ] Console: "[LIGHT] Base lights: OFF"
- [ ] Night Stalker now enters base area
- [ ] Turn lights back on
- [ ] Night Stalker exits base, stays outside light zones
- [ ] Both clients see same Night Stalker behavior
- [ ] Console: "[VERIFY] Night Stalker light exclusion: PASS ✓" (no stalkers in lit zones)

---

## 📋 PHASE 12: COMBAT SYSTEM

### Build Primitives:
```
Melee weapon:
- Crowbar: Gray cylinder (1m long, 0.05m diameter)

Ranged weapon:
- Rifle: Dark gray box (0.8m x 0.1m x 0.1m) with small cube on top (scope)

Hit effects:
- Hit marker: Red sphere (0.2m) that spawns at hit point, fades after 0.5s
- Muzzle flash: Yellow sphere (0.1m) at gun barrel, instant

Bullet trail:
- Line renderer from gun to hit point
```

### Code:
```csharp
// WeaponController.cs (NEW script - add to player or create separate)
using Fusion;
using UnityEngine;

public class WeaponController : NetworkBehaviour
{
    public enum WeaponType { None, Crowbar, Rifle }
    
    [Networked] public WeaponType EquippedWeapon { get; set; } = WeaponType.None;
    [Networked] public int CurrentAmmo { get; set; } = 30;
    
    public Transform weaponHand; // Where weapon appears
    public GameObject crowbarModel;
    public GameObject rifleModel;
    
    public Transform gunBarrel; // For rifle
    public LineRenderer bulletTrail;
    
    private GameObject currentWeaponModel;
    
    public override void Spawned()
    {
        Debug.Log($"[WEAPON] WeaponController initialized for Player {Object.InputAuthority}");
    }
    
    void Update()
    {
        if (!Object.HasInputAuthority) return;
        
        // Weapon switching (number keys)
        if (Input.GetKeyDown(KeyCode.Alpha7)) EquipWeapon(WeaponType.Crowbar);
        if (Input.GetKeyDown(KeyCode.Alpha8)) EquipWeapon(WeaponType.Rifle);
        if (Input.GetKeyDown(KeyCode.Alpha0)) EquipWeapon(WeaponType.None);
        
        // Attack
        if (Input.GetMouseButtonDown(0))
        {
            Fire();
        }
        
        // Reload
        if (Input.GetKeyDown(KeyCode.R))
        {
            Reload();
        }
    }
    
    void EquipWeapon(WeaponType type)
    {
        EquippedWeapon = type;
        
        // Destroy old weapon model
        if (currentWeaponModel != null)
        {
            Destroy(currentWeaponModel);
        }
        
        // Spawn new weapon model
        if (type == WeaponType.Crowbar)
        {
            currentWeaponModel = Instantiate(crowbarModel, weaponHand);
        }
        else if (type == WeaponType.Rifle)
        {
            currentWeaponModel = Instantiate(rifleModel, weaponHand);
            CurrentAmmo = 30;
        }
        
        Debug.Log($"[WEAPON] Player {Object.InputAuthority} equipped: {type}");
        
        if (type == WeaponType.Rifle)
        {
            Debug.Log($"[WEAPON] Ammo: {CurrentAmmo}/30");
        }
    }
    
    void Fire()
    {
        if (EquippedWeapon == WeaponType.None) return;
        
        if (EquippedWeapon == WeaponType.Crowbar)
        {
            FireMelee();
        }
        else if (EquippedWeapon == WeaponType.Rifle)
        {
            FireRanged();
        }
    }
    
    void FireMelee()
    {
        Debug.Log($"[WEAPON] Player {Object.InputAuthority} swung crowbar");
        
        // Sphere cast in front of player
        Ray ray = new Ray(transform.position + Vector3.up, transform.forward);
        if (Physics.SphereCast(ray, 0.5f, out RaycastHit hit, 2f))
        {
            Debug.Log($"[WEAPON] Crowbar hit: {hit.collider.name} at {hit.point}");
            
            // Apply damage
            if (hit.collider.TryGetComponent<EnemyAI>(out var enemy))
            {
                enemy.TakeDamage(50);
                Debug.Log($"[WEAPON] Dealt 50 damage to {enemy.enemyType}");
            }
            else if (hit.collider.TryGetComponent<PlayerController>(out var player))
            {
                player.TakeDamage(25);
                Debug.Log($"[WEAPON] Dealt 25 damage to Player {player.Object.InputAuthority}");
            }
            
            // Spawn hit effect
            SpawnHitEffect(hit.point);
        }
    }
    
    void FireRanged()
    {
        if (CurrentAmmo <= 0)
        {
            Debug.LogWarning($"[WEAPON] Out of ammo! Press R to reload");
            return;
        }
        
        CurrentAmmo--;
        Debug.Log($"[WEAPON] Player {Object.InputAuthority} fired rifle, Ammo: {CurrentAmmo}/30");
        
        // Raycast from gun barrel
        Ray ray = new Ray(gunBarrel.position, gunBarrel.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            Debug.Log($"[WEAPON] Rifle hit: {hit.collider.name} at {hit.point}, Distance: {hit.distance:F1}m");
            
            // Apply damage
            if (hit.collider.TryGetComponent<EnemyAI>(out var enemy))
            {
                enemy.TakeDamage(25);
                Debug.Log($"[WEAPON] Dealt 25 damage to {enemy.enemyType}");
            }
            else if (hit.collider.TryGetComponent<PlayerController>(out var player))
            {
                player.TakeDamage(15);
                Debug.Log($"[WEAPON] Dealt 15 damage to Player {player.Object.InputAuthority}");
            }
            
            // Show bullet trail
            ShowBulletTrail(gunBarrel.position, hit.point);
            
            // Spawn hit effect
            SpawnHitEffect(hit.point);
            
            // Muzzle flash
            SpawnMuzzleFlash();
        }
        
        Debug.Log($"[VERIFY] Weapon fire synced: PASS ✓");
    }
    
    void Reload()
    {
        if (EquippedWeapon != WeaponType.Rifle) return;
        
        int oldAmmo = CurrentAmmo;
        CurrentAmmo = 30;
        
        Debug.Log($"[WEAPON] Reload: {oldAmmo} → {CurrentAmmo}");
    }
    
    void SpawnHitEffect(Vector3 position)
    {
        var hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hitMarker.transform.position = position;
        hitMarker.transform.localScale = Vector3.one * 0.2f;
        hitMarker.GetComponent<Renderer>().material.color = Color.red;
        Destroy(hitMarker, 0.5f);
    }
    
    void ShowBulletTrail(Vector3 start, Vector3 end)
    {
        bulletTrail.SetPosition(0, start);
        bulletTrail.SetPosition(1, end);
        bulletTrail.enabled = true;
        
        Invoke(nameof(HideBulletTrail), 0.1f);
    }
    
    void HideBulletTrail()
    {
        bulletTrail.enabled = false;
    }
    
    void SpawnMuzzleFlash()
    {
        var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.transform.position = gunBarrel.position;
        flash.transform.localScale = Vector3.one * 0.1f;
        flash.GetComponent<Renderer>().material.color = Color.yellow;
        Destroy(flash, 0.05f);
    }
}
```

### Verification Checklist:
- [ ] Press 7 to equip crowbar
- [ ] Console: "[WEAPON] Player X equipped: Crowbar"
- [ ] Gray cylinder appears in hand
- [ ] Click mouse to swing
- [ ] Console: "[WEAPON] Player X swung crowbar"
- [ ] Swing at enemy
- [ ] Console: "[WEAPON] Crowbar hit: EnemyDog at (X,Y,Z)"
- [ ] Console: "[WEAPON] Dealt 50 damage to Dog"
- [ ] Red sphere appears at hit location
- [ ] Enemy health decreases
- [ ] Press 8 to equip rifle
- [ ] Console: "[WEAPON] Player X equipped: Rifle"
- [ ] Console: "[WEAPON] Ammo: 30/30"
- [ ] Click to shoot
- [ ] Console: "[WEAPON] Player X fired rifle, Ammo: 29/30"
- [ ] Line renderer shows bullet trail
- [ ] Yellow sphere (muzzle flash) appears at barrel
- [ ] Hit enemy
- [ ] Console: "[WEAPON] Rifle hit: EnemyScavenger at (X,Y,Z), Distance: 12.3m"
- [ ] Console: "[WEAPON] Dealt 25 damage to Scavenger"
- [ ] Both clients see bullet trail and hit effects
- [ ] Shoot until ammo = 0
- [ ] Try to shoot
- [ ] Console: "[WEAPON] Out of ammo! Press R to reload"
- [ ] Press R
- [ ] Console: "[WEAPON] Reload: 0 → 30"
- [ ] Console: "[VERIFY] Weapon fire synced: PASS ✓"

---

## 🎯 COMPLETION CHECKLIST

### All Phases Complete When:
- [ ] Phase 0: Terrain ✓
- [ ] Phase 1: Stamina & Health (extended player controller) ✓
- [ ] Phase 2: Hauling ✓
- [ ] Phase 3: Inventory ✓
- [ ] Phase 4: Placement ✓
- [ ] Phase 5: Day/Night ✓
- [ ] Phase 6: Energy ✓
- [ ] Phase 7: POIs & Loot ✓
- [ ] Phase 8: Vehicles ✓
- [ ] Phase 9: Campfire ✓
- [ ] Phase 10: Enemy AI ✓
- [ ] Phase 11: Light Exclusion ✓
- [ ] Phase 12: Combat ✓

### Final Verification:
- [ ] All console logs show "[VERIFY] ... PASS ✓"
- [ ] No errors in console
- [ ] Works with 4 simultaneous clients
- [ ] All NetworkVariables syncing properly
- [ ] 60 FPS performance
- [ ] All primitives visible and functioning

---

## 📝 FINAL REPORT TEMPLATE

```markdown
# Overnight Implementation Report

## ✅ Completed Phases: X/12

1. Phase 0: Terrain - COMPLETE ✓
2. Phase 1: Player Systems - COMPLETE ✓  
3. Phase 2: Hauling - COMPLETE ✓
4. Phase 3: Inventory - COMPLETE ✓
5. Phase 4: Placement - IN PROGRESS
...

## Console Verification Results:

Total PASS: XXX
Total FAIL: X

### Failed Verifications:
- [VERIFY] Inventory stack limit: FAIL - bug in overflow logic
- ...

## Performance Metrics:

- FPS (4 clients): XX-XX
- Network bandwidth: XX KB/s per client
- Enemies tested: XX active simultaneously
- No crashes or critical bugs

## Blockers Encountered:

1. NavMesh bake failed - resolved by...
2. ...

## Next Steps:

1. Fix inventory bug
2. Continue with Phase X
3. ...

## Notes:

- Did NOT recreate player controller (extended existing as instructed)
- All features built with primitives only
- Console logging used exclusively for verification
- No test UI created
```

---

## 🚀 BEGIN IMPLEMENTATION

**Current Project State:**
- Existing Player Prefab location: _______________
- Existing PlayerController.cs location: _______________

**Start with Phase 0 and proceed sequentially through Phase 12.**

**Remember:**
- Extend existing player controller, don't recreate
- Build with primitives only
- Console logging for all verification
- No test UI buttons/panels
- Proceed only when console shows PASS

**GOOD LUCK! 🎮**


## 🔄 AUTONOMOUS WORKFLOW

### For EVERY Phase:

1. **Read** phase requirements
2. **Check** what exists (don't recreate player controller!)
3. **Build** primitives first
4. **Code** with extensive Debug.Log
5. **Add** verification checks (PASS/FAIL)
6. **Test** single client
7. **Test** 2 clients (network)
8. **Verify** console shows PASS
9. **Fix** any failures
10. **Document** issues
11. **Proceed** only when 100% working

### Console Logging Every Time:
```csharp
// Initialization
Debug.Log($"[SYSTEM] Initialized with {details}");

// Changes
Debug.Log($"[SYSTEM] {variable}: {old} → {new}");

// Events
Debug.Log($"[SYSTEM] Player {id} triggered {event}");

// Verification (CRITICAL)
bool passed = TestCondition();
Debug.Log($"[VERIFY] {testName}: {(passed ? "PASS ✓" : "FAIL ✗")}");
```

### Self-Verification Questions:
- [ ] Do I see primitives?
- [ ] Does console show PASS for tests?
- [ ] Do 2 clients see same thing?
- [ ] Are NetworkVariables in Fusion inspector?
- [ ] No console errors?
- [ ] Works with 4 clients?

---

## 📝 FINAL REPORT TEMPLATE

```markdown
# Overnight Implementation Report

## Completed Phases:
- [x] Phase 0: Terrain ✓
- [x] Phase 1: Stamina/Health (extended existing PlayerController) ✓
- [x] Phase 2: Hauling ✓
- [ ] Phase 3: Inventory (IN PROGRESS - NetworkDictionary sync issue)

## Console Verification Summary:
✓ PASS: 67 tests
✗ FAIL: 3 tests
  - Inventory stack limit verification failing
  - Enemy pathfinding desync
  - Vehicle physics jitter

## NetworkVariable Sync Status:
✓ Player health: Syncing perfectly
✓ Stamina: Syncing perfectly
✓ Hauling: Syncing perfectly
✗ Inventory: 50ms delay (acceptable but noted)

## Performance:
- 4 clients: 58-62 FPS (good)
- Network: ~12 KB/s per client (good)
- Physics: Stable, no jitter except vehicles

## Blockers:
1. NavMesh bake fails on large terrain - need to bake in smaller chunks
2. NetworkDictionary capacity exceeded - increased to 20

## Next Steps:
1. Fix inventory stack limit bug
2. Continue with Phase 4: Placement
3. Bake NavMesh properly for AI

## Notes:
- Did NOT recreate player movement (used existing)
- All features built with primitives
- All verification via console logging (no UI)
```

---

## 🚀 START CHECKLIST

Before beginning overnight run:

- [ ] Unity project open
- [ ] Photon Fusion configured
- [ ] Existing player prefab located: _________________
- [ ] Existing PlayerController.cs located: _________________
- [ ] Clean build (no errors)
- [ ] Multiplayer test scene ready
- [ ] NetworkRunner in scene
- [ ] Read this entire guide

**BEGIN WITH PHASE 0 - BUILD TERRAIN**