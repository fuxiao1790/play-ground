# 001 — CombatVfxDispatchSystem owns the persistent VFX queue

**File:** `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`
**Depends on:** none (introduce alongside the old buffer; old path keeps working
until producers migrate)
**Scope:** small

## Change

Turn the presentation dispatch system into the queue owner + drainer, modeled on
`CombatApplyFinalizeSystem` (`Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs`).

- Remove the `VfxSingleton` entity creation/query and the
  `DynamicBuffer<VfxSpawnRequestElement>` usage.
- Add state:
  ```csharp
  internal NativeQueue<VfxPendingSpawn> PendingSpawns;
  internal JobHandle ProducerHandle;
  internal bool HasQueue => PendingSpawns.IsCreated;
  internal NativeQueue<VfxPendingSpawn>.ParallelWriter AsParallelWriter() =>
      PendingSpawns.AsParallelWriter();
  ```
- `OnCreate`: `PendingSpawns = new NativeQueue<VfxPendingSpawn>(Allocator.Persistent);`
- `OnUpdate`:
  ```csharp
  ProducerHandle.Complete();
  ProducerHandle = default;
  if (PendingSpawns.Count == 0) return;
  CombatVfxRoot.Instance?.DrainAndDispatch(ref PendingSpawns); // see 002
  PendingSpawns.Clear(); // DrainAndDispatch dequeues; Clear is a safety no-op
  ```
  (If `DrainAndDispatch` fully dequeues, the explicit `Clear()` is redundant —
  pick one; do not both dequeue and leave residue.)
- `OnDestroy`: `ProducerHandle.Complete(); if (PendingSpawns.IsCreated) PendingSpawns.Dispose();`

## Notes

- Keep the system in `PresentationSystemGroup`. It is created at world init, so
  simulation-group producers can resolve it via
  `World.GetExistingSystemManaged<CombatVfxDispatchSystem>()` from frame 0.
- `CompleteDependency()` is no longer the right sync — producers no longer touch
  this system's component dependency; the explicit `ProducerHandle.Complete()`
  is what gates the main-thread drain.

## Acceptance criteria

- System allocates/disposes one `Allocator.Persistent` queue with no leak
  (Complete before dispose).
- Exposes `AsParallelWriter()`, `ProducerHandle`, `HasQueue`.
- Drains via `CombatVfxRoot.Instance` and dispatches; early-outs when empty.
- No remaining reference to `VfxSingleton` or `VfxSpawnRequestElement` in this file.
