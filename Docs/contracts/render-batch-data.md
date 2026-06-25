# Render Batch Data

## Purpose

Define shared ECS render data used to batch projectile and AOE sprite visuals.

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
- `CombatRenderFaction`
- `CombatRenderTypeId`
- prepared transform matrices
- root-owned render resources keyed by faction/type id

## Guarantees

Renderable projectile/AOE entities can be grouped by faction and type id for
instanced sprite submission.

## Restrictions

Render state must not define gameplay domain or faction by itself. Domain still
comes from `ProjectileTag` or `AoeTag`; faction comes from `CombatFaction` or
render shared faction data.

## Lifetime

Render components live on projectile/AOE reusable entities. Render resources
live with the owning combat root and are released on root teardown.

## Ordering

Render preparation runs after simulation/apply. Batched render submission runs
in presentation.

## Related Layers

- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)

## Notes / TODOs

TODO: verify whether current render preparation includes both projectile and AOE
matrices in one shared system or split systems after recent code changes.
