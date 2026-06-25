# Skill To Combat Spawn

## Purpose

Trace how authored skills become plain-data combat spawn requests.

## Sequence

1. Authoring defines skills, supports, triggers, loadout slots, and validator
   prefab requirements.
2. Game logic compiles equipped sets into runtime skill definitions.
3. `SkillSpawnTranslator` turns runtime definitions into projectile or AOE
   spawn request data.
4. `PlayerSkillDriver` or mob attack code submits the request to `CombatRoot`.
5. `CombatRoot` validates registered resources and appends a spawn event to the
   shared scope buffer.
6. ECS expansion/apply materializes entities later through the normal spawn
   flow.

## Producers

Skill authoring assets, `SkillSetCompiler`, `SkillSpawnTranslator`,
`PlayerSkillDriver`, and mob attack code.

## Consumers

`CombatRoot`, spawn expansion systems, projectile/AOE apply systems, and ECS
simulation.

## Contracts Used

- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Combat Root API](../contracts/combat-root-api.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)

## Layer Boundaries Crossed

- [Game Logic](../layers/game-logic.md) to
  [Combat Bridge](../layers/combat-bridge.md)
- [Combat Bridge](../layers/combat-bridge.md) to
  [ECS Simulation](../layers/ecs-simulation.md)

## Ordering / Timing Requirements

Authored data must be compiled and snapshotted before ECS sees it. In-flight
entities use copied values, ids, and hashes, not live authoring objects.

## Failure / Edge Cases

Invalid loadouts should be caught by validators. Missing render/VFX/type
registration should fail before event submission. Recursive child behavior must
use event-template keys instead of managed nested references.

## Related Decisions

- [ADR-002](../decisions/adr-002-plain-data-snapshot-boundary.md)
- [ADR-003](../decisions/adr-003-event-command-spawn-pipeline.md)

## Notes / TODOs

Detailed reference:
[skill-system.md](../reference/game-logic/skill-system.md).
