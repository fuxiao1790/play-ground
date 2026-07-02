# Task Execution Packet

## Task
002-projectile-spawn-decouple.md

## Goal
Make projectile spawn pooling independent from render batch id by treating `CombatRenderBatchId` as normal component data.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/002-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/implementation-context.md`
- `.agent/rendering-rework/002-projectile-spawn-decouple.md`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`

## Behavior To Preserve
- Basic and child-spawner projectile reuse machinery remains.
- Timed child-spawner archetype distinction remains.
- Spawn command order handling through existing bucket/order arrays remains.

## Behavior To Change
- Dead slot queries no longer use shared batch-id filters.
- Projectile reuse can claim any dead slot in the matching archetype.
- Cold and reused projectile slots write current `RenderTypeId` into `CombatRenderBatchId`.
- Projectile spawn key becomes a single constant bucket.

## Relevant Global Context
- `CombatRenderBatchId` is a plain per-entity component after task 001.
- No stale batch id is allowed on reused slots.
- Counting-sort stays even though projectile key is degenerate.

## Dependencies Confirmed
- Task 001 completed: `CombatRenderBatchId` now implements `IComponentData`.

## Step-By-Step Instructions
- Add `CombatRenderBatchId` to basic and child projectile archetypes.
- Remove projectile `SetSharedComponentFilter` and `AddSharedComponent` usage for `CombatRenderBatchId`.
- Write batch id in common cold reset.
- Add chunk component handles and writes in both projectile reuse jobs.
- Reduce `ProjectileSpawnKey` to a fieldless constant key while leaving bucket machinery.

## Acceptance Criteria
- No `SetSharedComponentFilter` / `AddSharedComponent` for `CombatRenderBatchId` remains in projectile code.
- Reuse writes `CombatRenderBatchId` from `RenderTypeId`.
- Cold creation sets `CombatRenderBatchId` from `RenderTypeId`.

## Validation Required
- Search projectile file for shared component APIs and batch-id writes.
- Full build deferred until tasks 003 and 004 complete.

## Hard Boundaries
- Do not modify files outside the allowed list except directly required compile fixes.
- Do not remove the projectile bucketing machinery.
- Do not change architecture.
- Do not combine this task with later tasks.
