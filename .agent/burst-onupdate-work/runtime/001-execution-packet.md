# Task Execution Packet

## Task
001-resource-regen-job.md

## Goal
Move `ResourceRegenSystem` health and mana regeneration loops into separate Burst `IJobEntity` jobs.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/ResourceRegenSystem.cs`
- `.agent/burst-onupdate-work/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Targets/ResourceRegenSystem.cs`
- `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs`
- `Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs`

## Behavior To Preserve
- Health-only and mana-only entities continue regenerating.
- Health uses `math.min` with no lower clamp; mana uses `math.clamp(..., 0f, Max)`.
- Existing group/order attributes remain unchanged.

## Behavior To Change
- Per-entity work leaves managed `OnUpdate` and runs in Burst jobs.

## Relevant Global Context
- Jobs access unmanaged ECS data only. Chain schedules through `state.Dependency`; no structural changes or native-container handling applies.

## Dependencies Confirmed
- None required. `ResourceRegenSystem` exists and current loops are present.

## Step-By-Step Instructions
1. Add `Unity.Burst` and `Unity.Jobs` imports.
2. Replace foreach loops with `HealthRegenJob.ScheduleParallel(state.Dependency)` followed by `ManaRegenJob.ScheduleParallel(healthHandle)` assigned to `state.Dependency`.
3. Add separate nested `[BurstCompile]` partial `IJobEntity` structs with original health and mana math.

## Acceptance Criteria
- `OnUpdate` has no foreach/per-entity work.
- Two separate Burst jobs; neither requires both components.
- Final handle assigned to `state.Dependency`.
- Numeric behavior and attributes preserved.

## Validation Required
- Static source verification. Unity EditMode tests are user-run only with XML output.

## Hard Boundaries
- Do not edit unrelated files or add abstractions.
- Do not merge health and mana into one job.
- Stop on architectural ambiguity.
