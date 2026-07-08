# Task Execution Packet

## Task
004-update-tests.md

## Goal
Update tests to use typed interval trigger fields and add AOE scatter runtime setup coverage.

## Files Allowed To Modify
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`

## Behavior To Preserve
- Retain projectile-trigger-to-AOE mismatch validator/compiler tests.
- Existing interval setup and registry assertions remain.

## Behavior To Change
- Replace merged `IntervalSpawnTrigger` usages with the proper two trigger types.
- Use `projectileCount` and `echoCount`.
- Add scatter assertion for AOE interval setup.

## Dependencies Confirmed
- 001-003 complete.

## Validation Required
- Search tests for stale `IntervalSpawnTrigger` and interval `.spawnCount`.

## Hard Boundaries
- Do not modify production code or docs here.
