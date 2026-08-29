# 006 - Validation And Performance Proof

## Change

Complete cross-domain correctness validation and benchmark evidence.

Add development diagnostics for target count, node count, depth, bounds
containment, active lanes, nodes visited, child circles tested, surviving lanes,
leaf candidates, and exact tests. Query counters must use worker-local storage or
compile out when profiling is disabled; no atomics per candidate.

Add profiler markers separating target gather, tracking build, Morton sort/tree
build, and consumer query jobs. Use Burst Inspector to inspect BVH4 float4 and
BVH8 AVX/fallback kernels.

Use old-hash baseline captured before task 002, then capture same `BenchmarkLarge`
workload for BVH4 and BVH8. Compare steady frames after warm-up on same hardware.

## Acceptance Criteria

- Randomized brute-force validation covers sparse, clustered, identical-center,
  oversized, long-thin, negative-coordinate, and moving-target frames.
- Debug validator reports exact node/lane/reference on containment failure.
- No managed GC allocation appears in steady-state build/query markers.
- Profiler data exposes build cost and each collision consumer cost separately.
- Burst Inspector confirms packed four-lane BVH4 test and eight-lane AVX BVH8
  test on supported x86 target; fallback path remains present for unsupported ISA.
- Capture records target/node counts and average pruning metrics with timing.
- Results compare hash baseline, BVH4, BVH8; chosen default remains `8` unless
  evidence selects `4`.
- User runs:
  - EditMode: `BvhBroadphaseEditModeTests`, `TargetedResolveEditModeTests` ->
    `Logs/TestResults-EditMode-BvhBroadphase.xml`.
  - PlayMode: `ProjectileCollisionSimulationTests`,
    `ProjectileContinuousSimulationTests`, `ProjectileTrackingSimulationTests`,
    `AoeSimulationTests`, relevant `AoePlayModeTests`, and
    `TargetedSkillPlayModeTests` ->
    `Logs/TestResults-PlayMode-BvhBroadphase.xml`.
- XML files are reviewed before any pass claim. Agent does not run tests.

## Dependencies

- 001 through 005.

## Scope / Complexity

High. Cross-domain validation, profiling instrumentation, ISA inspection, and
three-way measurement.
