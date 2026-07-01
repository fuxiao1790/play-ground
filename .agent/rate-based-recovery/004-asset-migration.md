# 004 — Asset data migration

## Goal
Rewrite serialized asset data to the new fields/values so Unity does not silently
drop the removed fields to defaults.

## Skill assets (31 files under `Assets/ScriptableObjects/Skills/Skill/`)
Transform each `baseRecoveryTime: <t>` line to `baseRate: <1/t>`. This is a value
inversion, not just a key rename. Reciprocals of the current values:

| Asset | old `baseRecoveryTime` | new `baseRate` |
|---|---|---|
| Arrow2LargeSkill | 0.05 | 20 |
| Arrow2Skill | 0.5 | 2 |
| Arrow2StackingAoeSkill | 0.5 | 2 |
| ArrowLargeSkill | 0.15 | 6.666667 |
| ArrowSkill | 0.3 | 3.333333 |
| BasicAoeSkill | 0.45 | 2.222222 |
| BenchmarkArrow2ChildSkill | 0.5 | 2 |
| BenchmarkArrow2ChildStackingAoeSkill | 0.5 | 2 |
| BenchmarkArrow2RootSkill | 0.15 | 6.666667 |
| BenchmarkArrowChildSkill | 0.5 | 2 |
| BenchmarkArrowChildStackingAoeSkill | 0.5 | 2 |
| BenchmarkBulletRootSkill | 0.15 | 6.666667 |
| BenchmarkChildSkill | 0.5 | 2 |
| BenchmarkChildStackingAoeSkill | 0.5 | 2 |
| BenchmarkProjectileSpamStackingAoeSkill | 0.05 | 20 |
| BenchmarkRootSkill | 0.1 | 10 |
| BenchmarkSplinterChildSkill | 0.5 | 2 |
| BenchmarkSplinterChildStackingAoeSkill | 0.5 | 2 |
| BenchmarkSplinterRootSkill | 0.2 | 5 |
| BulletLargeSkill | 0.25 | 4 |
| BulletSkill | 0.2 | 5 |
| LingeringAoeSkill | 0.25 | 4 |
| LingeringAoeSkillLarge | 0.05 | 20 |
| LingeringAoeStackingAoeSkill | 0.25 | 4 |
| SplinterLargeSkill | 0.15 | 6.666667 |
| SplinterSkill | 0.1 | 10 |
| SplinterStackingAoeSkill | 0.1 | 10 |
| StackingAoeDetonationSkill | 0.05 | 20 |
| StarChildSkill | 0.2 | 5 |
| StarChildStackingAoeSkill | 0.2 | 5 |
| StarParentSkill | 0.15 | 6.666667 |

Notes:
- Many are triggered-only/child skills whose `RecoveryTime` is never read (only root
  slots feed `SkillSlotState`). Migrate them anyway so the serialized field matches
  the new class field and no stale `baseRecoveryTime` key lingers.
- Prefer editing the YAML directly (each file has one `baseRecoveryTime:` line). A
  scripted pass (`rate = 1 / time`) is acceptable; verify the field key is renamed to
  `baseRate`, not left as `baseRecoveryTime`.

## Support asset `Assets/ScriptableObjects/Supports/IncreasedRecoverySpeedSupport.asset`
- Field: `recoverySpeedMultiplier: 1.5` → `increasedRatePercent: 0.5` (multiplier `1.5`
  ≡ increased `+0.5`).
- Optionally rename the asset file to `IncreasedRateSupport.asset` for tidiness (rename
  the `.asset` + its `.meta` together to keep the asset GUID). The `m_Script` GUID
  inside the asset is unchanged by task 002's script rename, so binding is preserved
  regardless of the asset filename.

## Script file rename bookkeeping (from 002)
- Confirm `IncreasedRecoverySpeedSupport.cs.meta` was renamed to
  `IncreasedRateSupport.cs.meta` with its `guid:` intact.

## Acceptance
- No `baseRecoveryTime:` or `recoverySpeedMultiplier:` keys remain under
  `Assets/ScriptableObjects/`.
- Each skill asset's `baseRate` equals the reciprocal of its old time.

## Dependencies
- 001, 002 (new field names must exist before assets reference them).

## Scope
Medium (mechanical, 32 assets). Verify field keys, not just values.
