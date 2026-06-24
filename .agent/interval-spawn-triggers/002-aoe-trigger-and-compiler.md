# 002 — AoeIntervalSpawnTrigger type + compiler dispatch

**Scope:** Medium. Authoring type + runtime setup fields + compiler routing. No ECS yet.
**Dependencies:** 001.

## Changes

1. New trigger
   `Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs` (new `.cs.meta` GUID), mirroring
   the renamed trigger's fields: `intervalSeconds`, `intervalJitterPercent`, `spawnCount`,
   `sideSpreadDegrees`. `SourceSkillTags = Projectile | Aoe`, `TargetSkillTags = Aoe`.
   `CreateAssetMenu` → `"PlayGround/Skills/Triggers/Aoe Interval Spawn"`. Create a starter
   `.asset` under `Assets/ScriptableObjects/Triggers/`.

2. Runtime setup fields — let either source type carry either child kind:
   - New `RuntimeAoeIntervalSpawnSetup`
     `{ int SpawnerId; RuntimeAoeDefinition ChildDefinition; float IntervalSeconds; float IntervalJitterSeconds; int Count; float SideSpreadDegrees; }`
     (place beside `RuntimeChildSpawnSetup` in
     [RuntimeProjectileDefinition.cs](Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs)
     or a new file).
   - [RuntimeProjectileDefinition.cs](Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs):
     keep `ChildSpawnSetup`; add `AoeIntervalSpawnSetup`.
   - [RuntimeAoeDefinition.cs](Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs): add
     `ChildSpawnSetup` (reuse `RuntimeChildSpawnSetup`) **and** `AoeIntervalSpawnSetup`.

3. Compiler [SkillSetCompiler.cs](Assets/Scripts/Skills/SkillSetCompiler.cs): replace the
   single `ChildSpawnTrigger` branch with two, dispatching on the **parent runtime type**:
   - `ProjectileIntervalSpawnTrigger`: compile child → `RuntimeProjectileDefinition`; set
     `ChildSpawnSetup` on the parent. Generalize the existing `ApplyChildSpawn` to accept a
     parent that is either `RuntimeProjectileDefinition` or `RuntimeAoeDefinition` (both now
     have the property).
   - `AoeIntervalSpawnTrigger`: compile child → `RuntimeAoeDefinition`; build a
     `RuntimeAoeIntervalSpawnSetup` (apply `spawnCount` additive to child `Count`, floor 1;
     convert `intervalJitterPercent` → seconds as the projectile path does) and assign
     `AoeIntervalSpawnSetup`.
   - If the parent is an AOE with `LifetimeSeconds <= 0` (pulse, not lingering), skip the
     assignment (validator warns in 006). Keep the shared `nextChildSpawnerId` counter for
     both kinds so spawner ids stay globally unique.

## Acceptance criteria

- Unity compiles; `AoeIntervalSpawnTrigger` appears in the Create menu and serializes.
- Compiling a loadout populates exactly the right setup field for each of the 4 source×child
  combinations (assert in 006 tests):
  proj source + proj child → `RuntimeProjectileDefinition.ChildSpawnSetup`;
  proj source + aoe child → `RuntimeProjectileDefinition.AoeIntervalSpawnSetup`;
  lingering-aoe source + proj child → `RuntimeAoeDefinition.ChildSpawnSetup`;
  lingering-aoe source + aoe child → `RuntimeAoeDefinition.AoeIntervalSpawnSetup`.
- No ECS/runtime spawning yet — purely compile-tree population.
