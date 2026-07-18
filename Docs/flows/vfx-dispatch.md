# AOE VFX Dispatch

## Purpose

Trace visual-only requests from simulation to VFX Graph dispatch.

## Sequence

1. AOE collision, AOE lifetime, pulse, or AOE spawn systems create Basic or
   Timed VFX requests through `VfxEmit`.
2. Producer jobs enqueue requests into the corresponding shared native queue
   owned by `CombatAoeVfxDispatchSystem`, using `AsParallelWriter()`, and combine
   their job handles into `ProducerHandle`.
3. `CombatAoeVfxDispatchSystem` runs in presentation, completes `ProducerHandle`,
   and resolves the single `CombatVfxRoot.Instance`.
4. `CombatVfxRoot` drains the queue on the main thread.
5. `CombatAoeVfxDispatcher` stages requests by graph-kind id, uploads GPU
   buffers, and sends VFX Graph events.
6. The graph reads `Positions`, `AreaSizes`, and `SpawnCount` as spawn payloads
   in `Initialize Particles` and copies needed per-instance values into
   particle attributes. Alive particles must not reread these request buffers
   from `Update Particle` or `Output Particle`.

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
visual-only. Any future visual-budget dropping or prioritization must happen
before dispatch and must not change gameplay.

## Failure / Edge Cases

Missing VFX Graph contract fields make a graph invalid for this runtime. Events
that fail validation are not dispatched. A graph that reads request buffers from
`Output Particle` can make existing particles render with data from a later
dispatch batch for the same graph kind.

See
[Shared VFX Graph Area-Size Corruption](../reference/simulation/vfx-shared-graph-area-size-corruption.md)
for the confirmed reproduction, false leads, correct graph pattern, and review
checklist.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-002](../decisions/adr-002-plain-data-snapshot-boundary.md)

## Notes / TODOs

Detailed reference:
[vfx-system.md](../reference/simulation/vfx-system.md).
