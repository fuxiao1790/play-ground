# Task Execution Packet

## Task
002-single-thread-reuse.md

## Goal
Replace worker-range reuse jobs with one Burst `IJob` per apply pool. The job walks disabled chunks in query order, consumes command list entries in order, and reports the reused prefix length.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- Task context/log files under `.agent/spawn-apply-parallel-reuse/`

## Files Allowed To Create
- None beyond task orchestration files.

## Files Allowed To Delete
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`

## Behavior To Preserve
- Disabled slots are reused before cold creation.
- Cold creation still uses the existing archetypes and ECB fallback.
- Reset paths stamp faction, identity, collision, render, hit payload, lifetime, contact gates, and timed-spawn state.

## Behavior To Change
- Remove worker-range lanes, command-index streams, and remainder queues.
- Cold creation handles only commands after the reused prefix.

## Relevant Global Context
- Apply owns materialization and reuse.
- Domain queries must include `ProjectileTag` or `AoeTag`.
- Hot despawn/reuse uses enableable `Active`.

## Dependencies Confirmed
- Task 001 evidence: expansion systems expose `NativeList` command containers for projectile, impact AOE, and lingering AOE.

## Step-By-Step Instructions
- Read command lists directly from expansion systems.
- Capture disabled-slot chunks through each apply pool query.
- Schedule one Burst `IJob` per apply pool.
- Report reused prefix length and cold-create suffix.
- Remove obsolete parallel helper and lane/remainder artifacts.

## Acceptance Criteria
- Projectile apply reuses disabled projectile slots before cold creation.
- Impact AOE apply reuses disabled impact slots before cold creation.
- Lingering AOE apply reuses disabled lingering slots before cold creation.
- Cold creation handles only commands after the reused prefix.
- Parallel helper, command-index streams, and remainder queues are removed.

## Validation Required
- Build `PlayGround.Runtime.csproj`.
- Search for obsolete helper and lane/remainder artifacts.

## Hard Boundaries
- Do not modify files outside the allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
