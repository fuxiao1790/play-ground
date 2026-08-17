# Task Execution Packet

## Task
001-audio-root.md

## Goal
Delete unused `AudioManager`; add Sim-owned sound data contract and `AudioRoot` vessel with stable clip registry, listener binding, pooled voices, pending enqueue, and clear behavior. Retype dead `SkillDriver` field so project remains compilable. Do not implement batch selection yet.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillDriver.cs`
- `.agent/skill-sound-events/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Audio/SoundEvent.cs`
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `Assets/Tests/EditMode/AudioClipRegistryEditModeTests.cs`
- Unity `.meta` files directly associated with new files/folder if repository convention requires them.

## Files Allowed To Delete
- `Assets/Scripts/Audio/AudioManager.cs`
- `Assets/Scripts/Audio/AudioManager.cs.meta`
- Empty `Assets/Scripts/Audio/` folder and `Assets/Scripts/Audio.meta`

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Tests/EditMode/CombatVfxRootRegistrationTests.cs`
- `Assets/Scripts/System/PlayGround.Sim.asmdef`
- `Assets/Tests/EditMode/PlayGround.Tests.EditMode.asmdef`
- `Docs/coding-standards.md`

## Behavior To Preserve
- Project compiles while `SkillDriver` emits no sound.
- Sim assembly dependency set remains unchanged.
- Existing clip registration survives `Clear`.

## Behavior To Change
- `AudioManager` and per-call `PlaySound` disappear.
- New `AudioRoot` owns stable clip ids, pending event collection, listener cache, and voice pool vessel.
- `SkillDriver` field becomes `AudioRoot`; old `Awake` scene scan is removed, with correct wiring deferred to 003.

## Relevant Global Context
- Namespace: `PlayGround.System.Combat.Audio`.
- `SoundEvent` is unmanaged; `SkillSoundIds` is plain struct; id `0` means none.
- `AudioRoot.Instance` duplicate handling mirrors `CombatVfxRoot`.
- Prewarm in `Awake`; no per-call public playback API; `Enqueue` only appends.
- Listener is serialized `GameObject`, cached in `OnEnable` and `BindListener`; no camera fallback.
- Response policy stays serialized on root, never in payload.
- Use seeded `Unity.Mathematics.Random`, not global Unity random.

## Dependencies Confirmed
- None required.
- Existing `AudioManager` has only dead `SkillDriver` wiring and scene YAML references; no `PlaySound` callers.
- Baseline Sim asmdef hash: `5E3F95641A348ADD80D3291E501F682838C7FE2BB4BBDE10810A75620A99549E`.

## Step-By-Step Instructions
1. Delete old audio implementation and metadata/folder as task specifies.
2. Add exact `SoundCategory`, `SoundEvent`, and `SkillSoundIds` shapes from task.
3. Add `AudioRoot` serialized configuration, singleton lifecycle, stable registry, listener cache/binding, pending list, voice-pool vessel, `Enqueue`, `TryGetClip`, and `Clear`.
4. Include internal pool support needed by task 002, but do not add selection/drain behavior from 002.
5. Retype `SkillDriver` field to `AudioRoot`, update namespace import, and remove old `Awake` resolution line.
6. Add registry EditMode tests through public API.
7. Perform static/search validation only. Do not run Unity tests or Unity runner.

## Acceptance Criteria
- No source references `PlayGround.Audio` or `AudioManager`.
- Sim asmdef unchanged.
- Null registration returns 0; repeated clip registration dedupes; valid ids round-trip; invalid ids fail.
- `Clear` preserves registrations.
- Duplicate root disables itself without replacing instance.
- `Enqueue` does not play.
- Project source remains structurally compilable with no sound producer.

## Validation Required
- Search for removed namespace/type/callers.
- Compare Sim asmdef hash to baseline.
- Inspect diff and registry tests.
- Do not run Unity tests. Log requested EditMode class `AudioClipRegistryEditModeTests` for later user execution.

## Hard Boundaries
- Do not modify files outside allowed list except imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by task.
- Do not implement task 002 drain/ranking, task 003 producer, or task 004 docs.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
