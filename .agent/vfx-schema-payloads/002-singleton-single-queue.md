# 002 — Collapse dispatch singleton to one generic queue

## Goal
Hold a single `NativeQueue<VfxEvent>` in the dispatch singleton (no per-type queue),
keeping the existing create/complete/drain/dispose lifecycle intact.

## Files
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`

## Changes
- `CombatVfxDispatchSingleton`:
  ```csharp
  public struct CombatVfxDispatchSingleton : IComponentData
  {
      public NativeQueue<VfxEvent> Events;   // was PendingSpawns (VfxPendingSpawn)
      public JobHandle ProducerHandle;
  }
  ```
- `OnCreate` — create `Events = new NativeQueue<VfxEvent>(Allocator.Persistent)`.
- `OnDestroy` — `ProducerHandle.Complete()`; dispose `Events` if created (unchanged shape).
- `OnUpdate` — unchanged flow: `Complete()` + reset handle, early-out when `Events.Count == 0`,
  else `root.DrainAndDispatch(ref singleton.Events)`; keep the `CombatStatsSingleton.VfxEventsCreated`
  accumulation from the returned accepted count.

## Notes / constraints
- One queue + one `ProducerHandle` preserves the current job-safety model exactly; no producer
  chaining change (see task 004 + `reference_shared_queue_producer_chaining`).

## Acceptance
- Compiles; singleton exposes `Events`; lifecycle (create/complete/drain/dispose) unchanged.

## Depends on
- 001 (`VfxEvent`).
