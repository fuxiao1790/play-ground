# 002 — CombatVfxRoot drains the queue

**File:** `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
**Depends on:** 001 (queue type/ownership)
**Scope:** small

## Change

Replace the `NativeArray<VfxSpawnRequestElement>` drain with a queue drain.

Current:
```csharp
internal void DrainAndDispatch(NativeArray<VfxSpawnRequestElement> requests)
{
    if (dispatcher == null) return;
    for (int i = 0; i < requests.Length; i++)
    {
        VfxSpawnRequestElement e = requests[i];
        dispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position, e.AreaSize);
    }
    dispatcher.Dispatch();
}
```

New:
```csharp
internal void DrainAndDispatch(ref NativeQueue<VfxPendingSpawn> queue)
{
    if (dispatcher == null) return;
    while (queue.TryDequeue(out VfxPendingSpawn p))
    {
        dispatcher.StageSpawn(p.TypeId, p.Trigger, p.Position, p.AreaSize);
    }
    dispatcher.Dispatch();
}
```

## Notes

- `dispatcher.StageSpawn` already clamps `areaSize` via `math.max(0.01f, …)`, so
  the old flush-job `> 0f ? : 1f` clamp is not needed here.
- `StageSpawn`'s `maxPerFrame` cap and `(typeId, trigger)` routing are unchanged,
  preserving the visual budget guarantee.
- `Trigger` is now `byte` (from `VfxPendingSpawn`); `StageSpawn(int typeId, int
  trigger, …)` accepts it via implicit widening — no cast needed.

## Acceptance criteria

- `DrainAndDispatch` consumes a `NativeQueue<VfxPendingSpawn>` and empties it.
- Per-item staging + single `Dispatch()` preserved.
- No reference to `VfxSpawnRequestElement` remains in this file.
