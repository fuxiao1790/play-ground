# 003 — Schema table + generic graph instance + dispatcher/root

## Goal
Replace the hardcoded two-buffer `VfxTypeResources` with one generic, schema-driven
`VfxGraphInstance`, so each registered graph owns exactly the buffers its `DataType` declares
and the drain/upload path is a single loop over schema fields (no `switch`, no class-per-type).

## Files
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs` (rewrite)
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`

## Schema table
```csharp
public readonly struct VfxBufferField
{
    public readonly string PropertyName; // VFX graph exposed GraphicsBuffer name
    public readonly int Offset;          // byte offset within the payload blob
    public readonly int Stride;          // element size (bytes)
}

// One authored schema per VfxDataType. Field order == payload struct field order (task 001).
internal static class VfxSchemas
{
    // Point:     Positions(0,8)
    // Area:      Positions(0,8), AreaSizes(8,4)
    // AreaTimed: Positions(0,8), AreaSizes(8,4), Durations(12,4), TickRates(16,4)
    public static VfxBufferField[] For(VfxDataType type) => ...;
}
```
Property-name consts: `Positions`, `AreaSizes`, `Durations`, `TickRates`, `SpawnCount`, event `OnSpawn`.

## VfxGraphInstance (generic, replaces VfxTypeResources)
Fields: `VisualEffect Instance`, `VfxBufferField[] Schema`, `GraphicsBuffer[] Buffers`
(one per field), `NativeList<byte>[] Staging` (one per field, `Persistent`), `int MaxPerFrame`,
`int Count`.
- Construction: for each field, `Buffers[f] = new GraphicsBuffer(Structured, MaxPerFrame, field.Stride)`,
  `Staging[f] = new NativeList<byte>(MaxPerFrame * field.Stride, Persistent)`.
- `Stage(in VfxEvent e)`: if `Count >= MaxPerFrame` return false; for each field,
  append `field.Stride` bytes from `e.Payload + field.Offset` into `Staging[f]`
  (`MemCpy`/`AddRange`); `Count++`; return true.
- `Dispatch()`: if `Count == 0` return; for each field,
  `Buffers[f].SetData(Staging[f].AsArray(), 0, 0, Staging[f].Length)` +
  `Instance.SetGraphicsBuffer(field.PropertyName, Buffers[f])`;
  `Instance.SetInt(SpawnCount, Count)`; `Instance.SendEvent(OnSpawn)`;
  clear every `Staging[f]`; `Count = 0`. (Preserve the current world-origin transform reset.)
- `Dispose()`: dispose staging lists first, then release buffers, then destroy the GameObject.

> Impl risk (index #3): confirm byte-wise `GraphicsBuffer.SetData` start-index/count units land
> contiguously into the `Structured(stride=field.Stride)` buffer. If not, reinterpret each
> field's staging to its element type at upload.

## CombatVfxDispatcher
- One `Dictionary<VfxGraphKey, VfxGraphInstance> graphs` (ECS-owned `VfxGraphKey`; no int math).
- `Register(int typeId, VfxTrigger trigger, VisualEffectAsset asset, VfxDataType dataType, int maxPerFrame)`
  keys with `new VfxGraphKey(typeId, trigger)`. Takes the ECS `VfxTrigger`/`VfxDataType` directly:
  null asset → no-op; look up `VfxSchemas.For(dataType)`; **validate** the asset exposes each
  field's `GraphicsBuffer` property + the `SpawnCount` int (generic loop over schema, replaces
  `requireAreaSizeContract`); build the graph or log+skip on failure.
- `DrainAndDispatch(ref NativeQueue<VfxEvent> q)`:
  `while (q.TryDequeue(out VfxEvent e)) if (graphs.TryGetValue(new VfxGraphKey(e.TypeId, e.Trigger), out g)) if (g.Stage(e)) accepted++;`
  then `foreach g in graphs.Values g.Dispatch();` return `accepted`.
- Static `LiveResources` → `List<VfxGraphInstance>` (or `List<VisualEffect>`); keep
  `AliveParticleCount` logic (cull check + `aliveParticleCount`).

## CombatVfxRoot
- `Register(int typeId, VfxTrigger trigger, VisualEffectAsset asset, VfxDataType dataType, int maxPerFrame = 2048)`.
- `DrainAndDispatch(ref NativeQueue<VfxEvent> q)` delegates to the dispatcher.

## Acceptance
- Compiles; a `Point` graph allocates **only** a `Positions` buffer; an `AreaTimed` graph
  allocates `Positions`+`AreaSizes`+`Durations`+`TickRates`; no `switch (dataType)` in the
  drain/upload path (schema-loop only).

## Depends on
- 001, 002.
