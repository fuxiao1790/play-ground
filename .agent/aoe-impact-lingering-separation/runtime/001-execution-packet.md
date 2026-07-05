# Task Execution Packet

## Task
001-variant-discriminator-and-events.md

## Goal
Add impact/lingering AOE variant enum cases, split AOE event structs, register both scope buffers, and add lifetime-to-variant helpers.

## Files Modified
- Assets/Scripts/System/Common/IntervalChildTemplates.cs
- Assets/Scripts/System/Status/StackEffectSnapshot.cs
- Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs
- Assets/Scripts/System/Common/CombatEcsComponents.cs

## Dependencies Confirmed
- Existing single `AoeSpawnEvent` and `IntervalChildKind.Aoe` were present before patch.

## Acceptance Result
- Complete.

## Validation
- Search confirms no old exact script references remain.
