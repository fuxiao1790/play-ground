# Task Execution Packet

## Task
005-refcount-sweep-system.md

## Goal
Drain refcount deltas and atomically reclaim paired counter/template entries after pool cleanup.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/SpawnTemplateRefCountSystem.cs` (new)
- `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs` (required: dirty state specified by task)
- `Assets/Scripts/System/Core/CombatRoot.cs` (required: unregister marks singleton dirty as specified by task)

## Behavior To Preserve
- Maps remain immutable during Simulation; no job accesses count maps.

## Behavior To Change
- Late single-threaded system applies deltas and sweep removes reclaimable keys from both maps.

## Relevant Global Context
- Complete dependencies first; direct-read scope singleton and three command-map components.
- Clamp negative instance count to zero with editor assertion. Use temp key snapshot before removals.
- Empty queue plus clean state means no map scan. Managed unregister sets dirty.

## Dependencies Confirmed
- 001 state/count maps, 003 apply deltas, 004 trim deltas.

## Step-By-Step Instructions
- Add dirty flag to state.
- Mark dirty in unregister.
- Create ordered late sweep; map selection uses targeted/AOE/projectile helper.

## Acceptance Criteria
- Correct owner/instance/pin reclaim rules and no map work when clean.

## Validation Required
- Static inspection; Unity tests deferred.

## Hard Boundaries
- No new simulation job path or template-reader changes.
