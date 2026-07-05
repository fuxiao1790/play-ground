# 003 — AOE collision producers → stream writer

## Goal
Route impact-AOE and lingering-AOE hit emission through `NativeStream` lanes. Both jobs
share `AoeCollisionCore.RunCollision`/`EmitHit`, so the hit-writer parameter type changes
there once.

## Changes

### `Assets/Scripts/System/Aoe/AoeCollisionCore.cs`
- `RunCollision<TGate>(…)`: change the parameter
  `NativeQueue<CombatHitEvent>.ParallelWriter hitWriter` → `NativeStream.Writer hitWriter`.
- `EmitHit(…)`: same parameter type change.
- Inside `EmitHit`, `hitWriter.Enqueue(new CombatHitEvent { … })` → `hitWriter.Write(...)`.
  Vfx/Projectile/Aoe event writers keep their `NativeQueue.ParallelWriter` types.
- (`NativeStream` is in `Unity.Collections`, already imported.)

### `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- OnUpdate: `HasHitWriter = hitApply != null`; when set, allocate
  `new NativeStream(impactAoeQuery.CalculateChunkCountWithoutFiltering(), Allocator.TempJob)`,
  set `HitWriter = hitStream.AsWriter()`. Job already schedules on `impactAoeQuery`
  (`ScheduleParallel(impactAoeQuery, state.Dependency)`) — keep it.
- After scheduling: `if (hitApply != null) hitApply.ImpactHitStream = hitStream;`.
- `ImpactAoeCollisionJob`: `HitWriter` field → `NativeStream.Writer`; add
  `IJobEntityChunkBeginEnd` (Begin/End on `unfilteredChunkIndex` guarded by `HasHitWriter`,
  return `true`); `using Unity.Burst.Intrinsics;`.

### `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- Same shape as impact, using `lingeringAoeQuery` and `hitApply.LingeringHitStream`.
- `LingeringAoeCollisionJob`: `HitWriter` field → `NativeStream.Writer`; add
  `IJobEntityChunkBeginEnd`; `using Unity.Burst.Intrinsics;`.

## Acceptance criteria
- Compiles; impact hits → `ImpactHitStream`, lingering hits → `LingeringHitStream`.
- `AoeCollisionCore` no longer references `NativeQueue<CombatHitEvent>`.
- Each chunk brackets one Begin/End; no out-of-range assertions.

## Notes / risks
- Both AOE jobs already schedule with an explicit query, so the lane count from that same
  query matches their chunk indexing.
- `RunCollision` is generic over `TGate`; only the hit-writer param type changes, gate
  logic untouched.

## Dependencies
001 (needs `ImpactHitStream` / `LingeringHitStream` fields). Independent of 002.
