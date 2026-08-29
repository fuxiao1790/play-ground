# Target Collision BVH Broadphase Plan

## Summary

Replace collision-facing target spatial hashes with deterministic, full-rebuild,
wide bounding-circle BVH. Keep current target proxy snapshot as only gameplay
source. BVH leaves reference snapshot indices directly. Existing scalar
`CombatCollisionMath` and `CombatSweepMath` remain narrowphase authority.

Keep tracking grid and tracked-target ID map. Tracking probes directional cells;
that is different query domain from collision-circle traversal. Rename owner to
`TargetBroadphaseSystem` / `TargetBroadphaseSingleton` so name matches mixed
snapshot, collision BVH, and tracking acceleration ownership.

No `unsafe`. `PlayGround.Sim.asmdef` and project setting remain unchanged.

## Main Decisions

### Safe BVH4/BVH8 layouts

- `BvhConfig.ChildCount` accepts `4` or `8`; default `8`.
- BVH4 hot bounds use three `float4` values: X, Y, radius.
- BVH8 hot bounds use three `Unity.Burst.Intrinsics.v256` values.
- Metadata stays separate: child references, packed child kinds, active mask.
- BVH8 AVX path operates on `v256` values by value; no pointer loads or fixed
  buffers. Portable fallback tests two `float4` halves.
- One compile-time constant selects one layout/build/query family. Only selected
  native buffers are created and scheduled. No runtime width switching.
- Separate concrete width layouts are justified: safe C# has no const-sized
  inline arrays, while one max-width node would make BVH4 use BVH8 memory and
  invalidate cache-footprint comparison.

Changing width requires changing only `BvhConfig.ChildCount` and recompiling.
Adding width other than 4 or 8 requires new physical SIMD layout; unsupported
values fail during system creation.

### Tree model

- Full Morton rebuild every simulation update. Refit deferred until profiling
  proves rebuild cost unacceptable.
- Dynamic target-center bounds normalize Morton coordinates. Morton ties break by
  snapshot index for deterministic build.
- Bottom-up grouping: object lanes first, then node lanes until one root.
- Parent circle uses child-circle AABB center plus maximum
  `distance(center, childCenter) + childRadius`.
- Root always node for non-empty tree. Empty tree uses `RootNodeIndex = -1`.
- Child object reference is target snapshot index. No duplicate
  `BroadphaseObject` array and no entity/shape copy solely for BVH.
- In-place persistent buffers reused. Current `BuildHandle` / `ConsumerHandle`
  protocol already prevents rebuild while readers run; double buffering would add
  memory without concurrency benefit.

### Traversal model

- High-count BVH consumers use `IJobChunk`. Each chunk invocation creates one
  safe BVH4/BVH8 traversal workspace containing `FixedList512Bytes<int>`, then
  clears/reseeds it for each enabled entity in that chunk. No per-entity
  workspace construction and no shared scratch race.
- Traversal workspace returns one target snapshot index at a time.
- Node circle tests produce lane bitmask; set bits dispatch scalarly.
- Traversal stack capacity is validated from built depth and width. Overflow must
  fail validation, never silently prune.
- Query circle conservatively contains source exact shape:
  - discrete projectile/AOE: circle enclosing source world bounds;
  - continuous projectile: circle enclosing union of endpoint and travel-corridor
    bounds;
  - targeted acquisition: authored search circle.
- Returned leaf still passes faction, contact/repeat gate, AABB, and exact scalar
  narrowphase checks.
- Baseline DFS order is deterministic but not gameplay API. Continuous projectile
  keeps existing time-of-impact sort. Targeted keeps distance/index sort. AOE cap
  remains 32 hits but no longer promises cell-scan identity order.

## Constraints And Invariants

