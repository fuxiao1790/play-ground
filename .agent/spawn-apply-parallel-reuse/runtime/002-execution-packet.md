# Task Execution Packet

## Task
002-projectile-apply-migration.md

## Goal
Move projectile spawn apply from serial disabled-slot reuse to worker-lane parallel dead-slot reuse.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`

## Behavior To Preserve
- Projectile reset fields and enableable state match existing reuse/cold-create behavior.
- `LastReuseCount`, `LastColdCreateCount`, and profiler counter names stay.
- Overflow beyond free slots is cold-created.

## Behavior To Change
- Remove shared `NativeReference<int>` claim cursor.
- Reuse schedules as `IJobParallelFor` over worker lanes and chunk ranges.

## Relevant Global Context
- Worker owns lane and chunk range.
- Remainder contains command indices, not payload copies.
- No structural changes before captured chunks are no longer used.

## Dependencies Confirmed
- Task 001 helper exists and runtime build passed.
- Projectile apply has one projectile archetype and one disabled `Active` projectile dead-slot query.

## Step-By-Step Instructions
- Capture projectile disabled-slot chunks.
- Build worker ranges and command index lanes.
- Schedule parallel reuse job over worker count.
- Cold-create remainder command indices with existing projectile archetype.
- Remove old cursor work tracking and unsafe counter usage.

## Acceptance Criteria
- Projectile reuse uses `IJobParallelFor`.
- No shared claim counter remains.
- Reuse resets identity, kinematics, collision, lifetime, hit, tracking, render, contact gate, and timed-spawn state.
- Counts report reuse/cold split.

## Validation Required
- `dotnet build .\PlayGround.Runtime.csproj`

## Hard Boundaries
- Do not migrate AOE in this task.
- Do not alter projectile command generation.
