# 003 — Convert the NativeQueue producers

**Files:** `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`,
`Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`
**Depends on:** 001
**Scope:** small (both already use `NativeQueue<VfxPendingSpawn>.ParallelWriter`)

## Pattern

These two already write `VfxPendingSpawn` via a `NativeQueue.ParallelWriter`. The
only change is to drop the per-frame local queue + `VfxFlushJob` + dispose and
target the shared collector instead.

Replace the local container/flush block:
```csharp
var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
bool hasVfx = vfx != null && vfx.HasQueue;
NativeQueue<VfxPendingSpawn>.ParallelWriter vfxWriter =
    hasVfx ? vfx.AsParallelWriter() : default;
```
Pass `vfxWriter` into the producing job(s). After scheduling the writing job(s):
```csharp
if (hasVfx)
    vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, writeHandle);
state.Dependency = writeHandle; // no local queue dispose anymore
```

The job already guards on `Enqueue`; add a `bool HasVfxWriter` field and skip the
`Enqueue` when false, so test worlds without the dispatch system are safe (mirror
`HasHitWriter`).

## CombatLifetimeSystem specifics

- Remove `vfxPending`, `vfxEntity`, `vfxBuffers`, the `VfxFlushJob`, and
  `vfxPending.Dispose(...)`.
- `ProjectileLifetimeJob` and `AoeLifetimeJob` keep their
  `NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending` field; add
  `bool HasVfxWriter`; guard the `Enqueue`.
- The two jobs already chain (`aoeHandle` after `projectileHandle`) because they
  share the writer — keep that chaining; combine the final `aoeHandle` into
  `vfx.ProducerHandle`.

## AoePulseVfxSystem specifics

- Remove `vfxPending`, the `VfxFlushJob`, and `vfxPending.Dispose(...)`.
- `AoePulseVfxJob` keeps its `VfxPending` parallel writer; add `HasVfxWriter`;
  guard the `Enqueue`.
- Combine the pulse job handle into `vfx.ProducerHandle`; set `state.Dependency`
  to that handle.

## Acceptance criteria

- Neither system allocates a per-frame `NativeQueue<VfxPendingSpawn>` or schedules
  a `VfxFlushJob`.
- Trigger 2 (lifetime) and trigger 3 (pulse) requests reach the shared queue.
- Both combine into `CombatVfxDispatchSystem.ProducerHandle`.
- Null/`HasQueue` guarded so a world without the dispatch system compiles and runs.
