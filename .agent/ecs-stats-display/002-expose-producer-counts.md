# 002 — Expose producer counts

## Goal
Make the per-frame counts that the producing systems already compute readable by
the gather system, without changing what they compute. Each field is assigned
from the same local that feeds the existing `ProfilerCounterValue`, and is set on
**every** code path (including early returns) so the display never shows stale
values.

## Edits

### A. `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
On `ProjectileSpawnApplySystemBase` (shared by `BasicProjectileSpawnApplySystem`
and `ChildSpawnerProjectileSpawnApplySystem`):

- Add fields:
  ```csharp
  internal int LastColdCreateCount;
  internal int LastReuseCount;
  ```
- In `OnUpdate`, the `if (totalRequests == 0) return;` path must zero them first:
  ```csharp
  if (totalRequests == 0)
  {
      LastReuseCount = 0;
      LastColdCreateCount = 0;
      return;
  }
  ```
- Where it currently sets the profiler counters at the end:
  ```csharp
  _spawnReuseCounter.Value = reuseCount;
  _spawnColdCreateCounter.Value = totalRequests - reuseCount;
  LastReuseCount = reuseCount;
  LastColdCreateCount = totalRequests - reuseCount;
  ```

Both concrete subclasses inherit these fields; the gather system reads each
instance separately.

### B. `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- Add fields:
  ```csharp
  internal int LastColdCreateCount;
  internal int LastReuseCount;
  ```
- Zero on the `if (totalRequests == 0) return;` path.
- After `SpawnReuseCounter.Value = reuseCount;` /
  `SpawnColdCreateCounter.Value = totalRequests - reuseCount;`:
  ```csharp
  LastReuseCount = reuseCount;
  LastColdCreateCount = totalRequests - reuseCount;
  ```

### C. `Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs`
- Add field:
  ```csharp
  internal int LastHitEventCount;
  ```
- Right after `hitCount = HitQueue.Count;` (inside `CountHitsMarker`), set it so
  the `hitCount == 0` early return is also covered:
  ```csharp
  LastHitEventCount = hitCount;
  ```

### D. `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
- Add field:
  ```csharp
  internal int LastVfxEventCount;
  ```
- At the top of `OnUpdate`, after `ProducerHandle.Complete();`, before the
  `if (PendingSpawns.Count == 0) return;` early return:
  ```csharp
  LastVfxEventCount = PendingSpawns.IsCreated ? PendingSpawns.Count : 0;
  ```
  This records the count produced this frame regardless of whether a
  `CombatVfxRoot` exists to drain it.

## Constraints
- Plain `int` fields written on the main thread only; no job touches them.
- Do not change the computed values or the profiler-counter writes — only mirror
  them into the readable fields.

## Acceptance criteria
- All four producers expose readable last-frame counts.
- Every `OnUpdate` exit path sets the relevant field(s) (verify the early-return
  branches).
- No behavior change in spawning, finalize, or VFX dispatch.

## Dependencies
None. Consumed by 003.

## Scope
Small–medium (four files, additive).
