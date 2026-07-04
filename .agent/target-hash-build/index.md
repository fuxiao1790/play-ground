# Parallel Target Spatial-Hash Build

## Summary

Today four simulation systems each, on the main thread in `OnUpdate`:

1. `CompleteDependencyBeforeRO` on the target-proxy components (a main-thread sync stall),
2. extract 3–4 target arrays (`ToEntityArray` / `ToComponentDataArray`),
3. build a spatial hash over targets in a managed `for` loop,
4. dispose everything at end of frame.

The consumers and the hash they build:

| Consumer | Hash shape | Cell size | Extra outputs |
|---|---|---|---|
| `ProjectileTrackingSystem` | center-cell | 64 | `idById` map |
| `ProjectileCollisionSystem` | center-cell | 1 | `maxTargetRadius` |
| `LingeringAoeCollisionSystem` | bounds-spread | 64 | — |
| `ImpactAoeCollisionSystem` | bounds-spread | 64 | — |

The two AOE systems build a **byte-for-byte identical** hash (same targets, same cell
size 64, same bounds-spread insertion) — pure duplicate work — and run back-to-back.

This plan introduces one producer system, `TargetSpatialHashSystem`, that runs once at the
top of `SimulationSystemGroup`. It gathers the target arrays a single time and schedules
**one single-threaded Burst `IJob` per distinct hash** (three jobs) that run concurrently —
"one worker per spatial hash", as requested. The built maps are published on a **singleton
entity** (`TargetSpatialHashSingleton : IComponentData`), not on the system's fields. The
four consumer systems drop their gather/build code and become pure readers of the singleton.

Net effect: 4 gathers + 4 sync stalls + 4 managed builds (one duplicated) collapse to
1 gather + 3 parallel Burst builds, off the main thread, published through one entity.

## Result lives on an entity (core design constraint)

Per the review decision, the built hashes must live on a singleton component, and no system
may reach into another system's fields to obtain them. This also matches the existing
"prepare writes RW, consumers read RO, ECS carries the dependency" pattern used by
`CombatRenderInstanceBuffers` (memory `project_render_instance_buffers`).

`TargetSpatialHashSingleton` is a passive carrier of persistent container handles + job
handles:

```
struct TargetSpatialHashSingleton : IComponentData
{
    // stable persistent containers, allocated once in producer OnCreate:
    public NativeParallelMultiHashMap<long,int> ProjectileCollisionCells; // center, cell 1
    public NativeParallelMultiHashMap<long,int> TrackingCells;            // center, cell 64
    public NativeParallelHashMap<long,int>      TrackingIndicesById;      // id -> index
    public NativeParallelMultiHashMap<long,int> AoeOccupiedCells;         // bounds-spread, cell 64
    public NativeReference<float>               MaxTargetRadius;          // built in job
    public NativeArray<Entity>                  TargetEntities;           // frame snapshot
    public NativeArray<TargetPosition>          TargetPositions;
    public NativeArray<TargetCollisionShape>    TargetShapes;
    public NativeArray<TargetFaction>           TargetFactions;
    public int                                  TargetCount;             // set on main thread
    public JobHandle                            BuildHandle;             // build -> readers
    public JobHandle                            ConsumerHandle;          // readers -> rebuild
}
```

**Why the singleton carries container handles instead of the build jobs writing the
component:** the build jobs take the raw `NativeParallelMultiHashMap` fields as job fields;
those containers carry their own `AtomicSafetyHandle`, so writer→reader ordering is enforced
per-container. The component itself is written only on the main thread (handles set once at
create; `BuildHandle`/`MaxTargetRadius`(ref)/`TargetCount` updated per frame). Because **no
job writes the component type**, a consumer's `SystemAPI.GetSingleton<...>()` does **not**
force a main-thread completion of the build job — it just reads the struct. The consumer then
combines `BuildHandle` into its own `Dependency`, so the build overlaps the frame and the
container safety system is still satisfied. (If the build jobs wrote the component RW,
`GetSingleton` on a consumer would sync the build on the main thread and kill the overlap.)

## Distinct hashes after the change

The three cell/insertion shapes are genuinely different and cannot be merged:

- **ProjectileCollisionCells** — center-cell, cell size **1**, one entry/target; plus
  `MaxTargetRadius` (reduction over shapes).
- **TrackingCells** — center-cell, cell size **64**, one entry/target; plus
  `TrackingIndicesById` (target-id → array index).
- **AoeOccupiedCells** — bounds-spread, cell size **64**, one entry per cell each target's
  AABB overlaps. Shared by both AOE collision systems (the dedup).

Cell sizes are preserved exactly. Retuning them is out of scope (aoe-system.md
"Performance Notes": revisit cell size only with profiling).

## Constraints & invariants the change must respect

- **Target proxy data is frame-constant during simulation.** `TargetPosition` /
  `TargetCollisionShape` / `TargetFaction` are written only by managed code
  (`CombatTargetProxy.Push` via `SetComponentData` from MonoBehaviour `Update`; create/
  delete structural changes in `LateUpdate`). No `SimulationSystemGroup` system writes them.
  *Source:* `Assets/Scripts/System/Common/CombatTargetProxy.cs`, aoe-system.md "Target Proxy
  Bridge". → A single snapshot before the first consumer is valid for all four consumers.
