# Task Execution Packet

## Task
005-consumer-aoe-collision.md

## Goal
Update `LingeringAoeCollisionSystem` and `ImpactAoeCollisionSystem` to read the shared `AoeOccupiedCells` and target snapshot from `TargetSpatialHashSingleton`; update `AoeCollisionCore` to use `CombatSpatialHash` for query cell math.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`
- `.agent/target-hash-build/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/TargetSpatialHashSystem.cs`
- `Assets/Scripts/System/Common/CombatSpatialHash.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`

## Behavior To Preserve
- AOE hit qualification, overflow cap, contact gates, pulse deactivation, projectile-burst, AOE-spawn, and VFX behavior.
- AOE broadphase remains bounds-spread target hash with cell size 64.
- AOE cell walk remains inclusive min/max in cell-scan order.
- Existing output queue producer-handle forwarding remains unchanged.

## Behavior To Change
- Remove local target query, target extraction, target cell capacity pre-pass, occupied target cell build, and target/container disposal from both AOE collision systems.
- Both systems read `hash.AoeOccupiedCells`, target snapshot lists, and `hash.BuildHandle`.
- Both systems accumulate their collision job handle into singleton `ConsumerHandle`.
- `AoeCollisionCore` uses `CombatSpatialHash.MinCell/MaxCell/CellKey` with `AoeCellSize`.

## Relevant Global Context
- Producer system and singleton exist from task 002.
- Consumer must read singleton via `SystemAPI.GetSingleton<TargetSpatialHashSingleton>()`.
- Consumer must update `ConsumerHandle` via `SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>()`.
- Both AOE jobs read the same map as read-only.

## Dependencies Confirmed
- `TargetSpatialHashSingleton` exists with `AoeOccupiedCells`, target snapshot lists, `BuildHandle`, and `ConsumerHandle`.
- `CombatSpatialHash` exists with AOE cell size and cell key helper.

## Step-By-Step Instructions
- In both AOE systems, remove `targetQuery` fields and setup if no longer used.
- Keep early-outs for empty AOE queries.
- Get singleton after early-out.
- Combine `state.Dependency` with `hash.BuildHandle`.
- Feed `hash.TargetEntities.AsArray()`, `hash.TargetPositions.AsArray()`, `hash.TargetShapes.AsArray()`, `hash.TargetFactions.AsArray()`, and `hash.AoeOccupiedCells` into jobs.
- Schedule collision jobs and forward output producer handles exactly as before.
- Accumulate each collision handle into `ConsumerHandle`.
- Set `state.Dependency` to collision handle; do not dispose shared containers.
- In `AoeCollisionCore`, replace `MinCell`, `MaxCell`, and `CellKey` implementations/calls with `CombatSpatialHash`.
- Keep `TargetKey` in `AoeCollisionCore`.

## Acceptance Criteria
- No local target extraction or AOE occupied hash build remains in either AOE collision system.
- Only producer builds `AoeOccupiedCells`.
- `AoeCollisionCore` query cell math uses shared helper.

## Validation Required
- Search validation: no `CompleteDependencyBeforeRO`, target `ToEntityArray`, target `ToComponentDataArray`, `targetCellCapacity`, local `occupiedTargetCells` allocation/build/dispose remains in AOE collision systems.
- Search validation: `AoeCollisionCore` no longer has local `SpatialHashCellSize`, `MinCell`, `MaxCell`, or `CellKey`.
- Compile/build later in final validation.

## Hard Boundaries
- Do not modify producer, projectile systems, tests, or csproj in this task.
- Do not change architecture.
- Do not introduce new abstractions not described by this task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
