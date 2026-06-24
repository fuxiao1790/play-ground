# 006 — Apply systems bake the slim spawner component

## Scope

Update the spawn-apply systems to bake the slim spawner component (with `TemplateKey`) and the
zeroed hot timer state from the command. Archetypes are otherwise unchanged.

## Changes

- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`: where it currently sets
  `cmd.IntervalSpawner` (`RecordAoeReset` / `AoeSpawnJob`), bake the slim
  `AoeIntervalSpawnerComponent` (now `{ ChildKind, IntervalSeconds, IntervalJitterSeconds,
  TemplateKey }`) and the zeroed `AoeIntervalSpawnStateComponent` (initial cooldown from
  `IntervalSeconds` + jitter via `JitterSeed`). The `intervalSpawnerLingeringArchetype` and the
  `hasIntervalSpawner` key flag are unchanged (the spawner component just got smaller).
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
  (`ChildSpawnerProjectileSpawnApplySystem`): same — bake the slim
  `ProjectileChildSpawnerComponent`/`AoeIntervalSpawnerComponent` + zeroed
  `ProjectileChildSpawnStateComponent` from the command.

## Acceptance criteria

- Spawner entities are created/reused with the slim component carrying a valid `TemplateKey`
  and zeroed timer state; pooled reuse still works (profiler `*.Cold/Reuse` counters behave as
  before).
- No baking of removed embedded-template fields.

## Dependencies

004 (slim carriers). Can proceed in parallel with 005.
