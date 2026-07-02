# Task Execution Packet

## Task
001-parallel-apply-mechanism.md

## Goal
Add shared mechanics for parallel dead-slot spawn apply: worker chunk ranges, worker-lane command index stream, and overflow remainder collection.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- `.agent/spawn-apply-parallel-reuse/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`

## Behavior To Preserve
- Per-domain apply remains responsible for command payload interpretation and entity reset.
- Existing event and command handoff shape stays flat per domain.

## Behavior To Change
- Add reusable mechanics for worker-owned chunk ranges and command-index lanes.

## Relevant Global Context
- Apply captures disabled-slot chunks before structural changes.
- Lane count equals worker count.
- Command payloads are not copied into lanes; lanes carry command indices only.

## Dependencies Confirmed
- Projectile, impact AOE, and lingering AOE apply systems each currently own one archetype and one disabled-slot query.

## Step-By-Step Instructions
- Add range struct with chunk start and count.
- Add helper to compute worker count from matched chunks.
- Add helper to build contiguous worker chunk ranges.
- Add helper to build a `NativeStream` of command indices, one lane per worker.

## Acceptance Criteria
- Reusable documented helper exists for partition table and command-index lane build.
- Helper avoids copying command payloads.

## Validation Required
- Compile in later task validation.

## Hard Boundaries
- Do not alter domain apply behavior in this task.
- Do not change architecture.
- Do not combine later migration work into this packet.
