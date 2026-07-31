# Task Execution Packet

## Task
004-energy-gain-docs-update.md

## Goal
Document the three player energy-gain snapshot terms and their interval-trigger compile-time fold.

## Files Allowed To Modify
- `Docs/reference/game-logic/skill-system.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Docs/reference/game-logic/skill-system.md`
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

## Behavior To Preserve
- All Legacy sections and unrelated documentation remain unchanged.
- Existing documented Rate/Damage/AreaSize accumulator behavior remains clear.

## Behavior To Change
- List `baseEnergyGain`, `increasedEnergyGainPercent`, and `energyGainMultiplier` in the snapshot section.
- State these terms do not enter the per-skill accumulator.
- Explain trigger-local resolved energy formula and `UnitStatSheet` -> snapshot path after the existing interval trigger overview.

## Relevant Global Context
- Shipped formula: `Mathf.Max(0.01f, (energyPerSecond + snapshot.BaseEnergyGain) * (1f + snapshot.IncreasedEnergyGainPercent) * snapshot.EnergyGainMultiplier)`.
- Energy is per edge and resolved by `IntervalSpawnTrigger.ResolveEnergyPerSecond`, not a support-modifiable set stat.

## Dependencies Confirmed
- 001: final field names are `BaseEnergyGain`, `IncreasedEnergyGainPercent`, and `EnergyGainMultiplier`; authored fields use lower camel case.
- 002: final resolver and formula are present at both compiler sites.

## Step-By-Step Instructions
1. Add the three authored field names to the Layer 1.5 snapshot list.
2. Add the prescribed baking exception bullet after existing rate/damage/area bullets.
3. Directly after the existing interval trigger energy/mana paragraph, add a concise explanation with a fenced code formula and `UnitStatSheet`/snapshot path.
4. Verify markdown formatting and edit no other sections.

## Acceptance Criteria
- Names and formula exactly reflect code.
- Legacy and unrelated doc content is untouched.
- Formula is in a fenced code block and reads consistently with nearby fold documentation.

## Validation Required
- Inspect diff and use a search to confirm required terms/formula are documented only at targeted sections.

## Hard Boundaries
- Do not change source, tests, other docs, or legacy sections.
