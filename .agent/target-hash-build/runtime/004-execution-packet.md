# Task Execution Packet

## Task
004-consumer-projectile-collision.md

## Goal
Update `ProjectileCollisionSystem` so it reads target snapshot, projectile collision cells, and max target radius from `TargetSpatialHashSingleton`.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`

## Behavior To Preserve
- Projectile hit qualification, pierce, gates, impact-spawn, AOE-spawn, VFX, and deactivation behavior.
- Projectile collision broadphase remains center-cell target hash with cell size 1.
- Query AABB expansion by max target radius remains inside the collision job.
- Existing output queue producer-handle forwarding remains unchanged.

## Behavior To Change
- Remove local target query, target extraction, max-radius pass, target cell build, and target container disposal.
- Read shared snapshot and `ProjectileCollisionCells` from the singleton.
- Change `ProjectileCollisionJob.MaxTargetRadius` from `float` to `[ReadOnly] NativeReference<float>` and read `.Value` inside the job.
- Combine `hash.BuildHandle` into `state.Dependency` before scheduling collision.
- Combine collision handle into singleton `ConsumerHandle` after scheduling.
- Replace query-side cell math with `CombatSpatialHash`.

## Relevant Global Context
- Producer system and singleton exist from task 002.
- Consumer must read singleton via `SystemAPI.GetSingleton<TargetSpatialHashSingleton>()`.
- Consumer must update `ConsumerHandle` via `SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>()`.
- Do not read `hash.MaxTargetRadius.Value` on the main thread.

## Dependencies Confirmed
- `TargetSpatialHashSingleton` exists with projectile collision cells, target snapshot lists, target count, max target radius reference, build handle, and consumer handle.
- `CombatSpatialHash` exists with projectile collision cell size and cell key helper.

## Step-By-Step Instructions
- Remove `targetQuery` field and target query setup if no longer used.
- Keep existing early-out if `activeProjectileQuery.IsEmpty`.
- Get singleton after active projectile early-out.
- Combine `state.Dependency` with `hash.BuildHandle`.
- Feed `hash.TargetEntities.AsArray()`, `hash.TargetPositions.AsArray()`, `hash.TargetShapes.AsArray()`, `hash.TargetFactions.AsArray()`, `hash.ProjectileCollisionCells`, `hash.TargetCount`, and `hash.MaxTargetRadius` into `ProjectileCollisionJob`.
- Schedule collision and forward output producer handles exactly as before.
- Accumulate collision handle into `ConsumerHandle`.
- Set `state.Dependency` to collision handle; do not dispose shared containers.
- Replace local `FloorCell` and `CellKey` calls with `CombatSpatialHash`.
- Remove local `SpatialHashCellSize`, `FloorCell`, and `CellKey` if unused.

## Acceptance Criteria
- No local target extraction or hash build remains.
- `MaxTargetRadius` is read inside the job from `NativeReference<float>`.
- Existing output queue forwarding remains.

## Validation Required
- Search validation: no `CompleteDependencyBeforeRO`, target `ToEntityArray`, target `ToComponentDataArray`, local `targetCells`, local `maxTargetRadius`, local `FloorCell`, or local `CellKey` remains in projectile collision system.
- Compile/build later in final validation.

## Hard Boundaries
- Do not modify producer, tracking, AOE systems, or tests in this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
