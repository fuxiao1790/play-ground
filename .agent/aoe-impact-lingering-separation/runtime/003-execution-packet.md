# Task Execution Packet

## Task
003-route-producers.md

## Goal
Route ECS producers to impact or lingering AOE event queues by carried kind.

## Files Modified
- Assets/Scripts/System/Aoe/AoeCollisionCore.cs
- Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs
- Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs
- Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs
- Assets/Scripts/System/Common/TimedSpawnSystem.cs
- Assets/Scripts/System/Status/StatusProcessSystem.cs
- Assets/Scripts/System/Common/CombatRoot.cs

## Dependencies Confirmed
- Producers now receive `ImpactAoeSpawnExpansionSystem` and `LingeringAoeSpawnExpansionSystem` writers.

## Acceptance Result
- Complete.

## Validation
- Search confirms no producer emits variant-agnostic AOE events.
