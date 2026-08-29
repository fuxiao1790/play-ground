# 003 - Projectile BVH Queries

## Change

Migrate `ProjectileDiscreteCollisionSystem` only from collision cells to shared
BVH and from generated per-entity execution to explicit `IJobChunk` loops.

Each scheduled chunk invocation creates one traversal workspace outside its
enabled-entity loop. Reset/reseed workspace per discrete projectile.

Discrete projectile creates conservative query circle from source world bounds,
enumerates BVH object leaves through width-selected traversal, and retains
existing faction, contact gate, AABB, exact shape, hit emission, pierce, and
deactivation logic. Every visited node performs one complete packed SIMD child
circle test; projectile code never loops over child bounds geometrically.

`ProjectileContinuousCollisionSystem` is out of migration scope. It keeps
existing spatial-hash cell filtering, maximum-target-radius expansion, endpoint
and sweep-corridor bounds, exact endpoint/corridor tests, bounded candidate list,
TOI/index sort, hit emission, and impact-position behavior. Long sweep boxes must
not be converted into bounding-circle BVH queries.

Discrete job depends on broadphase `BuildHandle`, publishes read job to
`ConsumerHandle`, and keeps all event-lane producer handles unchanged.

## Acceptance Criteria

- Discrete projectile collision reads no collision hash cells, cell sizes, or
  `MaxTargetRadius`.
- Discrete job calls shared traversal API; no scalar child-circle loop,
  per-lane overlap arithmetic, or duplicate node-test implementation exists in
  discrete consumer.
- One traversal stack exists per concurrently executing chunk invocation, never
  per projectile and never shared between parallel chunks.
- Conservative query catches targets at discrete source world-bound edges.
- Discrete pierce remains `N + 1` accepted hits; contact gates still prevent
  repeat hits.
- Continuous source and query behavior remain unchanged and still reference
  retained spatial-hash broadphase. Diff review must show no continuous
  algorithm migration or query-circle construction.
- Friendly faction, empty tree, invalid faction, expired lifetime, exhausted
  pierce, on-hit spawn, and template-release behavior remain unchanged.
- `ProjectileCollisionSimulationTests` receives boundary regressions for BVH
  pruning and multi-level trees.
- Existing `ProjectileContinuousSimulationTests` remains regression coverage for
  unchanged spatial-hash, sweep, TOI, and hit-cap behavior; add no BVH-specific
  expectation to that class.

## Dependencies

- 001 - Safe Wide BVH Core.
- 002 - Target Broadphase Owner And Build.

## Scope / Complexity

Medium. One hot discrete query path; continuous path stays intact.
