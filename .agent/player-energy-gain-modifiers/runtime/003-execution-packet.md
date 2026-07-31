# Task Execution Packet

## Task
003-energy-gain-modifier-tests.md

## Goal
Add EditMode coverage for stat-sheet-to-snapshot energy fields, direct interval energy folding, and non-identity compile results for projectile and AOE interval triggers.

## Files Allowed To Modify
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Both allowed test files.
- `UnitStatSheet.cs`, `SkillStatSnapshot.cs`, `IntervalSpawnTrigger.cs`, and the two compiler methods.

## Behavior To Preserve
- Keep existing Identity-based projectile and AOE energy tests exactly as they are.
- Tests use existing asset creation, reflection `SetField`, and cleanup patterns.

## Behavior To Change
- Cover all three snapshot fields individually, the `1f` default multiplier, direct formula arithmetic, and both compiler paths with non-neutral snapshots.

## Relevant Global Context
- Snapshot fields are base `0f`, increased as a normalized fraction, and multiplier `1f`.
- Exact fold: `(energyPerSecond + BaseEnergyGain) * (1f + IncreasedEnergyGainPercent) * EnergyGainMultiplier`, floor `0.01f`.
- Trigger threshold is separate and must remain covered by unchanged Identity tests.

## Dependencies Confirmed
- 001: all three snapshot fields and aggregate wiring exist.
- 002: resolver exists and both compiler sites call it.

## Step-By-Step Instructions
1. In `ModifierFoldEditModeTests`, add a stat-sheet aggregate test that reflection-sets 20, 1.5, and 2 and asserts normalized/inherited snapshot values plus a fresh sheet multiplier of 1.
2. Add a direct resolver test against a hand-built snapshot where 2, 1, .5, 2 resolves to 9.
3. In `SkillValidationEditModeTests`, add non-identity compile-path coverage for projectile-child and AOE-child interval setups; assert hand-computed energy result on both.
4. Leave existing Identity tests intact.

## Acceptance Criteria
- At least one aggregate test covers all three fields.
- Direct formula test evaluates exactly to 9.
- Both runtime setup types receive a non-default resolved rate through `SkillSetCompiler.Compile`.
- Existing identity tests remain unchanged.

## Validation Required
- Run the focused EditMode test classes if Unity becomes available; otherwise document the active-editor limitation.
- Static inspect compilation syntax and unchanged existing tests.

## Hard Boundaries
- Do not modify production code, docs, test infrastructure, or existing Identity tests.
