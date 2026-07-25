# Task Execution Packet

## Task
002-supports-contribute-mana-cost.md

## Goal
Make named supports add a flat `SkillStat.ManaCost` modifier.

## Files Allowed To Modify
- MultipleProjectilesSupport.cs
- PiercingSupport.cs
- HomingSupport.cs
- MultipleAoesSupport.cs
- AddedDamageSupport.cs
- Directly affected tests.

## Relevant Global Context
- Mana cost folds once during `SkillSetCompiler.BuildRuntime`.
- Use existing `IBaseValueModifier` modifier sinks.

## Dependencies Confirmed
- `SkillStat.ManaCost` exists and runtime definitions resolve it.

## Acceptance Criteria
- Participating child supports add exactly their authored flat mana costs.
- Defaults of zero remain behavior-neutral where specified.

## Validation Required
- Focused compiler test when Unity project lock allows it; static check otherwise.

## Hard Boundaries
- Do not alter interval conversion; task 003 does that.
