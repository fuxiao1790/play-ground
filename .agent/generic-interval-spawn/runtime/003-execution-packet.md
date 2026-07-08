# Task Execution Packet

## Task
003-update-validator.md

## Goal
Update `SkillLoadoutValidator.IsIntervalSpawnTrigger` to recognize only the merged `IntervalSpawnTrigger`.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

## Behavior To Preserve
- Pulse AOE interval source warning stays unchanged.
- Existing generic source/target tag warning path stays unchanged.

## Behavior To Change
- `IsIntervalSpawnTrigger` returns true for `IntervalSpawnTrigger`.
- Target-tag mismatch warnings no longer fire for projectile/AOE interval target combinations because the merged trigger targets both tags.

## Relevant Global Context
- Runtime/ECS boundary unchanged.
- Validator is authoring/loadout validation only.

## Dependencies Confirmed
- Task 001 complete: `IntervalSpawnTrigger` exists.

## Step-By-Step Instructions
- Replace `link is ProjectileIntervalSpawnTrigger or AoeIntervalSpawnTrigger` with `link is IntervalSpawnTrigger`.

## Acceptance Criteria
- Pulse AOE source warning remains reachable through `IsIntervalSpawnTrigger`.
- No old interval trigger type names remain in validator.

## Validation Required
- Search validator for old trigger names and merged helper expression.

## Hard Boundaries
- Do not modify compiler, tests, docs, or runtime/ECS files in this task.
