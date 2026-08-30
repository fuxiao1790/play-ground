# Implementation Log

## Status
In progress

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-safe-wide-bvh-core.md | Complete | Additive only; 6 new files under Collision/Broadphase + BvhBroadphaseEditModeTests. |
| 002-target-broadphase-build.md | Complete | Owner renamed + BVH build wired; Burst gather job replaces main-thread snapshot copy. |
| 003-projectile-bvh-queries.md | Complete | Discrete collision now uses BVH `IJobChunk`; boundary and multi-level regressions added. |
| 004-continuous-hash-preservation.md | Complete | Continuous hash path preserved; adjacent-cell corridor-edge regression added. |
| 005-aoe-target-acquisition-hash-preservation.md | Complete | Preservation audit passed; no task-005 edits. |
| 006-validation-and-performance.md | Blocked | Code complete; awaiting user XML, Burst Inspector, profiler GC, and BVH4/BVH8 benchmark artifacts. |
| 007-docs-and-discrete-cleanup.md | Pending | |

## Completed Tasks
- 001-safe-wide-bvh-core.md — added `BvhConfig.cs`, `BvhTypes.cs`, `BvhBuilder.cs`,
  `BvhNodeTest.cs`, `BvhTraversal.cs`, `BvhValidation.cs` under
  `Assets/Scripts/System/Api/Collision/Broadphase/`, plus
  `Assets/Tests/EditMode/BvhBroadphaseEditModeTests.cs`. No existing file modified.
- 002-target-broadphase-build.md — renamed
  `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs` (+ its
  `.meta`, GUID preserved) to `TargetBroadphaseSystem.cs` via `git mv`;
  `TargetSpatialHashSystem`/`TargetSpatialHashSingleton` -> `TargetBroadphaseSystem`/
  `TargetBroadphaseSingleton` across 9 consumer sources and 5 test files (rename-only
  diffs). Owner now also owns `TargetCircles` (`NativeList<BvhCircle>`), `DiscreteBvh`
  (`BvhTree`) and `DiscreteBvhScratch` (`BvhBuildScratch`), all `Allocator.Persistent`,
  created in `OnCreate` after `BvhValidation.ValidateConfiguration()` and disposed in
  `OnDestroy` after both handles complete. The main-thread `ToEntityArray`/
  `ToComponentDataArray` + copy loop is replaced by a Burst `GatherTargetsJob : IJobChunk`
  scheduled sequentially (`Schedule`, never `ScheduleParallel` — one running write index
  across chunks) that fills the four snapshot lists plus the circle cache in one pass;
  the three retained hash-build jobs plus a new `BuildDiscreteBvhJob : IJob` all depend
  on the gather handle and combine into the same `BuildHandle`. Nothing queries the BVH
  yet (task 003).
- 003-projectile-bvh-queries.md — migrated only
  `ProjectileDiscreteCollisionSystem` from collision-hash enumeration to
  `BvhTraversalWorkspace` in a parallel `IJobChunk`. One workspace is created per
  chunk and reset per projectile using the world-AABB midpoint and half-diagonal.
  Explicit query preserves enabled `Active`/`CombatCollisionActiveTag`, disabled
  `ArmingTag`, present `TimedSpawnComponent`, and all original component/buffer access.
  Faction, contact gate, AABB, exact narrowphase, emissions, pierce, and deactivation
  remain in original order. Added multi-level, BVH false-positive pruning, and
  conservative-circle-edge PlayMode regressions; test projectiles now include disabled
  `TimedSpawnComponent` as required by runtime archetypes.
- 004-continuous-hash-preservation.md — verified continuous collision still reads
  `ProjectileCollisionCells` and `MaxTargetRadius`, builds endpoint/corridor union
  bounds, expands before cell enumeration, runs original exact filters, caps/sorts TOI
  candidates, and preserves impact/emission/lifecycle behavior. Runtime source unchanged
  beyond task-002 owner rename. Added `LongTravel_TargetNearCorridorEdgeInAdjacentHashCell_Hits`
  to prove radius expansion reaches a target center in adjacent hash cell.
- 005-aoe-target-acquisition-hash-preservation.md — verified impact/lingering AOE
  still use `AoeOccupiedCells` through `AoeCollisionCore`, including duplicate
  suppression and `MaxAoeTargetsPerTick = 32`; targeted chain/external acquisition
  still uses occupied-cell snapshots and preserves ranking/ties/exclusions/timing/mana/
  payload/lifecycle; tracking still uses `TrackingCells` plus `TrackingIndicesById`
  with target-index validation and directional bands. No task-005 files changed.

## Blockers
- Task 006 external evidence missing:
  `Logs/TestResults-EditMode-BvhBroadphase.xml`,
  `Logs/TestResults-PlayMode-BvhBroadphase.xml`, BVH4/BVH8 Burst Inspector kernel
  evidence, steady-state profiler GC/pruning captures, and same-hardware BVH4/BVH8
  `BenchmarkLarge` measurements. Agent cannot run Unity tests and no artifacts currently
  exist under `Logs/`, so task 007 must not start yet.

