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

- `CombatRenderComponent` (includes the affine atlas-UV basis `UvOriginU` /
  `UvV`, computed once when the spawn command is built)
- `CombatRenderElement` (the prepared `objectToWorld` TRS matrix)
- `CombatRenderActiveTag`
- `CombatRenderBatchId`
- `CombatInstanceData` — the per-instance GPU record (`objectToWorld` +
  `UvOriginU` + `UvV`, stride 96), scattered from the components above and
  uploaded to a `StructuredBuffer`
- one shared, manually-assembled `SpriteAtlas` asset (shared unit-quad mesh +
  shared material + a referenced, not owned, packed atlas page texture)

See [Combat Render System](../reference/simulation/combat-render-system.md) for
the full submission design.

`CombatRenderBatchId` is a plain `IComponentData` int, copied from the spawn
command's `RenderTypeId`. It is a kind identifier only — it does not select
or index anything at render time; `CombatRenderComponent`'s UV basis
(`UvOriginU` / `UvV`) already carries the atlas coordinates directly on the
entity. It does not partition chunks and does not partition spawn pools.

## Guarantees

The combat sprite atlas is **one manually-assembled `UnityEngine.U2D.SpriteAtlas`
asset** (each kind's `Sprite` added as a packable in the Unity Editor ahead of
time and packed onto a single page), assigned via a serialized field on
`CombatRoot` and threaded into `CombatRenderResourceRegistry.ConfigureAtlas(...)`.
There is no runtime packing: `Register(...)` resolves the atlas's packed copy of
the sprite via `SpriteAtlas.GetSprite(name)`, binds that sprite's atlas page as
the shared material's texture, and computes each kind's **affine UV basis**
(`UvOriginU` / `UvV`) from the packed sprite's `uv`/`vertices` — never
`sprite.rect` (source-texture space) — so a 90°-rotated packing still samples
correctly. If a sprite isn't a packable, `GetSprite` returns null and
registration fails loudly. The atlas must be packed at runtime
(`SpritePackerMode` = "Sprite Atlas V2 - Enabled"); an unpacked atlas resolves
sprites to their source textures and corrupts rendering.

Every active projectile/AOE entity across every kind draws in **one
`DrawMeshInstancedIndirect` per update** with one shared unit-quad mesh + one
shared material. Per-instance data (`CombatInstanceData`) is uploaded to a
`StructuredBuffer` and indexed by `SV_InstanceID`; there is no per-kind draw
call and no 1023-instance cap. The draw is recorded inside a URP
`ScriptableRendererFeature` (`Combat Indirect Render Feature` on
`Renderer2D.asset`), because the 2D Renderer does not execute immediate-mode
`Graphics.RenderMesh*` calls and only runs passes tagged `LightMode = Universal2D`.

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
in one active-only scatter pass fills a single `NativeList<CombatInstanceData>`
directly from each entity's own components (no registry lookup per entity — the
UV basis was already computed once, when the spawn command was built). The
system uploads that list to the instance `StructuredBuffer`, writes the
indirect args (`instanceCount` = active count), binds the buffer with
`Material.SetBuffer` (not `MaterialPropertyBlock`, which no-ops for indirect
draws), and publishes the draw inputs to a static handoff. A
`ScriptableRendererFeature` on `Renderer2D.asset` then issues one
`DrawMeshInstancedIndirect` inside the 2D render pass. It does not use
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
