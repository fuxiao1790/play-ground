# Task Execution Packet

## Task

007-combat-root-registration-and-spawn-api.md

## Goal

Add Sim-side targeted type registry, managed `CombatRoot` template/type registration and cast submission, plus external/timed targeted routing.

## Files Allowed To Modify

- `Assets/Scripts/System/Core/CombatRoot.cs`
- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`
- `Assets/Scripts/System/Spawning/TimedSpawnSystem.cs`
- `Assets/Scripts/System/Targeted/TargetedTypeRegistry.cs` (new)
- Tests directly covering this task.

## Relevant Global Context

- Targeted and lingering targeted use separate typed event lanes and the shared `TargetedSpawnTemplate` registry.
- Registry writes are managed pre-tick only; simulation reads it read-only.
- External requests must preserve legacy behavior: a default `AcquireAnchor` becomes `Position`.
- Timed child spawns use their source kinematics position for both origin and acquire anchor.
- Lanes are direct singleton accesses and producer handles must include timed-spawn writers.

## Dependencies Confirmed

- `TargetedSpawnCommand`, `TargetedSpawnTemplate`, targeted events, and both expansion/apply systems exist (tasks 002/003).
- Updated task 007 owns `TargetedTypeDefinition` and `TargetedTypeRegistry` in Sim. Task 008 owns only Skills-assembly authoring types.

## Acceptance Criteria

- Same targeted template hashes to same key; changed count hashes differently.
- Mana-gated targeted root cast preserves origin and anchor.
- Default external anchor resolves to position.
- Timed targeted variants route into their matching lanes.
- Same targeted definition reference returns same type id.
- VFX-only targeted definitions have no baked visual and do not throw.
- `SetTargetedVfxIds` updates registered definition ids.

## Hard Boundaries

- Do not change architecture or combine tasks.
- Stop on architectural ambiguity or missing dependency.

## Status

Ready: updated plan resolves former task-order conflict.
