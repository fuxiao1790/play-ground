---
name: interval-trigger-energy-gain-fold
description: Fold the player's energy-gain snapshot terms into IntervalSpawnTrigger's compiled EnergyPerSecond
---

# 002 — Interval Trigger Energy Gain Fold

## Scope

Resolve `EnergyPerSecond` through the player's base/increased/multiplying
energy-gain terms instead of using the trigger's raw authored value directly.
Depends on [001-player-energy-gain-stat-fields.md](001-player-energy-gain-stat-fields.md)
for the `SkillStatSnapshot` fields this reads.

## Changes

### `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

Add a new method next to `ManaToEnergyCost`
([IntervalSpawnTrigger.cs:10-11](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L10-L11)):

```csharp
public float ResolveEnergyPerSecond(SkillStatSnapshot snapshot) =>
    Mathf.Max(0.01f,
        (energyPerSecond + snapshot.BaseEnergyGain)
        * (1f + snapshot.IncreasedEnergyGainPercent)
        * snapshot.EnergyGainMultiplier);
```

Same fold shape as every other stat in this system
(`base + added` then `* (1+increased) * postMultiplier`), with `energyPerSecond`
playing the role of `base` and no `preMultiplier` term (nothing produces one).
The `0.01f` floor matches the clamp already applied at both call sites today
([SkillSetCompiler.cs:395](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L395),
[:437](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L437)), so it moves
into this method rather than being duplicated at each call site.

### `Assets/Scripts/Skills/SkillSetCompiler.cs`

Two call sites, both already have `snapshot` in scope:

- `ApplyChildSpawn`, line 395: replace
  `EnergyPerSecond = Mathf.Max(0.01f, trigger.energyPerSecond),` with
  `EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),`.
- `ApplyAoeIntervalSpawn`, line 437: replace
  `EnergyPerSecond = Mathf.Max(0.01f, trigger.energyPerSecond),` with
  `EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),`.

No other lines in either method change. `EnergyThreshold` (computed via
`trigger.ManaToEnergyCost(childDef.ManaCost)` on the line immediately above
each site) is untouched — it is a different axis (mana-cost-derived cost per
child), not affected by this plan.

## Acceptance Criteria

- Both call sites compile against `trigger.ResolveEnergyPerSecond(snapshot)`.
- With `snapshot == SkillStatSnapshot.Identity`, `ResolveEnergyPerSecond`
  returns exactly `Mathf.Max(0.01f, energyPerSecond)` — the pre-existing
  behavior, bit-for-bit.
- With a non-identity snapshot (e.g. `BaseEnergyGain = 1f`,
  `IncreasedEnergyGainPercent = 0.5f`, `EnergyGainMultiplier = 2f`, trigger
  `energyPerSecond = 2f`), the resolved value is
  `(2 + 1) * 1.5 * 2 = 9f`.
- `EnergyThreshold` computation and every other field on
  `RuntimeChildSpawnSetup`/`RuntimeAoeIntervalSpawnSetup` remain unchanged by
  this task.

## Dependencies

Depends on 001 (`SkillStatSnapshot.BaseEnergyGain` /
`IncreasedEnergyGainPercent` / `EnergyGainMultiplier` must exist first).
