# 002 Projectile Apply Collapse

## Goal

Collapse basic and timed-spawner projectile pools into one projectile archetype,
one projectile apply path, one disabled-slot query, and one scheduled reuse job.

## Scope

- Replace the two projectile archetypes with one archetype containing:
  - existing basic projectile components
  - `TimedSpawnComponent`
  - `TimedSpawnStateComponent`
- Remove the split apply implementation:
  - delete the separate basic vs child-spawner reuse scheduling paths
  - delete separate basic vs child-spawner dead-slot queries
  - delete separate basic vs child-spawner cold archetype selection
  - keep only thin compatibility wrappers if tests require named systems during migration, with no independent query/job logic inside them.
- Collapse expansion/apply handoff to one projectile command container. If
  expansion still emits two containers at the start of this task, combine them
  before scheduling reuse; by task end the apply system schedules from one
  projectile command list.
- Reuse query becomes `ProjectileTag` plus disabled `Active`, with no `TimedSpawnTag` include/exclude.
- The reuse query must be the only projectile slot-category filter:
  - use `WithAll<ProjectileTag>()`
  - use `WithDisabled<Active>()`
  - do not use `WithAll<TimedSpawnTag>()`, `WithNone<TimedSpawnTag>()`, or exact archetype matching.
- Reuse job writes common projectile data for every command and sets timed-spawn data/enabled state from `cmd.HasTimedSpawner` or equivalent.
- Schedule exactly one projectile reuse job per frame when there are projectile
  commands. The job runs against the single projectile disabled-slot query.
- Reuse job must not iterate a broader projectile chunk set and branch per entity
  to decide basic vs timed-spawner eligibility. It may only skip entities that are
  already active/claimed as part of bounded command consumption.
- Cold-create path creates only the unified projectile archetype and sets enableable states explicitly.
- Remove per-pool bucket/query dictionaries from projectile apply. With one
  projectile archetype there is no reason to group commands by basic vs timed
  slot kind before reuse.

## Acceptance Criteria

- A disabled basic projectile slot can be reused by a timed-spawner projectile.
- A disabled timed-spawner projectile slot can be reused by a non-timed projectile.
- Projectile apply has one reuse query and one reuse job; there is no separate
  basic apply job and child-spawner apply job.
- Projectile apply no longer maintains bucket/query maps keyed by timed-spawner
  or slot kind.
- No projectile apply query uses `TimedSpawnTag`.
- No projectile reuse job contains basic-vs-timed eligibility checks such as
  `if (hasTimedSpawner != commandNeedsTimedSpawner) continue;`.
- Disabled `Active` and `ProjectileTag` are enforced by query construction, not
  by manually scanning chunks and rejecting entities in the job.
- Cold-create counter still reports overflow entity creation.
- Basic projectile behavior, tracking, collision, render, and contact gates still reset correctly.

## Dependencies

Depends on 001.

## Complexity

Medium.
