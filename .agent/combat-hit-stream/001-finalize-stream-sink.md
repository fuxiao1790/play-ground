# 001 — Finalize system: 3-stream sink + lane reader

## Goal
Replace the single `NativeQueue<CombatHitEvent> HitQueue` sink in
`CombatApplyFinalizeSingleSystem` with three `NativeStream` fields fed by the producers,
read all three lane-by-lane inside the existing Burst finalize `IJob`, and dispose them
in bulk each frame. Define the producer-facing API that 002/003 consume.

## Changes
`Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`

### System fields / API
- Remove `HitQueue`, `AsParallelWriter()`, and the `OnCreate` queue allocation.
- Add three sink fields set by producers:
  ```csharp
  internal NativeStream ProjectileHitStream;
  internal NativeStream ImpactHitStream;
  internal NativeStream LingeringHitStream;
  ```
- Keep `ProducerHandle` unchanged (producers still combine into it).
- `OnDestroy`: complete `ProducerHandle`; dispose any created stream.
- Add a test/injection entry point (used by 004):
  ```csharp
  internal void InjectTestHits(NativeArray<CombatHitEvent> hits) {
      var s = new NativeStream(1, Allocator.TempJob);
      var w = s.AsWriter();
      w.BeginForEachIndex(0);
      for (int i = 0; i < hits.Length; i++) w.Write(hits[i]);
      w.EndForEachIndex();
      // route to any one field; ProjectileHitStream is fine
      ProjectileHitStream = s;
  }
  ```

### OnUpdate
- After `ProducerHandle.Complete()`, normalize missing streams so the job always has 3
  valid readers:
  ```csharp
  if (!ProjectileHitStream.IsCreated) ProjectileHitStream = new NativeStream(0, Allocator.TempJob);
  if (!ImpactHitStream.IsCreated)     ImpactHitStream     = new NativeStream(0, Allocator.TempJob);
  if (!LingeringHitStream.IsCreated)  LingeringHitStream  = new NativeStream(0, Allocator.TempJob);
  ```
- Hit count = `ProjectileHitStream.Count() + ImpactHitStream.Count() + LingeringHitStream.Count()`
  (used for `HitEventsCreated` stat, the `hitCount == 0` early-out, and map capacity).
- On the zero early-out: dispose all three streams, reset to `default`, return.
- Schedule the finalize job with three `NativeStream.Reader` fields (`.AsReader()`).
- After `Dependency.Complete()` and result extraction: dispose all three streams and set
  fields to `default` (so a non-running producer next frame reads as absent).

### FinalizeCombatSingleJob
- Replace the `HitQueue` field with three readers + keep `HitCount` (map capacity):
  ```csharp
  public NativeStream.Reader ProjectileHits;
  public NativeStream.Reader ImpactHits;
  public NativeStream.Reader LingeringHits;
  public int HitCount;
  ```
- Factor the current per-hit body (the first loop) into `ProcessHit(in CombatHitEvent hit,
  ref NativeHashMap<Entity,int> map, ref NativeList<TargetAccum> accums, ref int evictionCount)`
  — `continue` becomes `return`.
- Add a lane walker:
  ```csharp
  void ProcessStream(ref NativeStream.Reader r, ref NativeHashMap<Entity,int> map,
                     ref NativeList<TargetAccum> accums, ref int evictionCount) {
      for (int lane = 0; lane < r.ForEachCount; lane++) {
          int count = r.BeginForEachIndex(lane);
          for (int k = 0; k < count; k++)
              ProcessHit(r.Read<CombatHitEvent>(), ref map, ref accums, ref evictionCount);
          r.EndForEachIndex();
      }
  }
  ```
- `Execute`: build `map`(cap `HitCount`) + `accums`, call `ProcessStream` for each of the
  three readers, then run the existing second loop over `accums` unchanged. Dispose
  `accums`/`map` as today (no `hits` array anymore).

## Acceptance criteria
- File compiles; no `HitQueue`/`ToArray`/`TryDequeue` references remain.
- Damage/stack/crit/eviction results identical to the queue version for the same hits.
- Streams disposed every frame (no leak warnings), including the zero-hit early-out and
  the normalized placeholders.

## Notes / risks
- Verify `new NativeStream(0, Allocator.TempJob)` is legal (see index Open questions). If
  not, cache a persistent 1-lane empty stream for placeholders instead.
- Reader fields are read-only consumers; scheduling after `ProducerHandle.Complete()`
  means no outstanding writers.

## Dependencies
None. Defines the API for 002/003/004.
