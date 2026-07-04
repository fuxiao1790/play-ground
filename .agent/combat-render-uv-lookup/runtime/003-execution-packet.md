# Task Execution Packet

## Task
003-shader-and-bind.md

## Goal
Make shader fetch UV basis from `_UvBasis[renderId]`, and make render system upload/bind 68-byte instance records.

## Files Allowed To Modify
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `.agent/combat-render-uv-lookup/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`

## Behavior To Preserve
- One indirect draw per update.
- Existing mesh/material/args publishing.
- Matrix multiplication and URP 2D pass settings.
- Zero-active/no-mesh frames publish nothing.

## Behavior To Change
- Instance stride becomes 68.
- Shader instance struct becomes `float4x4 objectToWorld; uint renderMeta;`.
- Shader binds and indexes `_UvBasis`.
- Render system binds `_UvBasis` with `Material.SetBuffer`.

## Relevant Global Context
- Task 001 provides `registry.EnsureUvBasisBuffer()`.
- Task 002 provides packed `RenderMeta` at the end of `CombatRenderComponent`.
- Align flag is CPU-only and must be masked off in HLSL.

## Dependencies Confirmed
- 001 complete: `EnsureUvBasisBuffer()` exists.
- 002 complete: `CombatRenderComponent` has `RenderMeta` and no component UV fields.
- Code evidence: `CombatBatchedRenderSystem.OnCreate` asserts `InstanceDataStride` against component size.

## Step-By-Step Instructions
- Change `InstanceDataStride` to 68.
- Add `_UvBasis` shader property id.
- Call `EnsureUvBasisBuffer()` after null/empty guards and pass result into `Submit`.
- Bind `_UvBasis` with `SharedMaterial.SetBuffer`.
- Update shader structs and UV computation.

## Acceptance Criteria
- `_UvBasis` uses `Material.SetBuffer`.
- Draw count and zero-active behavior unchanged.
- Shader samples affine UV basis from per-kind table.

## Validation Required
- Search for old shader instance UV fields.
- Full compile after task 004.

## Hard Boundaries
- Do not add MPB binding.
- Do not pad to 72 unless concrete platform/build failure proves 68 invalid.
- Do not change draw submission architecture.
