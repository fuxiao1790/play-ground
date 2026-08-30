# Task Execution Packet

## Task
002-target-broadphase-build.md

## Goal
Rename `TargetSpatialHashSystem`/`TargetSpatialHashSingleton` to
`TargetBroadphaseSystem`/`TargetBroadphaseSingleton` (mechanical rename, same
namespace/folder), and extend the owner to also build the discrete-projectile
BVH from task 001 alongside the three retained hash builds it already does.
Replace the current main-thread synchronous snapshot copy with a scheduled
Burst gather job. Every other consumer keeps its current behavior — only its
reference to the renamed type changes.

## Files Allowed To Modify
Mechanical type-rename only (no logic change) in these 9 consumers — every one
was confirmed via repo-wide search to reference `TargetSpatialHashSystem` and/or
`TargetSpatialHashSingleton` by name:
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyUpdateApplySystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs`

Mechanical type-rename only (no logic change) in these 5 test files:
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/EditMode/TargetedResolveEditModeTests.cs`

Full rewrite (rename + new BVH build integration):
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
  → create `TargetBroadphaseSystem.cs` with the renamed types and the changes
  below, then delete the old file.

Do NOT touch `ProjectileDiscreteCollisionSystem.cs`'s collision *logic* (it
still reads `ProjectileCollisionCells`/`MaxTargetRadius` exactly as today —
only the type name of the singleton it fetches changes). Migrating it to BVH
queries is task 003, not this task.

## Files Allowed To Create
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetBroadphaseSystem.cs`

## Files Allowed To Delete
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`
  (after its content/behavior is fully carried into the new file).

## Files Likely Needed For Reading
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs` —
  current owner, read in full before you start; it is reproduced in
  `implementation-context.md` §"Current Owner Pattern Reference" but read the
  real file for exact code.
- `Assets/Scripts/System/Api/Collision/Broadphase/BvhTypes.cs`,
  `BvhBuilder.cs`, `BvhValidation.cs` (task 001, already merged) — the API you
  integrate: `BvhTree.Create/Dispose`, `BvhBuildScratch.Create/Dispose`,
  `BvhCircle.FromShape(position, radius, halfExtents, shapeType)`,
  `BvhBuilder.Build(ref tree, ref scratch, NativeArray<BvhCircle> objects, int
  objectCount)`, `BvhValidation.ValidateConfiguration()`. Note `BvhBuilder.Build`
  already does its own internal capacity growth
  (`EnsureCapacity`+`ResizeUninitialized` on the tree's lists) keyed off
  `BvhBuilder.ComputeNodeCount(objectCount)` — you do not need to duplicate
  node-count pre-sizing yourself, just call `Build` from inside a job.
  `Build` also safely no-ops on `objectCount == 0` without touching the
  `objects` array at all (checked in its source), so passing a
  not-yet-created/default `NativeArray<BvhCircle>` on the empty-target path is
  safe, matching how the existing empty-target branch already skips scheduling
  the hash-build jobs.
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs` — reference
  `IJobChunk` pattern used elsewhere in this codebase (`ComponentTypeHandle`s
  fetched/updated on the owning system, `ChunkEntityEnumerator`,
  `[BurstCompile]`). Your gather job is simpler (this query has no
  enableable/disabled filtering — every matching entity is "enabled" — and
  target counts are small, so a **sequential** `IJobChunk.Schedule(query,
  dependency)` with one running write-index field is sufficient; do not use
  `ScheduleParallel` for it, since a shared running index is not race-safe
  across parallel chunk execution and this data volume does not need it).
- `Docs/coding-standards.md` §"System Encapsulation" (singleton/handle
  ownership pattern you must preserve) and §"Allocation Rule".
- `Assets/Scripts/System/Targets/TargetCollisionShape.cs`,
  `CombatTargetProxy.cs` (for `TargetPosition`/`TargetFaction` field shapes,
  already summarized in the context file).

## Behavior To Preserve
- `BuildHandle`/`ConsumerHandle` completion protocol exactly as today: complete
  both at the top of `OnUpdate`, clear them, THEN clear/resize containers.
- Continuous projectile collision hash contents, cell size (`CombatSpatialHash`
  constants — untouched, still owned by that file), `MaxTargetRadius`
  computation, and build scheduling stay byte-for-byte behaviorally identical.
- `AoeOccupiedCells`, `TrackingCells`, `TrackingIndicesById` build logic,
  capacity math (`CalculateAoeCapacity`), and scheduling stay identical.
- Empty target count still: clears containers, clears snapshot list lengths,
  resets `MaxTargetRadius` to 0, publishes a trivial `BuildHandle`, returns
  early — and now additionally resets the BVH to empty the same way (root
  `-1`, node count 0) via the same `Build(..., 0)` no-op path or an equivalent
  explicit clear; do not schedule a BVH build job for zero targets.
- `OnCreate`/`OnDestroy` ordering and disposal-completeness (owner disposes
  only what it creates, after completing both handles).
