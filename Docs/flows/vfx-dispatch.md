# VFX Dispatch

## Purpose

Trace visual-only requests from simulation to VFX Graph dispatch.

## Sequence

1. Collision, lifetime, pulse, or spawn systems create `VfxPendingSpawn`.
2. Producer jobs enqueue requests into the shared
   `NativeQueue<VfxPendingSpawn>` owned by `CombatVfxDispatchSystem`, using
   `AsParallelWriter()`, and combine their job handles into `ProducerHandle`.
3. `CombatVfxDispatchSystem` runs in presentation, completes `ProducerHandle`,
   and resolves the single `CombatVfxRoot.Instance`.
4. `CombatVfxRoot` drains the queue on the main thread.
5. `CombatVfxDispatcher` stages requests by `(typeId, CombatVfxTrigger)`, caps count,
   uploads GPU buffers, and sends VFX Graph events.

## Producers

`AOE spawn expansion systems`, `ProjectileCollisionSystem`,
`ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`,
`CombatLifetimeSystem`, and `AoePulseVfxSystem`.

## Consumers

`CombatVfxDispatchSystem`, `CombatVfxRoot`, `CombatVfxDispatcher`, and
Visual Effect Graph assets.

## Contracts Used

- [VFX Requests](../contracts/vfx-requests.md)

## Layer Boundaries Crossed

- [ECS Simulation](../layers/ecs-simulation.md) to
  [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

VFX dispatch runs after simulation producers complete. VFX requests are
visual-only and may be capped without changing gameplay.

## Failure / Edge Cases

Missing VFX Graph contract fields make a graph invalid for this runtime. Events
past `maxPerFrame` can be dropped for visual budget control.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-002](../decisions/adr-002-plain-data-snapshot-boundary.md)

## Notes / TODOs

Detailed reference:
[vfx-system.md](../reference/simulation/vfx-system.md).
