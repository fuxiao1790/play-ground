# Task Execution Packet

## Task
004-gpu-submit.md

## Goal
Update indirect shader and batched render stride to consume compact 2D instance records.

## Files Allowed To Modify
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `.agent/combat-render-2d-transform/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `CombatRenderComponents.cs`
- `implementation-context.md`

## Behavior To Preserve
- `_InstanceData` and `_UvBasis` stay bound with `Material.SetBuffer`.
- `LightMode=Universal2D` and `#pragma target 4.5` stay unchanged.
- UV basis math remains keyed by mesh `IN.uv`.
- `WriteDirectJob` remains zero-copy `AddRange`.

## Behavior To Change
- Shader instance struct becomes `float4 rotation`, `float3 position`, `uint renderMeta`.
- Vertex transform reconstructs world x/y/z from compact basis.
- C# instance stride changes from 68 to 32.

## Relevant Global Context
- `CombatRenderComponent` is now the 32 B upload record.
- No repack layer.

## Dependencies Confirmed
- Task 001 defines compact render layout.
- Task 002 prepares compact fields.
- Task 003 seeds render metadata and authoring for prepare.

## Step-By-Step Instructions
- Replace shader struct and transform code.
- Change `InstanceDataStride` to 32.
- Keep `_instanceData` as `NativeList<CombatRenderComponent>`.
- Keep `WriteDirectJob.AddRange`.

## Acceptance Criteria
- Buffer stride matches C# struct and HLSL struct at 32 B.
- Sprites draw with same transform/UV behavior.

## Validation Required
- Run compile/build after this task if available.

## Hard Boundaries
- Do not change render submission binding method.
- Do not introduce per-instance repack.
