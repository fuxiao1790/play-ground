# Task Execution Packet

## Task
001-data-types.md

## Goal
Compact `CombatRenderComponent` to the 32 B GPU wire record and add `CombatRenderAuthoring` for CPU-only base scale/rotation.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `.agent/combat-render-2d-transform/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `CombatRenderComponents.cs`
- `implementation-context.md`

## Behavior To Preserve
- Render meta bit logic: render id in bits 0..30; align-to-velocity in bit 31.
- `RenderZ`, `AlignToVelocity`, `RenderTypeId`, and `IsRenderable` properties remain on `CombatRenderComponent`.

## Behavior To Change
- Remove full `Matrix4x4 objectToWorld` from the render component.
- Move base visual scale and rotation accessors/setters to `CombatRenderAuthoring`.

## Relevant Global Context
- `CombatRenderComponent` must be 32 B and remain the zero-copy GPU record.
- `CombatRenderAuthoring` must be 16 B and CPU-only.

## Dependencies Confirmed
- No prior implementation tasks required.
- Existing `CombatRenderComponent` is currently the 68 B matrix record in `CombatRenderComponents.cs`.

## Step-By-Step Instructions
- Define `CombatRenderComponent` with `float4 Rotation`, `float3 Position`, and `int RenderMeta`.
- Implement `RenderZ` over `Position.z`.
- Keep render meta accessors unchanged in behavior.
- Add `CombatRenderAuthoring` with `float2 BaseScale`, `float BaseSin`, `float BaseCos`.
- Move `VisualScale`, `VisualRotationSin`, `VisualRotationCos`, `SetVisualTransform`, and `SetVisual2D` to authoring, excluding render/meta fields from the setter.

## Acceptance Criteria
- `CombatRenderComponent` layout is 32 B.
- `CombatRenderAuthoring` layout is 16 B.
- No render component member crosses 16-byte boundary.
- Compile may remain broken until tasks 002-004.

## Validation Required
- Search/static validation only for this task; full compile after coherent task group.

## Hard Boundaries
- Do not modify registry methods in this task.
- Do not modify matrix utility in this task.
- Do not change architecture.
