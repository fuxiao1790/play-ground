# Task Execution Packet

## Task
003-projectile-bvh-queries.md

## Goal
Migrate `ProjectileDiscreteCollisionSystem` from spatial-hash cell enumeration
to the task-001/002 BVH, and from generated `IJobEntity` per-entity execution
to an explicit `IJobChunk` loop with one traversal workspace per chunk.
`ProjectileContinuousCollisionSystem` is completely out of scope — do not open
it except to visually confirm (for your own understanding) that its structure
is what task 004 will later verify stayed untouched.

User has already captured the required pre-migration performance baseline
(~217k active projectiles at ~16ms frame time, release build, `BenchmarkLarge`
scene, discrete-hash path) — recorded in `implementation-log.md`. You do not
need to do anything with this number; it is task 006's job to compare against
it later. It is mentioned here only so you understand why this migration is
now unblocked.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` (add new test
  methods only — do not restructure existing tests or shared helpers beyond
  what a new test method genuinely needs)

## Files Allowed To Create
- None expected. If you find you need a new small helper type, prefer adding
  it inside `ProjectileDiscreteCollisionSystem.cs` (it already hosts the
  internal `ProjectileHitEmission` static class) rather than a new file.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs` —
  read it in full before starting (you already have accurate line-by-line
  knowledge of it from the orchestrator's context below, but read the live
  file — it is the ground truth).
