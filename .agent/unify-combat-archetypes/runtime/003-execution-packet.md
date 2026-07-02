# Task Execution Packet

## Task
003-aoe-apply-collapse.md

## Goal
Split AOE apply into impact and lingering systems, with one lingering archetype using enableable timed spawn.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`

## Behavior To Preserve
- Impact AOE remains lean and one-shot.
- Lingering AOE lifetime, pulse VFX, contact gates, collision behavior.

## Behavior To Change
- Expansion routes impact and lingering commands separately.
- Impact and lingering each have one apply system and one reuse query.
- Lingering timed spawn is an enabled bit.

## Relevant Global Context
- Impact query uses `WithNone<CombatLifetimeComponent>`.
- Lingering query uses `WithAll<CombatLifetimeComponent>`.

## Dependencies Confirmed
- 001 changes `TimedSpawnComponent` to enableable.

## Step-By-Step Instructions
- Remove AOE bucketing.
- Implement `ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem`.
- Reset timed spawn only in lingering apply.

## Acceptance Criteria
- Impact and lingering pools stay disjoint.
- Non-timed/timed lingering slots can cross-reuse.

## Validation Required
- Compile later after references update.

## Hard Boundaries
- Do not merge impact and lingering archetypes.
