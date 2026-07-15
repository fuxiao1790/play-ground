# Task Execution Packet

## Task
001-promote-payload-component.md

## Goal
Promote `CombatHitPayload` to an indexable component on projectile, impact AOE, and lingering AOE entities.

## Files Allowed To Modify
- `Assets/Scripts/System/Application/CombatHitPayload.cs`
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Aoes/AoeEcsComponents.cs`
- `Assets/Scripts/System/Combat/Core/CombatRoot.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- Direct compile fixes caused by this task.

## Behavior To Preserve
- Authoring/config DTOs remain unchanged.
- Stack faction stamping remains in existing payload helper logic.
- On-hit spawn behavior remains unchanged.

## Behavior To Change
- ECS source entities carry standalone `CombatHitPayload`.
- `ProjectileHitComponent` and `AoeHitSpawnComponent` no longer contain nested payload data.

## Relevant Global Context
All hit source archetypes must include `CombatHitPayload` so later finalize can use one `ComponentLookup`.

## Dependencies Confirmed
- This is the first implementation task.

## Acceptance Criteria
- Projectile and both AOE archetypes include `CombatHitPayload`.
- Spawn apply writes the standalone payload component.
- Hit components are slimmed as planned.

## Validation Required
- Full compile is expected only after tasks 001-004 land together.
