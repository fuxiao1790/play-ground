# Task Execution Packet

## Task
001-skill-mana-cost-stat-fold.md

## Goal
Rename the authored spawn energy cost to folded mana cost and store the result in runtime skill definitions.

## Files Allowed To Modify
- Assets/Scripts/Skills/Modifiers/SkillStat.cs
- Assets/Scripts/Skills/SkillDefinition.cs
- Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs
- Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs
- Assets/Scripts/Skills/SkillSetCompiler.cs

## Behavior To Preserve
- Existing serialized asset values and current raw child cost access until task 003.

## Behavior To Change
- Fold mana cost at compile time into runtime definitions.

## Dependencies Confirmed
- None.

## Acceptance Criteria
- `SkillStat.ManaCost` exists.
- Runtime values equal authored values with identity modifiers.
- Project compiles.

## Validation Required
- Focused compiler tests or build check.

## Hard Boundaries
- Do not implement trigger conversion or supports in this task.
