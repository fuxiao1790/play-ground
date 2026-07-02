# Task Execution Packet

## Task
005-tests-and-profiling.md

## Goal
Add coverage for reuse, overflow remainder, count split, lossy behavior, and pool convergence; record profiling status.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `.agent/spawn-apply-parallel-reuse/profiling-note.md`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- `.agent/spawn-apply-parallel-reuse/profiling-note.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/idle-combat-system-cost/`
- Existing projectile and AOE playmode tests.

## Behavior To Preserve
- Existing spawn/collision/lifetime/timed-spawn behavior.

## Behavior To Change
- Add tests that force reuse + overflow split and convergence.
- Record profiling evidence or explain why profiling could not be captured.

## Relevant Global Context
- Uneven worker chunk ownership intentionally allows overflow even when free slots exist elsewhere.
- Counts must sum to total command count.

## Dependencies Confirmed
- Tasks 002 through 004 completed.
- Runtime and playmode test projects build.

## Step-By-Step Instructions
- Add projectile repeated forced-overflow deterministic test.
- Add impact AOE overflow/convergence test.
- Add lingering AOE overflow/timed-spawn reset test.
- Compile tests.
- Attempt profiling/test run where possible.

## Acceptance Criteria
- Tests exercise reuse and overflow-remainder paths.
- Existing spawn/collision/lifetime/timed-spawn test assembly compiles.
- Profiling note records status.

## Validation Required
- `dotnet build .\PlayGround.Tests.PlayMode.csproj`
- Focused Unity PlayMode runs when project lock permits.

## Hard Boundaries
- Do not add production-only test metadata.
- Do not broaden profiling beyond apply costs.
