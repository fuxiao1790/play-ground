# Task Execution Packet

## Task
005-tests-and-doc.md

## Goal
Update direct test construction sites and contract documentation for source-based hit payload lookup.

## Files Allowed To Modify
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Docs/contracts/combat-hit-and-tick-results.md`
- Direct compile fixes in tests caused by the event/component shape change.

## Behavior To Preserve
- Test intent and assertions.
- Authoring DTO construction sites remain unchanged unless compile requires otherwise.

## Behavior To Change
- Tests create real source entities with `CombatHitPayload` when directly enqueueing hit events.
- Docs describe `{ Source, Target }` and payload lookup guarantee.

## Relevant Global Context
Raw hit damage can no longer be pushed through `CombatHitEvent`; it must live on the source entity.

## Dependencies Confirmed
- Requires tasks 001-004.

## Acceptance Criteria
- Combat tests compile.
- Contract doc reflects new hit event shape.

## Validation Required
- Run relevant Unity test/build validation if available.
