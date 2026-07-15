# Task Execution Packet

## Task
002-reshape-hit-event.md

## Goal
Shrink `CombatHitEvent` to source and target entity references.

## Files Allowed To Modify
- `Assets/Scripts/System/Application/CombatHitEvent.cs`
- Direct compile fixes caused by field rename/removal in producers/finalize.

## Behavior To Preserve
- Finalize remains the only consumer.

## Behavior To Change
- Event carries `{ Source, Target }` only.

## Relevant Global Context
Removed data now lives on `CombatHitPayload` attached to the source entity.

## Dependencies Confirmed
- Requires task 001 payload component shape.

## Acceptance Criteria
- `CombatHitEvent` has exactly `Source` and `Target`.

## Validation Required
- Full compile is expected only after tasks 001-004 land together.
