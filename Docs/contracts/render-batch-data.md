# Render Batch Data

## Purpose

Define ECS render data used to batch projectile and AOE sprite visuals.

## Produced By

[Combat Bridge](../layers/combat-bridge.md) registers resources.
[ECS Simulation](../layers/ecs-simulation.md) writes runtime render components
and prepared matrices.

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Current render data includes:

- `CombatRenderComponent`
- `CombatRenderElement`
- `CombatRenderActiveTag`
- `CombatRenderBatchId`
- prepared transform matrices
- root-owned render resources keyed by render id

`CombatRenderBatchId` is a plain `IComponentData` int. Its value is copied from
the spawn command's `RenderTypeId` and selects the GPU resource batch at submit.
It does not partition chunks and does not partition spawn pools.

## Guarantees

Renderable projectile/AOE entities can be grouped by `CombatRenderBatchId` for
instanced sprite submission. The render resource registry remains the owner of
mesh/material/property resources for each render id.

## Restrictions

Render state must not define gameplay domain or faction by itself. Domain still
comes from `ProjectileTag` or `AoeTag`; faction comes from `CombatFaction`.

Spawn pooling must not key on `CombatRenderBatchId`. Reuse can claim any
disabled slot in the matching archetype and must overwrite the batch id from the
current spawn command.

## Lifetime

Render components live on projectile/AOE reusable entities. Render resources
live with the owning combat root and are released on root teardown.

## Ordering

Render preparation runs after simulation/apply. Batched render submission runs
in presentation, reads `CombatRenderElement` and `CombatRenderBatchId`, scatters
matrices into per-batch scratch buffers, and submits each non-empty registry
batch. It does not use shared-component filters or `ToComponentDataArray`.

## Related Layers

- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)

## Notes / TODOs

Deferred render optimizations:

- replace the main-thread scatter with parallel count/prefix-sum/scatter
- fold matrix generation into scatter and remove `CombatRenderElement`
