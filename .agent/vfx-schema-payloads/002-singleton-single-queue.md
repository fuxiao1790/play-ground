# 002 — Dispatch singleton: per-type queues + authored-type lookup

## Goal
Hold one `NativeQueue` per `VfxDataType` plus the registration-published
`(typeId,trigger) → VfxDataType` lookup that producers switch on, keeping the existing
create/complete/drain/dispose lifecycle.

## Files
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`

## Changes
```csharp
public struct CombatVfxDispatchSingleton : IComponentData
{
    public NativeQueue<VfxPointPayload> PointEvents;
    public NativeQueue<VfxAreaPayload> AreaEvents;
    public NativeQueue<VfxAreaTimedPayload> TimedEvents;

    // Authored VfxDataType per effect, indexed by VfxKey.FlatIndex(typeId, trigger).
    // Grown at registration (task 005); read-only in producers (task 004). Default slot = None.
    public NativeList<VfxDataType> DataTypeByKey;

    public JobHandle ProducerHandle;
}
```
- `OnCreate` — create the three queues (`Allocator.Persistent`) and
  `DataTypeByKey = new NativeList<VfxDataType>(Allocator.Persistent)` (empty; grows on registration).
- `OnDestroy` — `ProducerHandle.Complete()`; dispose the three queues + `DataTypeByKey` if created.
- `OnUpdate` — unchanged flow: `Complete()` + reset handle; early-out when all three queues are
  empty; else `root.DrainAndDispatch(ref singleton)` (passes the whole singleton — task 003);
  keep the `CombatStatsSingleton.VfxEventsCreated` accumulation from the returned accepted count.
- Add a public helper for registration to publish types (task 005 calls it on the main thread):
  ```csharp
  public void SetEffectDataType(int typeId, VfxTrigger trigger, VfxDataType type)
  {
      int i = VfxKey.FlatIndex(typeId, trigger);
      ref var s = ref SystemAPI.GetSingletonRW<CombatVfxDispatchSingleton>().ValueRW; // or cached
      if (s.DataTypeByKey.Length <= i) s.DataTypeByKey.Resize(i + 1, NativeArrayOptions.ClearMemory); // fills None
      s.DataTypeByKey[i] = type;
  }
  ```

## Notes / constraints
- One shared `ProducerHandle` still covers all three queues (a producer may enqueue into any of
  them); conservative + correct, preserving today's job-safety model (task 004 threads it).
- `DataTypeByKey` is written only at registration (main thread, no jobs in flight) and read-only
  during simulation → no job-safety conflict.

## Acceptance
- Compiles; singleton exposes three typed queues + `DataTypeByKey`; lifecycle unchanged.
- `SetEffectDataType` grows the list (filling gaps with `None`) and sets the slot.

## Depends on
- 001.
