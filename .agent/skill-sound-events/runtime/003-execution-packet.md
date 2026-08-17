# Task Execution Packet

## Task
003-skill-cast-producer.md

## Goal
Add only current producer: authored spawn sound/radius on four basic prefabs, pass through definitions/compiler/runtime snapshot, resolve stable clip ids during `CompileAndRegister`, and enqueue cast events only for successful root casts. Fix `SkillDriver` audio-root wiring in `OnEnable` with player-over-mob priority.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Validator/BasicAoePrefab.cs`
- `Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs`
- `Assets/Scripts/Skills/Validator/TargetedPrefab.cs`
- `Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`
- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs` only if directly required to preserve its no-audio-root regression behavior
- `.agent/skill-sound-events/implementation-log.md`

## Files Allowed To Create
- `Assets/Tests/EditMode/SkillCastSoundEditModeTests.cs`
- Direct Unity `.meta` file for new test if repository convention requires it.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Audio/SoundEvent.cs`
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- Existing authoring/compiler EditMode tests for construction patterns.

## Behavior To Preserve
- Skills with no prefab or no clip compile and cast silently without exceptions.
- Existing blocked continuous+tracking projectile refunds without sound.
- Async spawn rejection may refund after sound has already played; do not delay all sound.
- Child/triggered skill trees do not get sound ids registered or emit sound in this task.
- Sim asmdef unchanged.

## Behavior To Change
- All four basic prefab types expose only `SpawnSound` and `SpawnSoundRadius` authoring fields.
- Runtime root definitions carry copied spawn clip/radius plus resolved `SkillSoundIds`.
- Root slots register sounds during compile/recompile and enqueue `SoundCategory.Cast` events on actual fire.
- Player-faction cast priority is strictly higher than mob-faction priority, derived from existing faction field.
- `SkillDriver.OnEnable` resolves serialized root then `AudioRoot.Instance`; missing runtime root logs once and remains silent.

## Relevant Global Context
- Authoring chain mirrors VFX: prefab -> definition -> compiler -> runtime definition -> stable registry id.
- Clips do not belong on `Skill` ScriptableObject.
- `BasicAttackPrefab` is Sim-owned and may use `AudioClip` without new assembly references.
- Register at setup/recompile, never at cast time.
- Root slots only; no recursive registration and no ECS lane.
- Emit after `SpawnBlocked` guard, in actual spawn branch next to `SkillSpawnTranslator.Spawn`, before `ResetOnFire` is acceptable; same-frame refunded/blocked paths must not enqueue.
- Event position is caster transform XY; velocity `default`; radius copied; category `Cast`.
- Missing `AudioRoot` cannot throw from `Tick`.
- Avoid unexpected error logs in existing EditMode construction tests; setup error must describe runtime scene misconfiguration once without breaking `ProjectileContinuousAuthoringEditModeTests` no-root regression.

## Dependencies Confirmed
- Task 002 complete: `AudioRoot.Enqueue`, `Register`, `SkillSoundIds`, batch drain, culling/ranking/playback/counters exist.
- `SkillDriver` already imports audio namespace and owns serialized `AudioRoot` field from task 001.
- No current sound producer exists.
- All four authoring classes and three compiler branches exist.
- Sim asmdef remains baseline.

## Step-By-Step Instructions
1. Add `[SerializeField] AudioClip spawnSound`, `[SerializeField, Min(0f)] float spawnSoundRadius`, and public getters to all four prefab classes beside spawn VFX (or core visual fields for projectile). Add no other sound slots.
2. Add null-safe `SpawnSound` / `SpawnSoundRadius` definition pass-throughs: projectile, abstract AOE base plus both concrete AOE definitions, and targeted.
3. Add runtime base properties for copied authored `AudioClip SpawnSound`, `float SpawnSoundRadius`, and resolved `SkillSoundIds SoundIds` as required by explicit compiler/register chain.
4. Copy spawn sound and radius in projectile, AOE, and targeted `SkillSetCompiler` branches.
5. Add non-recursive `RegisterSounds` over compiled root slots and call from `CompileAndRegister` beside existing registration passes. Same clip must retain id across recompiles.
6. Resolve `audioRoot` in `OnEnable` from serialized field or `AudioRoot.Instance`; no scene scan. Report one clear runtime setup error if unresolved, while preserving existing EditMode no-root tests.
7. Enqueue only after blocked checks and only on actual root cast. Guard `SpawnId > 0 && audioRoot != null`. Use explicit float2 position, default velocity, copied radius, category Cast, and faction-derived priority with player > mob.
8. Add end-to-end EditMode authoring-chain tests: authored clip produces id > 0, stable across recompile, radius reaches runtime snapshot, null prefab yields id 0 without throw. Cover four prefab properties as practical without broadening.
9. Static/search/diff validation only. Do not run Unity tests or Unity runner.

## Acceptance Criteria
- No clip/prefab/null path throws; missing clip/id remains 0 and emits nothing.
- Same clip id stable across loadout recompilation.
- All four prefab types can author spawn sound/radius; no extra slots.
- Sim asmdef unchanged.
- Blocked or same-frame refunded cast emits no sound.
- Player event priority > mob event priority.
- `audioRoot` field is used; missing root logs once at runtime and Tick stays silent/safe.
- Holding fire remains continuous but subject to AudioRoot thinning/spacing.
- Existing `ProjectileContinuousAuthoringEditModeTests` no-root scenario remains valid.

## Validation Required
- Static trace of all three compiler branches and four prefab/definition chains.
- Search confirms only root-level sound registration/emission; no recursive/ECS lane.
- Inspect blocked/emission ordering and faction-priority mapping.
- `git diff --check`; Sim asmdef baseline hash.
- Do not run Unity tests. Log user request: EditMode `SkillCastSoundEditModeTests` and regression `ProjectileContinuousAuthoringEditModeTests`; combined export `Logs/TestResults-EditMode-SkillSoundEvents.xml`.
- Generated-project compilation remains unavailable for unrelated Unity package/analyzer failures; report honestly.

## Hard Boundaries
- Do not modify files outside allowed list except imports directly required by task.
- Do not add hit/expire/pulse/arming sounds, ECS sound lane, recursive child registration, or public per-call play API.
- Do not put sounds on `Skill` ScriptableObject.
- Do not add serialized priority configuration.
- Do not implement task 004 docs.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
