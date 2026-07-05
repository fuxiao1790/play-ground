# Task Execution Packet

## Task
002-hit-queue-singleton.md

## Goal
Move `CombatApplyFinalizeSingleSystem.HitQueue` and `ProducerHandle` into `CombatHitDispatchSingleton`, then migrate the three collision producers.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `.agent/combat-event-singletons/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`

## Behavior To Preserve
- Damage/status finalize timing.
- `AccrualFrame` and `LastHitEventCount` stay system-local.
- Managed `CombatApplyBridge` lookup remains allowed.

## Behavior To Change
- Hit queue native container and producer handle storage moves into singleton component data.

## Relevant Global Context
- Manual producer handle threading remains.
- Producers use absent-system tolerant singleton lookup.

## Dependencies Confirmed
- `001` introduced and migrated the VFX singleton pattern.
- `CombatApplyFinalizeSingleSystem` still owns `HitQueue`, `ProducerHandle`, and `AsParallelWriter`.

## Step-By-Step Instructions
- Add `CombatHitDispatchSingleton`.
- Create singleton with persistent hit queue in `OnCreate`.
- Complete/dispose through singleton in `OnDestroy`.
- In `OnUpdate`, complete/reset producer handle and drain singleton queue.
- Migrate `HitWriter` setup and post-schedule handle combine in the three collision systems.
- Remove old internal queue/handle/writer members from the sink.

## Acceptance Criteria
- No `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>()` remains in collision producers.
- Sink exposes no internal hit queue/handle/writer helper.
- Build or compile validation attempted and result logged.

## Validation Required
- Grep for `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>`.
- Grep for internal lane members on `CombatApplyFinalizeSingleSystem`.
- Compile/build check if available.

## Hard Boundaries
- Do not migrate projectile/impact/lingering spawn-event lanes in this task.
- Do not move `AccrualFrame` or `LastHitEventCount`.
