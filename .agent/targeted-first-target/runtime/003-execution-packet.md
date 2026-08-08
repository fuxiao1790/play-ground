# Task Execution Packet

## Task
003-apply-seeds-pose-from-anchor.md

## Goal
Materialize targeted entities at `AcquireAnchor`; arm only when authored arm time exists or no target was acquired.

## Files Allowed To Modify
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`

## Behavior
- Origin and link source stay caster origin.
- Link target and kinematics position use anchor.
- First link VFX starts at chain origin.
- Deferred-expiry logic stays unchanged.

## Dependencies Confirmed
- Task 002 complete: fields and propagation exist through expansion.

## Validation
- Static review checks pose and conditional arming.
- Do not run Unity tests.
