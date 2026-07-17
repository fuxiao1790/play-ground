# 001 — Singleton sort buffers + Burst bucketing job

## Scope

`Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`

## Change

1. Add to `CombatAoeVfxDispatchSingleton`:
   - `NativeList<float2> SortedPositions`
   - `NativeList<float> SortedAreaSizes`
   - `NativeList<int> BucketOffsets`
2. Allocate all three with `Allocator.Persistent` in `OnCreate`, alongside `PendingAoeSpawns`.
3. Dispose all three in `OnDestroy`, alongside `PendingAoeSpawns`.
4. Add nested `[BurstCompile] private struct BucketAoeVfxSpawnsJob : IJob` performing a two-pass
   counting sort:
   - Pass 1: snapshot the queue (`Pending.ToArray(Allocator.Temp)`), clear the queue, count items
     per `VfxId` bucket (`Allocator.Temp` scratch), drop out-of-range ids (`id < 1 || id > BucketCount`).
   - Compute prefix-sum offsets into `BucketOffsets` (resized to `BucketCount + 2`).
   - Pass 2: scatter into `SortedPositions`/`SortedAreaSizes` (resized to the pending upper bound,
     then trimmed to the accepted count via `.Length =`), applying `math.max(0.01f, areaSize)`
     (same clamp `StageAoeSpawn` applies today).
5. Rewrite `OnUpdate` to run the job synchronously (`.Run()`) after `ProducerHandle.Complete()`,
   then call the new `CombatVfxRoot.DrainAndDispatch` overload (see 002) with
   `singleton.SortedPositions.AsArray()` / `.SortedAreaSizes.AsArray()` / `.BucketOffsets.AsArray()`.

## Acceptance criteria

- No per-frame `Allocator.TempJob`/heap allocation for the sort output buffers — only `NativeList`
  resize (grow-only, reused across frames).
- Queue is only touched by the job after `ProducerHandle.Complete()`.
- Invalid `VfxId`s (≤0 or > current `owners.Count`) are silently dropped, matching current
  `StageAoeSpawn` behavior.
- Project compiles; `BucketAoeVfxSpawnsJob` is Burst-compilable (no managed references in its
  fields).

## Depends on

None.
