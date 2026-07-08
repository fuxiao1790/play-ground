# Task Execution Packet

## Task
004-shader-slot-index.md

## Goal
Use baked UV1 slot index instead of `SV_InstanceID`.

## Files Allowed To Modify
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Behavior To Preserve
- Transform and UV basis math.
- `LightMode = Universal2D`.

## Behavior To Change
- Vertex shader indexes `_InstanceData` from `TEXCOORD1.x`.

## Relevant Global Context
- The mesh is no longer instanced; every quad has a baked slot index.

## Dependencies Confirmed
- 002 provides UV1 stream.

## Step-By-Step Instructions
- Replace `SV_InstanceID` with `float2 slotIndex : TEXCOORD1`.
- Read `_InstanceData[(uint)round(slotIndex.x)]`.
- Update shader comments.

## Acceptance Criteria
- Shader compiles and renders same data for same slot.

## Validation Required
- Unity shader compile/manual visual check.

## Hard Boundaries
- Do not alter atlas sampling.
