# AOE VFX Requests

## Purpose

Define visual-only event data passed from simulation producers to VFX Graph
dispatch.

`CircularVfxSpawnRequest` carries graph id, world
position, and area size. `TimedCircularVfxSpawnRequest` adds duration and tick interval.
`LineSegmentVfxSpawn` carries a directional start point, end point, and width. The dispatcher routes
them through the Circular, TimedCircular, and LineSegment data shapes. AOE systems
emit the circular shapes; `TargetedResolveSystem` emits circular hit/expire requests plus one
`LineSegmentVfxSpawn` per resolved chain link. `ProjectileMovementSystem` emits one
`LineSegmentVfxSpawn` per active, non-arming projectile when it has travelled at least
its authored `StepDistance` since the last emitted segment, not once per frame. This is
enabled only when that projectile's authored trail VFX id is nonzero. These are currently
the only two `LineSegment` producers.

## Produced By

[ECS Simulation](../layers/ecs-simulation.md).

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Request data:

- `CircularVfxSpawnRequest`, `TimedCircularVfxSpawnRequest`, or `LineSegmentVfxSpawn`
- `int VfxId`
- `float2 Position`
- `float AreaSize`
- TimedCircular only: `float Duration`
- TimedCircular only: `float TickInterval`
- LineSegment only: `float2 StartPosition`, `float2 EndPosition`, `float Width`

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

Detailed failure mechanics for two skill sets sharing one graph with different
area sizes, the confirmed one-frame corruption incident, and the required
mixed-size test are in
[Shared VFX Graph Area-Size Corruption](../reference/simulation/vfx-shared-graph-area-size-corruption.md).

## Restrictions

VFX requests must not carry damage/status authority. Simulation jobs must not
call managed VFX objects directly.

## Lifetime

Requests live in the persistent queue for their data shape, owned by
`CombatAoeVfxDispatchSystem` until presentation completes producers, drains the
queues, and buckets requests by graph id.

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
