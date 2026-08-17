# Implementation Context

## Architectural Decisions
- Replace dead `AudioManager` outright with one `AudioRoot`; no compatibility API.
- `AudioRoot` and `SoundEvent` live in `PlayGround.Sim` under `Assets/Scripts/System/Audio/`, namespace `PlayGround.System.Combat.Audio`.
- Use one managed, frame-batched path: producers call `AudioRoot.Enqueue`; `AudioRoot.LateUpdate` ranks and plays. No ECS lane until a Burst producer exists.
- Author cast clips on basic prefabs and resolve stable integer ids during `SkillDriver.CompileAndRegister`.

## Global Invariants
- Assembly direction stays `PlayGround.Ui -> PlayGround.GameLogic -> PlayGround.Sim`; `PlayGround.Sim.asmdef` gains no PlayGround reference.
- `SoundEvent` remains unmanaged. Id `0` means none.
- No per-call playback API. Selection needs whole frame batch.
- Listener cull origin and scene `AudioListener` must be same GameObject.
- Missing audio wiring yields one setup error and silence, never combat exceptions.

## Ownership Boundaries
- `AudioRoot` owns registry, pending batch, selection policy, pooled voices, counters, listener binding, and response configuration.
- Producers own occurrence facts only: clip id, position, velocity, audible radius, category, priority.
- `SkillDriver` owns cast emission and player-versus-mob priority choice.
- Shared debug UI may read `AudioRoot.StatsText`; `AudioRoot` does not write UI.

## Data Flow
- Prefab `spawnSound` / radius -> definition pass-through -> runtime definition copy -> `AudioRoot.Register` -> `SkillSoundIds.SpawnId` -> `SkillDriver.Tick` enqueue -> `AudioRoot.LateUpdate` drain -> pooled `AudioSource` playback.

## Lifecycle / Allocation Rules
- Cross-MonoBehaviour resolution belongs in `OnEnable`, not `Awake`.
- Pool prewarms during setup and grows only to configured cap.
- Pending, grouping, and selected-index lists are reused; no per-event allocations.
- Pending batch clears every frame, including early-outs.
- `Clear` stops playback and clears pending/counters but preserves clip registration identity.
- `AudioRoot.Instance` is set in `Awake`, duplicate roots disable themselves, and instance clears in `OnDestroy`.

## ECS / Job / Threading Constraints
- Current producer is main-thread `SkillDriver.Tick`; `Enqueue` is main-thread only.
- No `NativeQueue`, bridge system, producer handle, or ECS sound component in this plan.
- Future Burst lane must drain into existing `AudioRoot.Enqueue` before `LateUpdate`.

## Determinism Requirements
- Pending insertion order has no meaning; explicit ranking determines survivors.
- Seeded `Unity.Mathematics.Random` avoids perturbing gameplay global random state.
- Same-clip spacing uses drain-start snapshot, so same-frame copies are not spacing-culled.

## Producer / Consumer Separation
- Producers enqueue during `Update` or earlier. `LateUpdate` enqueue ordering is undefined.
- Gameplay never reads sound events or audio counters.
- Audio policy values never appear in `SoundEvent` or other crossing structs.

## Reused Mechanisms
- Registry and duplicate-root shape mirror `CombatVfxRoot`.
- Clip authoring chain mirrors existing AOE VFX authoring chain.
- Voice pooling, same-clip spacing, and active bookkeeping are re-derived from deleted `AudioManager`.

## Introduced Mechanisms
- `SoundCategory`, unmanaged `SoundEvent`, plain `SkillSoundIds`, `AudioRoot` registry/pool/batch drain.
- Spatial cull, per-clip copy cap, priority/novelty ranking, repeat falloff, jitter/delay, diagnostic counters.

## Validation Requirements
- Agents must not run Unity tests or Unity test runner.
- Add specified EditMode tests and request user run them.
- User exports results to `Logs/TestResults-EditMode-SkillSoundEvents.xml`; claims require reviewing that XML.
- Static checks and permitted compile checks may be used; never claim tests passed without XML.
- Baseline SHA256 for `Assets/Scripts/System/PlayGround.Sim.asmdef`: `5E3F95641A348ADD80D3291E501F682838C7FE2BB4BBDE10810A75620A99549E`.

## Files / Systems Mentioned By The Plan
- Delete `Assets/Scripts/Audio/AudioManager.cs`, its meta, and empty folder/meta.
- Add `Assets/Scripts/System/Audio/SoundEvent.cs` and `AudioRoot.cs`.
- Update `SkillDriver`, four basic prefab classes, `SkillDefinition`, `SkillSetCompiler`, and `RuntimeSkillDefinition`.
- Add three EditMode test classes.
- Add `Docs/contracts/sound-events.md`; update presentation layer, coding standards, skill system, folder structure, and project overview docs.
