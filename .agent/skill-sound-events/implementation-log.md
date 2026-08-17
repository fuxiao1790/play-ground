# Implementation Log

## Status
Complete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-audio-root.md | Complete | Old manager removed; sound contract, root vessel, SkillDriver retype, and registry tests added. Static validation complete. |
| 002-drain-and-selection.md | Complete | Frame-batched ranking/drain, playback policy, counters, and hermetic selection tests added. Static validation complete. |
| 003-skill-cast-producer.md | Complete | Four prefab authoring chains, root-slot registration, cast enqueue, OnEnable wiring, and EditMode coverage added. Static validation complete. |
| 004-docs.md | Complete | Sound boundary/ownership/authoring/timing/selection contract and maintained doc maps updated. Static validation complete. |

## Completed Tasks
- `001-audio-root.md`
  - Deleted `Assets/Scripts/Audio/AudioManager.cs`, its metadata, and the empty folder metadata.
  - Added `Assets/Scripts/System/Audio/SoundEvent.cs` and `Assets/Scripts/System/Audio/AudioRoot.cs` with direct Unity metadata.
  - Retyped the dead `SkillDriver` audio field and removed its `Awake` scene scan.
  - Added `Assets/Tests/EditMode/AudioClipRegistryEditModeTests.cs` with direct Unity metadata.
  - Acceptance: old namespace/type/callers absent; registry ids are dense/stable; invalid ids fail; `Clear` preserves registrations; duplicate root cannot replace `Instance`; `Enqueue` has no playback/drain; no sound producer added.
  - Deviations: none.
- `002-drain-and-selection.md`
  - Completed `AudioRoot.LateUpdate` drain with listener/distance culling, id validation, deterministic per-clip grouping/cap, priority-plus-copy ranking, capacity selection, cross-frame spacing, playback response, and end-time bookkeeping.
  - Added cumulative `accepted`, `culledDistance`, `culledRedundant`, `culledNovel`, `culledSpacing`, and `rejected` diagnostics plus `StatsText()`.
  - Added reused grouping/copy/selection/start-time scratch and direct internal ranking access for `PlayGround.Tests.EditMode` without changing assembly references.
  - Independent static review fixes: fully qualified seeded `Unity.Mathematics.Random`, explicit listener `float2` construction, and restored `isPlaying` inactive-source pruning alongside null/end-time checks.
  - Added `Assets/Tests/EditMode/AudioRootSelectionEditModeTests.cs` covering all specified pure-policy cases plus same-frame spacing and invalid-id rejection.
  - Acceptance: novel equal-priority kinds beat repeats; priority remains first; per-clip cap and radii hold; selected count matches capacity; same-frame duplicates survive spacing; playback fields/delay/end-time are explicit; early-outs clear pending; no ECS/native lane or producer added.
  - Deviations: none.
- `003-skill-cast-producer.md`
  - Added only `spawnSound` and `spawnSoundRadius` authoring to `BasicAttackPrefab`, `BasicAoePrefab`, `LingeringAoePrefab`, and `TargetedPrefab`.
  - Added null-safe definition pass-throughs, runtime base sound properties/ids, and projectile/AOE/targeted compiler copies.
  - Added non-recursive root-slot sound registration during `CompileAndRegister`; `AudioRoot.Register` preserves same-clip ids across recompiles.
  - Added `SkillDriver.OnEnable` serialized-root/`AudioRoot.Instance` resolution and one runtime-only setup error when unresolved.
  - Added root-cast `SoundCategory.Cast` enqueue after blocked-cast guard, with caster position, default velocity, authored radius, and player priority `1` over mob priority `0`.
  - Added `SkillCastSoundEditModeTests.cs` with direct metadata, covering all four authoring/compiler chains, stable registration across recompile, radius propagation, and null-prefab id `0`.
  - Unity Roslyn review fix: test assigns its created `SkillLoadout` directly to private `runtimeLoadout`; removed inaccessible internal `CreateRuntimeClone()` call.
  - Acceptance: missing prefab/clip/root stays silent and safe; blocked continuous+tracking path reaches no enqueue; async rejection behavior unchanged; registration/emission remain root-only; no recursive or ECS sound lane added; Sim asmdef unchanged.
  - Deviations: none.
- `004-docs.md`
  - Added `Docs/contracts/sound-events.md` with producer/consumer ownership,
    prefab authoring chain, unmanaged payload shape, id/radius conventions,
    listener invariant, frame lifetime/ordering, culling/selection policy,
    asynchronous rejection behavior, and deferred native-lane extension.
  - Updated presentation ownership, combat event separation, skill authoring,
    folder/runtime maps, and project contract index for current `AudioRoot` and
    sound-event paths.
  - Acceptance: all required contract sections and invariants are present;
    `AudioRoot` Sim ownership and deliberate absence of a current ECS/native
    sound lane are self-contained in maintained docs; canonical combat-lane list
    remains unchanged; maintained docs contain no removed audio path, namespace,
    or type references.
  - Deviations: planned historical cross-reference
    `.agent/burst-onupdate-work/index.md` is absent. No broken link was added;
    native-lane safety reasoning is self-contained in
    `Docs/contracts/sound-events.md` as required by the execution packet.

