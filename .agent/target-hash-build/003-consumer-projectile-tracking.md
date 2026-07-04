# 003 — Consumer: `ProjectileTrackingSystem`

## Goal
Remove this system's own gather + hash build; read the shared snapshot from
`TargetSpatialHashSingleton`.

## Remove
- `CompleteDependencyBeforeRO<TargetPosition>` / `<TargetFaction>`.
- `ToEntityArray` / `ToComponentDataArray` for entities/positions/factions.
- The `TargetSpatialHashBuildMarker` build block that fills `targetIndicesById` + `targetCells`.
- Local `FloorCell` / `CellKey` / `TargetKey` / `TargetIdKey` build-side helpers (query-side
  cell math now calls `CombatSpatialHash` with `TrackingCellSize`).
- The per-container dispose plumbing for those local containers.

## Add
- `var hash = SystemAPI.GetSingleton<TargetSpatialHashSingleton>();`
- If `hash.TargetCount == 0`, keep existing early behavior (acquisition finds nothing).
- Feed `hash.TrackingCells`, `hash.TrackingIndicesById`, `hash.TargetEntities`,
  `hash.TargetPositions`, `hash.TargetFactions` into `ProjectileTargetAcquisitionJob`
  (fields already exist — just source them from the singleton).
- `state.Dependency = JobHandle.CombineDependencies(state.Dependency, hash.BuildHandle);`
  before scheduling `acquisitionJob`.
- After scheduling, accumulate into the singleton:
  `var rw = SystemAPI.GetSingletonRW<TargetSpatialHashSingleton>();`
  `rw.ValueRW.ConsumerHandle = JobHandle.CombineDependencies(rw.ValueRW.ConsumerHandle, acquisitionHandle);`
  (accumulate the read handle only — the steering job does not touch the snapshot, so it need
  not go into `ConsumerHandle`.)

## Notes
- `ProjectileTargetAcquisitionJob` reads the maps `[ReadOnly]`; no change to its body beyond
  where `TrackingSpatialHashCellSize`/`CellKey`/`FloorCell` come from → `CombatSpatialHash`.
- Steering job unchanged.

## Acceptance criteria
- Tracking behavior unchanged (same reacquisition, same reservoir selection) — verify against
  `ProjectileTrackingSimulationTests`.
- No local target extraction remains.

## Depends on
002.
