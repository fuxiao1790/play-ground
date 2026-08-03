# Spawn Event To Entity

## Purpose

Trace how projectile and AOE intent becomes reusable ECS entities.

## Sequence

1. Managed bridge or ECS producers create `ProjectileSpawnEvent`,
   `ImpactAoeSpawnEvent`, or `LingeringAoeSpawnEvent`.
2. Expansion systems drain scope buffers and native event queues after producer
   jobs complete.
3. Expansion owns count, spread, jitter, bounds, deterministic id, and command
   production.
4. Expansion writes `ProjectileSpawnCommand` or `AoeSpawnCommand`. Projectile
   commands are routed into a discrete or continuous list by their
   `ContinuousCollision` flag; the two lanes are separate pools from here on.
5. Apply systems count disabled slots in their own reuse pool and cold-create
   the deficit up front through `SpawnPoolTopUp.EnsureDisabledSlots`, since
   entity creation is structural.
6. One single-threaded Burst reuse job per pool then walks the disabled chunks
   with one command cursor and writes every command into an `Active`-disabled
   slot.
7. Spawned/reused entities join simulation on the next update.

## Producers

`CombatRoot`, `TimedSpawnSystem`, `ProjectileDiscreteCollisionSystem`,
`ProjectileContinuousCollisionSystem`, `AoeCollisionCore`, and
`StatusProcessSystem`.

## Consumers

`ProjectileSpawnExpansionSystem`, `ImpactAoeSpawnExpansionSystem`,
`LingeringAoeSpawnExpansionSystem`, `ProjectileDiscreteSpawnApplySystem`,
`ProjectileContinuousSpawnApplySystem`, `ImpactAoeSpawnApplySystem`, and
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

AOE variant is chosen at authoring from child lifetime (`Lifetime > 0` means
lingering, otherwise impact) and carried on the event kind. Producers route to
the matching AOE event queue exactly like they route projectile events.

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
