# Sound Events

## Purpose

Define the unmanaged occurrence data passed from simulation producers to one
managed, frame-batched playback point. Sound is presentation feedback only;
dropping, delaying, or reprioritizing it must never change gameplay state.

## Produced By

Spawn apply jobs call `SoundEmit.Enqueue` with the clip-keyed containers owned
by `SoundEventSingleton`:

- impact and lingering AOE materialization in `AoeSpawnApplySystem.cs`
- discrete and continuous projectile materialization
- targeted materialization
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
  -> spawn apply loop -> SoundEmit -> SoundEventSingleton clip bucket
  -> SoundEventDispatchSystem -> AudioRoot.EnqueueBucketed
  -> AudioRoot.LateUpdate selection and playback
```

Clips never enter ECS or jobs. Only the registered `int` id and `float` radius
cross the boundary. Only a spawn sound slot exists today; hit, expire, pulse,
and arming sound slots require matching producers before being added.

## Consumed By

`SoundEventDispatchSystem` is transport only. In `PresentationSystemGroup` it
completes every stored producer handle, walks the ECS-provided clip buckets,
and forwards each bucket to `AudioRoot`. It never sorts, culls, spaces, or plays.

`AudioRoot.LateUpdate` is the sole decision and playback point. It owns clip
registration, the managed pending buckets, selection policy, pooled voices,
listener binding, response configuration, and diagnostic counters.

## Fields / Shape

| Field | Use |
|---|---|
| `int ClipId` | Registry identity; `0` or an unknown id is silent. |
| `float2 Position` | Distance culling and pooled voice world position. |
| `float2 Velocity` | Reserved for motion-aware localization. |
| `float AudibleRadius` | Per-event cull and rolloff radius; non-positive uses the root default. |
| `SoundCategory Category` | Occurrence class; ECS spawn producers use `Spawn`. |
| `short Priority` | Reserved. Current spawn events use `0`; selection treats all sources equally. |

`SkillSoundIds` currently contains `SpawnId`. Audio response budgets, spacing,
falloff, jitter, delay, and voice configuration remain on `AudioRoot` because
they describe playback policy, not what occurred.

## Guarantees

- Registering the same clip returns the same id across a whole compiled tree
  and across recompiles.
- A missing clip, invalid id, missing `AudioRoot`, or invalid listener is silent
  and cannot throw into combat.
- The complete managed pending buckets are considered before playback; there is no
  immediate per-call play path.
- A mana-rejected cast is silent because the resource gate rejects it before
  spawn expansion.
- An armed AOE is silent at placement and emits its one spawn sound when arming
  completes, mirroring its deferred spawn VFX.
- Faction and source kind do not affect selection or Unity voice priority.

## Spatial Model

Each event is culled against its resolved audible radius using squared distance
from `AudioRoot`'s bound listener transform. The bound object must be the same
object that carries the scene `AudioListener`; otherwise policy distance and
Unity spatialization use different origins. Missing or destroyed listener state
logs once where appropriate and stays silent.

## Culling And Selection

`AudioRoot` rejects invalid ids and out-of-range events, limits copies per clip,
and applies cross-frame same-clip start spacing. A clip present in an eligible
batch always retains its first copy before its per-clip cap is applied.
Same-frame copies compare spacing against the drain-start snapshot, so the
per-clip frame cap—not queue order—controls a volley in one batch.

Every eligible clip reserves its first occurrence before repeat capacity is
allocated. Remaining capacity is divided by repeat demand using integer
proportional quotas and largest-remainder scans. Ties start from a rotating
bucket cursor so a stable clip id or hash iteration order cannot own the spare
slot every frame. This is linear in occurrence count plus a bounded
`voice budget * unique clip count` quota pass; there is no event grouping or
sorting in `AudioRoot`.

`maxActiveSources` is the duplicate-voice budget, not a hard unique-clip cap.
One eligible occurrence per clip survives even when that count exceeds the
budget. Playback never calls `Stop` to steal an active voice, so capacity policy
cannot truncate a sound already in progress.

Unity also has a global real-voice limit. `AudioRoot` clamps its duplicate budget
to four voices below `AudioSettings.GetConfiguration().numRealVoices`, regardless
of a larger scene-authored value. Every pooled source uses the same neutral
`AudioSource.priority` value. `SoundEvent.Priority` is currently ignored and is
never copied into Unity priority, because doing so would let Unity's global
voice manager reintroduce clip or faction preference after proportional
selection.
Repeat volume falloff, pitch jitter, delayed repeats, spatial blend, and rolloff
are playback response owned by `AudioRoot`.

## Lifetime

`SoundEventSingleton` owns one persistent
`NativeParallelMultiHashMap<int, SoundEvent>` keyed by clip id, a
`NativeParallelHashSet<int>` of represented clip ids, and the combined producer
handle. Apply systems reserve capacity at their existing materialization sync
point. The dispatcher completes producers and clears both containers on every
update path, including a missing `AudioRoot`; it disposes both during teardown.

Forwarded events live in `AudioRoot`'s reused per-clip managed buckets until its
next `LateUpdate` drain. Those buckets clear on every drain path. Pooled
`AudioSource` objects live under `AudioRoot` until root teardown. Prewarmed
voices begin unassigned; first use assigns each voice to a clip-id pool, and
expiry returns it to that same pool in O(1).

Every active voice stores its computed expiry and is also entered in a reused
binary min-priority queue keyed by that time. Pruning peeks only the earliest
expiry, dequeues expired roots in O(log active voices), and does no source or
active-voice list scan. Delayed starts remain reserved through their computed
end time even while `AudioSource.isPlaying` is false, preventing reuse from
cutting off scheduled playback.

## Ordering

Parallel multi-map insertion and hash-set iteration order have no semantic
meaning. Clip novelty, proportional quotas, rotating ties, spacing, and
capacity rules do not depend on either order.

The relative runtime order of `PresentationSystemGroup` dispatch and the
`AudioRoot.LateUpdate` callback has not yet been measured in the Unity Profiler.
If dispatch runs first, events join the current frame's batch; if `LateUpdate`
runs first, they join the next frame's batch. Do not change caps or add an
ordering workaround until a profiler capture records the actual player-loop
order.

## Restrictions

- Jobs add only unmanaged `SoundEvent` values through `SoundEmit`; they
  never call managed `AudioRoot`.
- Spawn sounds are emitted inside spawn apply loops after materialization, not
  from expansion jobs. Armed AOE completion remains its own timed producer.
- Emitting systems combine their scheduled jobs into
  `SoundEventSingleton.ProducerHandle` on the main thread.
- Managed producers call `AudioRoot.Enqueue`; they do not retain or write the
  native buckets. This preserves singleton ownership, safety handles, and teardown
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
