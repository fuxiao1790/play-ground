# AOE VFX Requests

## Purpose

Define visual-only event data passed from simulation producers to VFX Graph
dispatch.

The current payload is AOE-shaped: every request carries one world position and
one area size, and the dispatcher uploads `Positions` and `AreaSizes` for every
effect. Projectile systems do not emit these requests; the payload is reserved
for AOE-shaped visuals.

## Produced By

[ECS Simulation](../layers/ecs-simulation.md).

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Request data:

- `AoeVfxSpawnRequest`
- `int TypeId`
- `AoeVfxTrigger Trigger`
- `float2 Position`
- `float AreaSize`

Trigger values:

- `AoeVfxTrigger.Spawn` (`0`): AOE spawn or arming AOE activation
- `AoeVfxTrigger.Hit` (`1`): confirmed AOE hit
- `AoeVfxTrigger.Expire` (`2`): lingering AOE lifetime expire
- `AoeVfxTrigger.Pulse` (`3`): lingering AOE pulse
- `AoeVfxTrigger.Arming` (`4`): AOE arming telegraph

## Guarantees

Requests are visual-only. Dropping requests after a budget cap does not change
gameplay authority.

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
