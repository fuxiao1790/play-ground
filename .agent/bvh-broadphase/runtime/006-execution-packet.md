# Task Execution Packet

## Task
006-validation-and-performance.md

## Goal
Implement development-only BVH diagnostics and profiling separation, complete missing deterministic/randomized validation coverage, and prepare external XML/Burst/benchmark evidence gates. Do not run Unity tests or claim external gates passed.

## Files Allowed To Modify
- `Assets/Scripts/System/Api/Collision/Broadphase/BvhTraversal.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/BvhValidation.cs`
- `Assets/Scripts/System/Api/Collision/Broadphase/TargetBroadphaseSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs` — profiling only; no algorithm/query changes.
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs` — profiling only.
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs` — profiling only.
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs` — profiling only.
- `Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs` — profiling only.
- `Assets/Tests/EditMode/BvhBroadphaseEditModeTests.cs` — add missing validation cases only.

## Files Allowed To Create
- One small broadphase diagnostics/profiling source plus `.meta` only if keeping metrics in `BvhTraversal.cs`/owner would make responsibilities unclear.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- All allowed files.
- `Assets/Scripts/System/Api/Collision/Broadphase/BvhNodeTest.cs`, `BvhBuilder.cs`, `BvhTypes.cs`, `BvhConfig.cs`.
- Existing profiler marker/counter patterns under `Assets/Scripts/System/`.
- Named EditMode/PlayMode suites and `Docs/testing.md` for external gate names.

## Behavior To Preserve
- All build/query algorithms, consumer broadphase choices, ordering, caps, emissions, and lifecycle.
- Compile-time width selection; default remains 8 pending evidence.
- Safe C#/Burst/no per-query allocation.

## Behavior To Change
- Development profiling exposes target/node/depth/active-lane counts and discrete pruning metrics: query count, nodes visited, child circles tested, surviving lanes, leaf candidates, exact tests.
- Query metrics aggregate worker-locally and publish at most once per chunk; no atomic operation per candidate. Compile metric collection out when profiling is disabled.
- Profiler distinguishes gather, each retained hash build, Morton/BVH build, discrete query, and retained consumer query lanes.
- Validator failure text identifies exact node/lane/reference for containment failures.
- EditMode validation covers sparse, clustered, identical-center, oversized, long-thin, negative-coordinate, and moving-target frames with brute-force no-false-negative checks.

## Relevant Global Context
- Owner completes previous `ConsumerHandle`/`BuildHandle` before reusing persistent containers. This permits draining prior-frame chunk-local metric records safely in owner update.
- A persistent `NativeQueue` with one aggregate record per discrete chunk is acceptable worker-local aggregation: one enqueue per chunk, never per candidate. Guard queue, workspace counters, writes, and draining with `#if ENABLE_PROFILER` so release profiling-disabled code has no query-counter work.
- Prefer `ProfilerCounterValue` on main thread for published counts/averages. Avoid managed collections/strings/allocations in steady update.
- Runtime profiler markers must not change algorithms. If marking an `IJobEntity.Execute` would add a per-entity profiling sample on a high-count lane, use existing job/system samples plus a minimally invasive schedule marker and document limitation; do not migrate job shapes merely for profiling.

## Dependencies Confirmed
- Tasks 001–005 complete by source/static evidence.
- User baseline already recorded: about 217k active projectiles at about 16 ms in `BenchmarkLarge`, release build, before task 003.

## Step-By-Step Instructions
1. Read task and all relevant source/tests in full. Confirm current diagnostics/coverage gaps.
2. Add compile-time-disabled worker-local query metrics. Workspace owns counters; reset/query traversal updates them; discrete narrowphase records exact-test attempts. Aggregate one record per chunk into persistent owner-controlled storage, drain only after consumer completion, and publish counts/averages without managed GC.
3. Publish target count, BVH node count, depth, and total active lanes. Keep active-lane calculation exact and allocation-free.
4. Split target build profiling into gather, continuous collision hash, tracking hash, AOE occupied hash, and Morton/BVH build markers/counters. Expose discrete and retained consumer lanes distinctly without changing query shapes.
5. Improve validator containment failures to include parent node, parent lane, and child object/node reference (plus child lane when relevant). Add focused assertion on diagnostic text.
6. Add/extend brute-force EditMode cases for every required distribution/frame category. Reuse current helpers/harness; no alternate implementation path.
7. Audit BVH4/BVH8 source: full packed math before mask; BVH8 uses two complete
   `float4` halves and compiler-selected ISA, with no scalar lane geometry or x86
   intrinsics.
8. Static/compile validation only. Never run Unity tests/runner.
9. Report external gates still required exactly: EditMode/PlayMode XML paths, Burst Inspector BVH4/BVH8 evidence, same-hardware BVH4/BVH8 `BenchmarkLarge` captures with target/node/pruning/timing data and GC verification.

## Acceptance Criteria
- All task-file criteria implemented or explicitly left as external evidence gates.
- No managed GC allocation introduced in steady build/query paths.
- No atomics per candidate.
- No retained consumer migrates to BVH.
- Default width remains 8 without contrary benchmark evidence.

## Validation Required
- Static source/diff audit and non-test compile when available.
- `git diff --check`.
- Do not run Unity tests or Unity test runner.
- External gates cannot be marked passed without user artifacts.

## Hard Boundaries
- Do not change collision/acquisition/tracking behavior.
- Do not add runtime width selection or dual-query path.
- Do not add per-candidate shared writes/atomics or per-query allocations.
- Do not alter `allowUnsafeCode` or project settings.
- Stop on architectural ambiguity.
