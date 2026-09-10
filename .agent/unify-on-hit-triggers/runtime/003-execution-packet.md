# Task Execution Packet

## Task

003-test-migration.md

## Goal

Migrate deleted on-hit trigger test sites to `OnHitTrigger`; add AOE-source projectile-target and effect-owned projectile-burst coverage.

## Files Allowed To Modify

- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Existing test helpers and nearby compiler tests; `SkillSetCompiler`; runtime definitions.

## Behavior To Preserve

- Existing test assertions, mana-factor setup, independent trigger instances, and stack behavior.

## Behavior To Change

- Tests name/use `OnHitTrigger`; new assertions cover AOE-to-projectile attach and child-owned count/spread.

## Relevant Global Context

- Task 001 source changes complete. On-hit accepts Projectile/Aoe source and any target, stamping mana only after attachment.

## Dependencies Confirmed

- `OnHitTrigger` exists and compiler has `AttachOnHitTarget`.

## Step-By-Step Instructions

1. Substitute all ten old trigger constructions with `OnHitTrigger` and rename specified validator test.
2. Add compiler test for AOE source to projectile target.
3. Add compiler test ensuring projectile child count/spread come from its effect set.

## Acceptance Criteria

- No deleted trigger names remain under `Assets/Tests/`.
- Existing assertions preserved; two new EditMode cases present.

## Validation Required

- Static search/source inspection now. User must run EditMode and PlayMode suites and export required XML before pass status.

## Hard Boundaries

- Only task-listed tests; do not alter runtime behavior or test framework setup.