## Performance Baselines (for task 006 comparison)
- Discrete-hash baseline (pre-BVH-migration, captured by user 2026-08-30,
  `Assets/Scenes/BenchmarkLarge.unity`, release build): **~217k active
  projectiles at ~16ms frame time.** Recorded before task 003 touches discrete
  collision, per `002-target-broadphase-build.md`/`006-validation-and-
  performance.md` dependency. Task 006 must capture the same workload/build
  configuration for discrete BVH4 and BVH8 to compare against this number.
- Discrete BVH8 qualitative result (user report after WinPlayer Burst fixes,
  2026-08-30): release-build performance is roughly the same as the discrete-hash
  baseline, with almost no visible difference. Exact steady-frame projectile count/frame
  time and BVH4 comparison were not supplied, so this does not select a width or complete
  the three-way performance gate.
- BVH4 comparison state: `BvhConfig.ChildCount` temporarily changed from 8 to 4 after
  qualitative BVH8 report. Simulation compile check passes (0 errors; 8 unrelated
  warnings). Awaiting same release-build `BenchmarkLarge` result; restore/select final
  width after comparison.
- BVH4 compiled-kernel proof (user-exported `bvh-asm.txt`, 2026-08-30): passed.
  Hot node code loads center-X and center-Y with two contiguous `vmovups xmm` loads;
  radius is consumed directly from contiguous `[base + 32]` memory by packed `vaddps`.
  Query X/Y/radius use packed broadcast/shuffle. Full overlap equation is `vsubps`,
  packed `vmulps`, `vaddps`, packed `vcmpleps`, then one `vmovmskps`. No `vgather`,
  scalar per-lane comparison, or branch-based mask construction occurs before mask.
  Export is BVH4 (`ChildCount = 4`); BVH8 two-half `float4` proof remains pending.

## Validation Summary
- Task 001: agent ran no Unity tests (per plan). Static validation performed:
  Roslyn compile of the six new sources and of the new EditMode test file against the
  project's own Unity/Burst/Collections/Mathematics assemblies (0 errors, 0 warnings;
  the test file compiles without a `Unity.Burst` reference, matching the EditMode
  asmdef). Algorithm logic was additionally exercised in a scratch host at both
  `BvhConfig.ChildCount = 8` and `= 4` (build/containment/determinism/degenerate
  scenes, kernel masks vs scalar reference, randomized brute-force superset check,
  streaming past the 127-entry fixed stack) — all passed. `BvhBroadphaseEditModeTests`
  itself is written but NOT run; it awaits the user's EditMode run and the task 006 XML
  gate. Burst Inspector SIMD proof also remains a task 006 item.
- Orchestrator review of task 001: read all 6 new source files plus the test file in
  full. Code matches `bvh.md`/index.md/packet requirements: BVH4 kernel is one packed
  `float4` equation ending in `math.bitmask`; BVH8 kernel is real end-to-end AVX
  (`Avx.mm256_sub/mul/add/cmp/movemask_ps`) guarded by `Avx.IsAvxSupported`, with a
  portable fallback that runs the BVH4 kernel twice and ORs the two 4-bit masks; no
  scalar per-lane branch precedes any mask. Hot bounds are one flat `NativeList<float4>`
  (48B/node at width 4, 96B/node at width 8), cold metadata flattened
  `NativeArray`-style lists addressed by `nodeIndex*ChildCount+lane`; no `fixed` buffers,
  no pointers, `allowUnsafeCode` untouched (confirmed false in both `.asmdef` and
  `ProjectSettings.asset`). `git status` confirms only new files were added — no
  existing file touched.
- Attempted a headless Unity batchmode compile check
  (`Unity.exe -batchmode -nographics -quit -projectPath . -logFile
  Logs/BvhTask001CompileCheck.log`, not a test run) as an extra gate beyond the
  sub-agent's own scratch-host Roslyn check. It aborted immediately: "It looks like
  another Unity instance is running with this project open" — the user's own Editor
  currently has this project open, so a second headless instance cannot compile it.
  Did not attempt to close the user's Editor. Compile correctness therefore rests on
  the Roslyn scratch check plus manual review above, not on an authoritative Editor
  compile; user should glance at the Unity Console next time the Editor regains focus
  and recompiles, and flag anything red.
