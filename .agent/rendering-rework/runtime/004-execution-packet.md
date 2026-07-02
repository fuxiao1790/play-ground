# Task Execution Packet

## Task
004-render-submit-scatter.md

## Goal
Rewrite batched render submission to read plain `CombatRenderBatchId` and scatter matrices into reused per-batch buffers.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/004-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/implementation-context.md`
- `.agent/rendering-rework/004-render-submit-scatter.md`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- `CombatRenderPrepareSystem` continues to prepare `CombatRenderElement`.
- Projectile and AOE active counts remain split by domain.
- Disabled `CombatRenderActiveTag` entities do not render.
- Existing draw submission resources, layers, bounds, and z-order data are preserved.

## Behavior To Change
- Render no longer uses shared component filters or `ToComponentDataArray`.
- Active entities are read once by query and scattered by per-entity batch id.
- Per-batch scratch buffers are reused across frames and disposed on destroy.

## Relevant Global Context
- `CombatRenderBatchId` is a plain `IComponentData`.
- Registry entries identify valid GPU resource batches.
- Main-thread scatter is the planned correctness-first implementation.

## Dependencies Confirmed
- Task 001 completed: `CombatRenderBatchId` is `IComponentData`.
- Tasks 002 and 003 write batch id during projectile/AOE cold and reuse spawn.

## Step-By-Step Instructions
- Add reused per-batch `NativeList<Matrix4x4>` storage keyed by batch id.
- Add read-only component handles for `CombatRenderElement`, `CombatRenderBatchId`, and active render tag.
- Clear scratch buffers each frame, ensure buffers for registry ids, scatter projectile and AOE queries, then submit non-empty buffers.
- Remove `SetSharedComponentFilter`, `ResetFilter`, and `ToComponentDataArray` usage.

## Acceptance Criteria
- Correct sprites/layers/z ordering continue through registry resources and prepared matrices.
- Disabled render-active entities are skipped.
- Counts still reflect active projectiles/AOEs submitted for valid batches.
- No shared-component API remains in render system.

## Validation Required
- Search render system for removed APIs.
- Full build after task completes.

## Hard Boundaries
- Do not modify `CombatRenderPrepareSystem`.
- Do not introduce parallel compaction.
- Do not change architecture.
- Do not combine with later tasks.
