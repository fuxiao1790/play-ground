# Task Execution Packet

## Task
001-vfx-pending-singleton.md

## Goal
Move `CombatVfxDispatchSystem.PendingSpawns` and `ProducerHandle` into `CombatVfxDispatchSingleton`, then migrate all VFX producers to `TryGetSingletonRW<CombatVfxDispatchSingleton>`.

## Files Allowed To Modify
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`
- `.agent/combat-event-singletons/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`

## Behavior To Preserve
- VFX enqueue/drain timing and order.
- Main-thread handle combine after job schedule.
- Absent sink no-op behavior.

## Behavior To Change
- VFX native queue and producer handle storage moves from system fields to singleton component data.

## Relevant Global Context
- Native container dependencies inside singleton components remain manually threaded.
- Sink system owns singleton create/dispose lifecycle.
- Do not migrate hit or spawn-event reach-ins in collision systems during this task.

## Dependencies Confirmed
- `CombatVfxDispatchSystem` currently owns `PendingSpawns`, `ProducerHandle`, `HasQueue`, and `AsParallelWriter`.
- Producers currently call `GetExistingSystemManaged<CombatVfxDispatchSystem>()`.

## Step-By-Step Instructions
- Add `CombatVfxDispatchSingleton` with `PendingSpawns` and `ProducerHandle`.
- Create singleton in `CombatVfxDispatchSystem.OnCreate`.
- In `OnDestroy`, get singleton, complete `ProducerHandle`, then dispose queue.
- In `OnUpdate`, complete/reset producer handle and drain the singleton queue.
- Migrate seven VFX producer sites to `TryGetSingletonRW`.
- Remove old internal queue/handle/helper members from the sink.

## Acceptance Criteria
- No `GetExistingSystemManaged<CombatVfxDispatchSystem>()` remains.
- `CombatVfxDispatchSystem` exposes no internal VFX queue/handle/writer helper.
- Build succeeds.

## Validation Required
- Grep for `GetExistingSystemManaged<CombatVfxDispatchSystem>`.
- Compile/build check.

## Hard Boundaries
- Do not modify hit or spawn-event wiring in collision systems.
- Do not change update ordering or VFX payload semantics.
- Do not add a bridge/shim source of truth.
