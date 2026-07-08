# Task Execution Packet

## Task
007-vfx-sorting-layer-authoring.md

## Goal
Ensure combat VFX draw on `CombatVfx` layer with order `0`.

## Files Allowed To Modify
- Content/authoring files for VFX renderers.

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`
- VFX assets and skill prefabs.

## Behavior To Preserve
- VFX dispatch payloads and graph contracts.

## Behavior To Change
- Registered combat VFX render behind combat sprites.

## Relevant Global Context
- Runtime creates `VisualEffect` GameObjects from `VisualEffectAsset`, so the created renderer must get the sorting layer if no authored prefab renderer exists.

## Dependencies Confirmed
- 001 adds `CombatVfx`.

## Step-By-Step Instructions
- Locate registered VFX assets.
- Set corresponding VFX renderer sorting layer/order, or configure the runtime-created VisualEffect renderer if assets have no prefab renderer.

## Acceptance Criteria
- Combat VFX render on `CombatVfx`, order `0`.

## Validation Required
- Search and manual visual check.

## Hard Boundaries
- Do not change VFX graph data contract.
