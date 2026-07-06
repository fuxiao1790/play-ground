# Task Execution Packet

## Task
001-lingering-tag-discriminator.md

## Goal
Introduce `LingeringAoeTag` and move impact-vs-lingering routing from `CombatLifetimeComponent` presence to the tag.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `.agent/aoe-telegraph-refactor/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`

## Behavior To Preserve
- Impact AOEs are single contact pass.
- Lingering AOEs repeat and expire by lifetime.
- Pool reuse remains impact-to-impact and lingering-to-lingering.

## Behavior To Change
- Routing discriminator changes from lifetime presence to `LingeringAoeTag`.

## Relevant Global Context
- `Active` remains the occupancy gate.
- Reuse never crosses AOE archetypes.
- Do not touch enable-bit logic in this task.

## Dependencies Confirmed
- None.

## Step-By-Step Instructions
- Add `LingeringAoeTag` to AOE components.
- Add it to the lingering archetype only.
- Replace impact/lingering routing queries with tag checks.
- Update tests that used lifetime presence as impact/lingering assertions or helper queries.
- Leave lifetime enable assertions and `EnabledRef` reads for later tasks.

## Acceptance Criteria
- Build compiles.
- No remaining impact/lingering routing query keys on `CombatLifetimeComponent` presence.
- AOE simulation tests preserve meaning.

## Validation Required
- Run search checks after patch.
- Run build or explain if unavailable.

## Hard Boundaries
- Do not modify files outside the allowed list except directly required compile fixes.
- Do not change architecture.
- Do not combine with later tasks.
- Stop on architectural ambiguity.
