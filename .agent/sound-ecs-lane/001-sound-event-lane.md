# 001 — `SoundEventSingleton`, `SoundEmit`, `SoundEventDispatchSystem`

## Why

Emitters will be Burst jobs inside spawn expansion, so events cross from parallel
job code to the main thread. That is the pressure the canonical lane shape in
`Docs/coding-standards.md` §*System Encapsulation* exists to solve.

This task builds transport only. It emits nothing and decides nothing — the lane
is empty until [003](./003-emit-at-expansion.md) wires producers.

## Change

All three files land in `Assets/Scripts/System/Audio/`, namespace
`PlayGround.System.Combat.Audio`, beside the shipped `AudioRoot.cs` and
`SoundEvent.cs`.

### `SoundEventLane.cs`

```csharp
// ECS Lifecycle: singleton sound event lane; created and disposed by
// SoundEventDispatchSystem, written during simulation by spawn expansion jobs,
// drained and cleared by SoundEventDispatchSystem during presentation.
public struct SoundEventSingleton : IComponentData
{
    public NativeQueue<SoundEvent> Events;
    public JobHandle ProducerHandle;
}
```

The `ECS Lifecycle:` prefix is required by `coding-standards.md` §*ECS Lifecycle
Comments*. Mirrors `CombatApplyResultSingleton`
(`Assets/Scripts/System/Application/CombatApplyResults.cs:25-31`).

### `SoundEmit.cs`

Modelled on `VfxEmit` (`VfxEmit.cs:29-63`) so emit sites read the same on both
sides:

```csharp
public static void Enqueue(
    int clipId,
    float2 position,
    float audibleRadius,
    SoundCategory category,
    CombatFaction faction,
    in NativeQueue<SoundEvent>.ParallelWriter sounds)
{
    if (clipId <= 0)
    {
        return;
    }

    sounds.Enqueue(new SoundEvent { ... });
}
```

- The `clipId <= 0` guard is the same one `VfxEmit.cs:15` and `:37` use; it makes
  an unauthored clip cost one branch at the emit site.
- **Priority is derived here from `faction`**, not passed in. The shipped code
  derives it at the cast site from `SkillDriver`'s serialized field
  (`SkillDriver.cs:146`, player `1` / mob `0`); keep those values so behavior does
  not shift. Deriving in one place keeps every emit site from re-deciding, and
  nested spawns get correct priority for free.
- `Velocity` is written as `default`, matching the shipped cast emit.

### `SoundEventDispatchSystem.cs`

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SoundEventDispatchSystem : SystemBase
```

Shape copied from `CombatApplyBridge.cs:23-59`:

1. `OnCreate` — create the singleton entity and its `NativeQueue`
   (`Allocator.Persistent`).
2. `OnUpdate` — `CompleteDependency()`, then `lane.ProducerHandle.Complete()` and
   reset it to `default`.
3. Drain `lane.Events` into a field-held scratch, pushing each event through
   `AudioRoot.Instance.Enqueue(in soundEvent)`.
4. Clear the queue **on every path**, including early-outs where the queue is
   empty or `AudioRoot.Instance` is null. An uncleared lane replays stale sound.
5. `OnDestroy` — dispose the queue on every path, including partial construction.

**This system makes no decision.** It does not cull, rank, or choose voices —
`AudioRoot` does. Precedent: `CombatAoeVfxDispatchSystem` drains its queues and
calls `CombatVfxRoot.DrainAndDispatch` rather than deciding what to render.

Pushing through the existing `AudioRoot.Enqueue` (`AudioRoot.cs:280`) rather than
adding a batch entry point is deliberate: ECS and managed events then share one
`pending` list and rank together in one `LateUpdate` pass, so the per-clip frame
cap and the novelty ranking see the whole frame.

## Ordering — verify before choosing

`SoundEventDispatchSystem` runs in `PresentationSystemGroup`; `AudioRoot` drains
in `LateUpdate` (`AudioRoot.cs:128`). Both sit in the player loop's
`PreLateUpdate` phase and the relative order decides whether ECS events rank in
the frame they were produced.

**Confirm the actual order in the profiler.** Do not guess.

- **Dispatch before `LateUpdate`** — nothing to do. This is the intended shape.
- **Dispatch after `LateUpdate`** — ECS events rank one frame late (~16 ms,
  inaudible on its own) but rank *alongside the next frame's* events, which skews
  the per-clip frame cap by mixing two frames of spawns. If that shows up in the
  counters, invert the flow: have `AudioRoot.LateUpdate` pull the lane at the top
  of its drain instead of being pushed. `AudioRoot` is in Sim and may reference
  `Unity.Entities`, so this is a local change to one method.

Record whichever holds in the file's header comment, so the next reader does not
re-derive it.

## Ownership

`SoundEventDispatchSystem` creates `Events` in `OnCreate` and disposes it in
`OnDestroy`, including the path where creation partially failed
(`coding-standards.md` §*Native And ECS Handle Ownership*). No other system
allocates or disposes it.

Emitters obtain a `ParallelWriter` as a job field and combine their handle into
`ProducerHandle` on the main thread — never by reaching into this system
(`coding-standards.md` §*System Encapsulation*).

## Acceptance Criteria

- `SoundEvent` compiles inside a `[BurstCompile]` job unchanged — it is already
  unmanaged (`SoundEvent.cs:15-23`), so this is a confirmation, not a change.
- `SoundEventSingleton` exposes the container plus an explicit `ProducerHandle`,
  matching `CombatApplyResultSingleton`.
- `ProducerHandle` is completed before the queue is read and reset afterward.
- The queue is created in `OnCreate` and disposed in `OnDestroy` on every path.
- The lane is cleared every frame, including early-out frames.
- Nothing in this task culls, ranks, or plays.
- With `AudioRoot.Instance` null, the frame's events are dropped and the lane is
  still cleared — no replay next frame, no exception.
- No per-frame allocation: the drain scratch is a field.
- `AudioRoot.cs` and `SoundEvent.cs` are **unchanged** by this task.
- All three shipped EditMode test classes still pass.

## Dependencies

None — this is the first task of the follow-up plan. It assumes
`.agent/skill-sound-events/` has shipped, which it has.

## Scope

Small — one struct, one static helper, one system, all close copies of named
existing files.
