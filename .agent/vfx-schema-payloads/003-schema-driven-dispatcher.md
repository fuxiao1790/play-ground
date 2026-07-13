# 003 — Schema table + generic graph instance + dispatcher/root

## Goal
Replace the hardcoded two-buffer `VfxTypeResources` with ONE generic, schema-driven
`VfxGraphInstance` (no class-per-effect). Each registered graph owns exactly the buffers its
authored `VfxDataType` declares. The managed side drains the three typed queues and stages each
payload into its target graph by looping that type's schema — no `switch` on a runtime data-type
value, no class hierarchy.

## Files
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs` (rewrite)
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`

## Schema table
```csharp
public readonly struct VfxBufferField
{
    public readonly string PropertyName; // VFX graph exposed GraphicsBuffer name
    public readonly int PayloadOffset;   // byte offset of the field within ITS payload struct
    public readonly int Stride;          // element size (bytes)
}

// One schema per VfxDataType. Offsets are within that type's payload struct (after Key) —
// derive via UnsafeUtility.GetFieldOffset / Marshal.OffsetOf at static init (do NOT hardcode padding).
internal static class VfxSchemas
{
    // Point:     Positions<-VfxPointPayload.Position(8)
    // Area:      Positions<-VfxAreaPayload.Position(8), AreaSizes<-.AreaSize(4)
    // AreaTimed: Positions, AreaSizes, Durations<-.Duration(4), TickRates<-.TickRate(4)
    public static VfxBufferField[] For(VfxDataType type) => ...;
}
```
Property-name consts: `Positions`, `AreaSizes`, `Durations`, `TickRates`, `SpawnCount`, event `OnSpawn`.

## VfxGraphInstance (generic — replaces VfxTypeResources)
Fields: `VisualEffect Instance`, `VfxDataType DataType`, `VfxBufferField[] Schema`,
`GraphicsBuffer[] Buffers` (one per field), `NativeList<byte>[] Staging` (one per field,
`Persistent`), `int MaxPerFrame`, `int Count`.
- Construction: for each schema field, `Buffers[f] = new GraphicsBuffer(Structured, MaxPerFrame,
  field.Stride)`, `Staging[f] = new NativeList<byte>(MaxPerFrame * field.Stride, Persistent)`.
- `Stage<T>(in T payload) where T : unmanaged`: if `Count >= MaxPerFrame` return false; for each
  field, `MemCpy` `field.Stride` bytes from `(byte*)&payload + field.PayloadOffset` into
  `Staging[f]`; `Count++`; return true. (Generic offset memcpy — no aliasing recast, no per-type
  code; `T` is the type's payload struct.)
- `Dispatch()`: if `Count == 0` return; for each field,
  `Buffers[f].SetData(Staging[f].AsArray(), 0, 0, Staging[f].Length)` +
  `Instance.SetGraphicsBuffer(field.PropertyName, Buffers[f])`;
  `Instance.SetInt(SpawnCount, Count)`; `Instance.SendEvent(OnSpawn)`;
  clear staging; `Count = 0`. (Preserve the current world-origin transform reset.)
- `Dispose()`: dispose staging, release buffers, destroy the GameObject.

> Impl risk (index #3): confirm byte-wise `GraphicsBuffer.SetData` start-index/count units land
> contiguously into `Structured(stride=field.Stride)`; else reinterpret staging to the field
> element type at upload.

## CombatVfxDispatcher
- One `Dictionary<VfxGraphKey, VfxGraphInstance> graphs` (ECS-owned key; no int math).
- `Register(int typeId, VfxTrigger trigger, VisualEffectAsset asset, VfxDataType dataType, int maxPerFrame)`:
  null asset → no-op; `schema = VfxSchemas.For(dataType)`; **type-safety validation** — the asset
  must expose each schema field's `GraphicsBuffer` property + the `SpawnCount` int (generic loop;
  replaces `requireAreaSizeContract`). On mismatch, log which authored `VfxDataType` disagrees
  with the graph and skip. Build the graph keyed by `new VfxGraphKey(typeId, trigger)`.
- `DrainAndDispatch(ref CombatVfxDispatchSingleton s)`: three typed passes —
  ```
  while (s.PointEvents.TryDequeue(out var p)) if (graphs.TryGetValue(p.Key, out g)) if (g.Stage(p)) n++;
  while (s.AreaEvents.TryDequeue(out var a)) if (graphs.TryGetValue(a.Key, out g)) if (g.Stage(a)) n++;
  while (s.TimedEvents.TryDequeue(out var t)) if (graphs.TryGetValue(t.Key, out g)) if (g.Stage(t)) n++;
  ```
  then `foreach g in graphs.Values g.Dispatch();` return `n`. (Three typed loops + one generic
  graph — no data-type switch, no class-per-type.)
- Static `LiveResources` → `List<VfxGraphInstance>`; keep `AliveParticleCount` logic.

## CombatVfxRoot
- `Register(int typeId, VfxTrigger trigger, VisualEffectAsset asset, VfxDataType dataType, int maxPerFrame = 2048)`.
- `DrainAndDispatch(ref CombatVfxDispatchSingleton s)` delegates to the dispatcher.

## Acceptance
- Compiles; a `Point` graph allocates ONLY `Positions`; `AreaTimed` allocates all four buffers.
- No `switch (dataType)` in the managed path; routing uses `VfxGraphKey`.
- Registering an asset whose exposed buffers mismatch its authored `VfxDataType` is rejected with
  a clear error.

## Depends on
- 001, 002.
