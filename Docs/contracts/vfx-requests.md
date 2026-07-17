# AOE VFX Requests

## Purpose

Define visual-only event data passed from simulation producers to VFX Graph
dispatch.

The current payload is AOE-shaped: every request carries one graph-kind id, one
world position, and one area size. The dispatcher uploads `Positions` and
`AreaSizes` for every graph kind. Projectile systems do not emit these requests;
the payload is reserved for AOE-shaped visuals.

## Produced By

[ECS Simulation](../layers/ecs-simulation.md).

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Request data:

- `AoeVfxSpawnRequest`
- `int VfxId`
- `float2 Position`
- `float AreaSize`

`VfxId` identifies the registered VFX graph kind, not the AOE type and not the
reason the event was emitted. One graph represents every VFX event of that kind
on screen, so all producers that want the same graph must use the same `VfxId`.
Event-specific differences belong in payload data such as `Position` and
`AreaSize`, or in future payload fields if the graph needs them.

## Guarantees

Requests are visual-only. If a visual budget later drops or prioritizes
requests before dispatch, that must not change gameplay authority.

Dispatch keys on `VfxId` alone. Spawn, hit, expire, pulse, and arming are not
part of VFX identity.

`Positions`, `AreaSizes`, and `SpawnCount` are spawn payloads for the current
dispatch batch. They are not stable per-particle storage. A VFX graph must copy
per-instance request data from these buffers in `Initialize Particles` and then
animate/render from particle attributes. Reading request buffers from `Update
Particle` or `Output Particle` can make alive particles use data from a later
batch for the same graph kind.

## Restrictions

VFX requests must not carry damage/status authority. Simulation jobs must not
call managed VFX objects directly.

## Lifetime

Requests live in the persistent shared `NativeQueue<AoeVfxSpawnRequest>` owned by
`CombatAoeVfxDispatchSystem` until presentation completes producers and drains the
queue on the main thread.

## Ordering

Producer jobs enqueue during simulation. Presentation completes
`CombatAoeVfxDispatchSystem.ProducerHandle` before draining and dispatching.

## Related Layers

- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Flows

- [VFX Dispatch](../flows/vfx-dispatch.md)
- [Runtime Frame](../flows/runtime-frame.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

Detailed reference:
[vfx-system.md](../reference/simulation/vfx-system.md).
