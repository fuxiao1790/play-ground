# Discrete Projectile Collision BVH Broadphase Plan

## Summary

Replace only discrete projectile collision candidate enumeration with a
deterministic, full-rebuild, wide bounding-circle BVH. Keep current target proxy
snapshot as only gameplay source. BVH leaves reference snapshot indices
directly. Existing scalar `CombatCollisionMath` remains narrowphase authority.

Continuous projectile collision keeps existing spatial-hash broadphase.
Continuous queries use long endpoint/corridor union boxes; bounding-circle BVH
queries over those boxes are too loose and traverse too much tree. Existing
`CombatSweepMath`, cell-range filtering, maximum-target-radius expansion, TOI
collection, and sorting remain unchanged.

AOE occupied-cell queries and all target acquisition remain spatial-hash based.
This includes targeted-chain/external-anchor acquisition through AOE occupied
cells and projectile tracking acquisition through tracking cells/target ID map.
Owner may be named `TargetBroadphaseSystem` / `TargetBroadphaseSingleton` to
describe mixed snapshot, discrete BVH, and retained hash acceleration ownership.

No `unsafe`. `PlayGround.Sim.asmdef` and project setting remain unchanged.

This plan is implementation-independent. Evaluate work against this file and
the numbered task files; do not weaken requirements to match any existing
working-tree implementation or generated execution packet.

## Main Decisions

### Safe BVH4/BVH8 layouts

- `BvhConfig.ChildCount` accepts `4` or `8`; default `8`.
- BVH4 hot bounds use three `float4` values: X, Y, radius.
- BVH8 hot bounds use six `float4` values: low/high halves for X, Y, and radius.
- Metadata stays separate: child references, packed child kinds, active mask.
- BVH4 node lookup performs the complete overlap equation as one four-lane
  `float4` batch: subtract, multiply, add, compare, then `math.bitmask` (or an
  equivalent packed mask extraction). No scalar per-lane arithmetic,
  comparison, or `if` chain is allowed before the mask exists.
- BVH8 lookup performs two complete `float4` overlap tests, extracts
  one four-bit mask from each half, and combines them. It must not extract eight
  scalar lanes and rebuild the result with a loop. Source uses no x86 AVX/AVX2
  intrinsics or ISA checks; Burst selects supported instructions for target CPU.
- No pointer loads or fixed buffers. Do not rely on Burst auto-vectorization to
  repair scalar source; SIMD operations and mask extraction must be explicit.
- One compile-time constant selects one layout/build/query family. Only selected
  native buffers are created and scheduled. No runtime width switching.
- Separate concrete width layouts are justified: safe C# has no const-sized
  inline arrays, while one max-width node would make BVH4 use BVH8 memory and
  invalidate cache-footprint comparison.

Changing width requires changing only `BvhConfig.ChildCount` and recompiling.
Adding width other than 4 or 8 requires new physical SIMD layout; unsupported
values fail during system creation.

### Tree model

- Tree exists only for discrete projectile collision broadphase.
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

- `ProjectileDiscreteCollisionSystem` is sole BVH consumer.
- Discrete collision uses `IJobChunk`. Each chunk invocation creates one safe
  BVH4/BVH8 traversal workspace containing `FixedList512Bytes<int>`, then
  clears/reseeds it for each enabled projectile in that chunk. No per-entity
  workspace construction and no shared scratch race.
- Traversal workspace returns one target snapshot index at a time.
- Each visited node invokes exactly one selected width-wide SIMD circle test and
  produces a lane bitmask. Only after that mask exists may traversal inspect
  child kind/reference metadata and dispatch set bits scalarly.
- Traversal stack capacity is validated from built depth and width. Overflow must
  fail validation, never silently prune.
- Query circle conservatively contains discrete projectile source world bounds.
- Returned leaf still passes faction, contact/repeat gate, AABB, and exact scalar
  narrowphase checks.
- Baseline DFS order is deterministic but not gameplay API. Discrete projectile
  pierce and consequence behavior remain unchanged.
- Continuous projectile, AOE, targeted, and tracking traversal/order behavior
  remain on existing spatial-hash paths and are outside BVH traversal.

## Constraints And Invariants

