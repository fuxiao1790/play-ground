# Task Execution Packet

## Task
003-projectile-spawn-event-singleton.md

## Goal
Move projectile spawn lane state from `ProjectileSpawnExpansionSystem` to `ProjectileSpawnEventSingleton`, including inbound event queue and outbound command handoff.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Status/StatusProcessSystem.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`
- `.agent/combat-event-singletons/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`

## Behavior To Preserve
- Event queue drain timing.
- Scope dynamic-buffer drain.
- Per-frame `ProjectileCommands` TempJob allocation/disposal.
- Deterministic fan-out IDs and ordering.

## Behavior To Change
- Projectile lane queue, command list, and producer/pending handles move to `ProjectileSpawnEventSingleton`.

## Relevant Global Context
- Producers combine into singleton `ProducerHandle`.
- Apply system reads singleton `PendingHandle` and `Commands`.
- No duplicate system fields remain.

## Dependencies Confirmed
- `001` and `002` singleton conventions are implemented.
- Projectile expansion currently has internal `EventQueue`, `ProjectileCommands`, `ProducerHandle`, and `PendingHandle`.

## Step-By-Step Instructions
- Add `ProjectileSpawnEventSingleton`.
- Create persistent `EventQueue` singleton in expansion `OnCreate`; command list remains per-frame.
- Complete/dispose `PendingHandle`/`Commands` and `EventQueue` through singleton in `OnDestroy`.
- In `OnUpdate`, complete/dispose previous command list, complete/reset producer handle, drain singleton queue plus scope buffers, assign new `Commands`/`PendingHandle` back to singleton.
- Migrate five projectile event producers.
- Migrate `ProjectileSpawnApplySystem` to read singleton command handoff.

## Acceptance Criteria
- No `GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` remains.
- Expansion system exposes no internal projectile lane fields.
- Build or compile validation attempted and result logged.

## Validation Required
- Grep migrated reach-ins and internal fields.
- Compile/build check if available.

## Hard Boundaries
- Do not migrate impact or lingering lane storage in this task.
- Do not change dynamic-buffer input path.
