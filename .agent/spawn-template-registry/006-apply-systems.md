# 006 — Apply systems bake the unified TimedSpawnComponent

## Scope

Bake the unified `TimedSpawnComponent` + zeroed `TimedSpawnStateComponent` + `TimedSpawnTag`
onto source entities (projectile and lingering AOE) when the spawn command carries a timed
spawner. Replace the per-domain spawner-component baking.

## Changes

- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
  (`ChildSpawnerProjectileSpawnApplySystem`): archetype includes
  `TimedSpawnComponent, TimedSpawnStateComponent, TimedSpawnTag`. On cold-create and on reuse,
  bake `cmd.TimedSpawn` and a zeroed state whose `CooldownRemaining = IntervalSeconds +
  DeterministicJitter(SourceId, JitterSeed, IntervalJitterSeconds)`. Remove
  `InitialChildSpawnStateFor`/`ChildSpawner`/`AoeSpawner` baking.
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`: the interval-spawner lingering archetype
  includes the same three unified components; bake `cmd.TimedSpawn` + zeroed state (reuse the
  existing `InitialIntervalStateFor` logic, now reading `TimedSpawnComponent`). The
  `hasIntervalSpawner` reuse-key flag and dedicated archetype/dead-slot query remain (keyed on
  `TimedSpawnTag`).
- Both apply systems: select the spawner archetype when the command has a timed spawner
  (`TemplateKey != default`), else the plain archetype.

## Acceptance criteria

- Source entities are created/reused with the unified component carrying a valid `TemplateKey`
  and zeroed timer state; pooled reuse still works (profiler `*.Cold/Reuse` unchanged in shape).
- No baking of the deleted per-domain spawner components remains.

## Dependencies

004 (unified component). Parallel with 005.