| Constraint | Source | Plan response |
|---|---|---|
| ECS proxy data, not live GameObjects/Transforms/Colliders, drives simulation. | `Docs/layers/ecs-simulation.md`; `Docs/contracts/target-proxy.md` | Build from `TargetProxyTag`, `TargetPosition`, `TargetCollisionShape`, `TargetFaction`; keep managed data out. |
| Proxy create/update applies before broadphase; deletion remains after current-frame consumers. | `Docs/flows/runtime-frame.md`; `Docs/flows/target-proxy-lifecycle.md` | Replace ordering references with `TargetBroadphaseSystem`; retain existing system-group placement and dependency chain. |
| Shared native-container owner exposes handles; consumers depend on build and publish read handles. | `Docs/coding-standards.md`; `TargetSpatialHashSystem.cs` | Preserve singleton ownership, `BuildHandle`, `ConsumerHandle`, creation, completion, and disposal model. |
| Tree cannot mutate while query jobs read it. | `bvh.md` sections 27-30 | Owner completes prior consumers before resize/clear; same-frame consumers depend on build; next update waits before reuse. |
| No managed/per-query allocations on hot path; persistent buffers grow and reuse. | `Docs/performance.md`; `bvh.md` sections 14, 51-53 | Persistent snapshots, nodes, Morton entries, and build scratch; one fixed local traversal workspace reused across each chunk; no per-query native container. |
| No `unsafe` in gameplay/shared runtime. | `Docs/coding-standards.md`; `PlayGround.Sim.asmdef`; `ProjectSettings.asset` | Safe `float4`/`v256` storage and fixed lists; no project/assembly setting change. |
| Broadphase may return false positives, never false negatives. | `bvh.md` sections 11, 18, 45-46, 57-58 | Conservative source/object circles; brute-force subset validation; active-lane masking; small documented contact epsilon. |
| Exact shape logic and collision consequences stay unchanged. | `Docs/flows/collision-to-combat-result.md`; current collision systems | BVH yields snapshot index only. Existing faction/gate/AABB/exact hit and event emission remain consumer-owned. |
| Discrete and continuous projectile lanes remain separate; continuous hits resolve nearest along travel. | `ProjectileDiscreteCollisionSystem.cs`; `ProjectileContinuousCollisionSystem.cs` | Replace only candidate enumeration; preserve TOI candidate cap/sort and death/emission funnels. |
| AOE hit cap is 32; overlapping target currently needs hash de-duplication. | `CollisionConstants.cs`; `AoeCollisionCore.cs` | Keep cap. Remove `seen`: one BVH leaf per target guarantees uniqueness. Remove cell-order identity assertion. |
| Targeted rank/nearest selection is deterministic and excludes immediate previous target. | `TargetedAcquisition.cs`; `TargetedResolveSystem.cs` | Keep bounded nearest list, distance/index tie break, faction and `excludeKey`; feed it BVH candidates. |
| Tracking refresh relies on target-index validation plus ID lookup; acquisition uses directional grid bands. | `ProjectileTrackingSystem.cs` | Retain tracking map/grid and target snapshot indices; do not force tracking through collision BVH. |
| Performance target is about 50k projectiles, 20 targets, 120 fps. | `Docs/performance.md` | Compare old baseline, BVH4, BVH8 in `BenchmarkLarge`; inspect build stalls, query jobs, GC, and Burst assembly. |
| Agent never runs tests; XML under `Logs/` is only test evidence. | `Docs/project-overview.md`; `Docs/testing.md` | User runs named EditMode/PlayMode suites and exports required XML; implementation review reads XML before pass claim. |

## Mechanisms Reused Vs Introduced

Reused:

- target proxy snapshot arrays and snapshot-index references;
- singleton native-container ownership and explicit job-handle publication;
- persistent capacity growth and owner-only disposal;
- Burst chunk iteration and existing component/query contracts;
- exact collision math, sweep math, hit gates, caps, queues, and consequences;
- tracking spatial grid because its query semantics differ.

Introduced:

- `BvhConfig`, child kind, circle, Morton entry, BVH4/BVH8 hot/cold nodes;
- deterministic Morton/bottom-up builder;
- safe fixed-stack BVH candidate enumerators;
- validation and profiling data needed to prove containment and SIMD behavior.

No separate BVH object record introduced. Existing snapshot stays source of
entity, position, shape, and faction. Node object references point there.

