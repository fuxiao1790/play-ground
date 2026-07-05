# 002 — Projectile collision producer → stream writer

## Goal
Make `ProjectileCollisionSystem` write hit events into a per-frame `NativeStream` (lane =
chunk) instead of `hitApply.AsParallelWriter()`, and push that stream to the finalize
system.

## Changes
`Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`

### OnUpdate
- `HasHitWriter = hitApply != null` (no more `HitQueue.IsCreated`).
- When `hitApply != null`, allocate the stream sized to the iteration query:
  ```csharp
  int laneCount = activeProjectileQuery.CalculateChunkCountWithoutFiltering();
  var hitStream = new NativeStream(laneCount, Allocator.TempJob);
  ```
  (Past the `IsEmpty` guard, `laneCount >= 1`.)
- Job field: `HitWriter = hasHitWriter ? hitStream.AsWriter() : default`.
- **Schedule on the explicit query** so lane count matches the job's chunk indexing:
  `job.ScheduleParallel(activeProjectileQuery, state.Dependency)` (was `ScheduleParallel(state.Dependency)`).
- After scheduling, in the existing `if (hitApply != null)` block, also
  `hitApply.ProjectileHitStream = hitStream;` (alongside the `ProducerHandle` combine).

### ProjectileCollisionJob
- Change `public NativeQueue<CombatHitEvent>.ParallelWriter HitWriter;` →
  `public NativeStream.Writer HitWriter;`.
- Implement `IJobEntityChunkBeginEnd` (add `using Unity.Burst.Intrinsics;` for `v128`):
  ```csharp
  private partial struct ProjectileCollisionJob : IJobEntity, IJobEntityChunkBeginEnd
  {
      public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                               bool useEnabledMask, in v128 chunkEnabledMask) {
          if (HasHitWriter) HitWriter.BeginForEachIndex(unfilteredChunkIndex);
          return true;
      }
      public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                             bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted) {
          if (HasHitWriter) HitWriter.EndForEachIndex();
      }
  }
  ```
- In `Execute`, change the hit emit from `HitWriter.Enqueue(new CombatHitEvent { … })` to
  `HitWriter.Write(new CombatHitEvent { … })`. The `HasHitWriter && HasHitEvent(...)` guard
  is unchanged.
- Vfx/Projectile/Aoe event writers stay `NativeQueue.ParallelWriter` — untouched.

## Acceptance criteria
- Compiles; projectile hits land in `ProjectileHitStream`.
- Every chunk brackets exactly one Begin/End (return `true` always so `OnChunkEnd` pairs
  each `OnChunkBegin`).
- No `BeginForEachIndex` out-of-range assertion at runtime.

## Notes / risks
- Always Begin/End per chunk even when a chunk writes nothing (empty lane is fine).
- `activeProjectileQuery` already contains every component the job requires, so the
  explicit-query schedule is valid and its unfiltered chunk count matches the job's lanes.

## Dependencies
001 (needs `ProjectileHitStream` field).
