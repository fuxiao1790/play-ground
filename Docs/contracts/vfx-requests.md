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
- `VfxSpawnRequestElement`
- `CombatFaction Faction`
- `int TypeId`
- `byte Trigger`
- `float2 Position`
- `float AreaSize`

Trigger values:

- `0`: spawn, registered but not emitted by current projectile/AOE apply paths
- `1`: hit
- `2`: expire
- `3`: pulse

## Guarantees

Requests are visual-only. Dropping requests after a budget cap does not change
gameplay authority.

## Restrictions

VFX requests must not carry damage/status authority. Simulation jobs must not
call managed VFX objects directly.

## Lifetime

Requests live in native queues/streams until flushed to scope buffers, then live
until presentation dispatch drains and clears them.

## Ordering

Flush before presentation dispatch. Dispatch after simulation.

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
