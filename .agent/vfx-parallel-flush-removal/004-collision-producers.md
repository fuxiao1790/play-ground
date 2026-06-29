# 004 — Convert the NativeStream collision producers

**Files:** `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`,
`Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`,
`Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
**Depends on:** 001
**Scope:** medium (NativeStream → NativeQueue parallel writer; remove Begin/End)

## Pattern

These three write VFX through a per-frame `NativeStream` + `VfxStreamFlushJob`.
They *also* already write `CombatHitEvent` to the shared
`CombatApplyFinalizeSystem.HitQueue` via `AsParallelWriter()` + `ProducerHandle`
in the same block — mirror that for VFX.

### System-side scheduling
Remove:
```csharp
var vfxPending = new NativeStream(activeProjectileCount, Allocator.TempJob);
...
var vfxFlushHandle = new VfxStreamFlushJob { VfxEntity = ..., Pending = vfxPending,
    VfxBuffers = SystemAPI.GetBufferLookup<VfxSpawnRequestElement>() }.Schedule(collisionHandle);
JobHandle disposeVfxHandle = vfxPending.Dispose(vfxFlushHandle);
```
Add (next to the existing `hitApply` wiring):
```csharp
var vfx = state.World.GetExistingSystemManaged<CombatVfxDispatchSystem>();
// in job init:
VfxPending = vfx != null ? vfx.AsParallelWriter() : default,
HasVfxWriter = vfx != null && vfx.HasQueue,
// after ScheduleParallel:
if (vfx != null)
    vfx.ProducerHandle = JobHandle.CombineDependencies(vfx.ProducerHandle, collisionHandle);
```
Drop `disposeVfxHandle` from the final `state.Dependency` combine (no VFX
container to dispose now). Remove the per-system
`GetSingletonEntity<VfxSingleton>()` lookups.

### Job-side
- Change the field:
  `public NativeStream.Writer VfxPending;` →
  `public NativeQueue<VfxPendingSpawn>.ParallelWriter VfxPending;`
  add `public bool HasVfxWriter;`
- Delete `VfxPending.BeginForEachIndex(entityIndexInQuery)` /
  `EndForEachIndex()` and the `EndVfxStream` helper.
- Each `vfxPending.Write(new VfxPendingSpawn { … })` becomes
  `if (HasVfxWriter) VfxPending.Enqueue(new VfxPendingSpawn { … });`
- `ProjectileCollisionSystem` emits trigger 1 (hit) and trigger 2 (expire) — both
  paths convert; the expire helper that currently takes
  `ref NativeStream.Writer vfxPending` takes the parallel writer + `HasVfxWriter`.

## Notes

- The collision query no longer needs to match a `NativeStream` foreach count, so
  the "Pass …Query explicitly so [EntityIndexInQuery] stays in range / matches the
  NativeStream size" comments are obsolete — remove or update them. (Confirm no
  *other* reason the explicit query is required before deleting that argument;
  keep the explicit query if it is load-bearing for `[EntityIndexInQuery]` used
  elsewhere — only the NativeStream-size justification goes away.)
- `Trigger` stays `byte` in `VfxPendingSpawn`; the literal `1`/`2`/`0` assignments
  are unchanged.

## Acceptance criteria

- No `NativeStream` allocated for VFX in any of the three systems.
- `VfxStreamFlushJob` no longer referenced.
- Hit (1) and projectile-expire (2) and AOE-hit (1) requests reach the shared queue.
- Each combines `collisionHandle` into `CombatVfxDispatchSystem.ProducerHandle`.
- Null/`HasVfxWriter` guarded.
