# Task Execution Packet

## Task
001-single-thread-expansion.md

## Goal
Replace projectile and AOE expansion `NativeStream` output with owned `NativeList<TCommand>` containers filled by one Burst `IJob`.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- Task context/log files under `.agent/spawn-apply-parallel-reuse/`

## Files Allowed To Create
- None beyond task orchestration files.

## Files Allowed To Delete
- None for this task.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`
- `Assets/Scripts/System/Common/SpawnTemplateComponents.cs`

## Behavior To Preserve
- Expansion drains native producer queues and scope buffers after producers complete.
- Spawn math, deterministic ids, jitter, bounds, and VFX queue emission remain in expansion.

## Behavior To Change
- Expansion output is ordered `NativeList<ProjectileSpawnCommand>`, `NativeList<AoeSpawnCommand>` for impact, and `NativeList<AoeSpawnCommand>` for lingering.

## Relevant Global Context
- Events are gameplay intent; commands are allocation intent.
- Expansion owns template dereference and spawn math.
- Command order is the source of apply order.

## Dependencies Confirmed
- No prerequisite task.

## Step-By-Step Instructions
- Replace stream output with command lists.
- Keep one Burst `IJob` per expansion domain.
- Dispose previous-frame lists before replacing them.

## Acceptance Criteria
- Projectile expansion writes ordered `ProjectileSpawnCommand` values into one list.
- AOE expansion writes ordered impact and lingering `AoeSpawnCommand` values into separate lists.
- Expansion still drains producer queues and scope buffers after producers complete.
- Spawn math remains in expansion systems.

## Validation Required
- Build `PlayGround.Runtime.csproj`.
- Search for obsolete runtime `NativeStream` spawn expansion output.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
