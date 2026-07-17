# 002 — Refactor DrainAndDispatch / Dispatch to consume sorted slices

## Scope

`Assets/Scripts/System/Vfx/CombatVfxRoot.cs`, `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`

## Change

1. `CombatVfxRoot`:
   - Add `public int RegisteredVfxCount => owners.Count;`
   - Replace `DrainAndDispatch(ref NativeQueue<AoeVfxSpawnRequest> queue)` with:
     `internal int DrainAndDispatch(NativeArray<float2> sortedPositions, NativeArray<float> sortedAreaSizes, NativeArray<int> bucketOffsets)`
     — for each `vfxId` in `1..owners.Count`, slice `[bucketOffsets[vfxId], bucketOffsets[vfxId+1])`,
     skip empty/null-resource buckets, call `dispatcher.Dispatch(res, positionsSlice, areaSizeSlice)`.
     Return `sortedPositions.Length` (total accepted).
   - Delete `StageAoeSpawn` (its bounds check + clamp now live in `BucketAoeVfxSpawnsJob`).
2. `CombatAoeVfxDispatcher.Dispatch`:
   - New signature: `Dispatch(AoeVfxTypeResources res, NativeArray<float2> positions, NativeArray<float> areaSizes)`.
   - Call `res.EnsureBufferCapacity(positions.Length)`, then `SetData` straight from the passed-in
     slices (no more reading `res.Staging`/`res.AreaSizeStaging`).
3. `AoeVfxTypeResources`:
   - Remove `Staging`, `AreaSizeStaging`, `TryStage`, `ClearStaging`.
   - Remove their disposal lines from `Dispose()`.
   - `EnsureBufferCapacity` unchanged.

## Acceptance criteria

- `CombatVfxRoot` remains sole owner/disposer of `AoeVfxTypeResources`/`GraphicsBuffer`s;
  `CombatAoeVfxDispatcher` remains stateless (no fields, no ownership).
- No leftover references to `Staging`/`AreaSizeStaging`/`TryStage`/`ClearStaging`/`StageAoeSpawn`
  anywhere in `Assets/`.
- Project compiles.

## Depends on

001 (needs the agreed `DrainAndDispatch` call shape: positions/areaSizes/offsets arrays).
