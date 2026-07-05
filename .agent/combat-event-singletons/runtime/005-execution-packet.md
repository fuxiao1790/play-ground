# Task Execution Packet

## Task
005-lingering-aoe-spawn-event-singleton.md

## Goal
Move lingering AOE spawn lane state from `LingeringAoeSpawnExpansionSystem` to `LingeringAoeSpawnEventSingleton`, including inbound event queue and outbound command handoff.

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
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`

## Behavior To Preserve
- Lingering AOE event drain timing.
- Scope dynamic-buffer drain.
- Per-frame lingering command TempJob allocation/disposal.
- Timed sub-spawner behavior.

## Behavior To Change
- Lingering lane queue, command list, and producer/pending handles move to `LingeringAoeSpawnEventSingleton`.

## Relevant Global Context
- Manual handle threading stays unchanged.
- This completes all five planned lanes.

## Dependencies Confirmed
- Projectile and impact A+B lanes are complete.
- Lingering expansion still owns internal queue/list/handles.

## Step-By-Step Instructions
- Add `LingeringAoeSpawnEventSingleton`.
- Create persistent lingering queue singleton in `LingeringAoeSpawnExpansionSystem.OnCreate`.
- Complete/dispose previous command list and queue through singleton.
- Drain singleton event queue plus scope buffers.
- Store new `Commands` and `PendingHandle` back to singleton.
- Migrate five lingering event producers.
- Migrate `LingeringAoeSpawnApplySystem` to read singleton command handoff.

## Acceptance Criteria
- No `GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>()` remains.
- Lingering expansion exposes no internal lane fields.
- Build or compile validation attempted and result logged.

## Validation Required
- Grep migrated reach-ins and internal fields.
- Compile/build check if available.

## Hard Boundaries
- Do not alter spawn timing or lifetime/pulse behavior.
- Do not change scope buffer input path.
