# 004 — `SoundEventBridge`: merge, select, play

The only task in this plan with real design content. Everything else is plumbing.

## Why

`AudioManager.PlaySound` decides one event at a time, so it is
first-come-first-served — `AudioManager.cs:154` returns `null` once the pool is
full, meaning whoever enqueued earliest wins regardless of value. With mobs
casting (`MobRoot.cs:505`), a cast flood can consume the pool before a more
valuable sound is even considered.

The bridge sees the whole frame at once, so selection becomes a sort over a
batch. That is what makes the requirement — **prioritize many kinds over many
copies** — implementable at all.

## Change

New file `Assets/Scripts/System/Audio/SoundEventBridge.cs`:

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SoundEventBridge : SystemBase
```

Structure copied from `CombatApplyBridge.cs:23-59`:

1. `CompleteDependency()`
2. `lane.ProducerHandle.Complete()`, then reset it to `default`
3. drain `lane.Events` into a reused scratch `List<SoundEvent>`
4. `AudioManager.Instance.DrainManaged(scratch)` — append the managed side
5. select (below)
6. play survivors through `AudioManager`
7. clear the lane

Steps 3 and 4 are the single merge point for the two producer paths.

## Selection policy

Applied in order to the merged batch:

1. **Distance cull.** Drop events outside the audible rectangle. Cheapest filter
   and the only one that scales with arena size rather than clip count. See
   *Open question* below.
2. **Group by `ClipId`.** Count copies per clip.
3. **Rank.** Sort by, in order: `Priority` descending, then **novelty** — a
   clip's first copy outranks every clip's second copy, which outranks every
   third, and so on. This is the rule that implements "kinds over copies": a
   sound nobody has heard this frame always beats another copy of one already
   playing.
4. **Category budget.** Reserve a floor of voices per `SoundCategory` so one
   category cannot starve another. With only `Cast` producing today this is
   inert, but it is the mechanism that keeps cast sounds audible once hit sounds
   land, and building it now is why the lane exists.
5. **Take the top N**, where N is the free voice count reported by
   `AudioManager`.
6. **Volume falloff on repeats** — the Nth surviving copy of a clip plays at
   `repeatVolumeFalloff^(N-1)`, default ≈ `0.8`. Eight copies at full volume is
   louder *and* muddier than four with falloff, and the headroom it frees is
   what lets other kinds be heard. Note `source.volume` is never set today
   (`AudioManager.cs:91-95`), so pooled sources currently inherit whatever the
   previous sound left — this must be set explicitly regardless of falloff.

`AudioManager`'s existing per-clip cap and `sameSoundStartSpacingSeconds` stay as
a **backstop**, not the primary limiter. Do not delete them; do not tune them in
this task.

## Counters

`Docs/performance.md` §*Required Counters* lists `audio requests culled` as
required, so this is a deliverable. Split the existing single
`culledCapacity` into:

- `culledRedundant` — an extra copy of a clip already sounding. Expect this to be
  large. Working as designed, inaudible.
- `culledNovel` — a clip that would have been its kind's only instance and was
  dropped anyway. **The only cull a player can hear.** Target zero; if it climbs,
  raise `maxActiveSources`, not the per-clip cap.
- `culledDistance` — dropped by step 1.

Keep `accepted`, `culledSpacing`, and `rejected` as they are. Extend
`StatsText()` (`AudioManager.cs:113`) with the new fields so the existing debug
overlay picks them up.

## Acceptance Criteria

- Bridge creates `SoundEventSingleton.Events` in `OnCreate` and disposes it in
  `OnDestroy` on every path, including partial construction.
- `ProducerHandle` is completed before the queue is read and reset afterward.
- Both producer paths are drained every frame, even when one is empty.
- The lane is cleared every frame, including early-out frames — an uncleared
  lane replays stale sound next frame.
- No per-event allocation: scratch containers are fields reused across frames,
  as `CombatApplyBridge.cs:20` does.
- Nothing depends on `NativeQueue` drain order (`coding-standards.md` §*Why
  NativeQueue for These Sinks*); ordering comes only from the explicit sort.
- With more events than voices, a clip appearing once is never dropped in favour
  of a second copy of another clip. **This is the headline behavioral criterion.**
- `culledNovel` is `0` whenever distinct-kind count ≤ pool size.
- No sound plays when `AudioManager.Instance` is null; the frame's events are
  counted as dropped, not replayed later.

## Tests

EditMode. Extract the ranking into a pure static function over
`List<SoundEvent>` so it is testable without a scene, world, or audio hardware —
the same split that makes `TargetedAcquisition` testable. Add
`Assets/Tests/EditMode/SoundEventSelectionEditModeTests.cs` covering:

- novelty beats redundancy at capacity
- priority beats novelty
- category floor is respected when one category floods
- distance cull removes far events before ranking
- exactly `min(events, freeVoices)` survive

## Dependencies

[003](./003-sound-event-lane.md).

## Scope

**Largest task in the plan.** Bridge plus selection policy plus counters plus
tests.

## Open question — deliberately deferred, not forgotten

Distance culling needs a listener position, and `PlayGround.Sim` cannot reference
GameLogic to ask the player where it is. Two options, decide during
implementation:

- **Preferred:** the bridge reads `Camera.main`'s world rect directly. `Camera`
  is `UnityEngine`, available to Sim, and matches "audible = roughly on screen"
  for a top-down game.
- **Fallback:** `AudioManager` exposes a `ListenerPosition` property that the
  player root pushes each frame (GameLogic → Sim, an allowed direction), and the
  bridge reads it from `AudioManager.Instance`.

If neither is settled quickly, ship steps 2-6 and add distance culling as a
follow-up — it is a filter in front of the ranking, not part of it.
