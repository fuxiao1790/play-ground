# Task Execution Packet

## Task

005-update-tests.md

## Goal

Migrate test construction to `IntervalSpawnTrigger` and cover changed target/source validation behavior.

## Files Allowed To Modify

- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs`
- `Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs`
- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/EditMode/TargetedValidationEditModeTests.cs`

## Behavior To Preserve

- Existing test field setup and all unaffected interval scenarios.

## Behavior To Change

- Remove old target-mismatch expectation; assert AOE setup builds. Assert pulse-AOE source validation Error and generic message.

## Dependencies Confirmed

- Tasks 001-004 complete: merged trigger, compiler runtime dispatch, and Error validation behavior exist.

## Acceptance Criteria

- No obsolete trigger test type; changed behavior covered.

## Validation Required

- User-run Unity EditMode and PlayMode test suites with XML output.

## Hard Boundaries

- Do not run Unity tests; no non-test source changes.
