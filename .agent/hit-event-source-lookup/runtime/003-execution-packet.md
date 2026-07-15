# Task Execution Packet

## Task
003-producers-set-source.md

## Goal
Collision systems enqueue `{ Source, Target }` and read source payload only for gate checks.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- Direct compile fixes caused by this task.

## Behavior To Preserve
- On-hit spawn emission behavior.
- Empty payload gate behavior.

## Behavior To Change
- Producers no longer copy damage/stack fields into hit events.

## Relevant Global Context
`CombatHitPayload` is a read-only component on every hit source archetype.

## Dependencies Confirmed
- Requires task 001 and 002.

## Acceptance Criteria
- No producer copies payload fields into `CombatHitEvent`.
- Producers enqueue source and target entities.

## Validation Required
- Full compile is expected only after tasks 001-004 land together.
