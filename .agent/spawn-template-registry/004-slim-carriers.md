# 004 — Slim the spawner components/events/commands

## Scope

Replace the embedded child templates in the spawner components, events, and commands with the
`Hash128 TemplateKey`, and rename the mislabeled `SpawnerId`.

## Changes

1. **Spawner components** — drop embedded templates, add `Hash128 TemplateKey`:
   - `Assets/Scripts/System/Aoe/AoeEcsComponents.cs` `AoeIntervalSpawnerComponent`: remove
     `ProjectileChild` + `AoeChild`; keep `ChildKind, IntervalSeconds, IntervalJitterSeconds`;
     add `Hash128 TemplateKey`.
   - `Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs`
     `ProjectileChildSpawnerComponent` and `AoeIntervalSpawnerComponent`: remove `Child`; add
     `Hash128 TemplateKey`.
   - The hot timer-state components (`ProjectileChildSpawnStateComponent` /
     `AoeIntervalSpawnStateComponent`) are **unchanged**.

2. **Events/commands/requests** carry the slim spawner struct (with `TemplateKey`):
   - `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs` (`AoeSpawnEvent`, `AoeSpawnCommand`).
   - `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
     (`ProjectileSpawnEvent`, `ProjectileSpawnCommand`) and
     `Assets/Scripts/System/Projectile/ProjectileSpawnRequest.cs`.
   - `Assets/Scripts/System/Common/CombatRoot.cs` event builders (`AoeEventFor`,
     `ProjectileEventFor`, `ChildSpawnerComponentFor`) forward `TemplateKey` instead of building
     the embedded template.

3. **Rename `SpawnerId` → `JitterSeed`** (it's the deterministic jitter seed, not a registry id)
   across the spawner components, `*SpawnStateComponent`, the apply systems, and the tick
   systems' `DeterministicJitter`/`ChildId` calls. Update `SkillSpawnTranslator`/`SkillSetCompiler`
   field names accordingly (the compiler's `nextChildSpawnerId` → `nextJitterSeed`).

## Acceptance criteria

- `sizeof(AoeSpawnCommand)` and `sizeof(AoeIntervalSpawnerComponent)` drop to the tens-of-bytes
  range (no embedded template).
- Project compiles; no remaining references to the removed embedded-template fields.
- `SpawnerId` no longer appears; `JitterSeed` used consistently.

## Dependencies

001 (template/key types). Precedes 005 and 006.
