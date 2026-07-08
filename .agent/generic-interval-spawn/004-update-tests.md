---
name: update-tests
description: Rename type usages in tests and remove the two tests whose premise the merge intentionally invalidates
---

# 004 — Update tests

## Scope

- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Changes

### EditMode (`SkillValidationEditModeTests.cs`)

Delete these two tests outright — their asserted behavior is the exact
restriction this task removes, not a regression to guard against:

- `ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill` ([:41-58](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L41-L58))
  — asserted a target-tag-mismatch warning for Projectile-trigger-onto-AOE.
  No longer a mismatch once merged.
- `CompilerIgnoresProjectileIntervalTargetAoeSkill` ([:60-88](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L60-L88))
  — asserted the compiler no-ops for the same combo. After the merge this
  combo now populates `AoeIntervalSpawnSetup`, which is already covered by
  `CompilerPopulatesAoeIntervalSetupOnProjectileSource` (renamed below).

Rename type usages (no assertion changes) in the remaining tests:

- `CompilerConvertsProjectileIntervalJitterPercentToSeconds` ([:90-123](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L90-L123))
  — `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `CompilerPopulatesAoeIntervalSetupOnProjectileSource` ([:125-162](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L125-L162))
  — `AoeIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `CompilerPopulatesProjectileIntervalSetupOnLingeringAoeSource` ([:164-195](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L164-L195))
  — `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `CompilerPopulatesAoeIntervalSetupOnLingeringAoeSource` ([:197-228](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L197-L228))
  — `AoeIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `CompilerLeavesPulseAoeIntervalSourceAsNoOp` ([:230-260](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L230-L260))
  — `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `ValidatorWarnsWhenIntervalSpawnSourceIsPulseAoe` ([:262-277](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L262-L277))
  — `ProjectileIntervalSpawnTrigger` -> `IntervalSpawnTrigger`.
- `ValidatorWarnsWhenAoeIntervalSpawnSourceIsPulseAoe` ([:279-294](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L279-L294))
  — `AoeIntervalSpawnTrigger` -> `IntervalSpawnTrigger`. Consider merging this
  with `ValidatorWarnsWhenIntervalSpawnSourceIsPulseAoe` since after the
  rename both tests do the exact same thing (create a pulse-AOE source, a
  merged trigger, and assert the same warning) — keep both only if you want
  belt-and-suspenders coverage of both target kinds; otherwise delete one as
  redundant.
- Any further usage around [:489](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L489)
  (stacking-support interplay test) — rename only, no assertion changes
  expected; re-check when editing since exact line numbers will drift once
  earlier tests are deleted.

### PlayMode (`AoePlayModeTests.cs`)

Pure rename, no assertion changes (both tests use
`ScriptableObject.CreateInstance<T>()` directly, not the `.asset` fixtures):

- `CompileAndRegisterAssignsDedupedIntervalTemplateKeys` ([:290-382](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L290-L382))
  — `triggerA`/`triggerB` declared as `ProjectileIntervalSpawnTrigger` ->
  `IntervalSpawnTrigger`.
- `LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires` ([:384+](../../Assets/Tests/PlayMode/AoePlayModeTests.cs#L384))
  — `projectileTrigger` (`ProjectileIntervalSpawnTrigger`) and `aoeTrigger`
  (`AoeIntervalSpawnTrigger`) both become `IntervalSpawnTrigger`. They remain
  two separate instances (one per source/target pair in the test) — only the
  declared type changes.

## Acceptance Criteria

- Both test assemblies compile with zero references to
  `ProjectileIntervalSpawnTrigger`/`AoeIntervalSpawnTrigger`.
- EditMode suite: user runs it in Unity (harness cannot run Unity) and
  confirms all interval-spawn tests pass, including that the merged trigger
  now succeeds (rather than warning/no-op) for the previously-mismatched
  target combo.
- PlayMode suite: user runs `CompileAndRegisterAssignsDedupedIntervalTemplateKeys`
  and `LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires` and
  confirms unchanged pass/fail status (pure rename, behavior identical).

## Dependencies

Depends on [001](./001-merge-trigger-type.md), [002](./002-collapse-compiler-dispatch.md),
[003](./003-update-validator.md) landing first.
