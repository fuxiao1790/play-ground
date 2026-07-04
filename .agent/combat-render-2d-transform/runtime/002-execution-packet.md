# Task Execution Packet

## Task
002-prepare-transform.md

## Goal
Update render transform preparation so `CombatRenderAuthoring` plus kinematics writes compact `CombatRenderComponent` fields.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `.agent/combat-render-2d-transform/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `implementation-context.md`
- `001-data-types.md`

## Behavior To Preserve
- Velocity alignment math and fallback direction.
- Non-renderable and inactive entities collapse to zero-area quads.
- `RenderZ` and `RenderMeta` survive prepare.

## Behavior To Change
- Base scale/sin/cos comes only from `CombatRenderAuthoring`.
- Prepare writes `Rotation` and `Position.xy` instead of a full matrix.

## Relevant Global Context
- Compact record remains zero-copy GPU record.
- New authoring component is read-only in Burst prepare job.

## Dependencies Confirmed
- Task 001 completed: `CombatRenderComponent` has `Rotation`, `Position`, `RenderMeta`; `CombatRenderAuthoring` exists.

## Step-By-Step Instructions
- Rework `CombatRenderMatrixUtility` to produce compact 2D basis/position.
- Replace `DegenerateMatrix` with degenerate compact instance behavior.
- Add `CombatRenderAuthoring` to prepare query and type handles.
- Update size assert to 32.

## Acceptance Criteria
- Active transform math matches old world transform for local z=0 quads.
- Inactive entities zero `Rotation`.
- Prepare no longer reads base data from render fields.

## Validation Required
- Search/static validation now; compile after task 004.

## Hard Boundaries
- Do not alter spawn plumbing in this task.
- Do not alter GPU submit in this task.
