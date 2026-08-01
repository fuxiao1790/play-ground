# Task Execution Packet

## Task

001-events-and-scope-wiring.md

## Goal

Add unmanaged target-proxy create/update/delete event buffer contracts and shared-scope storage.

## Files Allowed To Modify

- `Assets/Scripts/System/Core/CombatScopeOwner.cs`

## Files Allowed To Create

- `Assets/Scripts/System/Targets/TargetProxyEvents.cs`

## Dependencies Confirmed

- None.

## Acceptance Criteria

- All event structs are unmanaged `IBufferElementData`.
- `CombatScopeOwner.Acquire` adds all three buffers.
- No behavior reads/writes them until later tasks.

## Validation Required

- Static contract and scope-wiring inspection; compilation after dependent pipeline tasks.
