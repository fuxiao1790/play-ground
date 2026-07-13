# 001 — Event data model (per-type payloads + ECS-owned types)

## Goal
Define one plain unmanaged payload struct per `VfxDataType` (each carrying exactly its GPU
fields + the routing key), plus the ECS-owned type vocabulary. Producers switch on an authored
`VfxDataType` (looked up per key, task 004) and enqueue the matching payload into that type's
queue (task 002).

## Files
- `Assets/Scripts/System/Vfx/VfxEcsComponents.cs` (rewrite)

## Changes

Remove `VfxPendingSpawn`. Add (all ECS/data-layer owned — see Ownership):

```csharp
// None = 0 so a default-initialized lookup slot means "no effect registered" (task 004).
public enum VfxDataType : byte { None = 0, Point = 1, Area = 2, AreaTimed = 3 }

// Replaces the raw trigger bytes. NOTE: the old per-tick `Pulse` trigger is GONE (task 006) —
// a lingering AOE's ongoing pulse is now carried by its Spawn effect (AreaTimed, duration+tickRate).
public enum VfxTrigger : byte { Spawn = 0, Hit = 1, Expire = 2, Arming = 3 }

public static class VfxKey
{
    public const int TriggerCount = 4;                 // == VfxTrigger member count
    public static int FlatIndex(int typeId, VfxTrigger t) => typeId * TriggerCount + (int)t;
}

// Routing key: which registered graph a payload targets. Replaces `typeId*256+trigger` math.
public readonly struct VfxGraphKey : IEquatable<VfxGraphKey>
{
    public readonly int TypeId;
    public readonly VfxTrigger Trigger;
    public VfxGraphKey(int typeId, VfxTrigger trigger) { TypeId = typeId; Trigger = trigger; }
    public bool Equals(VfxGraphKey o) => TypeId == o.TypeId && Trigger == o.Trigger;
    public override int GetHashCode() => (TypeId * 397) ^ (byte)Trigger;
}

// One payload struct per data type — EXACTLY its GPU fields + the routing key. Field order
// after Key == the type's schema order (task 003). These are the "plain unmanaged struct
// defining exactly the gpu data it needs".
public struct VfxPointPayload     { public VfxGraphKey Key; public float2 Position; }
public struct VfxAreaPayload      { public VfxGraphKey Key; public float2 Position; public float AreaSize; }
public struct VfxAreaTimedPayload { public VfxGraphKey Key; public float2 Position; public float AreaSize;
                                    public float Duration; public float TickRate; }
```

- Keep the ECS-lifecycle doc comment: transient native payloads, never added to entities, queued
  by simulation into the per-type `NativeQueue`s (task 002), drained in presentation.
- Add the **"add a new data type"** recipe: (1) `VfxDataType` value, (2) payload struct here,
  (3) a queue in the singleton (002), (4) a schema entry (003), (5) a producer switch case (004),
  (6) author it on the effect (005).

## Ownership (user directive)
`VfxDataType`, `VfxTrigger`, `VfxGraphKey`, `VfxKey`, and the payload structs are **ECS/data-layer
owned** (defined here) and are the single canonical types across producers, the singleton, the
managed `Register`/dispatcher/root, and the authoring bindings (005). No parallel managed enum,
no `byte`/`int` trigger args, no boundary conversion. The managed `VfxBufferSchema` (property
names/strides, 003) is the only managed-only piece, keyed by the ECS `VfxDataType`.

## Notes / constraints
- Payload structs are blittable/unmanaged → valid `NativeQueue` elements, Burst-safe to build.
- No blob / no `Pack<T>` — producers assign named fields on the chosen payload directly.
- `Unity.Mathematics` using required.

## Acceptance
- Compiles; `VfxPendingSpawn` removed (references updated in 002/004).
- Three payload structs exist, each embedding `VfxGraphKey`; `VfxDataType.None == 0`.

## Depends on
- none.
