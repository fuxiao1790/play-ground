# Implementation Log

## Status
Code complete (001-008). Task 009 is a handoff message to the user, not code.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-skills-authoring-and-compiler.md | Complete | Deleted OnHitTrigger.cs; SkillSetCompiler.cs (branch+AttachOnHitTarget+mana recursion), SkillLoadoutCompiler.cs (warnings), 3x Runtime*Definition.cs fields |
| 002-skilldriver-registration-walks.md | Complete | Removed all 5 Register*Recursive on-hit branches, 3x BuildOnHitSpawnRef overloads + call sites, onHitSpawn params from SkillIntervalTemplateBuilder's 3 Build*Template methods |
| 003-spawning-core-types.md | Complete | Deleted OnHitSpawnRef struct; shrunk SpawnTemplateRefEmit Acquire/Release x4 + EmitProjectile/EmitAoe; deleted EnsureValidChildKind(OnHitSpawnRef) overload; fixed stale comment |
| 004-projectile-pipeline.md | Complete | ProjectileHitComponent/ProjectileHitPayload/ProjectileSpawnRequest/ProjectileChildSpawnConfig fields dropped; both collision systems' EnqueueOnHitSpawn+4 writer lanes removed; ProjectileDiscreteSpawnApplySystem wiring fixed. Deviation: also fixed CombatLifetimeSystem.cs's ProjectileLifetimeJob (missed by initial file inventory, caught as compile-fix dependency of task 003) |
| 005-aoe-pipeline.md | Complete | Deleted AoeHitSpawnComponent; shrunk AoeSpawnRequest/AoeSpawnCommand; AoeCollisionCore EnqueueOnHitSpawn+writer params removed (also removed now-dead HashId/DirectionFromTo/salt consts); LingeringAoeCollisionSystem/ImpactAoeCollisionSystem query+handles+writers fixed; AoeSpawnApplySystem archetypes/WriteCommon/NeedsCollision/HitSpawnFor fixed |
| 006-targeted-and-combatroot.md | Complete | TargetedSpawnCommand field dropped; CombatRoot.cs AoeCommandFor + 3x SpawnTemplateFor overloads fixed |
| 007-tests.md | Complete | Deleted ~14 dedicated on-hit test methods across TargetedSkillPlayModeTests, ProjectileContinuousSimulationTests, ProjectileCollisionSimulationTests, AoeSimulationTests, AoePlayModeTests, SkillValidationEditModeTests (11 methods); fixed helper signatures (CreateContinuousProjectile, CreateProjectile, SpawnCircle) and 2 archetype defs (CombatPoolCleanupSystemTests PlayMode, AoeSimulationTests); swapped OnHitTrigger→IntervalSpawnTrigger as generic fixture in ClearSkillKeepsItsAdjacentTriggers (unrelated to on-hit semantics); adapted SkillSoundRecursiveRegistrationEditModeTests to drop the on-hit cyclic-recursion case while preserving its shared-clip-id/idempotent-registration coverage. `grep -rn "OnHitTrigger\|OnHitSpawnRef\|..." Assets/Tests/` returns nothing. |
| 008-docs.md | Complete | Removed OnHitTrigger section + compile pseudocode branch from skill-system.md; on-hit mention from skill-gameplay-system.md; file entry from folder-structure.md; component/registry-rule/checklist entries from spawn-template-registry.md; contract line from skill-runtime-snapshots.md; whole "Projectile Burst From AOE" section + component entry from aoe-system.md |
| 009-editor-steps-handoff.md | Ready to relay | Informational only — no code. Relayed to user in this turn's summary. |

## Completed Tasks
- 001–006: `grep -rn "OnHitSpawn|OnHitTrigger|AoeHitSpawnComponent|Impact.*Definition|OnHit.*SpawnDefinition" Assets/Scripts/` returns nothing — source-code removal is complete across Skills, SkillDriver, Spawning core, Projectiles, Aoes, Targeted, and CombatRoot.

## Blockers
- none

## Validation Summary
- Search-based verification only (grep), per each task file's Validation section. No compile check
  or test run performed — both are deferred to the user per project convention.
- Final case-insensitive `grep -ri "OnHitSpawn" Assets/` (broader than any single task's own
  validation pattern) caught one more site outside the original file inventory:
  `Assets/Tests/EditMode/TargetedContractsEditModeTests.cs` asserts `TargetedSpawnCommand`'s exact
  field set via reflection (`GetFields(BindingFlags.Instance | BindingFlags.Public)`) and had
  `"OnHitSpawn"` as a literal string in the expected array — missed by the `OnHitTrigger`/
  `\.OnHitSpawn\b`-anchored patterns used elsewhere since this was a bare string, not a member
  access. Fixed. Recommend the user re-run this exact broad grep after any future field rename in
  this area, since reflection-contract tests don't show up in a symbol-reference search.
- Final `grep -ri "OnHitSpawn" Assets/` and `grep -rn "OnHitTrigger|...|OnHitTargetedSpawnDefinition" ` (project-wide) both return nothing except this plan's own files and the still-pending `OnHitTrigger.asset` (task 009).
- Deviation: `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs` was missed by initial file
  inventory in both task 004 and 005; caught mid-implementation as a compile-fix dependency of task
  003's `SpawnTemplateRefEmit` signature shrink (it called `ReleaseProjectile`/`ReleaseAoe` with the
  old signatures). Fixed in both halves (`ProjectileLifetimeJob`, `AoeLifetimeJob`) during task 004.
  Both plan files (004, 005) updated to record this file going forward.
