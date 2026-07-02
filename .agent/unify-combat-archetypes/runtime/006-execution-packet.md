# Task Execution Packet

## Task
006-docs-and-cleanup.md

## Goal
Update docs and remove stale variant-archetype/tag/runtime names.

## Files Allowed To Modify
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/reference/simulation/project-aoe-system-common.md`
- `Docs/reference/simulation/project-ecs-implementation.md`
- `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`
- `Docs/flows/spawn-event-to-entity.md`
- `Docs/profiling.md`
- `Docs/folder-structure.md`
- `Docs/reference/architecture/architecture.md`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- Current docs and runtime search output.

## Behavior To Preserve
- Collision system ordering and names.

## Behavior To Change
- Docs describe projectile, impact AOE, and lingering AOE archetypes.
- Stats system uses new apply system names.

## Relevant Global Context
- Timed spawn bit exists only on projectile and lingering AOE.

## Dependencies Confirmed
- 001-005 complete before final validation.

## Step-By-Step Instructions
- Remove stale names and `TimedSpawnTag` language.
- Repoint profiler counters/system names.

## Acceptance Criteria
- Search finds no runtime dependency on retired names.

## Validation Required
- Search and compile.

## Hard Boundaries
- Do not change collision-system ordering.
