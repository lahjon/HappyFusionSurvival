# Hunger module — DORMANT BY DESIGN

This folder is the survival hunger/food mechanic, extracted out of `Player` / `UIShooter` during the
**Purge × Stardew** pivot. It is **intentionally not wired into the game** and should stay that way unless
someone is deliberately re-enabling hunger.

> Not a survival game. Hunger / food-as-survival / "day N of 7" framing is **out** of the current design
> (see the repo root `CLAUDE.md`). This module is kept — not deleted — only so the work can be revived
> cheaply if the design changes. Do **not** re-inline any of it into `Player`.

## What's here

| File | Role |
|------|------|
| `HungerSystem.cs` | `NetworkBehaviour` owning the networked `Hunger` stat. Drains in `FixedUpdateNetwork`, refills on Day entry via `MatchManager.PhaseChanged`. |
| `FoodCapability.cs` | An `ItemCapability` facet: eating restores hunger **only if a `HungerSystem` is present** on the eater. No-ops otherwise. |
| `HungerBarUI.cs` | Local UI that finds the local player's `HungerSystem` and shows a bar. Hides itself when no system exists. |

## Current runtime behaviour (module off)

`HungerSystem` is **not** on the `Player` prefab, so:

- Eating a food item no-ops (the `FoodCapability` finds no `HungerSystem`).
- The hunger bar hides itself.
- `Player` carries **no** hunger fields — core movement code is clean and must stay that way.

`FoodCapability` is still referenced by some food `ItemDefinition` assets (e.g. `Food.asset`). That's
harmless while the system is off, and is what lets food become useful again the moment hunger is switched
back on.

## Re-enabling (only if the design pivots back)

1. Add the `HungerSystem` component to the `Player` NetworkObject in the prefab via the **Inspector**
   (not via MCP — adding a `NetworkBehaviour` through MCP corrupts the Fusion bake).
2. Let Fusion re-bake the prefab (reimport it).
3. `FoodCapability` and `HungerBarUI` auto-detect the system — no other wiring required.
