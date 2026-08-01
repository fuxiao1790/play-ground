# Task Execution Packet

## Task

002-combat-target-proxy-rewrite.md

## Goal

Convert `CombatTargetProxy` mutators to enqueue-only lifecycle events; `Create` returns `bool` and uses managed pending-create token maps.

## Files Allowed To Modify

- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`

## Files Likely Needed For Reading

- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`

## Behavior To Preserve / Change

- Preserve payload creation helpers and read APIs.
- Enqueue all writes. Create uses shared proxy token, rejects re-entrant creates; delete cancels pending creation and resets target proxy immediately.
- `SetHealth` and `SetMana`: enqueue max update then current update in one FIFO buffer.

## Dependencies Confirmed

- 001 event structs exist in `Targets/TargetProxyEvents.cs`.
- 001 scope buffers are added in `CombatScopeOwner.Acquire`.

## Acceptance Criteria

- No direct structural/component mutations in mutating methods.
- Live proxy `Create` queues Push; double pending create no-ops.
- Pending delete removes both managed dictionary entries; entity delete is queued.

## Validation Required

- Static call/path inspection and targeted test/build if task scope permits.

## Hard Boundaries

- Do not modify registry, apply systems, actor roots, tests, or docs; those are later tasks.