- **Insertion order determines multihashmap iteration order, and behavior depends on it.**
  AOE collision keeps "first-N in cell-scan order" on overflow
  (`CollisionConstants.MaxAoeTargetsPerTick`); tracking's reservoir sampling walks targets in
  insertion order. Each build must stay a **single-threaded `IJob` inserting in array order**
  (not a parallel writer), so iteration order is identical to today and behavior is preserved.
  *Source:* `AoeCollisionCore.RunCollision`, `ProjectileTargetAcquisitionJob`.
- **Publish via a singleton component; do not reach into system fields.** Discovery is
  `SystemAPI.GetSingleton<TargetSpatialHashSingleton>()`. Cross-job ordering is threaded by
  the containers' own safety handles plus the `BuildHandle`/`ConsumerHandle` stored on the
  singleton. *Source:* review decision; pattern precedent `project_render_instance_buffers`.
- **CellKey/FloorCell must agree between build and query.** A consumer builds nothing but
  still computes which cells to *query* from its own bounds using the same cell size and
  `CellKey`; build and query cell math must be identical or lookups silently miss. Today the
  FNV `CellKey` + `FloorCell/MinCell/MaxCell` are copy-pasted across the three systems.
- **No managed `TargetCompanion` from jobs.** Unchanged; snapshot carries only unmanaged
  proxy components. *Source:* index.md change rules.
- **Persistent hot path.** Reuse the maps across frames (`Clear` + ensure capacity), matching
  `HitQueue`. *Source:* index.md change rules.

## Mechanisms reused vs. introduced

- **Reused:** singleton-component publication with RW-build/RO-read dependency carried by the
  container safety handles (as `CombatRenderInstanceBuffers` does); persistent native
  containers cleared/refilled per frame; single-threaded Burst `IJob` builds (as
  `CombatApplyFinalizeSingleSystem` chose for finalize).
- **Introduced:** one producer `ISystem` (`TargetSpatialHashSystem`), the
  `TargetSpatialHashSingleton` component, and a shared static `CombatSpatialHash` helper for
  `CellKey`/cell math. The helper *removes* triplicated hash math rather than adding a copy.

## Minimal/additive vs. refactor comparison

**Additive (least change):** wrap each system's existing build loop in its own Burst `IJob`
from that system's `OnUpdate`.
- Data flow: unchanged — 4 gathers, 4 sync stalls, duplicated AOE hash, triplicated cell math.
- New types: 4 private job structs; no shared result, nothing on an entity.
- Long-term cost: four snapshot pipelines to keep in sync; the shared "different hash per
  consumer" facility the user asked for never exists.

**Refactor (chosen):** one `TargetSpatialHashSystem` gathers once, builds all hashes in
parallel, publishes on a singleton entity; consumers read the singleton.
- Data flow: 1 gather → 3 parallel Burst builds → singleton → 4 readers; one sync point.
- Removed: per-consumer gather+build; duplicate AOE hash; triplicated cell math (→
  `CombatSpatialHash`).
- Long-term benefit: single source of truth on an entity; a future consumer (beams) reads an
  existing map instead of adding a fifth pipeline.

**Decision: refactor, publishing to a singleton entity.** Fewer data paths, one gather,
ownership on a component rather than smeared across four systems' fields.

## Design validation against the invariants

- *Frame-constant proxies* → one snapshot at group top; consumers never re-gather. ✅
- *Insertion order* → each build is a single-threaded `IJob` inserting in array order. ✅
- *Singleton publication, no field reaching* → containers on `TargetSpatialHashSingleton`;
  `GetSingleton` does not sync because no job writes the component type; ordering via
  `BuildHandle` + container safety handles. ✅
- *CellKey agreement* → both sides call `CombatSpatialHash`. ✅
- *Persistent hot path* → maps `Clear`ed and reused; only contents change. ✅

## Honest performance note

The three builds are each ~O(targetCount) and cheap individually; the dominant win is
**removing 4 main-thread gather+sync passes (→1) and the duplicated AOE hash**, and moving
build work off the main thread to overlap the frame. "One worker per hash" is the requested
and correct structure, but raw parallelism across three microsecond jobs is secondary to the
dedup and single gather. Validate with `ProfilerMarker`s (reuse
`ProjectileTrackingSystem.TargetSpatialHashBuild`; add one on the producer).

## Open decision (non-blocking)

Snapshot array gather: **recommended** — jobified gather into the singleton's persistent
arrays via `EntityQuery.CalculateBaseEntityIndexArrayAsync` + an `IJobChunk` (or
`ToComponentDataListAsync` per frame), producing a gather handle the build jobs depend on.
Fallback if fiddly: one main-thread `ToComponentDataArray` pass in the producer (still 4→1).
Either way the *builds* are jobified and the *maps* are persistent and published on the entity.

## Task list

- `001-shared-spatial-hash-helper.md` — extract `CombatSpatialHash` static (CellKey +
  FloorCell/MinCell/MaxCell); replace the three copies.
- `002-target-spatial-hash-system.md` — the producer `ISystem` + `TargetSpatialHashSingleton`:
  single gather, three parallel Burst build jobs, persistent maps, `BuildHandle` /
  `ConsumerHandle`, OnCreate alloc, OnDestroy dispose.
- `003-consumer-projectile-tracking.md` — read singleton `TrackingCells` +
  `TrackingIndicesById`.
- `004-consumer-projectile-collision.md` — read singleton `ProjectileCollisionCells` +
  `MaxTargetRadius`.
- `005-consumer-aoe-collision.md` — both AOE systems read the shared singleton
  `AoeOccupiedCells`.
- `006-validation.md` — parity checks, markers, and existing test coverage.

## Dependencies

001 → 002 → {003, 004, 005} → 006. 003–005 are independent of each other.
