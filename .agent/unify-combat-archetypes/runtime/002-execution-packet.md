# Task Execution Packet

## Task
002-projectile-apply-collapse.md

## Goal
Collapse basic and timed projectile pools into one apply system, one command container, one reusable archetype, and one reuse job.

## Files Allowed To Modify
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`

## Behavior To Preserve
- Projectile tracking, collision, render, contact gates, lifetime, cold-create counters.

## Behavior To Change
- One projectile command container.
- One reusable projectile archetype with timed-spawn components present.
- `TimedSpawnComponent` enabled only for commands with timed spawn.

## Relevant Global Context
- Reuse query must be `ProjectileTag` plus disabled `Active`; no timed eligibility branch.

## Dependencies Confirmed
- 001 changes `TimedSpawnComponent` to enableable.

## Step-By-Step Instructions
- Remove projectile bucket sort and old split systems.
- Build one `ProjectileSpawnApplySystem`.
- Reset timed-spawn data and enable state per command.

## Acceptance Criteria
- Disabled basic/timed slots can cross-reuse inside projectile pool.
- No projectile apply query uses `TimedSpawnTag`.

## Validation Required
- Compile later after tests and references update.

## Hard Boundaries
- Do not change projectile collision behavior.
