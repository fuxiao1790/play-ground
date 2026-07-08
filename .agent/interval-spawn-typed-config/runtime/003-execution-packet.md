# Task Execution Packet

## Task
003-template-scatter-threading.md

## Goal
Thread `RuntimeAoeIntervalSpawnSetup.ScatterRadius` into interval AOE spawn templates while leaving normal AOE templates on child `ScatterRadius`.

## Files Allowed To Modify
- `Assets/Scripts/Skills/PlayerSkillDriver.cs`

## Behavior To Preserve
- Top-level/on-hit AOE registration calls `BuildAoeTemplate` without override and keeps child `ScatterRadius`.
- Existing timed spawn/default parameter behavior stays source-compatible.

## Behavior To Change
- `RegisterAoeIntervalTemplate` passes setup scatter to `BuildAoeTemplate`.
- `BuildAoeTemplate` uses override when present.

## Dependencies Confirmed
- 002 complete: `RuntimeAoeIntervalSpawnSetup.ScatterRadius` exists.

## Validation Required
- Search `PlayerSkillDriver.cs` for `scatterRadiusOverride` and `setup.ScatterRadius`.

## Hard Boundaries
- Do not modify ECS systems or tests/docs in this task.
