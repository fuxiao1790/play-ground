# Task Execution Packet

## Task
001-impact-aoe-cold-create-burst.md

## Goal
Move impact AOE cold-create ECB recording from the managed main-thread loop into `ImpactAoeSpawnJob`.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `.agent/spawn-cold-create-burst/implementation-log.md`

## Files Allowed To Create
- None beyond this packet.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`

## Behavior To Preserve
- Impact AOE component values and enable bits.
- Main-thread ECB playback after job completion.
- Reuse counter records only reused disabled slots.

## Behavior To Change
- Use `Allocator.TempJob` for the impact create ECB.
- Record unreused impact AOE commands inside the Burst job.
- Delete the managed main-thread cold loop.

## Relevant Global Context
- ECB in a job must use `Allocator.TempJob`.
- Cold suffix is `Configs[commandIndex..]` after reuse chunk scan.
- Job stays single-threaded and preserves command order.

## Dependencies Confirmed
- `ImpactAoeSpawnJob` exists in `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`.
- `AoeSpawnApplyUtility.RecordImpactReset` is static and uses unmanaged component data.
- `CombatPoolCleanupSystem` uses a `TempJob` ECB from a Burst job.

## Step-By-Step Instructions
- Change impact `createEcb` allocator to `Allocator.TempJob`.
- Add `EntityCommandBuffer Ecb` and `EntityArchetype Archetype` to `ImpactAoeSpawnJob`.
- Pass `Ecb = createEcb` and `Archetype = _impactArchetype` into the job.
- Add a cold-suffix loop at the tail of `Execute()` before `ReuseCount.Value = commandIndex`.
- Remove the managed cold-create loop from `OnUpdate`.
- Compute `coldCreateCount = commands.Length - reuseCount`.

## Acceptance Criteria
- `ImpactAoeSpawnJob` compiles under Burst.
- Impact reuse plus cold count equals command count.
- AOE simulation tests pass or any inability to run is reported.

## Validation Required
- Compile/build.
- Run relevant AOE PlayMode tests when practical.

## Hard Boundaries
- Do not modify files outside the allowed list except imports directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
