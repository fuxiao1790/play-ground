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
6. Apply builds one command-index lane per worker and assigns each worker a
   disjoint chunk range.
7. One parallel apply job per domain resets disabled `Active` slots from that
   worker's lane.
8. Overflow command indices cold-create entities through an ECB.
9. Spawned/reused entities join simulation on the next update.

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

Pool misses cold-create overflow entities. Uneven reuse is intentional: one
worker may overflow its lane while another worker has spare slots, so the
resident pool can converge to a slightly larger steady-state size. Scope
membership does not identify domain or faction. Commands should stay small
enough for native stream block limits.

## Related Decisions

- [ADR-003](../decisions/adr-003-event-command-spawn-pipeline.md)
- [ADR-005](../decisions/adr-005-enableable-pooling-for-combat-entities.md)

## Notes / TODOs

Detailed reference:
[project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).
