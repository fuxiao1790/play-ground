# 002 — `AudioRoot` drain: spatial cull, ranking, playback, counters

The only task in this plan with real design content. Everything else is
plumbing.

## Why

A per-call play API decides one event at a time, so it is first-come-first-served:
a mob-cast flood that arrives early wins the pool and a later, more valuable sound
is refused. With mobs casting (`MobRoot.cs:505`), that is the normal case, not an
edge case.

The drain sees the **whole frame at once**, so selection becomes a sort over a
batch rather than a greedy guess. That is what makes the stated requirement —
*prioritize many kinds over many copies* — implementable at all.

## Change

### Drain point: `AudioRoot.LateUpdate`

Every producer enqueues from `Update`: `PlayerRoot.Update` (`PlayerRoot.cs:234`)
and `MobRoot.Update` (`MobRoot.cs:505`) both call `skillDriver.Tick`. Unity
completes every `Update` before any `LateUpdate`, so a `LateUpdate` drain sees
the complete frame's events with no ordering assumption between MonoBehaviours.

`Docs/coding-standards.md` §*Update Timing* already assigns this role to
`LateUpdate` — queued work that must see the finished frame.

**No ECS system, no `NativeQueue`, no `ProducerHandle`.** See
[index.md](./index.md) Decision 3 for why the lane is deferred until a Burst
producer exists.

Per frame:

1. Prune finished voices; compute `freeVoices`.
2. Resolve listener position; snapshot per-clip last-start times.
3. Rank the batch (below) into a reused scratch list of selected indices.
4. Play the survivors, setting `volume`, `pitch`, and any repeat delay.
5. Write back the new per-clip start times; accumulate counters.
6. `pending.Clear()` — **on every path**, including the early-out where
   `pending.Count == 0` or `freeVoices == 0`. An uncleared batch replays stale
   sound next frame.

### Spatial culling

`AudioRoot` serializes a `GameObject listenerObject`; its transform is the
listener position. Nothing about it is camera-specific — player, camera, or a
dedicated marker are all valid, and `AudioRoot` never inspects which.

Resolution rules:

- Cache `listenerObject.transform` in `OnEnable` and in `BindListener`
  ([001](./001-audio-root.md)).
- **Unassigned is a setup error.** Log once at `OnEnable`, naming the
  `AudioRoot`. No `Camera.main` fallback — `coding-standards.md` §*Unity Object
  Access* prefers serialized references and §*Fail Fast Validation* bans hiding
  bad setup behind convenience resolution.
- **A destroyed listener is normal, not a bug.** If the listener is the player,
  it is destroyed on death and rebound on respawn. Check the cached transform for
  Unity-null each drain; when it is gone, cull the whole batch and keep going.
  Do not log per frame — the setup error is a one-time message, and a dead player
  must not produce a console flood.

Either way — unassigned or destroyed — the frame's events count as
`culledDistance` and the game runs silent rather than throwing. Silence is the
right degradation over "skip the cull and play everything": the setup error is
already loud, and flooding the pool would make every other counter meaningless
at exactly the moment you are trying to read them.

**Per-event radius, not one global radius.** The cull test is:

```
distanceSq(event.Position, listenerPosition) > radius * radius
    where radius = event.AudibleRadius > 0 ? event.AudibleRadius
                                           : defaultAudibleRadius
```

Squared compare, never `sqrt`. A detonation and a dagger cast do not share an
audible range, and a single global radius forces one of them wrong: tuned for the
detonation it lets dagger spam in from off-screen, tuned for the dagger it makes
the detonation vanish. `AudibleRadius` rides on the payload
([001](./001-audio-root.md)), so the same number later drives the
`AudioSource.maxDistance` rolloff — the cull boundary and the falloff boundary
stay one value instead of drifting apart.

**`listenerObject` must be the object carrying the scene's `AudioListener`.**
Cull distance and pan distance are then measured from one point. If they diverge
— cull from the player, pan from the camera — the game culls sounds it would have
panned and pans sounds it culled, and the bug presents as "sounds cut out near
the screen edge" with nothing obviously wrong in either component.

Validate at setup, which the `GameObject` field makes direct:
`listenerObject.GetComponent<AudioListener>()` is null → log an error naming the
assigned object and the object that does carry the `AudioListener`. Same check in
`BindListener`.

