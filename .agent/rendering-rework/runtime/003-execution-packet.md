# Task Execution Packet

## Task
003-aoe-spawn-decouple.md

## Goal
Make AOE spawn pooling independent from render batch id while preserving impact, lingering, and timed-lingering reuse separation.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/003-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/implementation-context.md`
- `.agent/rendering-rework/003-aoe-spawn-decouple.md`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`

## Behavior To Preserve
- Impact, lingering, and timed-lingering AOE reuse variants remain distinct.
- Existing `_byKey` bucketing remains meaningful.
- Existing AOE reset semantics remain.

## Behavior To Change
- AOE dead-slot queries no longer filter by render batch id.
- Cold and reused AOE slots write current `RenderTypeId` into `CombatRenderBatchId`.
- `AoeSpawnKey` drops batch id and keeps only lingering/timed-spawner dimensions.

## Relevant Global Context
- `CombatRenderBatchId` is a plain per-entity component after task 001.
- Reused slots must not keep stale batch ids.
- AOE archetype distinctions other than batch id must be preserved.

## Dependencies Confirmed
- Task 001 completed: `CombatRenderBatchId` is `IComponentData`.

## Step-By-Step Instructions
- Add `CombatRenderBatchId` to all three AOE archetypes.
- Remove AOE `SetSharedComponentFilter` and `AddSharedComponent` usage for `CombatRenderBatchId`.
- Write batch id in cold `RecordAoeReset`.
- Add chunk component handle and writes in AOE reuse job.
- Update `AoeSpawnKey` constructor, equality, hash, and call site to drop batch id.

## Acceptance Criteria
- No `SetSharedComponentFilter` / `AddSharedComponent` for `CombatRenderBatchId` remains in AOE code.
- Reuse writes `CombatRenderBatchId` from `RenderTypeId`.
- Cold creation sets `CombatRenderBatchId` from `RenderTypeId`.

## Validation Required
- Search AOE file for shared component APIs and batch-id writes.
- Full build deferred until task 004 completes.

## Hard Boundaries
- Do not modify files outside the allowed list except directly required compile fixes.
- Do not collapse AOE lifetime/timed-spawner buckets.
- Do not change architecture.
- Do not combine this task with later tasks.
