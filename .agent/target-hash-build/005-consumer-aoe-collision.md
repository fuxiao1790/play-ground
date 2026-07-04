# 005 — Consumers: `LingeringAoeCollisionSystem` + `ImpactAoeCollisionSystem`

## Goal
Both AOE collision systems read the single shared `AoeOccupiedCells` from the singleton,
eliminating the duplicated bounds-spread build they each do today.

## Remove (in both systems)
- `CompleteDependencyBeforeRO<TargetPosition>` / `<TargetCollisionShape>` / `<TargetFaction>`.
- `ToEntityArray` / `ToComponentDataArray` for the four target arrays.
- The `targetCellCapacity` pre-pass and the `occupiedTargetCells` build loop.
- The four target-array dispose handles + `occupiedTargetCells.Dispose`.

## Add (in both systems)
- `var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();`
- Feed `hash.AoeOccupiedCells` into the job's `OccupiedTargetCells`, and
  `hash.TargetEntities/Positions/Shapes/Factions` into the job.
- `state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);`
  before `job.ScheduleParallel(query, state.Dependency)`.
- After scheduling: `rw.ValueRW.ConsumerHandle = CombineDependencies(rw.ValueRW.ConsumerHandle, collisionHandle);`
- Keep the existing forwarding to expansion/aoeExpansion/hitApply/vfx (output queues).

## `AoeCollisionCore` changes
- `MinCell` / `MaxCell` / `CellKey` / `SpatialHashCellSize` in `AoeCollisionCore` are replaced
  by `CombatSpatialHash` calls with `AoeCellSize`. `RunCollision`'s cell-walk
  (`min = MinCell(collision.BoundsMin); max = MaxCell(collision.BoundsMax)`) now calls the
  shared helper — build (producer) and query (core) use the same cell size and `CellKey`.

## Ordering note
Both AOE jobs read `AoeOccupiedCells` `[ReadOnly]`. `ImpactAoeCollisionSystem` still
`UpdateAfter(LingeringAoeCollisionSystem)`, so they do not run truly concurrently, but even if
they did, concurrent read-only access to the same map is legal. Both must accumulate their
read handle into `ConsumerHandle` so the producer waits for both before rebuilding next frame.

## Acceptance criteria
- Impact and lingering hit qualification, overflow cap (`MaxAoeTargetsPerTick`), contact
  gates, pulse deactivation, projectile-burst, and VFX behavior unchanged.
- Only one AOE bounds-spread hash is built per frame (in the producer), not two.
- `CombatPoolCleanupSystemTests` / any AOE collision tests still pass.

## Depends on
002.
