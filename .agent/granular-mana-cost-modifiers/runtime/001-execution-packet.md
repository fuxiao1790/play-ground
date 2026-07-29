# Task Execution Packet

## Task

001-mana-cost-modifier-supports.md

## Goal

Replace the flat generic modifier interfaces with seven stat-specific nested interface families, migrate all nine production supports, and rewrite obsolete generic-interface test helpers as direct accumulator tests.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Support/ModifierKindInterfaces.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- The nine support files explicitly listed by task 001.
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- The three obsolete helper classes and three factory methods inside `ModifierFoldEditModeTests.cs`.

## Files Likely Needed For Reading

- The allowed files and `Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs`.

## Behavior To Preserve

- Existing support fields and default resolved values.
- Existing accumulator fold formula and support iteration order.
- Projectile speed and lifetime contributions from `FasterProjectilesSupport`.

## Behavior To Change

- Supports dispatch through the ten specified nested interfaces instead of bare generic interfaces.
- Increased AOE/rate supports gain neutral `manaCostIncreasedPercent`; concentrated/faster supports gain neutral `manaCostMultiplier`.

## Relevant Global Context

- Use explicit interface methods wherever identical signatures modify different stats.
- No reflection, asset migration, or accumulator refactor.
- `StatModifierAccumulator` remains the sole support-side source of resolved stat values.

## Dependencies Confirmed

- None required; baseline code contains the old modifier interfaces and all listed supports.

## Step-By-Step Instructions

1. Replace `ModifierKindInterfaces.cs` exactly with task 001's seven families and unchanged behavior interfaces.
2. Replace compiler collection with its ten ordered `is` checks.
3. Migrate listed supports exactly as specified, preserving field ownership and adding neutral mana fields only where required.
4. Delete generic test helpers/factories and directly exercise `StatModifierAccumulator` in the two specified tests.

## Acceptance Criteria

- No bare modifier interfaces anywhere in `Assets/`.
- Exactly ten compiler interface checks.
- All nine supports use required stat-specific interfaces and preserve default behavior.
- The two accumulator formula tests retain their existing numeric assertions without test-only support helpers.

## Validation Required

- Static searches for obsolete interfaces and old helpers.
- Relevant Unity EditMode test run when possible.

## Hard Boundaries

- Do not modify files outside the allowed list except direct compile fixes.
- Do not implement trigger, new coverage, docs, or user editor verification tasks.
- Do not alter architecture or create additional abstractions.
