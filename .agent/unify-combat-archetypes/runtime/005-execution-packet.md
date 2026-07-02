# Task Execution Packet

## Task
005-tests-and-assertions.md

## Goal
Update tests to assert timed-spawn enableable state and three-archetype behavior.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Updated apply/expansion systems.

## Behavior To Preserve
- Existing collision, tracking, timed-spawn, and lifetime coverage intent.

## Behavior To Change
- Tests use `IsComponentEnabled<TimedSpawnComponent>()` instead of `TimedSpawnTag`.
- Tests instantiate renamed apply systems.

## Relevant Global Context
- Impact AOEs still lack lifetime.
- Non-timed projectile/lingering AOE have timed-spawn component present but disabled.

## Dependencies Confirmed
- 001-004 complete before final validation.

## Step-By-Step Instructions
- Repoint system setup names.
- Replace tag assertions/queries.
- Add/adjust cross-reuse checks.

## Acceptance Criteria
- No test asserts `TimedSpawnTag`.

## Validation Required
- Run relevant Unity tests if possible.

## Hard Boundaries
- Do not weaken test intent.
