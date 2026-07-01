# 005 — Tests

## Goal
Update the recovery tests to the rate model and to renamed types/fields.

## Global renames (both test files)
- Type `IncreasedRecoverySpeedSupport` → `IncreasedRateSupport` (declarations and
  `CreateAsset<…>`).
- `SetField(skill, "baseRecoveryTime", t)` → `SetField(skill, "baseRate", r)`.
- `SetField(support, "recoverySpeedMultiplier", 1.5f)` →
  `SetField(support, "increasedRatePercent", 0.5f)`.
- `new PlayerStatSnapshot(castSpeedMultiplier: x, …)` →
  `new PlayerStatSnapshot(increasedRatePercent: p, …)` (identity `p = 0`).

## `Assets/Tests/EditMode/CritEditModeTests.cs`

### `Compiler_UsesSkillBaseRecoveryTime` → `Compiler_DerivesRecoveryTimeFromBaseRate`
- `baseRate = 5`, `PlayerStatSnapshot.Identity`.
- Assert `result.RecoveryTime == 0.2` (`1 / 5`).

### `Compiler_StacksIncreasedRecoverySpeedSupport_Additively` → `Compiler_StacksIncreasedRateSupport_Additively`
- `baseRate = 5`; two `IncreasedRateSupport` each `increasedRatePercent = 0.5`
  (increased `+1.0` → `rate = 10`).
- Assert `result.RecoveryTime == 0.1`.

## `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`

### `RecoverySpeedFoldCombinesWithCastSpeedTimeMultiplier` → `RateFoldCombinesSupportAndPlayerIncreases`
- `baseRate = 2.5`; support `increasedRatePercent = 0.5`; snapshot
  `increasedRatePercent = 0.5` (increased `+1.0` → `rate = 5`).
- Assert `runtime.RecoveryTime == 0.2` (`1 / 5`), Within `0.0001`.
- Confirms the support increase and the player increase land in the **same** summed
  increased bucket (not multiplied).

## Acceptance
- All three tests compile against renamed types/fields and assert the derived
  `1 / rate` recovery.
- No test references `RecoverySpeed`, `baseRecoveryTime`, `castSpeedMultiplier`, or
  `recoverySpeedMultiplier`.

## Dependencies
- 001, 002, 003.

## Scope
Small. Two files, three tests.
