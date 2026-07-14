# VFX Requests

## Purpose

Define visual-only event data passed from simulation producers to VFX Graph
dispatch.

## Produced By

[ECS Simulation](../layers/ecs-simulation.md).

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Request data:

- `VfxPendingSpawn`
- `int TypeId`
- `CombatVfxTrigger Trigger`
- `float2 Position`
- `float AreaSize`

Trigger values:

- `CombatVfxTrigger.Spawn` (`0`): spawn, emitted by `AOE spawn expansion systems` for expansion-spawned AOEs
- `CombatVfxTrigger.Hit` (`1`): hit
- `CombatVfxTrigger.Expire` (`2`): expire
- `CombatVfxTrigger.Pulse` (`3`): pulse
- `CombatVfxTrigger.Arming` (`4`): arming telegraph

## Guarantees

Requests are visual-only. Dropping requests after a budget cap does not change
gameplay authority.

## Restrictions

VFX requests must not carry damage/status authority. Simulation jobs must not
call managed VFX objects directly.

## Lifetime

Requests live in the persistent shared `NativeQueue<VfxPendingSpawn>` owned by
`CombatVfxDispatchSystem` until presentation completes producers and drains the
queue on the main thread.

## Ordering

Producer jobs enqueue during simulation. Presentation completes
`CombatVfxDispatchSystem.ProducerHandle` before draining and dispatching.

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
