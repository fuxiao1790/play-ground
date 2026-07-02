# Task Execution Packet

## Task
004-query-and-simulation-updates.md

## Goal
Make timed spawn simulation depend on enabled `TimedSpawnComponent` rather than `TimedSpawnTag`.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- AOE collision, lifetime, pulse, and gate systems for confirmation.

## Behavior To Preserve
- Timed spawn emits projectile/AOE children with same deterministic tick behavior.

## Behavior To Change
- Query selects enabled `TimedSpawnComponent`, enabled lifetime, and active entities.

## Relevant Global Context
- Impact AOEs lack lifetime and timed-spawn, so they are excluded by construction.

## Dependencies Confirmed
- 001-003 complete before final validation.

## Step-By-Step Instructions
- Replace `TimedSpawnTag` query dependency with `TimedSpawnComponent` enabled semantics.

## Acceptance Criteria
- No simulation behavior depends on `TimedSpawnTag`.

## Validation Required
- Search for `TimedSpawnTag` after cleanup.

## Hard Boundaries
- Do not add a per-entity branch as the primary selector.
