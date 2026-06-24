# 005 — Translator + request/event/command plumbing

**Scope:** Medium. Carry interval-spawn configs from compiled runtime defs to baked entities.
**Dependencies:** 003 + 004 (needs the component/template shapes).

## Changes

1. Translator [SkillSpawnTranslator.cs](Assets/Scripts/Skills/SkillSpawnTranslator.cs):
   - `SpawnProjectile`: also build an `IntervalAoeChild` config from `def.AoeIntervalSpawnSetup`
     (when present) and pass it through the projectile spawn request. Existing
     `def.BuildChildSpawnConfig(...)` still handles proj→proj.
   - `SpawnAoe`: build proj-child and/or aoe-child interval configs from the AOE def's
     `ChildSpawnSetup` / `AoeIntervalSpawnSetup` and pass them into the `AoeSpawnRequest`.
   - Add `BuildIntervalAoeChild(RuntimeAoeDefinition child, ...)` and
     `BuildIntervalProjectileChild(RuntimeProjectileDefinition child, ...)` helpers analogous
     to the existing `RuntimeProjectileDefinition.BuildChildSpawnConfig` /
     `BuildChildImpactAoeSnapshot`. Honor stack-effect snapshots on children the same way.

2. Projectile chain
   ([ProjectileSpawnRequest.cs](Assets/Scripts/System/Projectile/ProjectileSpawnRequest.cs),
   `ProjectileSpawnEvent`, `ProjectileSpawnCommand` in
   [ProjectileSpawnExpansionSystem.cs](Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs)):
   add the `AoeIntervalSpawner` config + `ChildKind`. Route through the existing
   `HasChildSpawner` path so it lands in `ChildSpawnerProjectileCommandContainer` and is baked
   by `ChildSpawnerProjectileSpawnApplySystem`.

3. AOE chain
   ([AoeRuntimeEvents.cs](Assets/Scripts/System/Aoe/AoeRuntimeEvents.cs) `AoeSpawnRequest`,
   `AoeSpawnEvent`/`AoeSpawnCommand` in
   [AoeSpawnPipeline.cs](Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs)): add interval-spawner
   fields (`ChildKind`, interval/jitter, both child templates) + a `hasIntervalSpawner` flag.
   Propagate through `AoeExpansionJob` in
   [AoeSpawnExpansionSystem.cs](Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs).
   - Builder paths that must pass `default` (no interval spawner): `BuildImpactAoeEvent`,
     `BuildOnHitAoeSpawnEvent`, and stack detonation snapshots. Interval spawners attach only
     to root/managed-submitted AOEs.

4. `CombatRoot.Spawn(AoeSpawnRequest)` and any intermediate request→event builders forward the
   new fields (default when absent).

## Acceptance criteria

- Unity compiles; all existing spawn paths still pass `default` and behave unchanged.
- Each of the 4 combinations spawns end-to-end in Play mode (proj→proj regression + the 3
  new ones).
- New fields default to "disabled" everywhere they're not explicitly set (no accidental
  spawners on impact/on-hit AOEs).
