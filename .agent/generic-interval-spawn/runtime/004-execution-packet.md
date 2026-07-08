# Task Execution Packet

## Task
004-update-tests.md

## Goal
Update interval-spawn tests for the merged trigger type and remove two obsolete mismatch tests.

## Files Allowed To Modify
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Behavior To Preserve
- Existing compiler setup assertions for projectile children, AOE children, lingering source, pulse AOE no-op, and registry template behavior.
- Existing stack-trigger interplay assertions.

## Behavior To Change
- Remove tests that asserted projectile interval trigger cannot target AOE.
- Replace old trigger classes with `IntervalSpawnTrigger`.

## Relevant Global Context
- Merged trigger supports all projectile/AOE target combinations.
- Pulse AOE source warning/no-op still applies.

## Dependencies Confirmed
- Task 001: `IntervalSpawnTrigger` exists.
- Task 002: compiler dispatch accepts `IntervalSpawnTrigger`.
- Task 003: validator accepts `IntervalSpawnTrigger`.

## Step-By-Step Instructions
- Delete `ValidatorWarnsWhenProjectileIntervalTargetsAoeSkill`.
- Delete `CompilerIgnoresProjectileIntervalTargetAoeSkill`.
- Rename remaining `ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger` usages to `IntervalSpawnTrigger`.
- Keep separate trigger instances in PlayMode tests.
- Keep assertions unchanged unless names need clarity after merge.

## Acceptance Criteria
- Test assemblies have zero references to old interval trigger classes.
- Obsolete mismatch tests are gone.

## Validation Required
- Search affected test files for old class names.
- Later Unity build/test validation will cover compile.

## Hard Boundaries
- Do not modify production code or docs in this task.
