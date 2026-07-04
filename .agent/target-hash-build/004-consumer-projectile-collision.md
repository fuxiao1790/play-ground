# 004 — Consumer: `ProjectileCollisionSystem`

## Goal
Remove this system's own gather + hash build + `maxTargetRadius` loop; read the shared
snapshot from `TargetSpatialHashSingleton`.

## Remove
- `CompleteDependencyBeforeRO<TargetPosition>` / `<TargetCollisionShape>` / `<TargetFaction>`.
- `ToEntityArray` / `ToComponentDataArray` for the four target arrays.
- Pass-1 `maxTargetRadius` loop and Pass-2 `targetCells` build loop.
- Local `SpatialHashCellSize`, `FloorCell`, `CellKey` (query-side now uses
  `CombatSpatialHash` with `ProjectileCollisionCellSize`).
- The four target-array dispose handles + `targetCells.Dispose`.

## Add
- `var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();`
- Early-out unchanged if `activeProjectileQuery.IsEmpty`. If `hash.TargetCount == 0`, the job
  already early-outs on `TotalTargetCount == 0` — feed `hash.TargetCount`.
- Feed `hash.ProjectileCollisionCells` into `ProjectileCollisionJob.TargetCells`,
  `hash.TargetEntities/Positions/Shapes/Factions` into the job.
- `MaxTargetRadius`: the job currently reads a `float` field. Change it to read
  `hash.MaxTargetRadius` (`NativeReference<float>`) — add `[ReadOnly] public NativeReference<float> MaxTargetRadius;`
  and use `.Value` where `MaxTargetRadius` is read. (Reading it inside the job keeps it behind
  `BuildHandle`; do not `.Value` it on the main thread, which would sync the build.)
- `state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);`
  before `job.ScheduleParallel`.
- After scheduling: `rw.ValueRW.ConsumerHandle = CombineDependencies(rw.ValueRW.ConsumerHandle, collisionHandle);`
- Keep the existing producer-handle forwarding to expansion/aoeExpansion/hitApply/vfx
  (unchanged; those are the *output* queues).

## Notes
- `ProjectileCollisionJob` body unchanged except `MaxTargetRadius` becomes a
  `NativeReference<float>` read, and query-side cell math → `CombatSpatialHash`.
- The projectile hash is center-cell at cell size 1; the job already expands its query AABB by
  `MaxTargetRadius`, so boundary correctness is preserved.

## Acceptance criteria
- Collision, pierce, gates, impact-spawn, and VFX behavior unchanged.
- No local target extraction or hash build remains.

## Depends on
002.