| Constraint | Source | Plan response |
|---|---|---|
| ECS proxy data, not live GameObjects/Transforms/Colliders, drives simulation. | `Docs/layers/ecs-simulation.md`; `Docs/contracts/target-proxy.md` | Build from `TargetProxyTag`, `TargetPosition`, `TargetCollisionShape`, `TargetFaction`; keep managed data out. |
| Proxy create/update applies before broadphase; deletion remains after current-frame consumers. | `Docs/flows/runtime-frame.md`; `Docs/flows/target-proxy-lifecycle.md` | Replace ordering references with `TargetBroadphaseSystem`; retain existing system-group placement and dependency chain. |
| Shared native-container owner exposes handles; consumers depend on build and publish read handles. | `Docs/coding-standards.md`; `TargetSpatialHashSystem.cs` | Preserve singleton ownership, `BuildHandle`, `ConsumerHandle`, creation, completion, and disposal model. |
| Tree cannot mutate while query jobs read it. | `bvh.md` sections 27-30 | Owner completes prior consumers before resize/clear; same-frame consumers depend on build; next update waits before reuse. |
| No managed/per-query allocations on hot path; persistent buffers grow and reuse. | `Docs/performance.md`; `bvh.md` sections 14, 51-53 | Persistent snapshots, nodes, Morton entries, and build scratch; one fixed local traversal workspace reused across each chunk; no per-query native container. |
| No `unsafe` in gameplay/shared runtime. | `Docs/coding-standards.md`; `PlayGround.Sim.asmdef`; `ProjectSettings.asset` | Safe `float4` storage and fixed lists; no project/assembly setting change. |
| One node's geometric child test is SIMD; tree navigation and exact narrowphase are scalar. | `bvh.md` sections 4, 6, 12-13 | Explicit BVH4 packed compare/bitmask; BVH8 uses two complete `float4` packed tests and combines their masks. Reject scalar lane loops/branches inside geometric node test. |
| Broadphase may return false positives, never false negatives. | `bvh.md` sections 11, 18, 45-46, 57-58 | Conservative source/object circles; brute-force subset validation; active-lane masking; small documented contact epsilon. |
| Exact shape logic and collision consequences stay unchanged. | `Docs/flows/collision-to-combat-result.md`; current collision systems | Discrete BVH yields snapshot index only. Existing faction/gate/AABB/exact hit and event emission remain consumer-owned. |
| Discrete and continuous projectile broadphases remain different. | `ProjectileDiscreteCollisionSystem.cs`; `ProjectileContinuousCollisionSystem.cs`; user scope decision | Migrate discrete candidate enumeration only. Continuous keeps existing spatial-hash cell walk, maximum-target-radius expansion, endpoint/corridor exact tests, TOI cap/sort, and death/emission funnels. |
| Long continuous sweep boxes are inefficient as bounding-circle BVH queries. | User scope decision | Do not query BVH for continuous projectiles and do not add a BVH fallback/dual query path. |
| AOE occupied-cell traversal, de-dup, and 32-hit cap are existing behavior. | `CollisionConstants.cs`; `AoeCollisionCore.cs` | Preserve unchanged; AOE is outside this BVH replacement. |
| All target acquisition remains spatial-hash based. | `TargetedAcquisition.cs`; `TargetedResolveSystem.cs`; `ProjectileTrackingSystem.cs` | Targeted chain/external anchor selection keeps AOE occupied cells; projectile tracking keeps tracking cells and ID lookup. Preserve rank/tie/exclusion and directional acquisition behavior. |
| Tracking refresh relies on target-index validation plus ID lookup; acquisition uses directional grid bands. | `ProjectileTrackingSystem.cs` | Retain tracking map/grid and target snapshot indices; do not force tracking through collision BVH. |
| Performance target is about 50k projectiles, 20 targets, 120 fps. | `Docs/performance.md` | Compare discrete-hash baseline, discrete BVH4, and discrete BVH8 in `BenchmarkLarge`; verify continuous/AOE/targeted hash timings and behavior do not regress. |
| Agent never runs tests; XML under `Logs/` is only test evidence. | `Docs/project-overview.md`; `Docs/testing.md` | User runs named EditMode/PlayMode suites and exports required XML; implementation review reads XML before pass claim. |

## Mechanisms Reused Vs Introduced

Reused:

- target proxy snapshot arrays and snapshot-index references;
- continuous projectile collision spatial hash and maximum-target-radius
  expansion;
- AOE occupied-cell map, query de-dup/cap behavior, and targeted reuse of those
  cells;
- singleton native-container ownership and explicit job-handle publication;
- persistent capacity growth and owner-only disposal;
- Burst chunk iteration and existing component/query contracts;
- exact collision math, sweep math, hit gates, caps, queues, and consequences;
- tracking spatial grid and tracked-target ID map.

