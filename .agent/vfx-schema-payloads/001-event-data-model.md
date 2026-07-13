# 001 — Event data model (generic container + payload structs)

## Goal
Replace the single `VfxPendingSpawn` with a generic transient event plus per-type payload
structs, so the GPU payload becomes data the producer packs and the dispatcher reads by schema.

## Files
- `Assets/Scripts/System/Vfx/VfxEcsComponents.cs` (rewrite)

## Changes

Remove `VfxPendingSpawn`. Add:

```csharp
public enum VfxDataType : byte { Point = 0, Area = 1, AreaTimed = 2 }

// Replaces the raw trigger bytes (0=spawn 1=hit 2=expire 3=pulse 4=arming) used everywhere.
public enum VfxTrigger : byte { Spawn = 0, Hit = 1, Expire = 2, Pulse = 3, Arming = 4 }

// ECS-owned routing key — replaces the ad-hoc `typeId * 256 + (byte)trigger` int math so the
// managed dispatcher keys its graph dictionary on these types directly (no byte conversion).
public readonly struct VfxGraphKey : IEquatable<VfxGraphKey>
{
    public readonly int TypeId;
    public readonly VfxTrigger Trigger;
    public VfxGraphKey(int typeId, VfxTrigger trigger) { TypeId = typeId; Trigger = trigger; }
    public bool Equals(VfxGraphKey o) => TypeId == o.TypeId && Trigger == o.Trigger;
    public override int GetHashCode() => (TypeId * 397) ^ (byte)Trigger;
}

// Payload structs — each declares EXACTLY the GPU fields its graph consumes.
// Field order == schema order (see task 003). No routing fields here.
public struct VfxPointPayload     { public float2 Position; }
public struct VfxAreaPayload      { public float2 Position; public float AreaSize; }
public struct VfxAreaTimedPayload { public float2 Position; public float AreaSize;
                                    public float Duration; public float TickRate; }

// One generic transient event. Payload blob is a fixed 32-byte scratch region big enough
// for the largest payload (AreaTimed = 20B). Bump PayloadBytes if a future type needs more.
public unsafe struct VfxEvent
{
    public const int PayloadBytes = 32;
    public int TypeId;
    public VfxTrigger Trigger;
    public VfxDataType DataType;   // for validation/debug; routing uses (TypeId,Trigger)
    public fixed byte Payload[PayloadBytes];

    public static VfxEvent Pack<T>(int typeId, VfxTrigger trigger, VfxDataType type, in T payload)
        where T : unmanaged
    {
        VfxEvent e = default;
        e.TypeId = typeId; e.Trigger = trigger; e.DataType = type;
        UnsafeUtility.CopyStructureToPtr(ref UnsafeUtility.AsRef(payload), e.Payload);
        return e;
    }
}
```

- Keep the ECS-lifecycle doc comment: transient native payload, never added to entities,
  queued by simulation into the singleton `NativeQueue<VfxEvent>`, drained in presentation.
- `VfxTrigger` replaces every raw trigger literal across producers + registration (tasks
  004/005). The dispatcher keys graphs on `VfxGraphKey` (task 003), not an int.
- Add a short **"adding a new VFX data type"** recipe comment: (1) add a `VfxDataType`,
  (2) add a payload struct, (3) add its schema in `VfxSchemas` (task 003), (4) pack it in the
  producer, (5) register graphs with that `DataType`.

## Ownership (per user directive)
`VfxDataType`, `VfxTrigger`, `VfxGraphKey`, and the payload structs are **ECS/data-layer owned**
(defined here in `VfxEcsComponents.cs`) and are the single canonical types used across the whole
pipeline — producers, the singleton queue, **and** the managed `Register`/dispatcher/root. The
managed side must consume these directly: no parallel managed enum, no `byte`/`int` trigger
arguments, no conversion at the boundary.

## Notes / constraints
- `Pack` uses `UnsafeUtility.CopyStructureToPtr` (memcpy) — no pointer recast, no aliasing UB.
- `VfxEvent` is blittable/unmanaged → valid `NativeQueue` element and Burst-safe to build.
- `math`/`Unity.Collections.LowLevel.Unsafe` usings required.

## Acceptance
- Compiles; `VfxPendingSpawn` fully removed (all references updated in tasks 002/004).
- `VfxEvent.Pack(in VfxAreaTimedPayload)` round-trips the 20 payload bytes into the blob.
