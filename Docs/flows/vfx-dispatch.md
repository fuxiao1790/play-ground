# VFX Dispatch

## Purpose

Trace visual-only requests from simulation to VFX Graph dispatch.

## Sequence

1. Collision, lifetime, pulse, or spawn systems create `VfxPendingSpawn`.
2. Flush jobs append requests to `DynamicBuffer<VfxSpawnRequestElement>` on the
   shared `CombatScope`.
3. `CombatVfxDispatchSystem` runs in presentation.
4. It resolves `CombatVfxRoot` through the scope VFX catalog.
5. `CombatVfxRoot` drains and clears the buffer.
6. `CombatVfxDispatcher` stages requests by `(typeId, trigger)`, caps count,
   uploads GPU buffers, and sends VFX Graph events.

## Producers

`ProjectileCollisionSystem`, AOE collision systems, `CombatLifetimeSystem`, and
`AoePulseVfxSystem`.

## Consumers

`CombatVfxDispatchSystem`, `CombatVfxRoot`, `CombatVfxDispatcher`, and
Visual Effect Graph assets.

## Contracts Used

- [VFX Requests](../contracts/vfx-requests.md)

## Layer Boundaries Crossed

- [ECS Simulation](../layers/ecs-simulation.md) to
  [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

VFX dispatch runs after simulation data is flushed. VFX requests are visual-only
and may be capped without changing gameplay.

## Failure / Edge Cases

Missing VFX Graph contract fields make a graph invalid for this runtime. Events
past `maxPerFrame` can be dropped for visual budget control.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-002](../decisions/adr-002-plain-data-snapshot-boundary.md)

## Notes / TODOs

Detailed reference:
[vfx-system.md](../reference/simulation/vfx-system.md).