- System ordering attributes on the owner (`[UpdateBefore(typeof(
  ProjectileTrackingSystem))]` etc.) stay as-is — only the owning type's own
  name changes, not what it orders against.

## Behavior To Change
- Type names: `TargetSpatialHashSystem` → `TargetBroadphaseSystem`,
  `TargetSpatialHashSingleton` → `TargetBroadphaseSingleton`.
- Snapshot gather: replace the current main-thread `targetQuery.ToEntityArray/
  ToComponentDataArray(Allocator.Temp)` + scalar copy loop
  (`GatherTargets`/`ResizeSnapshotLists`) with one scheduled Burst `IJobChunk`
  that writes directly into the pre-sized persistent
  `TargetEntities`/`TargetPositions`/`TargetShapes`/`TargetFactions` lists
  (still resized via the existing `EnsureCapacity`+`ResizeUninitialized`
  pattern on the main thread first, same as today — only the per-element copy
  moves off the main thread and into a scheduled job). The same job also
  writes a new parallel `BvhCircle` snapshot entry per target (see below), so
  there is still exactly one gather pass over the query, not two.
- New singleton fields: a `BvhTree` and a `BvhBuildScratch` (task 001 types)
  for the discrete BVH, plus one persistent circle cache — e.g. `NativeList<
  BvhCircle> TargetCircles` — populated by the same gather job from
  `TargetPosition`/`TargetCollisionShape` via `BvhCircle.FromShape`. This is a
  derived cache (center+radius only), not a second entity/shape snapshot —
  the plan's "no duplicate BroadphaseObject array" rule is about not copying
  entity/shape/faction a second time, which this does not do.
- New scheduled job: a `[BurstCompile] IJob` wrapping `BvhBuilder.Build(ref
  tree, ref scratch, TargetCircles.AsArray(), targetCount)`, depending on the
  gather job's handle, combined via `JobHandle.CombineDependencies` into the
  same `BuildHandle` alongside the three existing hash-build jobs (which now
  also depend on the gather job's handle instead of running after a
  synchronous main-thread copy).