## Design Validation

- Concurrency: no writer overlaps tree reader; handle protocol unchanged.
- Ownership: one ECS owner creates, resizes, rebuilds, publishes, and disposes
  all target snapshot/acceleration memory.
- Data flow: proxy data is copied once into snapshot, then both tracking grid and
  collision BVH derive from that snapshot.
- Allocation: each chunk job reuses one stack-local fixed list for all entities
  in that chunk. Build storage is persistent; capacity grows geometrically and
  is reused.
- Correctness: every target exact shape gets conservative object circle; every
  source query gets conservative circle; parent circles contain children;
  randomized BVH results checked against brute force.
- Small/empty data: empty root short-circuits; 1..ChildCount targets occupy one
  masked root.
- Width: compile-time choice controls grouping, node representation, masks,
  traversal, validation, depth calculations, and scheduled implementation.
- Portability: BVH8 AVX path guarded by Burst ISA support; portable float4-halves
  path preserves semantics.
- Behavior: collision consequence order and ownership unchanged. Only old AOE
  overflow cell-order accident is deliberately removed from contract.
- Performance: hot bounds separated from cold metadata; metadata read only when
  geometric mask is nonzero; no gathers/pointers/per-query native containers.

## Minimal/Additive Vs Refactor

### Minimal/additive approach

- Resulting data flow: proxy snapshot -> three collision hashes plus BVH; selected
  consumers migrate independently.
- New concepts/types: BVH state beside `TargetSpatialHashSingleton`.
- Copies/translations: duplicate bounds/index data and duplicate rebuild work.
- Long-term cost: two collision broadphases, unclear source of truth, doubled
  dependency/disposal paths, migration flags, benchmark distortion.

### Refactor approach

- Resulting data flow: proxy snapshot -> one collision BVH plus one
  tracking-specific grid.
- Changed/removed concepts: rename owner; remove projectile collision cells, AOE
  occupied cells, max-target-radius expansion, and collision cell constants.
- Copies/translations avoided: BVH leaves reference snapshot indices directly;
  no duplicate broadphase object payload.
- Long-term benefit: one collision candidate path, clear ownership, no old/new
  synchronization, narrower tracking exception with explicit semantics.

### Decision

Choose refactor. Collision hash and BVH represent same domain concept. Keeping
both violates one-source/default decision rule. Tracking grid remains because it
implements different directional acquisition behavior, not collision overlap.

## Tasks

1. [001-safe-wide-bvh-core.md](001-safe-wide-bvh-core.md) - safe node layouts,
   build/traversal primitives, validation tests.
2. [002-target-broadphase-build.md](002-target-broadphase-build.md) - replace
   owner/singleton build while retaining tracking acceleration.
3. [003-projectile-bvh-queries.md](003-projectile-bvh-queries.md) - discrete and
   continuous projectile migration.
4. [004-aoe-bvh-queries.md](004-aoe-bvh-queries.md) - impact/lingering AOE
   migration and duplicate-path removal.
5. [005-targeted-bvh-acquisition.md](005-targeted-bvh-acquisition.md) - targeted
   resolve and external acquisition migration.
6. [006-validation-and-performance.md](006-validation-and-performance.md) -
   integration regression, profiling, BVH4/BVH8 and Burst verification.
7. [007-docs-and-hash-cleanup.md](007-docs-and-hash-cleanup.md) - delete collision
   hash remnants and update authoritative/reference docs.

## Open Questions / Deferred Work

- No blocking design questions.
- Refit, periodic rebuild, double buffering, near-first internal traversal,
  faction masks, large-object side trees, and wider-than-8 layouts deferred until
  measured need.
- Full replacement should not merge until user-provided XML is reviewed:
  `Logs/TestResults-EditMode-BvhBroadphase.xml` and
  `Logs/TestResults-PlayMode-BvhBroadphase.xml`.
- Performance comparison needs captures from same `BenchmarkLarge` workload and
  hardware for old hash baseline, BVH4, BVH8. User captures old-hash baseline
  before task 002 replaces it; task 006 consumes that saved capture.