- Task 002: agent ran no Unity tests (per plan). Static validation: `dotnet build` of the
  full `PlayGround.Sim` assembly (110 sources, all 16 Entities/Unity source generators
  active, `AllowUnsafeBlocks=False`) plus `PlayGround.Tests.EditMode` and
  `PlayGround.Tests.PlayMode` (built against the freshly compiled `PlayGround.Sim.dll`)
  — 0 errors, 0 warnings in all three. Temporary check csproj files were generated from
  the Unity-generated ones (project references swapped for
  `Library/ScriptAssemblies/*.dll`) and deleted afterward. `git grep TargetSpatialHash --
  Assets/` returns zero matches. Not verified by the agent: job-safety/Burst runtime
  behavior of the new gather + BVH build jobs (needs a PlayMode run), and the
  `Logs/TestResults-*` XML gate, which stays a task 006 item.
- Task 003: agent ran no Unity tests (per plan). Static validation: simulation-source
  `dotnet build` succeeded with 0 errors (8 unrelated existing warnings), `git diff
  --check` is clean, and scoped diff contains only the two allowed files. Static search
  confirms discrete source no longer references collision cells, `MaxTargetRadius`, or
  `CombatSpatialHash`, and contains no duplicate BVH node geometry. Orchestrator reviewed
  explicit query reconstruction, enableable masks, chunk-local workspace, conservative
  query construction, preserved candidate logic, and all three added regression methods.
  PlayMode behavior awaits user-run XML evidence in task 006.
- Task 004: agent ran no Unity tests (per plan). Static search confirms retained
  `ProjectileCollisionCells`, `MaxTargetRadius`, `CombatSpatialHash.FloorCell`, and
  `CellKey` use, with no `BvhTree`, `BvhTraversalWorkspace`, `DiscreteBvh`, or BVH query
  circle in continuous source. Runtime diff is only task-002 owner rename. Orchestrator
  reviewed added long-travel geometry: corridor upper edge remains in cell 0 while target
  center is in adjacent cell 1; target radius expansion includes it and exact corridor
  collision accepts it. `git diff --check` is clean.
- Task 005: agent ran no Unity tests (per plan). Static search across AOE, targeted,
  external gate, and tracking consumers found zero BVH/tree/workspace references.
  Evidence review confirms occupied-cell AOE traversal, fixed dedup list and 32-hit cap,
  targeted occupied-cell ranking/exclusion flows, and tracking cell/ID-map refresh and
  directional acquisition remain. Scoped diffs contain only task-002 owner renames;
  named EditMode/PlayMode regression suites remain present.
- Task 006 implementation: added `ENABLE_PROFILER`-gated per-chunk BVH query metrics
  (one queue enqueue per chunk; no per-candidate shared write), owner-published target/
  node/depth/active-lane and pruning counters, worker build/discrete-query markers,
  retained-consumer schedule markers, exact containment diagnostic coordinates, and
  sparse/clustered/identical-center/oversized/long-thin/moving-frame brute-force cases.
  Default `BvhConfig.ChildCount` remains 8. Simulation compile checks passed with and
  without `ENABLE_PROFILER` (0 errors; 8 unrelated existing warnings); `git diff --check`
  is clean. Static audit confirms full packed BVH4/BVH8 source kernels and no retained
  consumer migration. Task remains blocked on user-generated external artifacts listed
  above; no Unity tests were run and no pass claim is made.
- Task 006 Burst compile fix: Unity reported BC1091 because
  `ProfilerCounterValue<T>` fields shared `TargetBroadphaseSystem`'s static constructor
  with profiler markers referenced by nested Burst jobs. Moved every counter into a
  separate nested `ProfilerCounters` static class. Its independent constructor is reached
  only from managed `OnUpdate`; Burst-visible worker marker initialization no longer sees
  `ProfilerUnsafeUtility.CreateCounterValue__Unmanaged`. Profiling-enabled simulation
  compile check still succeeds with 0 errors (8 unrelated existing warnings). Unity Burst
  recompilation remains user validation; no tests were run.
- Task 006 AVX compile fix: WinPlayer Burst reported BC1200 because the public
  `Bvh8OverlapMaskAvx` method could be compiled as its own `SSE2AndLower` unit, where
  `WideNodeMask`'s caller-side AVX guard provided no block-local ISA proof. Wrapped every
  `mm256_*` intrinsic inside `if (Avx.IsAvxSupported)` within
  `Bvh8OverlapMaskAvx` itself and routed its baseline block to the existing two-`float4`
  portable kernel. Full AVX equation/movemask remains intact. Simulation compile check:
  0 errors (8 unrelated warnings); WinPlayer Burst rebuild remains user validation.
- User SIMD portability decision (2026-08-30): removed forced
  `Avx.mm256_*`, `v256`, `Avx.IsAvxSupported`, and x86-intrinsic imports from
  `BvhNodeTest`. BVH8 now always expresses two complete `float4` overlap equations and
  combines their masks; Burst chooses target-supported instruction encoding. Updated
  EditMode kernel regression and plan/context acceptance language. Tradeoff: compiler may
  keep BVH8 as two 128-bit batches rather than one 256-bit batch; no ISA is forced.
