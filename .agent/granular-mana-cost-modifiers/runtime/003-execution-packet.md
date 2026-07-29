# Task Execution Packet

## Task

003-mana-cost-modifier-tests.md

## Goal

Add seven focused EditMode tests for support-side mana cost modifiers and the new trigger-link factor.

## Files Allowed To Modify

- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Existing `SkillValidationEditModeTests.cs`, modified support classes, compiler, trigger classes, and test helpers.

## Behavior To Preserve

- All existing SkillValidation and ModifierFold test behavior.
- Tests should exercise public/real compiler behavior, without runtime test hooks.

## Behavior To Change

- Add exactly the seven specified cases: added, increased, multiplier, full support fold, no-mana support, triggered increased factor, and interval increased factor.

## Relevant Global Context

- Task 001 completed: per-stat interfaces dispatch support contributions via the existing accumulator.
- Task 002 completed: trigger links resolve increased percent and multiplier to one factor.
- Support stat modifiers are set-scoped; tag mismatch does not block stat modifier collection.

## Dependencies Confirmed

- `IManaModifiers` and the migrated supports exist.
- `TriggerLink.manaCostIncreasedPercent` and `ResolveManaCostFactor()` exist.
- `RuntimeSkillDefinition.IncomingManaCostFactor` has replaced the old property.

## Step-By-Step Instructions

1. Follow existing local test setup and cleanup patterns.
2. Add the seven exact task cases using real supports/triggers and specified numeric assertions.
3. Do not alter production files or existing test infrastructure.

## Acceptance Criteria

- All seven named behavior cases are covered.
- Existing tests remain unchanged/passing.
- Tests prove public compiled values and trigger/interval costs rather than implementation details.

## Validation Required

- Static check of seven test additions and test compilation-oriented inspection.
- Run relevant Unity EditMode tests when project access permits.

## Hard Boundaries

- Do not update documentation or assets.
- Do not alter task 001's rewritten fold tests.
- Do not add test-only runtime state.
