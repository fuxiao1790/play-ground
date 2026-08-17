# Sound Events

## Purpose

Define the unmanaged occurrence data passed from simulation producers to one
managed, frame-batched playback point. Sound is presentation feedback only;
dropping, delaying, or reprioritizing it must never change gameplay state.

## Produced By

Burst spawn producers call `SoundEmit.Enqueue` with
`SoundEventSingleton.Events.AsParallelWriter()`:

- impact and lingering AOE expansion in `AoeSpawnExpansionSystem.cs`
- `ProjectileSpawnExpansionSystem`
- `TargetedSpawnExpansionSystem`
- deferred armed-AOE completion in `CombatArmingSystem`

This covers every materialized spawn: root casts, trigger-linked skills,
interval children, on-hit spawns, stacking detonations, volley projectiles, and
AOE/targeted echoes. A volley or echo emits once per spawned entity; selection
and the per-clip frame cap collapse redundant copies at playback time.

`AudioRoot.Enqueue` remains the main-thread managed entry point. It currently
has no in-repository caller and is reserved for later managed UI, hurt, death,
or similar producers. Managed producers must use that entry point and must not
write the native ECS lane directly.

## Authored By

Designers author `spawnSound` and `spawnSoundRadius` on `BasicAttackPrefab`,
`BasicAoePrefab`, `LingeringAoePrefab`, and `TargetedPrefab`. The chain is:

```text
basic prefab sound/radius
  -> SkillDefinition
  -> SkillSetCompiler runtime definition tree
  -> recursive AudioRoot.Register
  -> SkillSoundIds.SpawnId + radius on spawn templates/commands
  -> SoundEmit -> SoundEventSingleton
  -> SoundEventDispatchSystem -> AudioRoot.Enqueue
  -> AudioRoot.LateUpdate selection and playback
```

Clips never enter ECS or jobs. Only the registered `int` id and `float` radius
cross the boundary. Only a spawn sound slot exists today; hit, expire, pulse,
and arming sound slots require matching producers before being added.

## Consumed By

`SoundEventDispatchSystem` is transport only. In `PresentationSystemGroup` it
completes every stored producer handle, drains the native queue, and forwards
the occurrences to `AudioRoot.Enqueue`. It never culls, ranks, spaces, or plays.

`AudioRoot.LateUpdate` is the sole decision and playback point. It owns clip
registration, the managed pending batch, selection policy, pooled voices,
listener binding, response configuration, and diagnostic counters.

## Fields / Shape

| Field | Use |
|---|---|
| `int ClipId` | Registry identity; `0` or an unknown id is silent. |
| `float2 Position` | Distance culling and pooled voice world position. |
| `float2 Velocity` | Reserved for motion-aware localization; currently a deterministic tie-break field. |
| `float AudibleRadius` | Per-event cull and rolloff radius; non-positive uses the root default. |
| `SoundCategory Category` | Occurrence class; ECS spawn producers use `Spawn`. |
| `short Priority` | Higher values rank first; player spawns use `1`, other factions use `0`. |

`SkillSoundIds` currently contains `SpawnId`. Audio response budgets, spacing,
falloff, jitter, delay, and voice configuration remain on `AudioRoot` because
they describe playback policy, not what occurred.

## Guarantees

- Registering the same clip returns the same id across a whole compiled tree
  and across recompiles.
- A missing clip, invalid id, missing `AudioRoot`, or invalid listener is silent
  and cannot throw into combat.
- The complete managed pending batch is considered before playback; there is no
  immediate per-call play path.
- A mana-rejected cast is silent because the resource gate rejects it before
  spawn expansion.
- An armed AOE is silent at placement and emits its one spawn sound when arming
  completes, mirroring its deferred spawn VFX.
- Player-faction priority applies equally to root and nested spawns.

## Spatial Model

Each event is culled against its resolved audible radius using squared distance
from `AudioRoot`'s bound listener transform. The bound object must be the same
object that carries the scene `AudioListener`; otherwise policy distance and
Unity spatialization use different origins. Missing or destroyed listener state
logs once where appropriate and stays silent.

## Culling And Selection

`AudioRoot` rejects invalid ids and out-of-range events, limits copies per clip,
and applies cross-frame same-clip start spacing. Same-frame copies compare
spacing against the drain-start snapshot, so the per-clip frame cap—not queue
order—controls a volley in one batch.

Survivors rank by priority, then clip novelty, then explicit occurrence fields.
Capacity limits pooled playback. Repeat volume falloff, pitch jitter, delayed
repeats, spatial blend, and rolloff are playback response owned by `AudioRoot`.

## Lifetime

`SoundEventSingleton` owns one persistent `NativeQueue<SoundEvent>` plus the
combined producer handle. The dispatcher completes producers and empties the
queue on every update path, including a missing `AudioRoot`; it disposes the
queue during teardown.

Forwarded events live in `AudioRoot`'s reused managed pending list until its
next `LateUpdate` drain. That list also clears on every drain path. Pooled
`AudioSource` objects live under `AudioRoot` until root teardown.

## Ordering

Parallel `NativeQueue` insertion order has no semantic meaning. Playback order
comes only from explicit ranking, grouping, spacing, and capacity rules.

The relative runtime order of `PresentationSystemGroup` dispatch and the
`AudioRoot.LateUpdate` callback has not yet been measured in the Unity Profiler.
If dispatch runs first, events join the current frame's batch; if `LateUpdate`
runs first, they join the next frame's batch. Do not change caps or add an
ordering workaround until a profiler capture records the actual player-loop
order.

## Restrictions

- Jobs enqueue only unmanaged `SoundEvent` values through `SoundEmit`; they
  never call managed `AudioRoot`.
- Emitting systems combine their scheduled jobs into
  `SoundEventSingleton.ProducerHandle` on the main thread.
- Managed producers call `AudioRoot.Enqueue`; they do not retain or write the
  native queue. This preserves singleton ownership, safety handles, and teardown
  ordering.
- No gameplay decision may read sound events, playback, or diagnostic counters.
- No per-call play API may bypass the batch.
- Sound occurrence facts must not be mixed into damage or VFX payloads.

## Related Layers

- [Layer Rules](../architecture/layer-rules.md)
- [Game Logic](../layers/game-logic.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Contracts

- [Combat Hit And Tick Results](./combat-hit-and-tick-results.md)
- [VFX Requests](./vfx-requests.md)
- [Skill Runtime Snapshots](./skill-runtime-snapshots.md)
