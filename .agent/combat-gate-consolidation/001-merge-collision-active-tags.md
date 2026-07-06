# 001 — Merge collision active tags into `CombatCollisionActiveTag`

## Goal

Replace `ProjectileCollisionActiveTag` and `AoeCollisionActiveTag` with one
generic enableable `CombatCollisionActiveTag`. Behavior-identical.

## Why safe

Every collision query already requires the domain identity tag
(`ProjectileTag` / `AoeTag`), so one shared collision gate cannot match the wrong
domain. Each site keeps its existing enable value; only the component type changes.

## Changes

### Component definitions
- Add `CombatCollisionActiveTag : IComponentData, IEnableableComponent` in
  [CombatEcsComponents.cs](../../Assets/Scripts/System/Common/CombatEcsComponents.cs)
  (Common, since it is now domain-agnostic). Carry a lifecycle comment matching the
  merged meaning: "enabled when the entity produces collision effects; disabled for
  visual-only or despawned entities so collision jobs skip them."
- Remove `ProjectileCollisionActiveTag` from
  [ProjectileEcsComponents.cs](../../Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs).
- Remove `AoeCollisionActiveTag` from
  [AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs).

### Archetypes + spawn apply (enable writes)
- [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs):
  archetype entry, `CollisionActiveHandle` type, the cold-create
  `SetComponentEnabled<ProjectileCollisionActiveTag>` in `RecordCommonProjectileReset`,
  and the reuse-job `collisionActiveMask` write — all -> `CombatCollisionActiveTag`.
- [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs):
  both archetypes (impact + lingering), both `CollisionActiveHandle`s, both
  reuse-job `collisionActiveMask` writes, both `RecordImpactReset` /
  `RecordLingeringReset` `SetComponentEnabled<AoeCollisionActiveTag>` calls -> the
  merged type. (`SpawnStateFor.Collision` field is unchanged in meaning; it only
  feeds the same bit under the new name.)

### Collision systems
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs):
  query/`WithAll`/handle for `ProjectileCollisionActiveTag` -> `CombatCollisionActiveTag`
  (keep `ProjectileTag` filter).
- [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs)
  and [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs):
  `AoeCollisionActiveTag` -> `CombatCollisionActiveTag` (keep `AoeTag` filter).
- [AoeCollisionCore.cs](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs):
  `RunCollision` / `Deactivate` `EnabledRefRW<AoeCollisionActiveTag>` param ->
  `EnabledRefRW<CombatCollisionActiveTag>`.

### Lifetime
- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs):
  `AoeLifetimeJob`'s `EnabledRefRW<AoeCollisionActiveTag> collisionActive` ->
  `CombatCollisionActiveTag`.

### Tests (symbol update only)
- [ProjectileSpawnPipelineTests.cs](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs)
- [ProjectileCollisionSimulationTests.cs](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs)
- [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)
- [CombatPoolCleanupSystemTests.cs](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs)
  — replace both old tag names with `CombatCollisionActiveTag`.

## Acceptance criteria

- No reference to `ProjectileCollisionActiveTag` or `AoeCollisionActiveTag` remains
  (grep clean).
- Compiles; no archetype references a removed type.
- Existing PlayMode suite passes unchanged (user-run): projectile/AOE damage,
  visual-only (no-collision) entities still skipped by collision jobs, pool
  cleanup counts unaffected.

## Scope

Small–medium. Mechanical rename/merge across ~9 source files + 4 test files. No
logic change.
