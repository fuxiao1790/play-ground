# 004 — Documentation

`Docs/` is a maintained design reference in this project, and sound adds a new
cross-layer contract. Leaving it undocumented would make the next reader
re-derive the assembly-boundary reasoning — and the reason there is *no* ECS lane
— from scratch.

## Change

### New: `Docs/contracts/sound-events.md`

Follow the shape of the existing contract docs; `combat-hit-and-tick-results.md`
is the closest sibling. Required sections:

- **Purpose** — carry sound intent from any producer to one playback point.
- **Produced By** — GameObject code via `AudioRoot.Enqueue`, during `Update` or
  earlier. Today that is `SkillDriver.Tick` only.
- **Authored By** — the basic prefab (`BasicAoePrefab`, `LingeringAoePrefab`,
  `TargetedPrefab`, `BasicAttackPrefab`), beside the VFX slots, travelling the
  same prefab → definition → compiler → `SkillSoundIds` chain VFX travels. Say
  plainly that clips do **not** go on the `Skill` ScriptableObject, and note the
  two asymmetries: `BasicAttackPrefab` has a sound slot but no VFX sibling, and
  only `spawnSound` exists until the remaining slots get producers.
- **Consumed By** — `AudioRoot.LateUpdate` only.
- **Fields / Shape** — `SoundEvent`, `SoundCategory`, and `SkillSoundIds`. State per field whether it is read today or reserved for
  localization, and record the admission rule from [index.md](./index.md)
  Decision 6: *a field belongs in the payload if it describes what happened in
  the world; it belongs on `AudioRoot` if it describes how audio should respond.*
  Without that written down, the next reader either strips `Velocity` as dead or
  starts adding jitter and budget knobs beside it.
- **Guarantees** — clip identity is an `int` id from the `AudioRoot` registry,
  `0` means none; ids are stable across `CompileAndRegister` re-runs;
  `AudibleRadius <= 0` means the project default; the batch is drained and
  cleared every frame.
- **Spatial model** — per-event `AudibleRadius` cull, squared-distance compare,
  measured from the transform of `AudioRoot`'s serialized `listenerObject`. The
  listener is any GameObject the game assigns; nothing is camera-specific.
  **That object must carry the scene's `AudioListener`** or the game culls what
  it would have panned. Name this as an invariant, not a tip. Note
  `BindListener` for runtime rebinding, and that a missing or destroyed listener
  yields silence plus a one-time error, never an exception or a per-frame log.
- **Restrictions**
  - Producers must enqueue during `Update` or earlier. A producer enqueuing from
    `LateUpdate` has undefined ordering against the drain and may slip a frame.
  - No gameplay decision may read a sound event or the audio counters.
  - There is no per-call play API by design — selection needs the whole frame's
    batch.
- **Lifetime** — pending batch cleared every frame, including early-out frames.
- **Ordering** — insertion order carries no meaning; ranking is explicit.
- **Known behavior** — a cast rejected asynchronously via
  `SkillDriver.ReceiveSpawnRejected` has already sounded. Deliberate; the
  alternative is delaying every cast sound by a frame.
- **Extension point** — when a Burst producer needs sound, add a
  `NativeQueue<SoundEvent>` lane plus a system that drains it into
  `AudioRoot.Enqueue` ahead of `LateUpdate`. `SoundEvent` is already unmanaged
  and `Enqueue` is already the single merge point, so nothing existing changes.
  Record *why* it was not built up front (no Burst producer exists;
  `CombatApplyBridge` already replays hits on the main thread and can enqueue
  directly), cross-referencing `.agent/burst-onupdate-work/index.md` §*Why the
  dual event path stays* for the safety-contract reasoning that applies once a
  lane does exist.

### Update: `Docs/layers/presentation-and-feedback.md`

- §*Owns* — add the audio root and audio budget, beside the existing
  "`CombatVfxRoot`, `CombatAoeVfxDispatcher`, VFX Graph buffers, and VFX dispatch
  caps" entry.
- §*Inputs* — add the pending sound batch.
- §*Outputs* — add pooled voice playback.
- §*Main Systems / Modules* — add `Assets/Scripts/System/Audio/AudioRoot.cs`.
- §*Related Contracts* — link the new contract doc.

### Update: `Docs/coding-standards.md`

- §*Combat Event Separation* — add a line keeping sound events out of damage,
  spawn, and VFX payloads, consistent with the existing separations.
- Do **not** add anything to §*System Encapsulation*'s canonical combat-lane
  list. Sound has no lane, and listing it there would be the exact mistake this
  plan avoids.

### Update: `Docs/reference/game-logic/skill-system.md`

Wherever the skill authoring chain is described, add the sound slot beside the
VFX slot. A designer looking for "where do I put the sound" must find the answer
in the skill docs, not only in the audio contract.

### Update: `Docs/folder-structure.md`

Record `Assets/Scripts/System/Audio/` and remove `Assets/Scripts/Audio/` if it is
listed.

### Update: `Docs/project-overview.md`

Add the new contract to the *Contracts and flows* index.

## Acceptance Criteria

- New contract doc exists and follows the section shape of its siblings.
- Every doc that lists presentation systems or folders mentions the audio ones.
- No doc still refers to `Assets/Scripts/Audio/`, namespace `PlayGround.Audio`,
  or `AudioManager`.
- The reasoning behind `AudioRoot` living in Sim, **and** the reasoning behind
  there being no sound lane, are both written down in `Docs/` — a reader who
  never opens `.agent/` should find both.

## Dependencies

[002](./002-drain-and-selection.md) — the shape must be settled before it is
documented. Can land alongside or after [003](./003-skill-cast-producer.md).

## Scope

Small — one new doc, five edits.
