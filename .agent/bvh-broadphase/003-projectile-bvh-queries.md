# 003 - Projectile BVH Queries

## Change

Migrate `ProjectileDiscreteCollisionSystem` and
`ProjectileContinuousCollisionSystem` from collision cells to shared BVH and
from generated per-entity execution to explicit `IJobChunk` loops.

Each scheduled chunk invocation creates one traversal workspace outside its
enabled-entity loop. Reset/reseed workspace per projectile. Continuous job also
reuses one fixed TOI candidate list per chunk and clears only its logical length
per projectile.

Discrete projectile creates conservative query circle from source world bounds,
enumerates BVH object leaves, then retains existing faction, contact gate, AABB,
exact shape, hit emission, pierce, and deactivation logic.

Continuous projectile computes current endpoint plus optional sweep-corridor
union AABB, encloses it in query circle, enumerates BVH leaves, then retains
existing exact endpoint/corridor tests, bounded candidate list, TOI/index sort,
hit emission, and impact-position behavior.

Both jobs depend on broadphase `BuildHandle`, publish read job to
`ConsumerHandle`, and keep all event-lane producer handles unchanged.

## Acceptance Criteria

- No projectile collision code reads collision hash cells, cell sizes, or
  `MaxTargetRadius`.
- One traversal stack exists per concurrently executing chunk invocation, never
  per projectile and never shared between parallel chunks.
- Conservative query catches targets at source AABB and swept-corridor edges.
- Discrete pierce remains `N + 1` accepted hits; contact gates still prevent
  repeat hits.
- Continuous path preserves nearest-along-travel order and
  `MaxContinuousHitsPerFrame` behavior.
- Friendly faction, empty tree, invalid faction, expired lifetime, exhausted
  pierce, on-hit spawn, and template-release behavior remain unchanged.
- Existing PlayMode classes `ProjectileCollisionSimulationTests` and
  `ProjectileContinuousSimulationTests` receive boundary regressions for BVH
  pruning and multi-level trees.

## Dependencies

- 001 - Safe Wide BVH Core.
- 002 - Target Broadphase Owner And Build.

## Scope / Complexity

Medium-high. Two hot query paths; consequence logic stays intact.
