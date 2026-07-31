# Task Execution Packet

## Task
002-interval-trigger-energy-gain-fold.md

## Goal
Resolve interval-trigger `EnergyPerSecond` from the player snapshot's base, increased, and multiplier energy-gain terms.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`

## Behavior To Preserve
- The `0.01f` energy floor.
- Trigger mana-to-energy threshold calculation and all other runtime setup fields.
- Identity snapshot behavior: raw authored energy rate with existing floor.

## Behavior To Change
- Both interval spawn setup types use `(authoredEnergy + BaseEnergyGain) * (1 + IncreasedEnergyGainPercent) * EnergyGainMultiplier`, floored to `0.01f`.

## Relevant Global Context
- Energy gain is per trigger edge. Resolve it locally, not through `SkillStat` or `StatModifierAccumulator`.
- The compiler methods already receive `snapshot`; introduce no new plumbing or representation.

## Dependencies Confirmed
- Task 001 complete: `SkillStatSnapshot.BaseEnergyGain`, `IncreasedEnergyGainPercent`, and `EnergyGainMultiplier` are present and aggregate from `UnitStatSheet`.

## Step-By-Step Instructions
1. Add `ResolveEnergyPerSecond(SkillStatSnapshot)` next to `ManaToEnergyCost`, using the exact specified fold and floor.
2. Replace only the `EnergyPerSecond` assignment in `ApplyChildSpawn`.
3. Replace only the `EnergyPerSecond` assignment in `ApplyAoeIntervalSpawn`.
4. Do not touch `EnergyThreshold` or any other setup fields.

## Acceptance Criteria
- Both compiler sites call the trigger resolver.
- Identity preserves raw energy plus floor.
- Example `(2 + 1) * 1.5 * 2` resolves to `9f`.
- Threshold and other setup values remain unchanged.

## Validation Required
- Static search for both resolver call sites and absence of the two old raw formulas.
- Run relevant compile or EditMode validation when available.

## Hard Boundaries
- Do not add enum cases, accumulator changes, supports, tests, or docs in this task.
