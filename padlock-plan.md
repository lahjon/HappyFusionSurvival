# Padlock Interactable — Implementation Plan

A networked keypad lock that mirrors the **Computer** station pattern. Player interacts → a
world-space keypad (digits 1–9 + Confirm) appears, only for them. Typing is local; submitting a
**correct** code replicates an unlock that fires a `UnityEvent` for the whole match (one-shot).
Correct = green flash, wrong = red flash (local feedback).

## Interpretation of the request (confirm if wrong)
- **"9 digits"** = a 3×3 keypad of digit buttons **1–9** the player taps to build a code of
  configurable length (e.g. `"1234"`), plus a **Confirm** button (and a Clear/backspace). The
  expected `Code` string is set per-instance in the Inspector.
- If you actually meant a **fixed 9-character code** (type exactly 9 digits then confirm), the only
  change is fixing the entry length to 9 — the architecture is identical.

## Decisions locked in
- **Networked unlock, one-shot.** Correct code → `[Networked] bool IsUnlocked` flips on the state
  authority, the `UnityEvent` fires once (on the authority), and the door/whatever stays open for
  everyone. Re-interacting an unlocked padlock shows "already unlocked".
- **Keypad UI + green/red feedback are LOCAL.** Only the boolean result replicates. Validation
  happens on the state authority (never trust the client's "I got it right").

## Why this shape
Follows the existing **station + session + screen** trio (exactly how `Computer` works), reusing:
- `InteractableStation` base (IInteractable boilerplate, host range re-check).
- `ComputerSession`'s camera-dock pattern (zoom to a `ScreenViewTransform`, own the camera, gate
  interactions, register with `MenuManager` for Escape).
- `ComputerScreen`'s procedural world-space canvas + button build.

No new shared primitives. We do **not** reuse the simpler `InteractableSession<T>` base because, like
the computer, the padlock docks/animates the camera (that base explicitly excludes camera-owning
sessions).

---

## Files to create

### 1. `Assets/Scripts/Stations/Padlock.cs` (NetworkBehaviour : InteractableStation)
The networked station + the authoritative code check.

- Inspector:
  - `string Code = "1234"` — expected combination (digits).
  - `int MaxLength = 4` — max entry length (drives keypad clamp). For the literal-9-digit reading,
    set `Code` to 9 chars and `MaxLength = 9`.
  - `Transform KeypadViewTransform` — camera dock pose (same role as `Computer.ScreenViewTransform`).
  - `float ZoomDuration`, `AnimationCurve ZoomEase` — copy Computer's defaults.
  - `UnityEvent OnUnlocked` — fired once when the correct code is accepted (wire the door, lights,
    etc. in the Inspector).
  - `bool ConsumeOnUnlock = true` — one-shot; after unlock `CanInteract` reflects locked-open.
- Networked state:
  - `[Networked] bool IsUnlocked { get; set; }` — replicated result, state-auth writes only.
