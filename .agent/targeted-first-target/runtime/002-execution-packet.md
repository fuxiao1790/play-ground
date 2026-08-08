# Task Execution Packet

## Task
002-gate-acquires-first-target.md

## Goal
For targeted root casts, gate resolves nearest hostile near aim anchor and stamps position/flag.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`
- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Behavior
- Other spawn kinds and interval/on-hit targeted paths stay unchanged.
- Complete hash build before main-thread snapshot read.
- Missing template means unchanged event, no acquisition.

## Dependencies Confirmed
- Task 001 complete: `TargetedAcquisition` exists and resolve calls it.

## Validation
- Static search confirms flag propagation and template normalization.
- Do not run Unity tests.
