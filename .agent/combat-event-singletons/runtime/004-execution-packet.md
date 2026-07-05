# Task Execution Packet

## Task
004-impact-aoe-spawn-event-singleton.md

## Goal
Move impact AOE spawn lane state from `ImpactAoeSpawnExpansionSystem` to `ImpactAoeSpawnEventSingleton`, including inbound event queue and outbound command handoff.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
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
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`

## Behavior To Preserve
- Impact AOE event drain timing.
- Scope dynamic-buffer drain.
- Per-frame impact command TempJob allocation/disposal.
- Existing VFX singleton wiring from task 001.

## Behavior To Change
- Impact lane queue, command list, and producer/pending handles move to `ImpactAoeSpawnEventSingleton`.

## Relevant Global Context
- Manual handle threading and main-thread handle mutation stay unchanged.
- Do not migrate lingering lane in this task.

## Dependencies Confirmed
- Projectile lane task is complete and provides template for A+B handoff.
- Impact expansion currently still owns internal queue/list/handles.

## Step-By-Step Instructions
- Add `ImpactAoeSpawnEventSingleton`.
- Create persistent impact queue singleton in `ImpactAoeSpawnExpansionSystem.OnCreate`.
- Complete/dispose previous command list and queue through singleton.
- Drain singleton event queue plus scope buffers.
- Store new `Commands` and `PendingHandle` back to singleton.
- Migrate five impact event producers.
- Migrate `ImpactAoeSpawnApplySystem` to read singleton command handoff.

## Acceptance Criteria
- No `GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>()` remains.
- Impact expansion exposes no internal lane fields.
- Build or compile validation attempted and result logged.

## Validation Required
- Grep migrated reach-ins and internal fields.
- Compile/build check if available.

## Hard Boundaries
- Do not migrate lingering lane storage.
- Do not change scope buffer input path.