## Blockers
- None.

## Validation Summary
- Static search: zero `PlayGround.Audio`, `AudioManager`, or `PlaySound` references under `Assets/Scripts` and `Assets/Tests`.
- Static scope check: playback exists only in task-002 `AudioRoot.LateUpdate`; `Enqueue` remains append-only; task-003 adds only the root-cast producer in `SkillDriver.Tick`.
- Task-002 static policy inspection: ranking has no `Time`, `Camera`, `AudioSource`, Unity object access, or allocation expression; forbidden ECS/native/selection-wrapper dependencies are absent.
- Task-002 static drain inspection: every normal `LateUpdate` exit clears `pending`; playback explicitly assigns position, volume, pitch, priority, spatial blend, rolloff mode, min/max distance, immediate first-copy start, delayed repeat start, and pitch/delay-adjusted end time.
- Task-002 test inspection: 12 hermetic `AudioRootSelectionEditModeTests` cases cover all specified ranking cases; test source contains no playback or `LateUpdate` invocation.
- `Assets/Scripts/System/PlayGround.Sim.asmdef` SHA256 remains `5E3F95641A348ADD80D3291E501F682838C7FE2BB4BBDE10810A75620A99549E`; no asmdef diff.
- `git diff --check` passed; task-file trailing-whitespace checks passed; new selection-test metadata GUID is unique.
- Task-003 static trace confirms all four prefab/definition authoring chains feed the three compiler branches and runtime base `SpawnSound`/`SpawnSoundRadius` properties.
- Task-003 registration search finds one non-recursive `RegisterSounds` pass over `compiledSlots`; emission search finds one producer in `SkillDriver.Tick` and no ECS sound lane or extra sound slots.
- Task-003 ordering inspection confirms continuous+tracking `SpawnBlocked` refund precedes cast enqueue; enqueue remains inside the spawn/reset branch; player priority `1` is greater than mob priority `0`.
- `SkillDriver.OnEnable` uses serialized `audioRoot` first, then `AudioRoot.Instance`; unresolved runtime setup logs once, while EditMode construction remains log-free and `Tick` null-safe.
- Task-003 `git diff --check` passed; `SkillCastSoundEditModeTests.cs.meta` GUID is unique.
- Task-003 follow-up search confirms `SkillCastSoundEditModeTests` has no `CreateRuntimeClone` call outside its assembly boundary.
- Task-004 static search finds zero `Assets/Scripts/Audio/`,
  `PlayGround.Audio`, or `AudioManager` references under `Docs/`.
- Task-004 required-term inspection confirms `SoundEvent`, `SoundCategory`,
  `SkillSoundIds`, all four basic prefab types, current/reserved field uses,
  listener co-location/`BindListener`, frame drain/clear, explicit selection,
  async rejection, and deferred `NativeQueue<SoundEvent>` extension are covered.
- Task-004 coding-standard inspection confirms `SoundEvent` separation was
  added only under Combat Event Separation; canonical combat-lane examples are
  unchanged and contain no sound lane.
- Task-004 inspected exact diffs for all five existing docs plus the complete
  new contract. All task-added local link targets exist. Broad link inspection
  also found the pre-existing out-of-scope missing `Docs/memory/index.md` target
  linked by `Docs/project-overview.md`; task 004 did not add or alter that link.
- Task-004 `git diff --check` passed for tracked allowed files; new contract
  trailing-whitespace search passed. Scope inspection finds only the six allowed
  maintained doc paths plus this implementation log changed for task 004.
- Final review compiled `PlayGround.Sim` directly with Unity's current Roslyn
  response files. It passed after qualifying `Unity.Mathematics.Random`; one
  listener-lookup deprecation was removed by using setup-only
  `FindAnyObjectByType<AudioListener>()`.
- Generic generated-project `dotnet build` remains unsuitable: full EditMode build stops in unchanged Unity Render Pipeline package code (`PassesData.cs`, CS8168/CS8347), while narrow compile-check references a missing external `Microsoft.Unity.Analyzers.dll`. Final validation instead invoked Unity's own current Roslyn response files directly: `PlayGround.Sim`, `PlayGround.GameLogic`, and `PlayGround.Tests.EditMode` (with both later-added test sources supplied explicitly) all compiled successfully. The first test compile caught an inaccessible internal `CreateRuntimeClone` call; task 003 replaced it with the test-owned loadout, and recompilation passed.
- Unity tests not run per project rule. User run requested: EditMode, class `AudioClipRegistryEditModeTests`; export `Logs/TestResults-EditMode-SkillSoundEvents.xml` for review.
- Unity tests not run per project rule. User run requested: EditMode, class `AudioRootSelectionEditModeTests`; export combined results to `Logs/TestResults-EditMode-SkillSoundEvents.xml` for review.
- Unity tests not run per project rule. User run requested: EditMode, classes `SkillCastSoundEditModeTests` and `ProjectileContinuousAuthoringEditModeTests`; export combined results to `Logs/TestResults-EditMode-SkillSoundEvents.xml` for review.
- Task-004 is documentation-only; no Unity tests are needed or were run.
