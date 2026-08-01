# Task Execution Packet

## Task

007-test-updates.md

## Goal

Update PlayMode tests for queued target-proxy lifecycle timing without weakening assertions.

## Files Allowed To Modify

- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs` only if audit finds same-frame proxy assumption.

## Dependencies Confirmed

- Event buffers, queued proxy methods, all apply systems, roots and caster update exist.

## Required Changes

- Hand-rolled scopes add all proxy event buffers.
- After lifecycle enqueue, tick world/apply system before using target proxy or asserting ECS component/lifetime state.
- Create result is bool; read `target.CombatTargetProxy` only after tick.
- Audit real CombatRoot tests for immediate proxy assumptions.

## Acceptance Criteria

- Preserve strength and intent of all assertions.
- No synchronous lifecycle expectation remains.

## Validation

- Static call-site/timing audit; run scoped tests where possible.
