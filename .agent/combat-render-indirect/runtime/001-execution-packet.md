# Task Execution Packet

## Task
001-indirect-shader.md

## Goal
Add `Combat/AtlasIndirectSprite`, reading `CombatInstanceData` from `_InstanceData` by `SV_InstanceID`.

## Files Allowed To Modify
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader.meta`

## Files Allowed To Create
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader.meta`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Shaders/CombatAtlasInstancedSprite.shader`
- `.agent/combat-render-indirect/index.md`

## Behavior To Preserve
- Transparent unlit URP sprite render state.
- Atlas UV remap formula.
- Existing instanced shader remains in place.

## Behavior To Change
- New shader uses `StructuredBuffer<CombatInstanceData>` and explicit matrix transform.

## Relevant Global Context
- C# and HLSL `CombatInstanceData` layout must match: `float4x4` plus `float4`, stride 80.

## Dependencies Confirmed
- Existing old shader exists at `Assets/Shaders/CombatAtlasInstancedSprite.shader`.

## Step-By-Step Instructions
- Add shader named `Combat/AtlasIndirectSprite`.
- Use `#pragma target 4.5`.
- Sample `_MainTex`; read `_InstanceData[SV_InstanceID]`; transform with `mul(inst.objectToWorld, float4(positionOS, 1))`.

## Acceptance Criteria
- Shader has no `UNITY_INSTANCING_BUFFER`, `unity_ObjectToWorld`, or MPB array dependency.
- Render state matches old shader.

## Validation Required
- Runtime compile/import check.
- Manual on-screen matrix convention check remains required.

## Hard Boundaries
- Do not delete old shader.
- Do not change registry or render system in this task.
