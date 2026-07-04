# Task Execution Packet

## Task
003-consumer-projectile-tracking.md

## Goal
Update `ProjectileTrackingSystem` so it reads target snapshot and tracking maps from `TargetSpatialHashSingleton` instead of gathering targets and building a local tracking hash.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`

## Behavior To Preserve
- Existing target refresh, reacquisition, reservoir selection, cooldown, and steering behavior.
- `TargetKey` and `TargetIdKey` are still needed by acquisition job for target identity checks.
- Tracking query cell size remains 64 and uses same FNV cell key.
- Steering job does not read shared target snapshot and must not be added to `ConsumerHandle`.

## Behavior To Change
- Remove local target query, main-thread `CompleteDependencyBeforeRO`, local arrays/maps, local hash build marker, and disposal.
- Acquisition job reads `hash.TargetEntities.AsArray()`, `hash.TargetPositions.AsArray()`, `hash.TargetFactions.AsArray()`, `hash.TrackingIndicesById`, and `hash.TrackingCells`.
- Combine `hash.BuildHandle` into `state.Dependency` before scheduling acquisition.
- After acquisition job schedules, combine acquisition handle into singleton `ConsumerHandle`.

## Relevant Global Context
- Producer system and singleton exist from task 002.
- Consumer must read singleton via `SystemAPI.GetSingleton<TargetSpatialHashSingleton>()`.
- Consumer must update `ConsumerHandle` via `SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>()`.
- Use `CombatSpatialHash.FloorCell(..., CombatSpatialHash.TrackingCellSize)` and `CombatSpatialHash.CellKey(...)` for query-side cell math.

## Dependencies Confirmed
- `TargetSpatialHashSingleton` exists with `TrackingCells`, `TrackingIndicesById`, `TargetEntities`, `TargetPositions`, `TargetFactions`, `TargetCount`, `BuildHandle`, and `ConsumerHandle`.
- `CombatSpatialHash` exists with tracking cell size and cell key helper.

## Step-By-Step Instructions
- Remove `targetQuery` field and target query setup if no longer used.
- Remove `Unity.Profiling` import if no marker remains.
- In `OnUpdate`, get the singleton, combine `hash.BuildHandle` into `state.Dependency`, and schedule acquisition with shared maps/snapshot arrays.
- Schedule steering after acquisition as before.
- Set `ConsumerHandle` to include acquisition handle only.
- Set `state.Dependency` to steering handle or combined relevant handles; do not dispose shared containers.
- Replace acquisition query cell math with `CombatSpatialHash`.
- Remove local `FloorCell`, local `CellKey`, and tracking cell size constant if unused.

## Acceptance Criteria
- No local target extraction remains in `ProjectileTrackingSystem`.
- Tracking behavior unchanged.
- No disposal of shared singleton containers.
- `ConsumerHandle` accumulates acquisition handle only.

## Validation Required
- Search validation: no `CompleteDependencyBeforeRO`, `ToEntityArray`, `ToComponentDataArray`, or `TargetSpatialHashBuildMarker` remains in tracking system.
- Compile/build later in final validation.

## Hard Boundaries
- Do not modify producer or collision systems in this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
