---
name: energy-gain-modifier-tests
description: EditMode coverage for the new energy-gain stat sheet fields and their fold into compiled interval-trigger EnergyPerSecond
---

# 003 — Energy Gain Modifier Tests

## Scope

Add EditMode tests covering the new fields end to end: `UnitStatSheet` →
`SkillStatSnapshot` conversion, and the resolved `EnergyPerSecond` at both
interval-trigger compile sites. Depends on
[001-player-energy-gain-stat-fields.md](001-player-energy-gain-stat-fields.md)
and [002-interval-trigger-energy-gain-fold.md](002-interval-trigger-energy-gain-fold.md).

## Changes

### `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`

Add a test mirroring `UnitStatSheetRateIncreaseUsesAuthoredPercentPoints`
([ModifierFoldEditModeTests.cs:162-182](../../Assets/Tests/EditMode/ModifierFoldEditModeTests.cs#L162-L182)) —
same `CreateAsset<UnitStatSheet>` + `SetField` pattern used there — asserting:

- `SetField(sheet, "increasedEnergyGainPercent", 20f)` →
  `snapshot.IncreasedEnergyGainPercent == 0.2f`.
- `SetField(sheet, "baseEnergyGain", 1.5f)` → `snapshot.BaseEnergyGain == 1.5f`.
- `SetField(sheet, "energyGainMultiplier", 2f)` →
  `snapshot.EnergyGainMultiplier == 2f`.
- A freshly-created, all-defaults `UnitStatSheet` resolves
  `EnergyGainMultiplier == 1f` (regression guard for the mob
  `CreateInstance` field-initializer requirement from task 001).

### `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

Extend or add a sibling to `CompilerMapsProjectileEnergyRateAndCost`
([SkillValidationEditModeTests.cs:279-301](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L279-L301))
using a non-identity `SkillStatSnapshot` (not `SkillStatSnapshot.Identity`)
with known `BaseEnergyGain`/`IncreasedEnergyGainPercent`/`EnergyGainMultiplier`
values, and assert `setup.EnergyPerSecond` equals the hand-computed fold
result. Do the same for the AOE-interval sibling
([:303-330](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L303-L330)).

Keep the two existing `Identity`-based tests exactly as they are — they are
the backward-compatibility regression guard (see task 002's acceptance
criteria); do not weaken or delete them.

### Direct fold test (either file)

One small test calling `IntervalSpawnTrigger.ResolveEnergyPerSecond` directly
against a hand-built `SkillStatSnapshot` (via the constructor from task 001),
asserting the exact arithmetic — e.g. `energyPerSecond = 2f`,
`BaseEnergyGain = 1f`, `IncreasedEnergyGainPercent = 0.5f`,
`EnergyGainMultiplier = 2f` → `9f` (matches the worked example in task 002's
acceptance criteria). This isolates the fold formula from compiler plumbing.

## Acceptance Criteria

- New tests pass; all pre-existing EditMode tests in both files continue to
  pass unmodified, including the two `Identity`-based `EnergyPerSecond`
  assertions.
- At least one test exercises `UnitStatSheet` → `SkillStatAggregator.Aggregate`
  → `SkillStatSnapshot` for each of the 3 new fields individually.
- At least one test exercises the full compile path (`SkillSetCompiler.Compile`)
  producing a non-default `EnergyPerSecond` on both `RuntimeChildSpawnSetup`
  and `RuntimeAoeIntervalSpawnSetup`.

## Dependencies

Depends on 001 and 002.
