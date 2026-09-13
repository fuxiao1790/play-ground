# Task Execution Packet

## Task
`005-test-fixture-archetype-updates.md`

## Goal
Restore direct projectile test fixtures to matching `ProjectileMovementJob` after its new required component term.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- Newly found test fixture files only if they directly create `ProjectileTag` entities and step `ProjectileMovementSystem`.

## Behavior To Preserve
- All existing assertions and default no-trail behavior.

## Behavior To Change
- Add `ProjectileTrailVfxComponent` only to directly created projectile entities in worlds that execute movement.

## Dependencies Confirmed
- `ProjectileTrailVfxComponent` exists; movement `Execute` requires it.

## Acceptance Criteria
- Every direct movement-test projectile entity includes component; no expected values change.

## Validation Required
- Static search/inspection only. Unity tests deferred to user.

## Hard Boundaries
- Do not modify production code or test assertions.
