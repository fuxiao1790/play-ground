# ADR-003: Event Command Spawn Pipeline

## Status

Accepted

## Context

Combat spawns can come from managed input, skills, timed child effects,
collision consequences, and status detonations. Those producers should not also
own entity allocation details.

## Decision

Separate spawn intent from allocation intent. Producers emit spawn events.
Expansion systems convert events into one-entity commands. Apply systems reuse
disabled slots or cold-create overflow.

## Consequences

Spawn math has one owner per domain. Entity reuse has one owner per apply path.
Collision and status systems can produce follow-up effects without allocating
entities or calling managed code.

## Alternatives Considered

- Direct entity creation from producers: rejected because it scatters allocation
  policy and structural changes.
- Put volley/count data into commands: rejected because commands must represent
  one resolved entity.

## Related Docs

- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [ECS Simulation](../layers/ecs-simulation.md)
