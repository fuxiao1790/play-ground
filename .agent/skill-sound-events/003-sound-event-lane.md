# 003 — `SoundEvent`, `SoundCategory`, and the lane singleton

## Why

One event type both producers speak, and one native lane for the Burst side that
follows the canonical shape in `Docs/coding-standards.md` §*System
Encapsulation*.

## Change

New file `Assets/Scripts/System/Audio/SoundEventComponents.cs`, namespace
`PlayGround.System.Combat.Audio`.

```csharp
public enum SoundCategory : byte
{
    None = 0,
    Cast,
    Hit,
    Spawn,
    Death,
    Ui
}

public struct SoundEvent
{
    public int ClipId;            // 0 = none; see task 002
    public float2 Position;
    public SoundCategory Category;
    public short Priority;        // higher wins; ties broken by novelty in task 004
}

// ECS Lifecycle: singleton sound event lane; created and disposed by
// SoundEventBridge, written during simulation by Burst producers, drained and
// cleared by SoundEventBridge during presentation.
public struct SoundEventSingleton : IComponentData
{
    public NativeQueue<SoundEvent> Events;
    public JobHandle ProducerHandle;
}
```

The `ECS Lifecycle:` comment prefix is required by `coding-standards.md`
§*ECS Lifecycle Comments* and must be updated in any later change that alters
this lifecycle.

Also add the managed producer path to `AudioManager` (Decision 3 in
[index.md](./index.md) — managed code must never raw-enqueue the native lane):

```csharp
private readonly List<SoundEvent> pendingManaged = new();

// Main thread only.
public void Enqueue(in SoundEvent soundEvent);

// Called by SoundEventBridge during drain; returns the list and clears it.
public void DrainManaged(List<SoundEvent> into);
```

`pendingManaged` is reused across frames and never reallocated
(`coding-standards.md` §*Allocation Rule*).

## Ownership

`SoundEventBridge` (task 004) creates `Events` in `OnCreate` and disposes it in
`OnDestroy`, including the path where creation partially failed
(`coding-standards.md` §*Native And ECS Handle Ownership*). No other system
allocates or disposes it.

`SoundCategory` values are additive-only — no producer exists for `Hit`,
`Spawn`, `Death`, or `Ui` yet. They are declared now because the enum is the
extension point that makes later sound work plumbing-free, which is the stated
reason for building the lane before it is needed.

## Acceptance Criteria

- `SoundEvent` is fully unmanaged and Burst-usable (no managed field, no
  reference type).
- `SoundEventSingleton` exposes the container plus an explicit `ProducerHandle`,
  matching `CombatApplyResultSingleton` in
  `Assets/Scripts/System/Application/CombatApplyResults.cs`.
- `AudioManager.Enqueue` appends without allocating after warmup.
- `DrainManaged` leaves `pendingManaged` empty and its capacity intact.
- Nothing in this task plays a sound or drains the native queue.

## Dependencies

[002](./002-audio-clip-registry.md).

## Scope

Small — two structs, one enum, two `AudioManager` methods.
