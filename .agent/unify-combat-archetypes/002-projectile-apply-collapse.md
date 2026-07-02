# 002 Projectile Apply Collapse

## Goal

Collapse the basic and child-spawner projectile pools into one projectile
archetype, one projectile apply system, one disabled-slot query, and one
scheduled reuse job.

## Current State Notes

- The split is two `SystemBase` subclasses (`BasicProjectileSpawnApplySystem`,
  `ChildSpawnerProjectileSpawnApplySystem`) over `ProjectileSpawnApplySystemBase`,
  each reading a separate expansion command container and creating a separate
  archetype.
- The per-system `ProjectileSpawnKey` bucketing is already degenerate
  (`Equals => true`, `GetHashCode => 0`): the `BucketCommandsJob` and its
  counting-sort scratch (`_keyToIndex`, `_keys`, `_counts`, `_offsets`, `_order`,
  `_deadSlotQueriesByKey`) produce exactly one bucket today. This whole apparatus
  is deletable, not just the dictionaries.
- `ProjectileSpawnCommand` already carries `HasTimedSpawner`, so the unified reuse
  job can drive `SetComponentEnabled<TimedSpawnComponent>` per command directly.

## Scope

- Replace the two projectile archetypes with one archetype containing the current
  basic components plus `TimedSpawnComponent` and `TimedSpawnStateComponent`.
- Collapse to one concrete `ProjectileSpawnApplySystem`. Remove the base/subclass
  split and the abstract `CreateArchetype`/`CommandContainer`/`BuildDeadSlotQuery`/
  `ScheduleReuseJob`/`CreateProjectileEntity` seams. Keep a thin obsolete alias
  only if tests require a named system during migration, with no independent
  query/job logic.
- Delete the degenerate bucket sort: no `ProjectileSpawnKey`, no `BucketCommandsJob`,
  no counting-sort scratch, no `_deadSlotQueriesByKey`. Schedule one reuse job
  directly against the drained command array.
- Edit `ProjectileSpawnExpansionSystem`:
  - Replace `BasicProjectileCommandContainer` + `ChildSpawnerProjectileCommandContainer`
    with one `ProjectileCommandContainer`.
  - Remove the `template.HasTimedSpawner` routing in `WriteCommand`/`ProjectileExpansionJob`;
    always `AddNoResize` to the single container.
  - Update the two-container capacity/bound bookkeeping to one bound.
  - Repoint `[UpdateBefore(BasicProjectileSpawnApplySystem)]` and
    `[UpdateBefore(ChildSpawnerProjectileSpawnApplySystem)]` to the single apply system.
- Reuse query: `WithAll<ProjectileTag>()` plus `WithDisabled<Active>()`. No
  `TimedSpawnTag` include/exclude, no exact-archetype matching.
- Reuse job writes common projectile data for every command and sets timed-spawn
  data plus `SetComponentEnabled<TimedSpawnComponent>` from `cmd.HasTimedSpawner`.
  When timed spawn is enabled it also resets `TimedSpawnStateComponent`; when
  disabled it may leave state untouched or reset to default.
- Schedule exactly one projectile reuse job per frame when there are projectile
  commands, against the single projectile disabled-slot query. The job may skip
  already-active slots for bounded consumption but must not branch on basic vs
  timed eligibility.
- Cold-create path creates only the unified projectile archetype and sets
  enableable states explicitly, including `TimedSpawnComponent` enabled/disabled.

## Acceptance Criteria

- A disabled basic projectile slot can be reused by a timed-spawner projectile,
  and vice versa.
- Projectile apply has one archetype, one reuse query, and one reuse job.
- Projectile expansion writes one command container; no `HasTimedSpawner` routing.
- No projectile apply query or archetype uses `TimedSpawnTag`.
- No projectile reuse job contains basic-vs-timed eligibility checks such as
  `if (hasTimedSpawner != commandNeedsTimedSpawner) continue;`.
- Disabled `Active` and `ProjectileTag` are enforced by query construction.
- Cold-create counter still reports overflow entity creation.
- Basic projectile behavior, tracking, collision, render, and contact gates still
  reset correctly; timed spawn ticks only when enabled.

## Dependencies

Depends on 001.

## Complexity

Medium.
