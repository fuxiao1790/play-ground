# Task Execution Packet

## Task
001-unit-stat-sheet-type.md

## Goal
Create the authored ScriptableObject that holds a unit's combat stats.

## Files Allowed To Modify
- None.

## Files Allowed To Create
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillStatSnapshot.cs`
- `Docs/coding-standards.md`

## Behavior To Preserve
- No existing runtime behavior changes in this task.
- Common layer must not depend on Skills.

## Behavior To Change
- Add a new `UnitStatSheet` asset type with clamped read-only properties.
- Add internal runtime configuration for code-created clones.

## Relevant Global Context
- `UnitStatSheet` owns immutable authored base stats.
- Authored assets are not mutated at runtime; runtime clone mutation is allowed.
- Offense defaults match `SkillStatSnapshot.Identity`.

## Dependencies Confirmed
- No prerequisite task dependencies.
- `SkillStatSnapshot.Identity` currently uses `(0f, 1f, 0f, 1.5f, 1f)`.

## Step-By-Step Instructions
- Create `Assets/Scripts/Common/Stats/UnitStatSheet.cs`.
- Namespace: `PlayGround.Common.Stats`.
- Add `[CreateAssetMenu(menuName = "PlayGround/Units/Unit Stat Sheet")]`.
- Add serialized fields for vitals, movement, and offense with specified defaults.
- Add clamped getters.
- Add `internal void SetRuntimeValues(float maxHealth, float moveSpeed)`.

## Acceptance Criteria
- Compiles.
- Asset creatable via `Create > PlayGround > Units > Unit Stat Sheet`.
- Getters clamp as specified.

## Validation Required
- Source inspection/search.
- Compile/build if available.

## Hard Boundaries
- Do not modify files outside the allowed list except compile fixes directly caused by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.
