# 002 — `TargetSpatialHashSystem` + `TargetSpatialHashSingleton`

## Goal
One producer that gathers the target snapshot once and builds all three hashes in parallel
Burst jobs, publishing them on a singleton entity.

## Placement
`[UpdateInGroup(typeof(SimulationSystemGroup))]`, before every consumer. The earliest
consumer is `ProjectileTrackingSystem` (`UpdateAfter(ProjectileSimulationSystem)`), so mark
`[UpdateBefore(typeof(ProjectileTrackingSystem))]`, `[UpdateBefore(typeof(ProjectileCollisionSystem))]`,
`[UpdateBefore(typeof(LingeringAoeCollisionSystem))]`, `[UpdateBefore(typeof(ImpactAoeCollisionSystem))]`.
(`OrderFirst = true` is an acceptable simpler alternative — proxy data is frame-constant so
running at the very top of the group is safe.)

## Component
`TargetSpatialHashSingleton : IComponentData` as defined in `index.md`. It is a passive
carrier: container handles set once in `OnCreate`; `BuildHandle`, `ConsumerHandle`,
`TargetCount` updated per frame on the main thread. `MaxTargetRadius` is a
`NativeReference<float>` written inside a build job.

## OnCreate
- Allocate persistent: `ProjectileCollisionCells`, `TrackingCells`, `AoeOccupiedCells`
  (`NativeParallelMultiHashMap<long,int>`), `TrackingIndicesById`
  (`NativeParallelHashMap<long,int>`), `MaxTargetRadius` (`NativeReference<float>`), and the
  four snapshot arrays as persistent `NativeList<T>` (or allocate per frame — see gather).
- Create the singleton entity and set the component with the container handles.
- `RequireForUpdate` is not needed; the system must run even with zero targets (to publish an
  empty, valid snapshot so consumers reading the singleton always find it).

## OnUpdate
1. Read the singleton RW. `ConsumerHandle.Complete()` (last frame's readers are done), then
   `ConsumerHandle = default`.
2. `int count = targetQuery.CalculateEntityCount();` set `TargetCount`. If `count == 0`,
   `Clear()` all maps, set `BuildHandle = state.Dependency`, write back, return early.
3. **Gather (recommended jobified):** size the four snapshot arrays to `count`; schedule a
   gather (`CalculateBaseEntityIndexArrayAsync` + `IJobChunk`, or `ToComponentDataListAsync`)
   → `gatherHandle`. Fallback: main-thread `ToComponentDataArray` (still one gather).
4. `Clear()` the three maps + id map on the main thread (cheap; keeps capacity). Ensure
   capacity ≥ needed (`AoeOccupiedCells` needs Σ per-target cell span; compute in-job or
   pre-size to a heuristic and let the job grow via `Capacity`).
5. Schedule three **single-threaded** Burst `IJob`s, each depending on `gatherHandle`:
   - `BuildProjectileCollisionHashJob` → `ProjectileCollisionCells` (center, cell 1) and
     reduce `MaxTargetRadius` from shapes.
   - `BuildTrackingHashJob` → `TrackingCells` (center, cell 64) and `TrackingIndicesById`.
   - `BuildAoeOccupiedHashJob` → `AoeOccupiedCells` (bounds-spread, cell 64).
   Each inserts in array order (indices `0..count-1`) so iteration order matches today.
6. `BuildHandle = JobHandle.CombineDependencies(hJobA, hJobB, hJobC)`; also combine the
   gather handle. `state.Dependency = BuildHandle`. Write the singleton back.

## Build-job insertion rules (must match current code exactly)
- **ProjectileCollision:** `cell = FloorCell(pos, 1); map.Add(CellKey(cell), i)` — center only.
  `MaxTargetRadius = max over i of CombatCollisionMath.BoundingRadius(shape)`.
- **Tracking:** `indicesById.TryAdd(TargetIdKey(TargetKey(entity)), i); cell = FloorCell(pos, 64); cells.Add(CellKey(cell), i)`.
- **Aoe:** `min=MinCell(BoundsMin,64); max=MaxCell(BoundsMax,64);` loop `y,x` inclusive,
  `cells.Add(CellKey(x,y), i)`.

## OnDestroy
`BuildHandle.Complete(); ConsumerHandle.Complete();` then dispose all maps, the id map, the
`NativeReference`, and the snapshot arrays if persistent.

## Dependency threading contract (documented in code)
- Build jobs operate on raw container fields, **not** the singleton component → consumers'
  `GetSingleton` does not sync the build.
- Consumers combine `BuildHandle` into their `Dependency` before scheduling, then accumulate
  their read-job handle into `ConsumerHandle` via `GetSingletonRW`.
- Producer completes `ConsumerHandle` at the top of the next `OnUpdate` before `Clear`.

## Acceptance criteria
- Singleton exists after first update; maps are non-null and valid at zero targets.
- No `CompleteDependencyBeforeRO` sync remains needed here beyond what the gather requires.
- Burst-compiles; no managed access in jobs.

## Scope
Medium–large. New system + component + three job structs.

## Depends on
001 (`CombatSpatialHash`).
