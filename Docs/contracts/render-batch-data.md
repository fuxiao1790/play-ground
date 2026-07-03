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

- `CombatRenderComponent` (includes `UvRect`, computed once when the spawn
  command is built)
- `CombatRenderElement`
- `CombatRenderActiveTag`
- `CombatRenderBatchId`
- prepared transform matrices
- one shared, manually-assembled atlas (mesh + material + a referenced,
  not owned, atlas texture asset)

`CombatRenderBatchId` is a plain `IComponentData` int, copied from the spawn
command's `RenderTypeId`. It is a kind identifier only — it does not select
or index anything at render time; `CombatRenderComponent.UvRect` already
carries the atlas coordinates directly on the entity. It does not partition
chunks and does not partition spawn pools.

## Guarantees

The combat sprite atlas is **one manually-assembled texture asset** (sliced
into per-kind `Sprite`s in the Unity Editor ahead of time), assigned via a
serialized field on `CombatRoot` and threaded into
`CombatRenderResourceRegistry.ConfigureAtlas(...)`. There is no runtime
packing: `Register(...)` computes each kind's UV rect directly from
`sprite.rect`/atlas texture dimensions, and throws if the sprite passed in
isn't actually sliced from the configured atlas texture (fail loud on a
missed content-authoring step, rather than silently misrendering). Every
active projectile/AOE entity across every kind draws with one shared
unit-quad mesh + one shared instanced material, in as few
`Graphics.RenderMeshInstanced` calls as the 1023-instance-per-call cap
requires (not one call per kind).

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
in presentation, reads `CombatRenderElement` and `CombatRenderComponent`, and
in one active-only scatter pass fills a shared transform buffer and a
parallel UV-rect buffer directly from each entity's own components (no
registry lookup per entity — `UvRect` was already computed once, when the
spawn command was built). Submission then chunks that shared buffer pair at
the 1023-instance cap and calls `Graphics.RenderMeshInstanced` once per chunk
against the registry's shared mesh/material. It does not use
shared-component filters or `ToComponentDataArray`.

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