- `Assets/Scripts/System/Api/Collision/Broadphase/BvhTraversal.cs`,
  `BvhTypes.cs` (task 001) — the API you call:
  `BvhTraversalWorkspace.Create(in BvhTree source)`,
  `.Reset(float2 center, float radius)`, `.TryMoveNext(out int objectIndex)`.
  Nothing else in the BVH API is relevant to this task.
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetBroadphaseSystem.cs`
  (task 002) — `TargetBroadphaseSingleton.DiscreteBvh` is the tree you query;
  `TargetEntities`/`TargetPositions`/`TargetShapes`/`TargetFactions` are
  unchanged snapshot arrays, same index space as BVH leaf references.
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs` — this
  codebase's only existing `IJobChunk` example: `[BurstCompile]`,
  `ChunkEntityEnumerator`, `EntityTypeHandle` pattern. It does NOT show
  enableable-component read/write access (`EnabledRefRW`) from inside
  `IJobChunk`, because its query only reads a plain `Entity` array — you will
  need the standard Unity Entities 1.x pattern for that (see below), since
  nothing in this repo demonstrates it yet.
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
  — READ ONLY, for comparison of the `IJobEntity` attributes you must
  reproduce as an explicit query (it has the identical
  `[WithDisabled(typeof(ArmingTag))]`/`[WithPresent(typeof(TimedSpawnComponent))]`
  shape, just with `ProjectileContinuousTag` required instead of excluded).
  Do not modify this file.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` — read in
  full for existing helpers (`AddTarget(float2 position, float radius,
  CombatFaction faction)`, `CreateProjectile(...)`, `TickSimulationOnly(float
  dt)`, `ReadFinalizedHitCount()`) before adding new tests. Match existing
  style; do not introduce a second world-setup pattern.

## Behavior To Preserve
- Every check inside the per-target candidate loop stays byte-identical:
  faction exclusion, `ProjectileHitEmission.IsGated`, `CombatCollisionMath.
  BoundsIntersect` (AABB check — still runs even though the BVH already did a
  broadphase circle test; it is a legitimate second, tighter filter, not
  redundant), `CombatCollisionMath.Hit` exact narrowphase, hit/spawn/gate
  emission calls, pierce decrement and `Deactivate` on exhaustion.
- All four early-outs at the top of the per-entity body stay: faction ==
  `CombatFaction.None`, `lifetime.Remaining <= 0f`, `PierceRemaining < 0`, and
  (optionally, see step-by-step) a `TotalTargetCount == 0` short-circuit.
- `ProjectileHitEmission` (the internal static class in the same file) is
  shared with `ProjectileContinuousCollisionSystem` — do not change its
  signatures or behavior. You may call it exactly as today.
- Job scheduling stays parallel (`ScheduleParallel`), unlike task 002's gather
  job. Each chunk's `BvhTraversalWorkspace` is a local, chunk-scoped value —
  there is no shared mutable state across chunks here, so parallel scheduling
  is safe and required to preserve the original's throughput characteristics
  (target: ~50k projectiles per `Docs/performance.md`).
- `BuildHandle`/`ConsumerHandle`/spawn-lane `ProducerHandle` wiring in
  `OnUpdate` stays exactly as it is today — only what the job itself reads
  from the singleton and what query it's scheduled against changes.
- Discrete pierce remains "N+1 accepted hits"; contact gates still prevent
  repeat hits within cooldown.

## Behavior To Change
- Candidate enumeration: replace the cell-range loop
  (`CombatSpatialHash.FloorCell`/`TargetCells.TryGetFirstValue`/
  `TryGetNextValue`) with one `BvhTraversalWorkspace` per chunk, `Reset` once
  per enabled entity with a conservative query circle, `TryMoveNext` in place
  of the multi-hashmap iteration.
- Query circle construction: derive it from the projectile's own
  `CombatCollisionComponent.BoundsMin`/`BoundsMax` (the same AABB already used
  for the old cell-expansion math and for the `BoundsIntersect` narrowphase
  pre-check) — NOT from `collision.Radius`/`HalfExtents`/`ShapeType` directly,
  and NOT via `CombatCollisionMath.BoundingRadius` (that's for target/object
  circles, not source query circles). Circumscribe the AABB:
  `center = (BoundsMin + BoundsMax) * 0.5`,
  `radius = distance(center, BoundsMax)` (half-diagonal). This trivially and
  exactly contains the AABB, which itself already contains the true shape by
  construction, so it satisfies "query circle conservatively contains
  discrete projectile source world bounds" with no per-shape-type branching
  needed.
- Remove from the job: `TargetCells` (`NativeParallelMultiHashMap`),
  `MaxTargetRadius`. Add: the discrete BVH tree (read-only), sourced from
  `hash.DiscreteBvh` in `OnUpdate`.
- Job type: `IJobEntity` → `[BurstCompile] struct ... : IJobChunk`, with an
  explicit `Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool
  useEnabledMask, in v128 chunkEnabledMask)`.
- Query construction: the system currently has TWO different effective
  queries — `activeProjectileQuery` (built with plain `GetEntityQuery`, used
  only for the `.IsEmpty` pre-check) and the `IJobEntity`'s own
  auto-generated query (derived from `Execute`'s parameters plus the
  `[WithAll]`/`[WithNone]`/`[WithDisabled]`/`[WithPresent]` attributes on the
  job type — this is the one that actually governed which entities got
  processed). These two are NOT identical today: the manual
  `activeProjectileQuery` does not encode the `ArmingTag`-disabled or
  `TimedSpawnComponent`-present requirements at all, which was harmless
  because that query was only ever used as a coarse "is there any work"
  check. Now that you need one explicit, precise `EntityQuery` to schedule an
  `IJobChunk` against, build ONE query (via `EntityQueryBuilder`, not the
  plain `ComponentType[]` form — you need `WithDisabled`/`WithPresent`, which
  the plain form cannot express) that reproduces the FULL original
  `IJobEntity` semantics exactly, and use that same query both for the
  `.IsEmpty` check and for scheduling. Getting this wrong is the single
  highest-risk mistake in this task: too loose and arming/non-collidable
  projectiles start colliding; too strict (e.g. plain `WithAll<TimedSpawnComponent>()`
  instead of `WithPresent`) and every non-timed-spawn projectile silently
  stops colliding entirely, exactly the bug the existing code comment warns
  about.

## Relevant Global Context
- No new native allocation per query or per entity. The workspace is the one
  fixed-capacity struct from task 001; do not add a second candidate buffer.
- The BVH may return false positives (pruned only by the downstream AABB/exact
  checks, unchanged); it must never miss a true hit.
- This task does not touch continuous, AOE, or tracking — those are
  read-only-reference checks in tasks 004/005, not modified here.

## Dependencies Confirmed
- Task 001 complete: `BvhTraversalWorkspace` API available and reviewed.
- Task 002 complete: `TargetBroadphaseSingleton.DiscreteBvh` is built every
  frame from the same snapshot as `TargetEntities`/`TargetPositions`/
  `TargetShapes`/`TargetFactions`, combined into `BuildHandle`, verified by
  the orchestrator via full manual review of `TargetBroadphaseSystem.cs`
  (source-level `NativeList` handle-sharing and node-count/depth recurrence
  both checked against `BvhBuilder`'s actual implementation).
- Discrete-hash performance baseline captured by user (see Goal section) —
  this was the blocking condition on starting this task; it is now satisfied.

## Step-By-Step Instructions
1. Read `ProjectileDiscreteCollisionSystem.cs` in full.
2. In `OnCreate`, replace `activeProjectileQuery`'s construction with an
   `EntityQueryBuilder` that expresses, in one place, everything the current
   `IJobEntity` attributes + parameter list require:
   - `WithAll<ProjectileTag, Active, CombatCollisionActiveTag>()` (matches
     type-level `[WithAll]`; `Active`/`CombatCollisionActiveTag` are
     `IEnableableComponent`, so `WithAll` here means "present and enabled",
     matching today's behavior).
   - `WithNone<ProjectileContinuousTag>()`.
   - `WithDisabled<ArmingTag>()`.
   - `WithPresent<TimedSpawnComponent>()` (present regardless of enabled
     state — do NOT use `WithAll` here, see the code comment already in this
     file explaining why).
   - Required read-only presence for `ProjectileIdentityComponent`,
     `CombatHitPayload`, `CombatKinematicsComponent`, `CombatCollisionComponent`.
   - Required read-write presence for `CombatLifetimeComponent`,
     `ProjectileHitComponent`, and the `ProjectileContactGateElement` buffer.
   Store typed handles for every component/buffer the job needs
   (`EntityTypeHandle`; `ComponentTypeHandle<T>` for each `in`/`ref`
   component; `ComponentTypeHandle<Active>`/`ComponentTypeHandle<ArmingTag>`
   for the enabled-state accessors; `BufferTypeHandle<ProjectileContactGateElement>`),
   `.Update(ref state)` each one at the top of `OnUpdate` before scheduling
   (mirroring task 002's `TargetBroadphaseSystem` pattern for handle
   refresh).
3. In `OnUpdate`, keep the `if (query.IsEmpty) return;` guard (now against the
   single unified query), the singleton/lane fetch block, and the
   `BuildHandle` dependency merge exactly as they are. Build the job with the
   new handles plus `Tree = hash.DiscreteBvh` (or however you name the field)
   instead of `TargetCells`/`MaxTargetRadius`. Schedule with
   `job.ScheduleParallel(query, state.Dependency)` (four-argument overload
   taking the explicit query — this replaces the parameterless
   `ScheduleParallel()` that `IJobEntity` provided implicitly). Everything
   after scheduling (`ProducerHandle`/`ConsumerHandle` wiring) stays
   unchanged, just keyed off the same `collisionHandle`.
4. Rewrite the job struct as `IJobChunk`:
   - Fields: same `[ReadOnly] NativeArray<Entity> TargetEntities` /
     `TargetPositions` / `TargetShapes` / `TargetFactions` as today, the BVH
     tree, the queues, `SpawnTemplateDeltas`, plus the typed handles from
     step 2 (component handles as job fields now, since `IJobChunk` doesn't
     inject them implicitly the way `IJobEntity` does).
   - `Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool
     useEnabledMask, in v128 chunkEnabledMask)`: pull `NativeArray<T>` views
     for every plain component via `chunk.GetNativeArray(ref handle)`, the
     `BufferAccessor<ProjectileContactGateElement>` via
     `chunk.GetBufferAccessor(ref bufferHandle)`, and — for the two
     enableable components you must read/write per entity
     (`Active`, `ArmingTag`) — use Unity's `chunk.GetEnabledMask(ref
     componentTypeHandle)` to get an `EnabledMask`, then
     `enabledMask.GetEnabledRefRW<T>(indexInChunk)` per entity to obtain the
     same `EnabledRefRW<T>` the old `Execute` received as a parameter. This
     is standard Unity Entities 1.x API for converting `IJobEntity`'s
     implicit enableable-parameter access into explicit `IJobChunk` code;
     verify the exact method names compile against this project's Entities
     package version rather than assuming — if the exact API differs, use
     whatever the installed Entities version actually exposes for "get an
     `EnabledRefRW<T>` for entity index i in this chunk from a
     `ComponentTypeHandle<T>`", the intent is non-negotiable even if the
     precise call shape needs adjusting.
   - Create exactly ONE `BvhTraversalWorkspace` at the top of `Execute`
     (before the entity loop) via `BvhTraversalWorkspace.Create(Tree)` — this
     satisfies "each chunk invocation creates one workspace" because one
     `Execute` call IS one chunk invocation.
   - Iterate with `ChunkEntityEnumerator` exactly like
     `CombatPoolCleanupSystem`'s `PoolTrimJob`, and for each entity index run
     the original per-entity body verbatim, with two changes: (a) fetch
     `Entity`/component values from the chunk arrays by index instead of
     method parameters; (b) replace the cell-range double-loop with:
     build the query circle from `collision.BoundsMin/BoundsMax` (see
     "Behavior To Change" above), `workspace.Reset(queryCenter,
     queryRadius);`, then `while (workspace.TryMoveNext(out int targetIdx))`
     wrapping the exact same body the old `do { ... } while
     (TargetCells.TryGetNextValue(...))` loop had (faction check through
     pierce decrement/Deactivate), substituting `targetIdx` for the old
     `targetIdx`.
5. Double check `ProjectileHitEmission` itself needs zero changes — you are
   only changing how candidates are found, not what happens once one is
   found.
6. Add new regression tests to `ProjectileCollisionSimulationTests.cs`
   (per task acceptance criteria): at least one scenario with more targets
   than `BvhConfig.ChildCount` forcing a multi-level tree, with the hit
   target placed at a deep/edge node, and at least one boundary scenario
   where a target sits just outside the projectile's exact AABB but would
   have been included by a looser/wrong query circle (proving the
   `BoundsIntersect`/exact-hit checks still correctly prune BVH false
   positives) and one where a target sits right at the conservative circle's
   edge and must still be found (no false negative). Use existing helpers
   (`AddTarget`, `CreateProjectile`, `TickSimulationOnly`,
   `ReadFinalizedHitCount()` or equivalent) and match existing test style —
   do not build a second test harness.
7. Self-review: grep the rewritten file for `TargetCells`,
   `ProjectileCollisionCells`, `MaxTargetRadius`, `CombatSpatialHash` — none
   should remain in `ProjectileDiscreteCollisionSystem.cs`. Grep for any
   scalar per-lane BVH node math you might have accidentally duplicated —
   there should be none; all geometry work happens inside
   `BvhTraversalWorkspace`/`BvhNodeTest`, never in this file.

## Acceptance Criteria
(Copied from `003-projectile-bvh-queries.md`.)
- Discrete projectile collision reads no collision hash cells, cell sizes, or
  `MaxTargetRadius`.
- Discrete job calls shared traversal API; no scalar child-circle loop,
  per-lane overlap arithmetic, or duplicate node-test implementation exists in
  discrete consumer.
- One traversal stack exists per concurrently executing chunk invocation,
  never per projectile and never shared between parallel chunks.
- Conservative query catches targets at discrete source world-bound edges.
- Discrete pierce remains `N + 1` accepted hits; contact gates still prevent
  repeat hits.
- Continuous source and query behavior remain unchanged and still reference
  retained spatial-hash broadphase. Diff review must show no continuous
  algorithm migration or query-circle construction (you are not touching that
  file at all, so this is automatically satisfied — but self-confirm with
  `git diff` before finishing that it truly has zero changes).
- Friendly faction, empty tree, invalid faction, expired lifetime, exhausted
  pierce, on-hit spawn, and template-release behavior remain unchanged.
- `ProjectileCollisionSimulationTests` receives boundary regressions for BVH
  pruning and multi-level trees.
- Existing `ProjectileContinuousSimulationTests` remains regression coverage
  for unchanged spatial-hash, sweep, TOI, and hit-cap behavior; add no
  BVH-specific expectation to that class (you are not touching it).

## Validation Required
- `git diff --stat` must show changes in exactly the two allowed files.
- `git diff -- Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
  must be empty.
- Self-review against the query-reconstruction hazard and the
  enableable-component `IJobChunk` conversion, both called out above.
- Do NOT run Unity tests. The orchestrator will attempt a headless compile
  check afterward (may be unavailable if the user's Editor has the project
  open — not your problem to solve).

## Hard Boundaries
- Do not modify `ProjectileContinuousCollisionSystem.cs`, `ProjectileHitEmission`'s
  public behavior/signatures, `CombatSpatialHash.cs`, or anything under
  `Assets/Scripts/System/Api/Collision/Broadphase/`.
- Do not add a fallback/dual path that queries both the hash and the BVH.
- Do not change pierce/gate/emission semantics — only candidate enumeration
  and the job's ECS execution shape change.
- Do not reopen index-level decisions.
- Stop and report on genuine architectural ambiguity — e.g. if the installed
  Entities package version truly lacks a way to get a per-entity
  `EnabledRefRW<T>` from inside `IJobChunk` (it does have one; if you cannot
  find it, report exactly what you tried rather than falling back to a
  structural change or a scalar `IJobEntity`-adjacent workaround).
