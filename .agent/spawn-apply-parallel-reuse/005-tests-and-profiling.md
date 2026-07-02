# 005 Tests And Profiling

## Goal

Cover the parallel apply and lossy-reuse paths and confirm the intended cost win.

## Scope

- Reuse/overflow tests per domain:
  - all commands reused when free slots ≥ commands
  - overflow cold-creates when commands exceed free slots in the assigned chunks
  - reuse + cold counts sum to total commands
  - profiler counters (`*.Reuse`, `*.Cold`) report the split
- Forced-imbalance test: seed dead slots into few chunks and many commands into
  the mismatched partitions; assert some cold-create occurs (documents the lossy
  behavior) and that the pool converges (fewer cold-creates on subsequent frames
  once capacity grows).
- Cross-reuse tests from the unify plan still pass (basic↔timed projectile,
  non-timed↔timed lingering; impact/lingering disjoint).
- Profiling: capture busy-scene apply cost before/after; confirm reuse moved off a
  single worker. Compare against `.agent/idle-combat-system-cost/` baselines if
  present.

## Acceptance Criteria

- Tests exercise both the reuse and the overflow-remainder paths.
- Existing spawn/collision/lifetime/timed-spawn tests pass.
- A profiling note records the apply cost change and confirms parallel execution.

## Dependencies

Depends on 002, 003, and 004.

## Complexity

Medium.
