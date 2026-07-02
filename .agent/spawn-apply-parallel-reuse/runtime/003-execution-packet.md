# Task Execution Packet

## Task
003-aoe-apply-migration.md

## Goal
Move impact and lingering AOE spawn apply from serial disabled-slot reuse to worker-lane parallel dead-slot reuse.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`

## Behavior To Preserve
- Impact and lingering pools stay disjoint.
- Impact reset does not add lifetime, pulse, gates, or timed-spawn fields.
- Lingering reset restores lifetime, pulse, gate buffer, and timed-spawn state.
- Existing count fields and profiler counter names stay.

## Behavior To Change
- Remove shared `NativeReference<int>` claim cursors.
- Both AOE apply jobs schedule as `IJobParallelFor` over worker lanes and chunk ranges.
- Overflow remainder command indices are cold-created through the correct archetype.

## Relevant Global Context
- Apply captures disabled chunks before structural changes.
- Lanes carry command indices only.
- Worker owns disjoint chunk ranges.

## Dependencies Confirmed
- Task 001 helper exists.
- Task 002 completed and runtime build passed.
- Impact AOE has one disabled-slot query with no `CombatLifetimeComponent`.
- Lingering AOE has one disabled-slot query with `CombatLifetimeComponent`.

## Step-By-Step Instructions
- Migrate impact apply to captured chunks, worker ranges, command lanes, and remainder queue.
- Migrate lingering apply to captured chunks, worker ranges, command lanes, and remainder queue.
- Remove old cursor fields and unsafe counter usage from both jobs.
- Preserve reset logic and count reporting.

## Acceptance Criteria
- Impact and lingering reuse use `IJobParallelFor`.
- No shared claim counter remains in AOE apply.
- Overflow cold-creates the correct archetype per system.
- Lingering resets lifetime, pulse, gates, and timed-spawn enable correctly.

## Validation Required
- `dotnet build .\PlayGround.Runtime.csproj`

## Hard Boundaries
- Do not alter AOE expansion or collision systems.
- Do not combine with determinism/test/doc tasks.