`defaultAudibleRadius` should exceed the visible half-diagonal — hearing
something just off-screen is correct, hearing nothing past the edge is not.
Roughly `1.5 ×` half-diagonal is the starting point.

A radius rather than a view rectangle: for a top-down game the two answers differ
only at the corners, and the rectangle costs rotation handling and a camera-size
query every frame for that.

### No separate selection type — the manager owns processing

Culling, ranking, jitter, and playback all live on `AudioRoot`. There is no
`SoundSelection` class, no `SoundSelectionSettings`, no `SoundPlayRequest`, no
`SoundCullCounts`. Four types to serve one ranking method is decomposition that
hides the logic rather than removing any, and `Docs/coding-standards.md`
§*Root Component Rule* already puts one coordinating root per concern.

Consequences:

- Tuning values are the serialized fields on `AudioRoot`
  ([001](./001-audio-root.md)) read directly, not packed into a settings struct.
- Copy index, chosen volume, and chosen delay are **locals in the drain**. They
  never cross a type boundary, so nothing about jitter appears in any payload.
- Counters are fields on `AudioRoot`, incremented in place.
- Scratch is a reused `List<int>` of selected indices into `pending`, not a
  second list of copied request structs.

**One discipline survives the merge:** the ranking method takes `now`,
`listenerPosition`, and `freeVoices` as **parameters**, and touches no
`AudioSource`, no `Time`, and no `Camera`. It stays an `internal` method on
`AudioRoot` — not a separate class — but a test can call it on a plain
`AudioRoot` instance without ever triggering playback.

That matters concretely: EditMode has no audio device, `Time.time` does not
advance, and `AudioSource.isPlaying` does not behave as it does in play mode. A
ranking test that reached into the pool would be testing Unity's editor
behaviour, not the policy. Parameterising the three inputs keeps the test
hermetic without adding a type.

## Selection policy

Applied in order to the batch:

1. **Spatial cull.** Drop events beyond their own `AudibleRadius` (above).
   Cheapest filter and the only one that scales with arena size rather than clip
   count.
2. **Drop `ClipId <= 0`** into `rejected`. Should not happen — producers guard —
   but the batch is public API.
