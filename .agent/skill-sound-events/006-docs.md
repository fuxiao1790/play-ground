# 006 — Documentation

`Docs/` is a maintained design reference in this project, and the sound lane adds
a new cross-layer contract. Leaving it undocumented would make the next reader
re-derive the assembly-boundary reasoning from scratch.

## Change

### New: `Docs/contracts/sound-events.md`

Follow the shape of the existing contract docs — `vfx-requests.md` is the closest
sibling. Required sections, matching
`Docs/contracts/combat-hit-and-tick-results.md`:

- **Purpose** — carry sound intent from any producer to one playback point.
- **Produced By** — GameObject code via `AudioManager.Enqueue`; ECS/Burst code via
  `SoundEventSingleton.Events.AsParallelWriter()`.
- **Consumed By** — `SoundEventBridge` only.
- **Fields / Shape** — `SoundEvent`, `SoundCategory`, `SoundEventSingleton`.
- **Guarantees** — clip identity is an `int` id from the `AudioManager` registry,
  `0` means none; the two producer containers carry one element type and merge at
  one point.
- **Restrictions** — managed code must never enqueue the native lane directly
  (record the safety-contract reason, cross-referencing the same decision in
  `.agent/burst-onupdate-work/index.md`); no gameplay decision may depend on a
  sound event.
- **Lifetime** — both containers cleared every frame by the bridge.
- **Ordering** — bridge runs in `PresentationSystemGroup`; `NativeQueue` order is
  meaningless, ranking is explicit.

### Update: `Docs/layers/presentation-and-feedback.md`

- §*Owns* — add the sound bridge and audio budget, beside the existing
  "`CombatVfxRoot`, `CombatAoeVfxDispatcher`, VFX Graph buffers, and VFX dispatch
  caps" entry.
- §*Inputs* — add the sound event lane.
- §*Outputs* — add pooled voice playback.
- §*Main Systems / Modules* — add `Assets/Scripts/System/Audio/SoundEventBridge.cs`
  and `Assets/Scripts/System/Audio/AudioManager.cs`.
- §*Related Contracts* — link the new contract doc.

### Update: `Docs/coding-standards.md`

- §*System Encapsulation* — add `SoundEventSingleton` to the canonical
  combat-lane examples list.
- §*Combat Event Separation* — add a line keeping sound events out of damage,
  spawn, and VFX payloads, consistent with the existing separations.

### Update: `Docs/folder-structure.md`

Record `Assets/Scripts/System/Audio/` and remove `Assets/Scripts/Audio/` if it
is listed.

### Update: `Docs/project-overview.md`

Add the new contract to the *Contracts and flows* index.

## Acceptance Criteria

- New contract doc exists and follows the section shape of its siblings.
- Every doc that lists presentation systems, combat lanes, or folders mentions
  the audio ones.
- No doc still refers to `Assets/Scripts/Audio/` or namespace `PlayGround.Audio`.
- The assembly-boundary reasoning behind `AudioManager` living in Sim is written
  down somewhere in `Docs/`, not only in this plan folder — a reader who never
  opens `.agent/` should still find out why.

## Dependencies

[004](./004-sound-event-bridge.md) — the shape must be settled before it is
documented. Can land alongside or after [005](./005-skill-cast-producer.md).

## Scope

Small — one new doc, five edits.
