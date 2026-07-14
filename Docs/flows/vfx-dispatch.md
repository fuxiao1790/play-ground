# AOE VFX Dispatch

## Purpose

Trace visual-only requests from simulation to VFX Graph dispatch.

## Sequence

1. AOE collision, AOE lifetime, pulse, or AOE spawn systems create
   `AoeVfxSpawnRequest`.
2. Producer jobs enqueue requests into the shared
   `NativeQueue<AoeVfxSpawnRequest>` owned by `CombatAoeVfxDispatchSystem`, using
   `AsParallelWriter()`, and combine their job handles into `ProducerHandle`.
3. `CombatAoeVfxDispatchSystem` runs in presentation, completes `ProducerHandle`,
   and resolves the single `CombatVfxRoot.Instance`.
4. `CombatVfxRoot` drains the queue on the main thread.
5. `CombatAoeVfxDispatcher` stages requests by `(typeId, AoeVfxTrigger)`, caps count,
   uploads GPU buffers, and sends VFX Graph events.

## Producers

`AOE spawn expansion systems`, `ImpactAoeCollisionSystem`,
`LingeringAoeCollisionSystem`, `CombatLifetimeSystem`, and
`AoePulseVfxSystem`.

## Consumers

`CombatAoeVfxDispatchSystem`, `CombatVfxRoot`, `CombatAoeVfxDispatcher`, and
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