3. **Group by `ClipId`**, assigning each event a **copy index** (`0` = that
   clip's first copy this frame).
4. **Per-clip frame cap.** Drop every event with
   `copyIndex >= maxCopiesPerClipPerFrame` into `culledRedundant`. **This applies
   regardless of how many voices are free** — see *Duplicates are capped by kind,
   not by scarcity* below.
5. **Rank.** Sort by `Priority` descending, then by copy index **ascending**:
   a clip's first copy outranks any clip's second copy, which outranks any third.
   This is the rule that implements "kinds over copies" — a sound nobody has
   heard this frame always beats another copy of one already sounding.
6. **Take the top `freeVoices`.**
7. **Volume falloff on repeats.** A copy plays at
   `repeatVolumeFalloff^copyIndex`. With the cap at `3` the ladder is
   `1.0, 0.8, 0.64` and terminates — no inaudible `0.13` tail consuming a voice.

### Duplicates are capped by kind, not by scarcity

Step 4 is not a capacity measure. Ten copies of one clip started in the same
frame are sample-aligned and sum coherently — `+6 dB` at two copies, `+12 dB` at
four — so they read as one loud muddy hit, not as ten. Free voices do not make
that sound good. The cap therefore holds at `maxCopiesPerClipPerFrame` whether
`freeVoices` is `4` or `40`; scarcity only decides how many *kinds* survive.

Pitch jitter and repeat-start delay ([001](./001-audio-root.md)) handle the
copies that do survive.

**No per-category voice floor.** The previous revision of this plan reserved a
floor per `SoundCategory`; with `Cast` as the only producing category it is inert
by that plan's own admission, and an inert mechanism cannot be validated by any
test that reflects real behavior. `SoundCategory` still rides on the event and is
reported in the counters, so adding the floor when a second category ships is a
change to this one function — not to the data contract, not to any producer.

`sameSoundStartSpacingSeconds` remains a **cross-frame backstop**, not the
primary limiter: within a frame the cap and the ranking decide, across frames
spacing prevents a machine-gun. It must be evaluated against a snapshot taken at
drain start ([001](./001-audio-root.md)) — checking and updating per play would
cull every in-frame duplicate, since all of them share one `Time.time`, and
silently disable everything on this page.

## Counters

`Docs/performance.md` §*Required Counters* lists `audio requests culled` as
required, and §*Audio* names pooled AudioSources, same-clip duplicate culling,
and priority/distance culling. These are a deliverable, not a test hook
(`coding-standards.md` §*Test Hooks*).

| Counter | Meaning |
|---|---|
| `accepted` | played this frame |
| `culledDistance` | outside the event's `AudibleRadius` |
| `culledRedundant` | an extra copy of a clip already sounding, from the per-clip cap or from top-N — expect this to be large; working as designed, inaudible |
| `culledNovel` | a clip that would have been its kind's only instance and was dropped anyway. **The only cull a player can hear.** Target zero; if it climbs, raise `maxActiveSources`, not the per-clip cap |
| `culledSpacing` | rejected by the cross-frame backstop |
| `rejected` | `ClipId <= 0` or unknown id |

Expose them through a `StatsText()` on `AudioRoot`. `DebugOverlay` owns the
shared label (`coding-standards.md` §*Debug UI Ownership*); `AudioRoot` exposes
the data and does not write to it.

## Acceptance Criteria

- With more events than voices, a clip appearing once is never dropped in favour
  of a second copy of another clip. **This is the headline behavioral criterion.**
- `culledNovel` is `0` whenever distinct-kind count ≤ `freeVoices`.
- A clip never exceeds `maxCopiesPerClipPerFrame` copies in one frame **even when
  the pool is nearly empty**. The cap is not a function of `freeVoices`.
- Two events with different `AudibleRadius` at the same distance cull
  independently — the larger radius survives where the smaller does not.
- An event with `AudibleRadius <= 0` culls against `defaultAudibleRadius`.
- Spacing does not cull in-frame duplicates: a frame containing N copies of one
  clip, with voices free and N ≤ cap, plays N sounds. This is the regression
  guard for the snapshot requirement.
- `pending` is cleared every frame including early-out frames.
- Exactly `min(survivingEvents, freeVoices)` sounds play.
- `volume` is set explicitly on every play, never inherited from the previous
  sound on that pooled source.
- Copy index `0` is never delayed; copies after it are.
- No per-event allocation: `pending`, the grouping scratch, and the selected-index
  scratch are fields reused across frames, as `CombatApplyBridge.cs:20` does.
- The ranking method touches no `UnityEngine` object and reads no `Time`,
  `Camera`, or `AudioSource` — `now`, `listenerPosition`, and `freeVoices` arrive
  as arguments.
- No jitter, delay, falloff, or budget value appears in `SoundEvent` or in any
  struct crossing between producer and manager. They are `AudioRoot` fields and
  drain locals only.
- Nothing depends on `pending` insertion order; ordering comes only from the
  explicit sort.
- A frame with no listener — unassigned, or destroyed at runtime — counts every
  event as `culledDistance` and plays nothing. It does not throw, does not replay
  next frame, and does not log per frame.
- `BindListener` takes effect on the next drain, and re-runs the `AudioListener`
  co-location check.

## Tests

EditMode. Add `Assets/Tests/EditMode/AudioRootSelectionEditModeTests.cs`. Instantiate
`AudioRoot` with `new GameObject().AddComponent<AudioRoot>()` and destroy it in
teardown — the same pattern task 001 uses for the registry — then call the
ranking method directly with supplied `now` / `listenerPosition` / `freeVoices`.
No playback is triggered, so no audio device is needed.

- novelty beats redundancy at capacity
- priority beats novelty
- spatial cull removes far events before ranking
- per-event `AudibleRadius` is honoured: same distance, different radius,
  different outcome
- `AudibleRadius <= 0` falls back to `defaultAudibleRadius`
- per-clip cap holds with `freeVoices` far above the batch size
- exactly `min(events, freeVoices)` survive
- repeat falloff applies by copy index, not to the first copy
- `culledNovel` is `0` when distinct kinds fit in the pool
- empty batch and `freeVoices == 0` both produce empty output and no counts

Ranking results are observed through the selected-index scratch and the counter
fields. `AudioRoot` exposes them because `Docs/performance.md` §*Required
Counters* demands them and `DebugOverlay` reads them — they are a real
diagnostic, not a hook added for tests (`coding-standards.md` §*Test Hooks*).

## Dependencies

[001](./001-audio-root.md).

## Scope

**Largest task in the plan.** Drain, selection policy, counters, tests.
