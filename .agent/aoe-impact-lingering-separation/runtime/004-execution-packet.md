# Task Execution Packet

## Task
004-split-expansion-systems.md

## Goal
Replace combined AOE expansion with impact and lingering expansion systems over shared expansion core.

## Files Modified
- Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs
- Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs
- Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs
- Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs
- Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs
- Assets/Scripts/System/Common/TimedSpawnSystem.cs
- Assets/Scripts/System/Status/StatusProcessSystem.cs

## Dependencies Confirmed
- Impact and lingering event structs exist.
- Producer routing targets both new systems.

## Acceptance Result
- Complete.

## Validation
- Search confirms no old exact script references remain.
