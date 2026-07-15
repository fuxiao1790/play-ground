# Task Execution Packet

## Task
004-finalize-source-lookup.md

## Goal
Finalize reads payload via read-only `ComponentLookup<CombatHitPayload>` indexed by source entity.

## Files Allowed To Modify
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
- Direct compile fixes caused by this task.

## Behavior To Preserve
- Existing target bucketing, stack accrue, damage, crit, and result logic.

## Behavior To Change
- Finalize reads `hit.Target` and payload fields from source lookup.

## Relevant Global Context
Both projectile and AOE sources carry the same payload component.

## Dependencies Confirmed
- Requires tasks 001 and 002.

## Acceptance Criteria
- Finalize reads no damage/stack fields from `CombatHitEvent`.

## Validation Required
- Run compile/build after 001-004.
