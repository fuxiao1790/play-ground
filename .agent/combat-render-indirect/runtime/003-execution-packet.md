# Task Execution Packet

## Task
003-render-system-rewrite.md

## Goal
Rewrite `CombatBatchedRenderSystem` to upload one combined instance buffer and issue one `Graphics.RenderMeshIndirect`.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- `CompleteDependency()` before main-thread render reads.
- `LastActiveProjectileCount` and `LastActiveAoeCount`.
- Active-only scan using `CombatRenderActiveTag` and `IsRenderable`.
- Shared world bounds/layer/shadow settings.

## Behavior To Change
- Replace transform + UV parallel lists, `SetVectorArray`, `MaxInstancesPerDraw`, and `RenderMeshInstanced` loop with one `NativeList<CombatInstanceData>` and one indirect draw.

## Relevant Global Context
- Projectile and AOE queries append into the same list for one draw.
- Zero-active frames skip upload and draw.

## Dependencies Confirmed
- `CombatInstanceData` exists.
- `Combat/AtlasIndirectSprite` shader file exists.

## Step-By-Step Instructions
- Scatter active chunks into `_instances`.
- Ensure buffer capacity, upload instance data, populate args, submit.
- Bind `_InstanceData` on `RenderParams.matProps` when buffer identity changes.
- Guard unsupported indirect argument buffers.

## Acceptance Criteria
- One `Graphics.RenderMeshIndirect` call per update when active count > 0.
- No render-system `SetVectorArray`, `MaxInstancesPerDraw`, or `RenderMeshInstanced` remains.

## Validation Required
- Runtime project compile.
- Search verification.
- Manual Frame Debugger check remains required.

## Hard Boundaries
- Do not change simulation systems or render prepare.
