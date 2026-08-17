# Sound Events

## Purpose

Define the unmanaged occurrence data passed from sound producers to one managed,
frame-batched playback point.

Sound is presentation feedback only. Dropping, delaying, or reprioritizing a
sound must never change gameplay state.

## Produced By

GameObject code calls `AudioRoot.Enqueue` on the main thread during `Update` or
earlier. Today, `SkillDriver.Tick` is the only producer: it emits one
`SoundCategory.Cast` event for an accepted root cast when that root has a
registered spawn clip.

## Authored By

Designers author `spawnSound` and `spawnSoundRadius` on the basic prefab beside
its visual/VFX fields:

- `BasicAttackPrefab`
- `BasicAoePrefab`
- `LingeringAoePrefab`
- `TargetedPrefab`

The authoring chain is:

```text
basic prefab spawnSound/radius
  -> SkillDefinition pass-through
  -> SkillSetCompiler runtime copy
  -> RuntimeSkillDefinition SpawnSound/radius
  -> AudioRoot.Register during SkillDriver.CompileAndRegister
  -> SkillSoundIds.SpawnId
  -> SkillDriver.Tick SoundEvent
```

Clips do not live on the `Skill` ScriptableObject. `BasicAttackPrefab` has a
sound slot but no sibling VFX slot. Only `spawnSound` exists today; hit, expire,
pulse, and arming sound slots do not exist until matching producers are added.

## Consumed By

`AudioRoot.LateUpdate` is the only consumer. `AudioRoot` owns clip registration,
the pending batch, selection policy, pooled voices, listener binding, response
configuration, and diagnostic counters.

`AudioRoot` lives at `Assets/Scripts/System/Audio/` in `PlayGround.Sim` while
belonging to the presentation layer. This keeps assembly direction
`PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`: current GameLogic can
call it, and future ECS presentation can reach the same merge point without a
reference from Sim back to GameLogic or UI.

## Fields / Shape

`SoundEvent` is unmanaged and contains occurrence facts:

| Field | Current use |
|---|---|
| `int ClipId` | Registry identity used to resolve the clip; invalid ids are rejected. |
| `float2 Position` | Squared-distance culling and pooled voice world position. |
| `float2 Velocity` | Reserved for future motion-aware localization; currently only participates in deterministic occurrence tie-breaking. |
| `float AudibleRadius` | Per-event spatial cull and voice rolloff radius. |
| `SoundCategory Category` | Classifies the occurrence; today `Cast` is emitted and category only participates in deterministic occurrence tie-breaking. Category-specific response is reserved. |
| `short Priority` | Higher values survive selection first and map to voice priority. |

`SoundCategory` defines `None`, `Cast`, `Hit`, `Spawn`, `Death`, and `Ui`.
Categories other than `Cast` reserve an unmanaged shape for later producers;
they are not current producers.

`SkillSoundIds` is a plain runtime identity bundle. It currently contains only
`int SpawnId`, copied onto each compiled root definition.

Payload admission rule: a field belongs in `SoundEvent` when it describes what
happened in the world. A value belongs on `AudioRoot` when it describes how
audio should respond. Budgets, copy caps, spacing, falloff, jitter, delay, and
voice configuration therefore do not cross in the event.

## Guarantees

- Clip identity is an `int` allocated by the `AudioRoot` registry; `0` means no
  clip/sound.
- Registering the same clip returns the same id, including
  `CompileAndRegister` reruns.
- `AudibleRadius <= 0` uses `AudioRoot`'s project default audible radius.
- Missing clip or root wiring stays silent and cannot throw into combat.
- The complete pending batch is considered before playback; there is no
  immediate per-call playback path.
- Pending data is drained and cleared every frame, including early-out frames.
- `AudioRoot.Clear` stops playback and clears pending state/counters while
  preserving registered clip identity.

## Spatial Model

Each event is culled against its resolved audible radius with a squared-distance
comparison. Distance is measured from the transform of `AudioRoot`'s serialized
`listenerObject`; this object is assigned by the game and is not camera-specific.

Listener co-location is an invariant: `listenerObject` must be the same
GameObject that carries the scene `AudioListener`. Otherwise culling and Unity's
spatial playback would use different origins. `BindListener` supports runtime
rebinding. Missing or invalid setup logs once and stays silent. A listener
destroyed at runtime stays silent without logging per frame. Neither case throws
into combat.

## Culling And Selection

`LateUpdate` rejects invalid clip ids, culls out-of-radius events, limits copies
per clip, and applies cross-frame same-clip spacing. Same-frame copies compare
spacing against the drain-start snapshot, so one selected copy does not spacing-
cull another copy from the same batch.

Remaining events rank by higher `Priority`, then novelty: first copies of more
clip kinds beat repeated copies when voices are scarce. Explicit occurrence
fields break remaining ties. Capacity limits pooled playback; response values
such as repeat volume falloff, pitch jitter, delayed repeats, spatial blend, and
rolloff stay owned by `AudioRoot`.

## Restrictions

- Producers must enqueue during `Update` or earlier. `LateUpdate` producer
  ordering against the drain is undefined and may slip an event by one frame.
- `Enqueue` is main-thread only in the current path.
- No gameplay decision may read `SoundEvent` data or `AudioRoot` counters.
- No per-call play API may bypass the batch; selection requires the whole
  frame's pending events.
- Do not mix sound occurrence facts into damage, spawn, or VFX payloads.

## Lifetime

Pending events live in one reused managed list from enqueue until that frame's
`LateUpdate` drain. The list clears on every drain path, including missing
listener, no free voice, and empty-batch paths. Pooled `AudioSource` objects live
under `AudioRoot` and are reused up to the configured cap.

## Ordering

Pending insertion order carries no meaning. Survivors are chosen by explicit
clip grouping, priority, novelty, occurrence tie-breaks, spacing, and capacity.
Producers must not infer playback order from enqueue order.

## Known Behavior

A cast rejected asynchronously through `SkillDriver.ReceiveSpawnRejected` has
already sounded. This is deliberate: waiting for rejection would delay every
cast sound by one frame.

## Extension Point

No ECS/native sound lane exists because no Burst producer currently emits
sound. Adding one now would create unused queue ownership and dependency work.
`CombatApplyBridge` already replays finalized hits on the main thread, so a
future hit sound can enqueue directly without a native lane.

When a Burst producer does need sound, add a `NativeQueue<SoundEvent>` lane and
a system that completes its producer handles and drains it into
`AudioRoot.Enqueue` before `AudioRoot.LateUpdate`. `SoundEvent` is already
unmanaged and `Enqueue` is already the single additive merge point, so the
managed producer path and playback policy do not change. The lane must follow
existing singleton-owned container and explicit producer-handle safety rules;
jobs must never call managed `AudioRoot` directly.

## Related Layers

- [Layer Rules](../architecture/layer-rules.md)
- [Game Logic](../layers/game-logic.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Contracts

- [Combat Hit And Tick Results](./combat-hit-and-tick-results.md)
- [VFX Requests](./vfx-requests.md)
- [Skill Runtime Snapshots](./skill-runtime-snapshots.md)
