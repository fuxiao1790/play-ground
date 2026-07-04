# Task Execution Packet

## Task
002-target-spatial-hash-system.md

## Goal
Add `TargetSpatialHashSystem` and `TargetSpatialHashSingleton`: gather target proxy data once, build projectile collision, tracking, and AOE target hashes in three single-threaded Burst jobs, and publish through a singleton component.

## Files Allowed To Modify
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`
- `Assets/Scripts/System/Common/CombatTargetProxy.cs`
- `Assets/Scripts/System/Common/CombatCollisionMath.cs`
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`

## Behavior To Preserve
- One target snapshot is in target query order.
- Build jobs insert in snapshot array order `0..TargetCount-1`.
- Projectile collision hash: center cell, cell size 1, one entry per target, max target radius reduction over all shapes.
- Tracking hash: center cell, cell size 64, one entry per target, `TrackingIndicesById.TryAdd(TargetIdKey(TargetKey(entity)), i)`.
- AOE hash: bounds-spread, cell size 64, inclusive min/max cells.

## Behavior To Change
- Add producer that owns persistent native containers and publishes them on a singleton.
- Producer runs before tracking, projectile collision, lingering AOE collision, and impact AOE collision.

## Relevant Global Context
- `CombatSpatialHash` exists from task 001.
- Target proxy components are frame-constant during simulation.
- Build jobs must be single-threaded `IJob`, not parallel writers.
- Consumers will later read singleton maps and accumulate their read job into `ConsumerHandle`.
- Use fallback main-thread gather if simpler; this is allowed by the task. The build work still must be Burst jobs.

## Dependencies Confirmed
- `Assets/Scripts/System/Common/CombatSpatialHash.cs` exists and defines required constants/helpers.
- Target proxy components exist in `CombatTargetProxy.cs`.
- Current consumers still build their own hashes; later tasks will remove that.

## Step-By-Step Instructions
- Create `TargetSpatialHashSingleton : IComponentData` in the new file.
- Include persistent maps: projectile collision cells, tracking cells, tracking indices by id, AOE occupied cells.
- Include persistent `NativeReference<float> MaxTargetRadius`.
- Include persistent snapshot lists or arrays for entities, positions, shapes, and factions. If using `NativeList<T>`, consumers can later pass `.AsArray()` to jobs.
- Include `int TargetCount`, `JobHandle BuildHandle`, and `JobHandle ConsumerHandle`.
- In `OnCreate`, allocate persistent containers with capacity at least 1, create a singleton entity, and set the component.
- Create target query for `TargetProxyTag`, `TargetPosition`, `TargetCollisionShape`, `TargetFaction`.
- In `OnUpdate`, complete prior `ConsumerHandle`, reset it, gather target data once, clear maps/id map, ensure capacity, schedule three single-threaded Burst `IJob`s, combine handles into `BuildHandle`, set `state.Dependency`, and write singleton back.
- If target count is zero, clear maps, set `TargetCount = 0`, set `MaxTargetRadius.Value = 0f`, set `BuildHandle = state.Dependency`, write back, and return.
- In `OnDestroy`, complete build/consumer handles and dispose every persistent native container if created.
- Add profiler markers `TargetSpatialHashSystem.Gather` and `TargetSpatialHashSystem.Build`.

## Acceptance Criteria
- Singleton exists after first update; maps are valid even with zero targets.
- No managed access in jobs.
- Build jobs use `CombatSpatialHash` and insert in array order.
- Producer does not reach into consumer system fields.

## Validation Required
- Search validation for new singleton/system/jobs and `CombatSpatialHash` usage.
- Compile/build later in final validation.

## Hard Boundaries
- Do not modify consumer systems in this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
