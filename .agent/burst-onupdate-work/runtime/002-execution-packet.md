# Task Execution Packet

## Task
002-spawn-event-gather-job.md

## Goal
Replace four managed queue/buffer gathers with one registered generic Burst gather job and deferred `NativeList` inputs.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`
- `.agent/burst-onupdate-work/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Spawning/GatherSpawnEventsJob.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- The three allowed expansion files and their spawn pipeline event definitions.

## Behavior To Preserve
- Both managed scope buffers and parallel lane queues merge once per frame and scope buffers clear once.
- Existing lane command disposal, pending/producer handle handling, VFX producer chaining, system ordering, and template-missing behavior remain.

## Behavior To Change
- Always schedule gather and expansion, including empty event frames.
- Gather uses queue `ToArray` and buffer native arrays into a job-owned `NativeList`; expansion reads `AsDeferredJobArray`.

## Relevant Global Context
- Queue order is not meaningful. Native container ownership/disposal is mandatory. Generic Burst jobs require concrete registrations. No new lane handles.

## Dependencies Confirmed
- None. All four target expansion systems and their scope queries exist.

## Step-By-Step Instructions
1. Add shared `[BurstCompile] GatherSpawnEventsJob<T> : IJob` plus four assembly registrations.
2. After existing producer completion, create `NativeList<T>` and `ToArchetypeChunkArray`.
3. Schedule gather on `Dependency`, pass `events.AsDeferredJobArray()` to existing expansion job, and chain required VFX producer handle.
4. Dispose events and scope chunks through final dependency on normal and missing-template paths.
5. Remove all pre-count, managed queue drain, managed buffer copy/clear, and zero-total early-out code.

## Acceptance Criteria
- One Burst generic gather shared by all four systems with four registrations.
- No managed gather loops/pre-counts remain.
- Lane command and handle behavior remains otherwise unchanged.
- Events and scope chunk arrays dispose on all paths; scope buffers clear once in gather.

## Validation Required
- Static source checks. Burst Inspector and Unity PlayMode/EditMode tests must be user-run with XML results.

## Hard Boundaries
- Do not collapse queue/buffer paths or add handles.
- Do not alter system ordering or downstream command split behavior.
