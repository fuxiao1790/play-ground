# 006 - Validation And Performance Proof

## Change

Complete discrete-projectile BVH correctness validation, retained-hash
regression validation, and benchmark evidence.

Add development diagnostics for target count, node count, depth, bounds
containment, active lanes, nodes visited, child circles tested, surviving lanes,
leaf candidates, and exact tests for discrete BVH queries. Query counters must
use worker-local storage or compile out when profiling is disabled; no atomics
per candidate.

Add profiler markers separating target gather, continuous/AOE/tracking hash
builds, Morton sort/tree build, discrete BVH query job, and retained hash
consumer jobs. Use Burst Inspector to inspect complete BVH4 and BVH8 kernels
from broadcast/subtract through packed comparison and bitmask. Packed storage or
packed add/subtract alone is insufficient proof.

Use discrete-hash baseline captured before discrete task 003 migration, then
capture same `BenchmarkLarge` workload for discrete BVH4 and BVH8. Continuous,
AOE, targeted, and tracking settings/paths remain identical in all captures.
Compare steady frames after warm-up on same hardware.

## Acceptance Criteria

- Randomized brute-force validation covers sparse, clustered, identical-center,
  oversized, long-thin, negative-coordinate, and moving-target frames.
- Debug validator reports exact node/lane/reference on containment failure.
- No managed GC allocation appears in steady-state build/query markers.
- Profiler data exposes BVH build/discrete query cost separately from retained
  continuous/AOE/targeted/tracking hash build/query costs.
- Source audit finds no scalar per-lane arithmetic, comparison loop, or `if`
  chain inside BVH4/BVH8 geometric node tests before lane mask exists.
- Burst Inspector confirms BVH4 packed subtract/multiply/add/compare followed by
  one mask extraction across four lanes.
- Burst Inspector confirms BVH8 performs two complete packed `float4` overlap
  equations plus two packed mask extractions; no eight-lane scalar loop.
- Source and compiled path contain no forced x86 AVX/AVX2 intrinsics or ISA
  branch; Burst selects supported instructions for target CPU.
- Benchmark evidence is invalid if inspected query kernel scalarizes child
  overlap math or constructs mask through per-lane branches.
- Capture records target/node counts and average discrete pruning metrics with
  timing.
- Results compare discrete hash baseline, discrete BVH4, and discrete BVH8;
  chosen default remains `8` unless evidence selects `4`.
- Continuous projectile profiling still shows spatial-hash broadphase and no BVH
  traversal. Long-corridor regression behavior and timing remain comparable to
  baseline.
- AOE and targeted profiling/tests still show occupied-cell broadphase and
  unchanged cap/ranking behavior.
- Projectile tracking profiling/tests still show tracking-cell/ID-map
  acquisition and no BVH traversal.
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

High. Discrete BVH validation, retained-hash regression proof, profiling
instrumentation, ISA inspection, and three-way measurement.
