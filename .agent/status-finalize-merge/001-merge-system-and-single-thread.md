# 001 — Merge status into finalize + single-thread the status job

## Goal

`CombatApplyFinalizeSingleSystem` becomes the single owner of both the finalize
pass and the status pass. `StatusProcessSystem` is deleted. `StatusProcessJob`
runs single-threaded Burst.

## Changes

### A. Migrate status state onto the finalize system

Add to `CombatApplyFinalizeSingleSystem`:
- `private const int MaxDetonationsPerTarget = 256;` (from status).
- `private EntityQuery targetStackQuery;`
- `private int nextAoeId = 1;` and `private int nextProjectileDetonationSourceId = 1;`
- `private static int ReserveIdBlock(ref int nextId, int entityCount)` (moved
  verbatim).

In `OnCreate`, also create the status query:
```
targetStackQuery = EntityManager.CreateEntityQuery(
    ComponentType.ReadOnly<TargetPosition>(),
    ComponentType.ReadWrite<TargetStackEntry>());
```
In `OnDestroy`, `targetStackQuery.Dispose();` (before/after the existing hit-queue
teardown — order irrelevant).

### B. Convert `StatusProcessJob` to single-threaded Burst

Move the `[BurstCompile] partial struct StatusProcessJob : IJobEntity` (and its
helpers `BuildDetonationSpawn`, `TargetKey`, `HashId`) into the finalize file /
class scope. Field changes:
- `ImpactAoeEventWriter : NativeQueue<ImpactAoeSpawnEvent>.ParallelWriter`
  → `ImpactAoeEventQueue : NativeQueue<ImpactAoeSpawnEvent>` (and the two twins).
- Rename the `Has...Writer` bools to `Has...Queue` (cosmetic; keep the guards).
- In `BuildDetonationSpawn`, `XxxEventWriter.Enqueue(...)` → `XxxEventQueue.Enqueue(...)`.
- **Keep** `public int AccrualFrame;` on the job and the decay guard
  `if (DeltaTime > 0f && entry.LastAccruedFrame != AccrualFrame)` **unchanged**.
  (Do NOT delete the guard — that was the `broken` branch's mistake.)

Schedule with `.Schedule(targetStackQuery, deps)` (single-threaded), not
`.ScheduleParallel`. `[EntityIndexInQuery]` remains valid.

### C. Restructure `OnUpdate` — the load-bearing part

Target shape (inside the existing `Marker.Auto()` scope):

```
complete hit-dispatch ProducerHandle; ProducerHandle = default
AccrualFrame++
bridge = GetExistingSystemManaged<CombatApplyBridge>()
bridge?.DisposeFinalizedCombat()

// ---- FINALIZE PASS (hits only) ----
hitCount = HitQueue.Count
LastHitEventCount = hitCount
stats.HitEventsCreated += hitCount     // if singleton present
JobHandle finalizeHandle = Dependency
if (hitCount > 0)
{
    allocate resultsList/statusList/evictionRef (TempJob)
    finalizeHandle = new FinalizeCombatSingleJob { ... AccrualFrame = AccrualFrame ... }.Schedule(Dependency)
    finalizeHandle.Complete()
    harvest evictions
    results/status ToArray(Persistent); dispose temp lists
    bridge?.SetFinalizedCombat(results, statusSnapshots)   // or dispose arrays if bridge null
}
else
{
    HitQueue.Clear()   // no-op safety; queue already empty
    // no bridge publish this frame (matches today: no new results)
}

// ---- STATUS PASS (every frame with target stacks) ----   <-- NOT inside the hitCount==0 return
int entityCount = targetStackQuery.CalculateEntityCount()
if (entityCount > 0)
{
    acquire the 3 lane singletons + queues (TryGetSingletonRW, as status does today)
    aoeIdBase = ReserveIdBlock(ref nextAoeId, entityCount)
    projIdBase = ReserveIdBlock(ref nextProjectileDetonationSourceId, entityCount)
    JobHandle statusDeps = finalizeHandle   // already complete when hits ran
    combine each present lane's ProducerHandle into statusDeps
    JobHandle statusHandle = new StatusProcessJob { ...plain queues..., AccrualFrame = AccrualFrame }
        .Schedule(targetStackQuery, statusDeps)
    for each present lane: ProducerHandle = CombineDependencies(ProducerHandle, statusHandle)
    Dependency = statusHandle
}
```

Critical: **there is no `return` between the finalize block and the status
block.** The old `if (hitCount == 0) { Clear; return; }` is replaced by the
`else { Clear; }` above so control always falls through to the status pass.

### D. Delete the reach-in and the file

- Delete `Assets/Scripts/System/Status/StatusProcessSystem.cs` (+ `.meta`).
- The `World.GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>()` lookup
  and its comment are gone with the file; `AccrualFrame` is now read directly as
  a same-class field.
- Fix `using`s in the finalize file if any status-referenced type
  (`TargetPosition`, the three `*SpawnEventSingleton`/`*SpawnEvent`,
  `IntervalChildKind`, `StackDetonationKind`) is not already imported.

## Acceptance criteria

- Project compiles after 002 + 003 land.
- `rg "StatusProcessSystem"` over `Assets/Scripts` returns no matches.
- `rg "GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>"` returns none.
- `rg "ScheduleParallel|ParallelWriter" Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
  returns none.
- The decay guard line `entry.LastAccruedFrame != AccrualFrame` still present.
- Static trace of `OnUpdate`: on a constructed `hitCount == 0` frame, control
  reaches the status `.Schedule`. (No `return` before it.)
- **PlayMode (user-run):** `AoeSimulationTests` + `ProjectileCollisionSimulationTests`
  green; detonation fires same-frame on threshold cross; stacks still decay/expire
  on no-hit frames; total lifetime ≈ authored `Lifetime`.

## Scope / risk

Medium. One file heavily edited, one deleted. Risk concentrated in step C
(early-return trap) and the producer-handle plumbing in the status block. No new
types.

## Open judgment call (non-blocking)

Status pass kept **async** (`.Schedule` + published `ProducerHandle`), so
expansion overlaps it as in also-working. Alternative: `.Run()` it (complete lane
handles first, set `ProducerHandle = default`). Both correct; async is the
smaller delta. Decide at implementation; does not affect behavior, only overlap.
