# 006 Docs And Cleanup

## Goal

Make docs match the three-archetype lifecycle and remove stale variant-archetype
and `TimedSpawnTag` language and dead code.

## Scope

- Update docs:
  - `Docs/reference/simulation/projectile-system.md`
  - `Docs/reference/simulation/aoe-system.md`
  - `Docs/reference/simulation/spawn-template-registry.md`
  - `Docs/reference/simulation/project-aoe-system-common.md`
  - `Docs/reference/simulation/project-ecs-implementation.md`
  - `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`
  - `Docs/flows/spawn-event-to-entity.md`
  - `Docs/profiling.md` (profiler counter/system names)
  - `Docs/folder-structure.md`, `Docs/reference/architecture/architecture.md`
- Replace wording that describes basic vs timed-spawner projectiles as separate
  archetypes, and lingering vs timed-spawner lingering as separate archetypes.
- State the target explicitly: three archetypes — projectile, impact AOE,
  lingering AOE — with timed spawn as an enableable bit on projectile and
  lingering only. Keep the statement that impact AOE lacks
  `CombatLifetimeComponent` (that is still true and is the discriminator).
- Document the memory tradeoff: two archetypes removed at small chunk-width cost;
  impact hot path intentionally kept lean.

- Remove dead code:
  - `TimedSpawnTag` if no compatibility need remains.
  - The old split apply systems: `BasicProjectileSpawnApplySystem`,
    `ChildSpawnerProjectileSpawnApplySystem`, and the single `AoeSpawnApplySystem`
    (now `ImpactAoeSpawnApplySystem` + `LingeringAoeSpawnApplySystem`).
  - The removed bucketing types (`ProjectileSpawnKey`, `BucketCommandsJob`,
    `AoeSpawnKey`, `AoeSpawnBucket`) and their scratch state.
  - Stale profiler counters for the retired systems.
  - Repoint update-ordering attributes that reference removed systems:
    - `ProjectileSpawnExpansionSystem` `[UpdateBefore(BasicProjectileSpawnApplySystem)]`
      and `[UpdateBefore(ChildSpawnerProjectileSpawnApplySystem)]`.
    - `AoeSpawnExpansionSystem` `[UpdateBefore(AoeSpawnApplySystem)]`,
      `[UpdateBefore(BasicProjectileSpawnApplySystem)]`,
      `[UpdateBefore(ChildSpawnerProjectileSpawnApplySystem)]`.
    - Any `[UpdateAfter]`/`[UpdateBefore]` in `CombatStatsGatherSystem` or others
      naming the removed apply systems (compile-time break if missed).
- Do not change collision-system ordering: `ImpactAoeCollisionSystem` and
  `LingeringAoeCollisionSystem` are retained.

## Acceptance Criteria

- Docs and lifecycle comments describe one projectile, one impact AOE, and one
  lingering AOE archetype.
- Docs still state impact AOE has no `CombatLifetimeComponent`.
- Search for `TimedSpawnTag` shows no runtime dependency, or only an explicitly
  obsolete compatibility declaration.
- No references remain to `BasicProjectileSpawnApplySystem`,
  `ChildSpawnerProjectileSpawnApplySystem`, or the pre-split `AoeSpawnApplySystem`.
- Project compiles with all update-ordering attributes repointed.

## Dependencies

Depends on 001 through 005.

## Complexity

Small to medium.
