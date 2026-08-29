# 002 - Target Broadphase Owner And Build

## Change

Refactor `TargetSpatialHashSystem` / `TargetSpatialHashSingleton` into
`TargetBroadphaseSystem` / `TargetBroadphaseSingleton`.

Owner keeps target entity/position/shape/faction snapshot, tracking cells, and
tracking ID lookup. Add selected BVH buffers plus persistent Morton/build scratch
for discrete projectile collision only.

Retain existing projectile collision cells and `MaxTargetRadius` for continuous
projectile broadphase. Retain AOE occupied cells for impact/lingering AOE and
targeted chain/external anchor acquisition. Retain tracking cells and target ID
lookup for projectile tracking acquisition. Do not rename or delete retained
data unless a narrow rename is required to clarify ownership and every retained
consumer is updated mechanically in same task.

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
- One target snapshot feeds discrete BVH and all retained hashes; no second
  entity/shape snapshot.
- Owner allocates and builds only selected concrete BVH4 or BVH8 hot-node
  representation; it never creates both widths or converts nodes at query time.
- Continuous projectile collision hash contents, cell size, radius expansion,
  and build scheduling remain behaviorally unchanged.
- AOE occupied-cell and targeted chain/external acquisition behavior remain
  unchanged.
- Projectile tracking refresh/acquisition behavior remains unchanged.
- Empty and changing target counts leave no stale root, entries, or indices.
- Existing test worlds update system registration/name and still construct full
  owner lifecycle.

## Dependencies

- 001 - Safe Wide BVH Core.
- User-provided `BenchmarkLarge` discrete-hash baseline capture, recorded before
  task 003 migrates discrete queries. Task 002 does not remove retained maps.

## Scope / Complexity

High. Shared ECS ownership, one new discrete BVH beside retained specialized
hashes, scheduling, persistent lifetime, and system ordering.
