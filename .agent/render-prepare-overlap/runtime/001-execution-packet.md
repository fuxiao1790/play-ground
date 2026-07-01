# Task Execution Packet

## Task
001-split-render-prepare-system.md

## Goal
Move render matrix preparation from `CombatBatchedRenderSystem` into a new `CombatRenderPrepareSystem` that schedules `RenderPrepareJob` at `PresentationSystemGroup` `OrderFirst` and leaves it in flight until `CombatBatchedRenderSystem` consumes `CombatRenderElement`.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`

## Behavior To Preserve
- Same render batch registry loop.
- Same projectile/AOE render query filters.
- Same submit buffer and `SubmitAll` behavior.
- Same active projectile/AOE count updates.
- Same matrix math via `CombatRenderMatrixUtility.ElementFor`.

## Behavior To Change
- Schedule `RenderPrepareJob` earlier in presentation.
- Do not complete the prepare job in the prepare system.
- Join dependency at start of `CombatBatchedRenderSystem.OnUpdate`.

## Relevant Global Context
- Kinematics are final before presentation.
- `CombatApplyBridge` and `CombatVfxDispatchSystem` do not own render component data.
- Use ECS component dependency chaining, not manual job handles.
- No new data type or second render-matrix path.

## Dependencies Confirmed
- No prior task dependencies.
- Current `RenderPrepareJob` exists only in `CombatBatchedRenderSystem.cs`.
- Active `CombatApplyBridge` is before `CombatBatchedRenderSystem`.

## Step-By-Step Instructions
- Create `CombatRenderPrepareSystem` under `PlayGround.System.Common`.
- Add `[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]`.
- Move prepare query, type handles, and `RenderPrepareJob` into the new system.
- In prepare `OnUpdate`, update handles and schedule parallel job, assigning to `Dependency`.
- Remove prepare query, handles, schedule block, and nested job from `CombatBatchedRenderSystem`.
- Start `CombatBatchedRenderSystem.OnUpdate` with `CompleteDependency()`.

## Acceptance Criteria
- Project compiles.
- Only one `RenderPrepareJob` definition exists.
- Projectiles and AOEs render from prepared matrices.
- No new job safety errors expected.

## Validation Required
- Search for single `RenderPrepareJob`.
- Build the Unity project / C# solution.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
