# Task Execution Packet

## Task
003-dispatch-system-per-shape.md

## Goal
Drain Basic and Timed VFX request queues from one presentation system, completing the shared producer handle once and dispatching through the single root.

## Files Allowed To Modify
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`

## Behavior To Preserve
- Complete `ProducerHandle` once before reading queues.
- Clear pending queues if no root exists.
- Basic dispatch remains old behavior for Basic-only frames.
- Stats count dispatched VFX requests.

## Behavior To Change
- OnUpdate considers both Basic and Timed queues.
- Basic bucketing keys on `DecodeLocalIndex(VfxId)`.
- Timed bucketing scatters positions, area sizes, durations, and tick intervals.
- Dispatch uses per-shape root drain entry points.

## Relevant Global Context
- One queue per shape; one shared producer handle.
- Bucketing arrays are per-shape and grow-only.
- Shape is encoded in id; local index is dense within shape.

## Dependencies Confirmed
- Task 001 singleton has `PendingBasicSpawns` and `PendingTimedSpawns`.
- Task 002 root has per-shape registered counts and drain methods.

## Step-By-Step Instructions
- Replace single-queue early return with both-queue check.
- Clear all shape queues when root is missing.
- Rename basic bucket job and key by decoded local index.
- Add timed bucket job.
- Sum dispatched counts into `LastVfxEventCount` and stats.

## Acceptance Criteria
- Compiles.
- Producer handle completed exactly once.
- Basic and Timed requests dispatch via their shape paths.
- Stats include all shapes.

## Validation Required
- Run available compile validation or document external blockage.
- Search OnUpdate for one producer completion and both queue paths.

## Hard Boundaries
- Do not update emit sites in this task.
- Do not change authoring in this task.
