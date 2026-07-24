# Implementation Log

## Status

Implementation complete under the revised Assets/Scripts asmdef layout. Unity compile, test-suite, and BenchmarkLarge smoke validation remain user-pending because the interactive editor owns the project.

> Placement correction: the user replaced the embedded-package plan. Sim, GameLogic,
> SkillUi, and Debugging now live as asmdefs under `Assets/Scripts/`; all temporary
> `Packages/com.playground.*` folders and manifest/lock entries were removed.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-sever-sim-to-gamelogic-edges.md | Complete | Removed unused Skills import, dead AOE config cache, and dead registration API. Static checks and Unity headless compile passed. |
| 002-relocate-aoeconfig-authoring.md | Complete | Moved AoeConfig plus .meta to Skills/Authoring, changed namespace/imports, and preserved GUID. Static acceptance checks passed; Unity recompile deferred because the project editor was already open and batch validation instances blocked. |
| 003-split-common.md | Complete | Verified the three sim primitives are plain and that simulation does not use Common stats, status effects, or persistent authoring. No source change belongs to this task. |
| 004-create-sim-package.md | Complete | Created `com.playground.sim` and PlayGround.Sim; moved the System tree and three shared primitives with GUIDs preserved. |
| 005-create-gamelogic-package.md | Complete | Created `com.playground.game-logic` and moved all assigned game-logic files with GUIDs preserved. |
| 006-retire-runtime-repoint-asmdefs.md | Complete | Deleted legacy Runtime asmdef and repointed UI, Editor, and test consumers. |
| 007-verify-and-document.md | Complete | Static boundary guardrails and docs updated; Unity validation is user-pending. |
| 008-invert-stats-display-coupling.md | Complete | Existing code now removes simulation push and lets PerformanceText pull CombatStatsSingleton from the ECS world. Static acceptance checks passed. |
| 009-create-debugging-package.md | Complete | Created the exempt Debugging leaf and moved debug scripts with GUIDs preserved. |

## Completed Tasks

- 001-sever-sim-to-gamelogic-edges.md: changed `Assets/Scripts/System/Core/CombatRoot.cs`; `RegisterConfig|configTypeIds` search returned no matches; `git diff --check` and Unity 6000.4.8f1 headless compile passed.
- 002-relocate-aoeconfig-authoring.md: moved `AoeConfig.cs` and its `.meta` to `Assets/Scripts/Skills/Authoring/`; updated `StackingTriggerDef.cs`. The GUID remained `61193c22c38d448ba6e3c5bca6b9be5f`; both forbidden-import searches returned no matches.
- 003-split-common.md: verified the package ownership classification with source searches; no source files changed.
- 004-create-sim-package.md: created `Packages/com.playground.sim`; removed the stale `.cs.delete`; moved 96 System metadata files (excluding prior AoeConfig relocation) plus the three Common primitive metadata files with zero missing or changed GUIDs.
- 005-create-gamelogic-package.md: created `Packages/com.playground.game-logic`; moved 101 game-logic metadata files plus AoeConfig with zero missing or changed GUIDs.
- 006-retire-runtime-repoint-asmdefs.md: removed PlayGround.Runtime; SkillUi now references GameLogic, and Editor/Test asmdefs now reference Sim plus GameLogic. Added both embedded packages to packages-lock.
- 009-create-debugging-package.md: created `Packages/com.playground.debugging`; moved Debugging scripts and folder metadata with zero changed GUIDs.
- 007-verify-and-document.md: added package boundary rules, updated layer paths and folder overview, and updated the stale SkillUi assembly-qualified scene/asset identifiers.

## Blockers

- None.

## Validation Summary

- 001: Passed static acceptance checks and Unity headless compile/import.
- 002: Passed static acceptance checks and `git diff --check`. Unity batch validation could not complete safely while the interactive editor held the project; two blocked batch instances were stopped. A direct `dotnet build` also failed only in Unity's Render Pipelines package cache (`PassesData.cs` CS8168/CS8347), before project runtime code compiled; it is not a task-code failure.
- 003: Passed source classification checks. Sim imports only the three designated primitives and none of `Common.Stats`, `Common.StatusEffects`, `UnitStatSheet`, or `PersistentScriptableObject`.
- 008: `PerformanceText` search under `Assets/Scripts/System` and `CombatStatsBinding` search under `Assets Packages` both returned no matches. User confirmed the pull-based direct ECS-world design.
- 004: PlayGround.Sim has no PlayGround assembly reference; System source is absent from Assets; stale delete file is absent; `git diff --check` passed. Unity import remains user-pending while the interactive editor is open.
- 005: GameLogic has the expected Sim reference and no SkillUi reference; metadata checks passed; `git diff --check` passed. Unity import remains user-pending while the interactive editor is open.
- 006: No asmdef references PlayGround.Runtime; packages-lock JSON parses with both embedded packages; `git diff --check` passed. Unity compile remains user-pending while the interactive editor is open.
- 009: Sim, GameLogic, and SkillUi asmdefs contain no Debugging reference; all four Debugging metadata GUIDs match; packages-lock JSON parses; `git diff --check` passed.
- 007: Static assembly graph is `SkillUi -> GameLogic -> Sim`; Sim has no game-logic upward import; core packages have no Debugging reference; no old SkillUi assembly identifier remains. Unity compile/tests/BenchmarkLarge and the deliberate temporary compile-failure sanity check were not run because the interactive editor owns the project.
