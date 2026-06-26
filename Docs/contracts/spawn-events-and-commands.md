# Spawn Events And Commands

## Purpose

Define the ECS boundary between gameplay spawn intent and one-entity allocation
intent.

## Produced By

Events are produced by [Combat Bridge](../layers/combat-bridge.md) and internal
[ECS Simulation](../layers/ecs-simulation.md) producers. Commands are produced
by expansion systems.

## Consumed By

Events are consumed by expansion systems. Commands are consumed by apply
systems.

## Fields / Shape

Event types:

- `ProjectileSpawnEvent`
- `AoeSpawnEvent`

Command types:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`

Events may contain count, spread, jitter, base direction, faction, type ids,
movement, hit payloads, tracking, render data, and optional timed/impact/on-hit
snapshots.

Commands describe exactly one ECS entity with resolved position, velocity,
bounds, identity, hit payload, lifetime, render state, and optional timed-spawn
state.

## Guarantees

Events are gameplay intent. Commands are allocation intent. Expansion owns spawn
math. Apply owns reuse and cold creation.

## Restrictions

Do not create projectile/AOE entities directly from managed gameplay,
collision, status, or timed-spawn code. Do not put managed references in event
or command payloads.

## Lifetime

Events live until drained from scope buffers or native queues. Commands live
until consumed by apply systems in the same spawn phase.

## Ordering

Producer jobs must complete before expansion drains native queues. Apply runs
after expansion and after current-frame collision.

## Related Layers

- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)

## Related Flows

- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

Detailed references:
[spawn-template-registry.md](../reference/simulation/spawn-template-registry.md) and
[project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).
