# 002 - Target Broadphase Owner And Build

## Change

Refactor `TargetSpatialHashSystem` / `TargetSpatialHashSingleton` into
`TargetBroadphaseSystem` / `TargetBroadphaseSingleton`.

Owner keeps target entity/position/shape/faction snapshot, tracking cells, and
tracking ID lookup. Replace projectile collision cells, AOE occupied cells, and
`MaxTargetRadius` with selected BVH buffers plus persistent Morton/build scratch.

Replace four main-thread temporary snapshot copies with scheduled Burst gather
into pre-sized persistent arrays. Schedule collision BVH build and tracking-grid
build from same snapshot. Combine into `BuildHandle`; preserve `ConsumerHandle`
completion before buffer mutation and owner-only teardown.

Precompute required node count/root from target count and configured width before
scheduling. Grow all persistent capacities geometrically; empty target set clears
logical lengths and publishes root `-1`.

## Acceptance Criteria

- Create/update proxy systems still precede broadphase build.
- All collision and tracking consumers can depend on one published `BuildHandle`.
- Previous consumers complete before any persistent buffer clear/resize/write.
- Owner disposes only containers it creates, after build/consumer completion.
- No per-frame managed allocation and no explicit per-target native allocation.
- One target snapshot feeds both accelerators; no second entity/shape snapshot.
- Tracking refresh/acquisition behavior remains unchanged.
- Empty and changing target counts leave no stale root, entries, or indices.
- Existing test worlds update system registration/name and still construct full
  owner lifecycle.

## Dependencies

- 001 - Safe Wide BVH Core.
- User-provided `BenchmarkLarge` old-hash baseline capture, recorded before this
  task removes collision maps. If unavailable, capture from pre-change revision
  before performance comparison.

## Scope / Complexity

High. Shared ECS ownership, scheduling, persistent lifetime, and system ordering.
