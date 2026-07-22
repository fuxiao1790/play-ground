# 001 — Runtime & ECS energy fields (vocabulary)

## Goal
Replace the interval timing vocabulary with energy vocabulary across the ECS timer
config/state and the compiled runtime setup types. This task defines the fields; producers
(002) and consumers (003) are updated in their own tasks and share this vocabulary.

## Changes

### `Assets/Scripts/System/Spawning/TimedSpawnComponents.cs`
- `TimedSpawnComponent`: remove `IntervalSeconds`, `IntervalJitterSeconds`; add
  - `float EnergyPerSecond;`      // accrual rate (from trigger)
  - `float EnergyThreshold;`      // base cost to emit one child (from child skill)
  - `float EnergyThresholdJitter;`// max per-tick jitter added to the threshold (energy units)
  Keep `Faction`, `SourceId`, `ChildKind`, `TemplateKey`, `JitterSeed`.
- `TimedSpawnStateComponent`: rename `CooldownRemaining` -> `EnergyAccumulated`. Keep `TickIndex`.

### `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `RuntimeChildSpawnSetup`: remove `IntervalSeconds`, `IntervalJitterSeconds`; add
  `EnergyPerSecond`, `EnergyThreshold`, `EnergyThresholdJitter`.
- `RuntimeAoeIntervalSpawnSetup`: same field replacement.

### `Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs`
- `ProjectileChildSpawnConfig`: replace `IntervalSeconds`/`IntervalJitterSeconds` with
  `EnergyPerSecond`/`EnergyThreshold`/`EnergyThresholdJitter`; update the constructor params,
  clamps (`Max(0f, ...)`), and the `Enabled` getter (`EnergyPerSecond > 0f && EnergyThreshold > 0f`
  instead of `IntervalSeconds > 0f`). Keep the field name/order discipline of the struct.

## Acceptance criteria
- Solution defines the new fields; the interval fields no longer exist anywhere in these
  four types.
- Does **not** compile on its own until 002/003 land (references in compiler/driver/system
  still name the old fields). That is expected — this task owns the vocabulary; 002/003 own
  the producers/consumers. Land 001+002+003 together.

## Scope
Small. Mechanical field replacement across 3 files, 5 types. No behavior here.

## Dependencies
None. Prereq for 002 and 003.
