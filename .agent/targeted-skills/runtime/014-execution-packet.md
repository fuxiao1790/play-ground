# Task Execution Packet

## Task

014-playmode-integration-tests.md

## Goal

Add `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs` covering targeted
combat end-to-end: casts, chains, lifecycle, triggers, faction routing, VFX,
pool reuse, and mixed cleanup.

## Files Allowed To Modify

- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`
- `Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs.meta`
- Test fixtures under `Assets/Tests/` only when directly needed.
- `.agent/targeted-skills/implementation-log.md`

## Relevant Global Context

- Tests observe gameplay effects, public APIs, entity counts, VFX dispatch, or
  diagnostics. No production test hooks.
- Targeted resolves from spatial-hash proxy data; it has no collision component.
- Targeted spawn resolves next simulation update. On-impact targeted children
  land one frame after their source impact.
- Pooling disables/reuses slots; cleanup includes targeted alongside projectile
  and AOE pools.
- Unity test execution is user-owned. XML result required before pass claim.

## Dependencies Confirmed

- Tasks 012 and 013 are complete in implementation log.
- Existing PlayMode fixtures establish `CombatRoot`, proxy targets, ECS worlds,
  pool cleanup, and VFX observation patterns.

## Acceptance Criteria

- All twelve scenarios listed in task 014 are covered.
- Tests use real runtime behavior and preserve existing tests.

## Hard Boundaries

- No hand-authored Unity YAML.
- Do not add production-only observability.
- Do not run Unity; provide XML-producing command to user.