Introduced:

- `BvhConfig`, child kind, circle, Morton entry, BVH4/BVH8 hot/cold nodes;
- deterministic Morton/bottom-up builder;
- explicit width-wide SIMD node-overlap kernels plus safe scalar fixed-stack
  discrete-projectile BVH candidate enumerator;
- validation and profiling data needed to prove containment and SIMD behavior.

No separate BVH object record introduced. Existing snapshot stays source of
entity, position, shape, and faction. Node object references point there.

## Design Validation

- Concurrency: no writer overlaps tree reader; handle protocol unchanged.
- Ownership: one ECS owner creates, resizes, rebuilds, publishes, and disposes
  all target snapshot/acceleration memory.
- Data flow: proxy data is copied once into snapshot, then discrete collision
  BVH, continuous collision hash, AOE occupied cells, and tracking grid derive
  from that snapshot.
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
- SIMD: BVH4 executes full overlap math and mask extraction across one `float4`;
  BVH8 executes two complete `float4` tests and combines their masks. Scalar
  traversal begins only after packed mask production.
- Portability: no x86 intrinsics or runtime ISA branch. Burst chooses supported
  instruction encoding for the target; source remains two explicit vector halves.
- Behavior: discrete collision consequence ownership remains unchanged.
  Continuous, AOE, targeted, and tracking behavior remain unchanged on existing
  hash paths.
- Performance: hot bounds separated from cold metadata; metadata read only after
  geometric mask production; no scalar lane extraction in node geometry, no
  gathers/pointers/per-query native containers. Burst Inspector must prove
  packed compare/mask codegen rather than merely packed loads or add/subtract.

## Scoped Coexistence Decision

- Resulting data flow: target snapshot -> discrete projectile BVH + retained
  continuous collision hash + retained AOE occupied cells + retained tracking
  grid/ID map.
- Coexistence is intentional, not a migration shim. Query geometry differs:
  compact discrete source bounds suit bounding-circle BVH traversal; long
  continuous sweep boxes do not.
- BVH leaves reference snapshot indices directly. Retained hashes also reference
  same snapshot indices. No duplicate entity/shape source is introduced.
- Do not migrate continuous, AOE, targeted, or tracking consumers during this
  plan. Do not delete containers/constants/jobs they require.
- No runtime selector or dual-query comparison path. Each consumer has one
  authoritative broadphase chosen by query shape.

## Tasks

1. [001-safe-wide-bvh-core.md](001-safe-wide-bvh-core.md) - safe node layouts,
   explicit full-width SIMD overlap kernels, build/traversal primitives, and
   validation tests.
2. [002-target-broadphase-build.md](002-target-broadphase-build.md) - add
   discrete BVH build while retaining continuous/AOE/tracking hash acceleration.
3. [003-projectile-bvh-queries.md](003-projectile-bvh-queries.md) - discrete
   projectile migration only.
4. [004-continuous-hash-preservation.md](004-continuous-hash-preservation.md) -
   prove continuous projectile collision remains on existing spatial hash.
5. [005-aoe-target-acquisition-hash-preservation.md](005-aoe-target-acquisition-hash-preservation.md) -
   prove AOE and all target-acquisition paths remain on spatial hashes.
6. [006-validation-and-performance.md](006-validation-and-performance.md) -
   integration regression, profiling, BVH4/BVH8 comparison, and mandatory
   full-kernel Burst verification.
7. [007-docs-and-discrete-cleanup.md](007-docs-and-discrete-cleanup.md) - remove
   only discrete-projectile hash dependencies and update docs without deleting
   retained hash paths.

## Open Questions / Deferred Work

- No blocking design questions.
- Refit, periodic rebuild, double buffering, near-first internal traversal,
  faction masks, large-object side trees, wider-than-8 layouts, and any future
  BVH designed for elongated sweep queries are deferred until measured need.
- Discrete replacement should not merge until user-provided XML is reviewed:
  `Logs/TestResults-EditMode-BvhBroadphase.xml` and
  `Logs/TestResults-PlayMode-BvhBroadphase.xml`.
- Performance comparison needs captures from same `BenchmarkLarge` workload and
  hardware for discrete-hash baseline, discrete BVH4, and discrete BVH8. Retained
  continuous/AOE/targeted hash workloads must stay identical across captures.