- IInteractable surface (override from base):
  - `CanInteract` → `true` while locked; you may still allow opening when unlocked to show the
    "already open" state, or return false — TBD, default: still interactable, panel shows green.
  - `LockedReason` → `""` (lock isn't "busy", you interact to *try* the code).
  - `OnInteract(scanner)` → `scanner.GetComponent<PadlockSession>()?.TryOpen(this)`.
- Authoritative check (the important bit):
  - `[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)] void RPC_SubmitCode(string entry, RpcInfo info)`:
    - Re-validate range host-side via `IsWithinHostRange(...)` (convention from base).
    - If already `IsUnlocked` → ignore.
    - If `entry == Code` → set `IsUnlocked = true`, invoke `OnUnlocked` on the authority,
      `RPC_Result(info.Source, true)` back to the submitter for the green flash.
    - Else → `RPC_Result(info.Source, false)` for the red flash. (No state change.)
  - `[Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)] void RPC_Result(PlayerRef target, bool correct)`
    or simpler: a `[Networked]` submission-attempt counter + `OnChangedRender` if we want the flash
    to tolerate lost ticks. **Chosen:** targeted RPC for the flash (it's a one-shot local cue, and the
    authoritative `IsUnlocked` boolean already covers the durable state). Will reconcile during impl.
  - `public void OpenMenu()` — the standard zero-arg menu opener (per the menu-systems convention),
    resolving the local player's `PadlockSession` via `Runner.GetPlayerObject(LocalPlayer)` with
    `FindAnyObjectByType` fallback.

### 2. `Assets/Scripts/Stations/PadlockSession.cs` (MonoBehaviour, on Player prefab)
Local orchestrator — **direct adaptation of `ComputerSession`** (camera dock + gate + menu screen).

- `RequireComponent(GameInputActions, InputContextController)`.
- Implements `IInteractionGate` (`AllowInteractions => Current == null`) and `IMenuScreen`
  (`MenuName="Padlock"`, `DismissOnEscape=true`, `CloseFromMenu()=>RequestClose()`).
- `static bool IsAnyAtPadlock` — read by `Player.LateUpdate` to skip its own camera write (same hook
  the computer uses; see step 5).
- `public Padlock Current` + `event Action<Padlock> OpenedChanged` — the keypad UI binds to this.
- Camera phases `Idle→ZoomingIn→Docked→ZoomingOut` lerping to `Padlock.KeypadViewTransform`
  (copied from `ComputerSession`).
- `TryOpen(padlock)` / `RequestClose()` / `FinishClose()` — identical lifecycle to `ComputerSession`
  (enter inventory input mode → free cursor; `MenuManager.Open/Close`; auto-close on walk-away).
- `public void Submit(string entry)` — forwards to `Current.RPC_SubmitCode(entry)`.
- `Initialize()` called from `PlayerInput` (step 4).

### 3. `Assets/Scripts/Stations/PadlockScreen.cs` (MonoBehaviour, on Padlock prefab)
Local world-space keypad — **adaptation of `ComputerScreen`**'s procedural canvas builder.

- Builds a world-space `Canvas` on a `KeypadAnchor` transform (same `PixelsPerMeter` approach).
- Two roots like the computer:
  - **Idle**: a closed-padlock graphic / "LOCKED" (or "UNLOCKED" once `IsUnlocked`), shown to everyone.
  - **Keypad**: shown only to the local player whose `PadlockSession.Current == this`.
- Keypad layout: a **display field** (shows entered digits, masked or plain), a **3×3 grid of
  buttons 1–9**, a **Clear/⌫** button, and a **Confirm** button. Built with the same `MakeButton`
  helper style.
- Local typing state: `string _entry`. Digit press → append (clamp to `MaxLength`); Clear → reset;
  Confirm → `_session.Submit(_entry)`.
- Feedback: subscribe to the padlock's result (RPC/event) → flash the display/panel **green**
  (correct) or **red** (wrong, then clear entry). Bind via `PadlockSession.OpenedChanged` exactly
  like `ComputerScreen.TryBind/OnOpenedChanged`.
- When `IsUnlocked` becomes true, the keypad shows green/locked-open and disables further entry.

### 4. `Assets/Scripts/PlayerInput.cs` (edit — one block)
Add, alongside the existing session inits (~line 126, next to `computerSession`):
```csharp
var padlockSession = GetComponent<PadlockSession>();
if (padlockSession != null) padlockSession.Initialize();
```

### 5. `Assets/Scripts/Player.cs` (edit — one guard)
Wherever `LateUpdate` skips the camera write for `ComputerSession.IsAnyAtComputer`, also OR in
`PadlockSession.IsAnyAtPadlock` so the padlock session owns the camera while docked. (Single
boolean condition extension — find the existing `IsAnyAtComputer` check and add the padlock flag.)

---

## Prefab / scene work (Unity Editor, via MCP — will ask before mutating)
1. **`Assets/Prefabs/Padlock.prefab`** — model + `NetworkObject` + `Padlock` + `PadlockScreen` +
   `InteractionPrompt` + child `KeypadAnchor` (glass) and `KeypadView` (camera dock). Mirror the
   `Computer.prefab` structure. Use `PadlockScreen`'s `[ContextMenu] Frame Camera View` equivalent
   to place `KeypadView`.
2. **Player prefab** — add `PadlockSession` component next to `ComputerSession`.
3. Set `Code`, wire `OnUnlocked` (e.g. to a door's open method) per placed instance.

## Verification (Editor, no automated tests in this project)
- Enter Play (isolated host). Walk to padlock → prompt shows → interact → camera docks, keypad
  appears, cursor frees.
- Wrong code → red flash, entry clears, `IsUnlocked` stays false, event does not fire.
- Correct code → green flash, `OnUnlocked` fires once, `IsUnlocked` true; re-interact shows open.
- Second client (ParrelSync): sees the padlock flip to unlocked / door open after the first client
  solves it; its own keypad attempts are independent until then.
- Escape closes the keypad (via `MenuManager`); walking away auto-closes.

## Open question for you
- **Keypad shape:** 1–9 keypad building a short code (my assumption) **or** a literal 9-digit entry?
  Either works; just confirm so I size the entry/`Code` correctly.