- `OnCreate`: call `BvhValidation.ValidateConfiguration()` once (throws clearly
  for an unsupported `BvhConfig.ChildCount`, satisfying task 001's "unsupported
  width fails clearly during owner system creation" acceptance criterion),
  then create the `BvhTree`/`BvhBuildScratch`/`TargetCircles` alongside the
  existing containers.
- `OnDestroy`: dispose the three new pieces alongside the existing ones, after
  completing both handles, same ordering discipline as today.

## Relevant Global Context
(Condensed — see `implementation-context.md` for more.)
- Single ECS owner creates/resizes/rebuilds/publishes/disposes all target
  snapshot + BVH + retained-hash native memory — no new second owner, no
  reach into another system's private fields.
- No `unsafe`. No per-frame managed allocation. No per-target native
  allocation. Persistent buffers only, grown geometrically, never shrunk.
- Full BVH rebuild every update (already what `BvhBuilder.Build` does) — no
  refit, no double buffering.
- One target snapshot feeds discrete BVH and all retained hashes.
- This task does not migrate any consumer's query logic — it only makes the
  BVH exist and stay current. Task 003 is the only task allowed to change
  `ProjectileDiscreteCollisionSystem`'s actual collision algorithm.

## Dependencies Confirmed
- Task 001 complete: `BvhConfig`, `BvhChildKind`, `BvhCircle`, `BvhTree`,
  `BvhBuildScratch`, `BvhBuilder`, `BvhNodeTest`, `BvhTraversalWorkspace`,
  `BvhValidation` all exist under
  `Assets/Scripts/System/Api/Collision/Broadphase/` (verified present on disk
  and reviewed by the orchestrator; `BvhBroadphaseEditModeTests` written but
  not yet run by the user — that does not block this task, which only
  consumes the build/tree API, not test results).
- The `BenchmarkLarge` discrete-hash performance baseline capture mentioned in
  this task's plan-file dependencies is required before task 003 migrates
  discrete queries, not before this task. Do not attempt to run or profile
  `Assets/Scenes/BenchmarkLarge.unity` yourself — that is a user action for
  later.

## Step-By-Step Instructions
1. Read the current `TargetSpatialHashSystem.cs` in full.
2. Write `TargetBroadphaseSystem.cs`: rename the singleton struct and the
   system struct, add the three new fields (`BvhTree`, `BvhBuildScratch`,
   `NativeList<BvhCircle> TargetCircles` or your chosen name), update the ECS
   Lifecycle comment to mention the BVH ownership too.
3. In `OnCreate`: call `BvhValidation.ValidateConfiguration()` first (so a bad
   compile-time width fails before any container is created), then create the
   new containers the same way the existing ones are created
   (`Allocator.Persistent`, size-1 initial capacity matching the existing
   `NativeList`/`NativeReference` pattern already in this file).
4. Replace `GatherTargets` with a scheduled `IJobChunk` (see
   `CombatPoolCleanupSystem.cs` for the codebase's `IJobChunk` idiom). Resize
   the four existing persistent snapshot lists exactly as today
   (`ResizeSnapshotLists`), also resize `TargetCircles` to `targetCount`, then
   schedule the gather job (`Schedule`, not `ScheduleParallel`) against
   `state.Dependency`, producing a `JobHandle` you thread through instead of
   completing dependencies synchronously before a main-thread loop. Complete-
   before-read `CompleteDependencyBeforeRO` calls for
   `TargetPosition`/`TargetCollisionShape`/`TargetFaction` should move to
   whatever point the job needs safe read access — confirm the right point by
   how the existing three hash-build jobs already handle this (they don't call
   `CompleteDependencyBeforeRO` themselves; they rely on the main-thread
   `GatherTargets` having already done it before scheduling). If your gather
   job itself reads `TargetProxyTag`/`TargetPosition`/`TargetCollisionShape`/
   `TargetFaction` via chunk component handles, it needs the same completion
   before scheduling (typed handles from `SystemAPI.GetComponentTypeHandle<T>
   (isReadOnly: true)`, `.Update(ref state)`'d each frame) — structural
   safety here is standard `IJobChunk` practice, not a special case.
5. Add a `[BurstCompile] private struct BuildBvhJob : IJob` that calls
   `BvhBuilder.Build(ref Tree, ref Scratch, Circles, TargetCount)` and
   schedule it depending on the gather job's handle. Combine its handle with
   the three existing hash-build handles into `singleton.BuildHandle` (extend
   the existing `JobHandle.CombineDependencies` call to include it — note
   `CombineDependencies` has a 4-argument overload, or nest two calls).
6. Update the empty-target-count branch to also reset the BVH (call
   `BvhBuilder.Build(ref singleton.DiscreteBvh, ref singleton.DiscreteBvhScratch,
   default, 0)` directly, no job needed, matching how the branch already
   handles the hash containers synchronously).
7. Update `OnDestroy` to complete both handles then dispose the three new
   containers alongside the existing ones.
8. Update the 9 consumer files: change `TargetSpatialHashSingleton` →
   `TargetBroadphaseSingleton` and `TargetSpatialHashSystem` →
   `TargetBroadphaseSystem` wherever they appear (type usages, generic type
   arguments, `[UpdateBefore]`/`[UpdateAfter]` attribute arguments). No other
   line in these files should change.
9. Update the 5 test files the same mechanical way.
10. Delete the old `TargetSpatialHashSystem.cs`.
11. Grep the whole `Assets/` tree afterward for `TargetSpatialHash` to confirm
    zero remaining source references (docs references are out of scope for
    this task — task 007 updates those).

## Acceptance Criteria
(Copied from `002-target-broadphase-build.md`.)
- Create/update proxy systems still precede broadphase build.
- All collision and tracking consumers can depend on one published
  `BuildHandle`.
- Previous consumers complete before any persistent buffer clear/resize/write.
- Owner disposes only containers it creates, after build/consumer completion.
- No per-frame managed allocation and no explicit per-target native
  allocation.
- One target snapshot feeds discrete BVH and all retained hashes; no second
  entity/shape snapshot.
- Owner allocates and builds only selected concrete BVH4 or BVH8 hot-node
  representation; it never creates both widths or converts nodes at query
  time. (Automatically satisfied — `BvhTree`'s layout is fixed by
  `BvhConfig.ChildCount` at compile time; do not add any width-conversion
  logic.)
- Continuous projectile collision hash contents, cell size, radius expansion,
  and build scheduling remain behaviorally unchanged.
- AOE occupied-cell and targeted chain/external acquisition behavior remain
  unchanged.
- Projectile tracking refresh/acquisition behavior remains unchanged.
- Empty and changing target counts leave no stale root, entries, or indices.
- Existing test worlds update system registration/name and still construct
  full owner lifecycle.

## Validation Required
- Self-review: grep for `TargetSpatialHash` across `Assets/` after your
  changes — must return zero matches (docs excluded, out of scope).
- Self-review the gather job specifically for the sequential-vs-parallel
  scheduling hazard called out above — a shared running write index is only
  safe under sequential `Schedule`.
- Report exact list of files changed/created/deleted.
- Do NOT run Unity tests. The orchestrator will attempt a headless compile
  check afterward (may be unavailable if the user's own Editor has the
  project open, as it was during task 001 — do not treat that as a blocker on
  your side, just report what you could and could not verify yourself).

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces
  directly required by this task.
- Do not change `ProjectileDiscreteCollisionSystem`'s collision algorithm —
  rename only. Task 003 owns that migration.
- Do not touch `CombatSpatialHash.cs`.
- Do not add a BVH query/traversal call anywhere yet — this task only builds
  the tree, nothing consumes it.
- Do not reopen index-level decisions (BVH8 default, full-rebuild-only,
  single-owner model are settled).
- Stop and report rather than guessing on genuine architectural ambiguity
  (e.g. if `IJobChunk` truly cannot express the gather without introducing a
  second query or a structural change — report the specific blocker).
