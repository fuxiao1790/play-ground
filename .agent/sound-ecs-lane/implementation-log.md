# Implementation Log

## Status
Implementation complete; Unity test and player-loop ordering evidence pending.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-sound-event-lane.md | Complete | Persistent lane, Burst helper, and transport-only presentation dispatcher added; Roslyn compile passed. |
| 002-ids-to-spawn-commands.md | Complete | Recursive registration and id/radius propagation through every command kind added; EditMode coverage added. |
| 003-emit-at-expansion.md | Complete | Spawn/arming emitters wired and old managed cast enqueue removed; Roslyn compile passed. |
| 004-docs.md | Complete | Sound contract and maintained runtime maps updated for the ECS lane. |

## Completed Tasks

- `001-sound-event-lane.md`
  - Added `SoundEventSingleton`, `SoundEmit`, and `SoundEventDispatchSystem` with Unity metadata.
  - Dispatcher completes/reset producer dependencies, drains through a reused scratch list, clears a missing-root frame, and owns queue disposal.
  - Selection and playback remain entirely in `AudioRoot`.
- `002-ids-to-spawn-commands.md`
  - Made `SkillDriver.RegisterSounds` recursive across interval, impact/on-hit, targeted, and stacking edges.
  - Added sound id/radius storage beside AOE and targeted VFX registry data.
  - Added `SoundIds` and `SpawnSoundRadius` to projectile, AOE, and targeted commands and all skill template builders.
  - Added `SkillSoundRecursiveRegistrationEditModeTests` for nested own ids, shared-id dedupe, stability, and null sound.
- `003-emit-at-expansion.md`
  - Projectile, AOE, targeted, and armed-AOE paths enqueue `SoundCategory.Spawn` through the native lane.
  - Armed AOEs persist sound id/radius in `AoeIdentityComponent` and emit only when arming completes.
  - Removed the `SkillDriver.Tick` managed enqueue, preventing root double sound and pre-gate sound.
- `004-docs.md`
  - Updated the sound contract, canonical lane list, presentation inputs/modules, skill authoring map, folder map, and completed ECS-sound TODO.
  - Documentation explicitly records that presentation-versus-`LateUpdate` order still needs profiler evidence.

## Blockers

- No code blocker.
- Runtime evidence outstanding: confirm `SoundEventDispatchSystem` versus `AudioRoot.LateUpdate` order in the Unity Profiler. If dispatch is later, ECS events intentionally enter the next managed batch until evidence justifies a pull-based drain.
- Unity tests were not run per project rule; XML evidence is not yet available.

## Deviations

- Task 003 named `CombatArmingSystem` as a deferred emitter but did not name storage for the id/radius after materialization. The implementation stores those unmanaged values on existing `AoeIdentityComponent` and stamps them in `AoeSpawnApplyUtility`; no new owner or managed reference was introduced.
- The referenced historical `.agent/burst-onupdate-work/index.md` is absent. The native-lane safety reasoning is self-contained in maintained docs instead of adding a broken link.

## Validation Summary

- Unity Roslyn response-file compiles passed for `PlayGround.Sim`, `PlayGround.GameLogic`, and `PlayGround.Tests.EditMode` with new sources supplied explicitly where stale response files omitted them.
- `git diff --check` passed.
- `PlayGround.Sim.asmdef` SHA256 remains `5E3F95641A348ADD80D3291E501F682838C7FE2BB4BBDE10810A75620A99549E`.
- Static search finds no managed cast-site `audioRoot.Enqueue`; all current simulation emits go through `SoundEmit`.
- Maintained docs contain no statement that sound is root-only or that no native lane exists.
- Required user run: EditMode classes `AudioClipRegistryEditModeTests`, `AudioRootSelectionEditModeTests`, `SkillCastSoundEditModeTests`, and `SkillSoundRecursiveRegistrationEditModeTests`; export `Logs/TestResults-EditMode-SoundEcsLane.xml`.
