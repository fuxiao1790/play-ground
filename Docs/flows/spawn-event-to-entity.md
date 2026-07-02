# Spawn Event To Entity

## Purpose

Trace how projectile and AOE intent becomes reusable ECS entities.

## Sequence

1. Managed bridge or ECS producers create `ProjectileSpawnEvent` or
   `AoeSpawnEvent`.
2. Expansion systems drain scope buffers and native event queues after producer
   jobs complete.
3. Expansion owns count, spread, jitter, bounds, deterministic id, and command
   production.
4. Expansion writes `ProjectileSpawnCommand` or `AoeSpawnCommand`.
5. Apply systems capture disabled-slot chunks for their domain reuse pool.
6. One single-threaded Burst reuse job per domain walks disabled chunks with one
   command cursor and resets reusable `Active` slots.
7. Commands after the reused prefix cold-create entities through an ECB.
8. Spawned/reused entities join simulation on the next update.

## Producers

`CombatRoot`, `TimedSpawnSystem`, `ProjectileCollisionSystem`,
`AoeCollisionCore`, and `StatusProcessSystem`.

## Consumers

`ProjectileSpawnExpansionSystem`, `AoeSpawnExpansionSystem`,
`ProjectileSpawnApplySystem`, `ImpactAoeSpawnApplySystem`, and
`LingeringAoeSpawnApplySystem`.

## Contracts Used

- [Spawn Requests](../contracts/spawn-requests.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [Render Batch Data](../contracts/render-batch-data.md)

## Layer Boundaries Crossed

- [Combat Bridge](../layers/combat-bridge.md) to
  [ECS Simulation](../layers/ecs-simulation.md)
- Internal producers stay within [ECS Simulation](../layers/ecs-simulation.md).

## Ordering / Timing Requirements

Expansion must wait for native event producers. Apply runs after collision.
Commands are one entity; events are gameplay intent.

## Failure / Edge Cases

Pool misses cold-create overflow entities. Reuse is single-cursor and packs
available disabled slots before cold creation. Scope membership does not
identify domain or faction. Commands should remain small enough for efficient
native-list storage and Burst job copying.

## Related Decisions

- [ADR-003](../decisions/adr-003-event-command-spawn-pipeline.md)
- [ADR-005](../decisions/adr-005-enableable-pooling-for-combat-entities.md)

## Notes / TODOs

Detailed reference:
[project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).
