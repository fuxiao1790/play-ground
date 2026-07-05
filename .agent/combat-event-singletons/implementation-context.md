# Implementation Context

## Architectural Decisions
- Move combat-lane native queues/lists and their manual job handles out of sink system fields and into per-lane singleton `IComponentData`.
- Keep one source of truth per lane: the singleton owns the native container values and handles.
- Preserve current behavior, update order, drain timing, deterministic ordering, and manual dependency threading.

## Global Invariants
- No producer reaches into another system's internal native containers.
- Producers tolerate absent sinks by using `SystemAPI.TryGetSingletonRW`.
- Handles remain manually threaded because ECS does not track dependencies inside native containers stored in singleton components.
- Mutate singleton handles only on the main thread after scheduling producer jobs.

## Ownership Boundaries
- Each sink system creates its lane singleton in `OnCreate`.
- Each sink system completes pending handles and disposes its native containers in `OnDestroy`.
- Existing scope `DynamicBuffer<*SpawnEvent>` paths stay owned by the combat scope and are not part of this migration.

## Data Flow
- VFX: producers enqueue `VfxPendingSpawn`; `CombatVfxDispatchSystem` drains in presentation.
- Hits: collision producers enqueue `CombatHitEvent`; `CombatApplyFinalizeSingleSystem` drains in simulation.
- Spawn lanes: producers enqueue `*SpawnEvent`; expansion drains queues and buffers, writes `Commands`; apply systems consume command lists.

## Lifecycle / Allocation Rules
- Persistent inbound queues are created once and disposed by their sink.
- Spawn command lists keep existing per-frame `Allocator.TempJob` create/dispose lifecycle.
- No bridge/shim container, duplicate ownership, or new per-frame allocation layer.

## ECS / Job / Threading Constraints
- Schedule jobs against extracted native containers, not component refs inside jobs.
- Combine producer handles into the singleton `ProducerHandle`.
- Expansion-to-apply handoff uses singleton `PendingHandle`.

## Determinism Requirements
- Do not reorder producers, drains, or scheduling.
- Do not alter update attributes or fan-out ID/jitter logic.

## Producer / Consumer Separation
- Damage, spawn events, spawn commands, tick results, and VFX requests remain distinct typed paths.
- `CombatApplyBridge` managed lookup is outside this native-container migration.

## Reused Mechanisms
- Match `TargetSpatialHashSingleton` for native containers plus job handles in singleton component data.
- Match `CombatStatsSingleton` for cross-system singleton access.

## Introduced Mechanisms
- `CombatVfxDispatchSingleton`
- `CombatHitDispatchSingleton`
- `ProjectileSpawnEventSingleton`
- `ImpactAoeSpawnEventSingleton`
- `LingeringAoeSpawnEventSingleton`

## Validation Requirements
- Build/compile after task changes.
- Grep migrated `GetExistingSystemManaged<...>` reach-ins.
- Confirm sink systems expose no internal native lane fields/writer helpers after each lane.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Status/StatusProcessSystem.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`
