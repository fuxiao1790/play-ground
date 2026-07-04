# Task Execution Packet

## Task
003-batched-render-direct-buffer.md

## Goal
Replace render scatter plus `NativeList`/`SetData` with direct GPU buffer lock and sequential writes from `CombatRenderComponent`.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

## Behavior To Preserve
- Buffer capacity grows by doubling.
- Shared material buffer binding and indirect submit behavior.
- Active projectile/AOE debug counters.

## Behavior To Change
- Single render upload query for projectile and AOE entities.
- All matching entities are uploaded, including disabled render tags.
- No `CombatInstanceData`, no `NativeList`, no active-mask scatter filter.

## Relevant Global Context
- Disabled entities already have degenerate matrix from prep.
- `CombatRenderComponent` is exactly the GPU buffer layout.
- Batched render must call `CompleteDependency()` before locking GPU buffer.

## Dependencies Confirmed
- Task 001 provides GPU-shaped component.
- Task 002 writes all entity matrices into that component.

## Step-By-Step Instructions
- Remove separate projectile/AOE render queries for upload.
- Add unified query with `WithAny<ProjectileTag, AoeTag>()` and `IgnoreComponentEnabledState`.
- Lock `GraphicsBuffer` for `CombatRenderComponent`, write chunk components sequentially, unlock.
- Preserve separate telemetry counts with lightweight active queries.

## Acceptance Criteria
- No scatter loop or `NativeList<CombatInstanceData>`.
- Direct lock/unlock upload.
- Single upload query writes projectile and AOE components in one pass.
- Instance capacity and submit behavior preserved.

## Validation Required
- Search for removed upload constructs.
- Build after test compile fixes.

## Hard Boundaries
- Do not modify shader.
- Do not recompute matrices in batched render.
