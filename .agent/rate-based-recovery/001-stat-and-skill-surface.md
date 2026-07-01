# 001 — Stat + Skill authoring surface

## Goal
Turn the exposed recovery-time/recovery-speed vocabulary into a rate.

## Changes

### `Assets/Scripts/Skills/Modifiers/SkillStat.cs`
- Rename enum member `RecoverySpeed` → `Rate`.

### `Assets/Scripts/Skills/Skill.cs`
- Replace the serialized field and accessor:
  - `[SerializeField, Min(0.01f)] private float baseRecoveryTime = 0.2f;`
    → `[SerializeField, Min(0.01f)] private float baseRate = 5f;`
  - `public float BaseRecoveryTime => baseRecoveryTime;`
    → `public float BaseRate => baseRate;`
- Default `5f` corresponds to the old default `0.2s` (`1 / 0.2`).
- Do **not** add `[FormerlySerializedAs("baseRecoveryTime")]` — the stored numbers
  change meaning (time→rate) and are inverted by task 004; importing the old value
  under the new field would be wrong.

## Downstream references to update (compile-driven, done in 003)
- `SkillSetCompiler.ResolveRecoveryTime` reads `set.Skill.BaseRecoveryTime` and
  `Resolve(SkillStat.RecoverySpeed, ...)` — updated in task 003.
- `IncreasedRecoverySpeedSupport` references `SkillStat.RecoverySpeed` — updated in
  task 002.

## Acceptance
- `SkillStat` has `Rate`, no `RecoverySpeed`.
- `Skill` exposes `BaseRate` (float, `Min(0.01)`, default `5`) and no
  `baseRecoveryTime`/`BaseRecoveryTime`.
- Project compiles once 002 and 003 land (this task alone breaks references — land
  001–003 together).

## Scope
Small. Two files. Coupled with 002/003 for a compiling build.
