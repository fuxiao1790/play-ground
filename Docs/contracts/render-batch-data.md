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

- `CombatRenderComponent` (the prepared `objectToWorld` TRS matrix plus packed
  `RenderMeta`: render id in bits 0..30, align-to-velocity in bit 31)
- `CombatRenderActiveTag`
- `CombatRenderKindId`
- `CombatInstanceData` - the per-instance GPU record (`objectToWorld` +
  `RenderMeta`, stride 68), scattered directly from `CombatRenderComponent` and
  uploaded to `_InstanceData`
- `CombatUvBasis` - the per-kind GPU record (`OriginU` + `V`, stride 32),
  uploaded by `CombatRenderResourceRegistry` to `_UvBasis` and indexed by render id
- one shared, manually-assembled `SpriteAtlas` asset (shared unit-quad mesh +
  shared material + a referenced, not owned, packed atlas page texture)

See [Combat Render System](../reference/simulation/combat-render-system.md) for
the full submission design.

`CombatRenderKindId` is a plain `IComponentData` int, copied from the spawn
command's `RenderTypeId`. It is a kind identifier only; it does not partition
chunks and does not partition spawn pools. The same render id is packed into
`CombatRenderComponent.RenderMeta`, and the shader masks it out to index the
static per-kind `_UvBasis` table.

## Guarantees

The combat sprite atlas is **one manually-assembled `UnityEngine.U2D.SpriteAtlas`
asset** (each kind's `Sprite` added as a packable in the Unity Editor ahead of
time and packed onto a single page), assigned via a serialized field on
`CombatRoot` and threaded into `CombatRenderResourceRegistry.ConfigureAtlas(...)`.
There is no runtime packing: `Register(...)` resolves the atlas's packed copy of
the sprite via `SpriteAtlas.GetSprite(name)`, binds that sprite's atlas page as
the shared material's texture, and computes each kind's **affine UV basis**
(`UvOriginU` / `UvV`) from the packed sprite's `uv`/`vertices`, never
`sprite.rect` (source-texture space), so a 90-degree rotated packing still samples
correctly. The atlas is static, so the registry uploads those per-kind UV bases to
`_UvBasis` only when the registered kind set changes. If a sprite is not a
packable, `GetSprite` returns null and registration fails loudly. The atlas must be
packed at runtime (`SpritePackerMode` = "Sprite Atlas V2 - Enabled"); an unpacked
atlas resolves sprites to their source textures and corrupts rendering.

Every active projectile/AOE entity across every kind draws in **one
`DrawMeshInstancedIndirect` per update** with one shared unit-quad mesh + one
shared material. Per-instance data (`CombatInstanceData`) is uploaded to
`_InstanceData` and indexed by `SV_InstanceID`; per-kind UV basis data is uploaded
to `_UvBasis` and indexed by the instance render id. There is no per-kind draw call
and no 1023-instance cap. The draw is recorded inside a URP
`ScriptableRendererFeature` (`Combat Indirect Render Feature` on
`Renderer2D.asset`), because the 2D Renderer does not execute immediate-mode
`Graphics.RenderMesh*` calls and only runs passes tagged `LightMode = Universal2D`.

## Restrictions

Render state must not define gameplay domain or faction by itself. Domain still
comes from `ProjectileTag` or `AoeTag`; faction comes from `CombatFaction`.

Spawn pooling must not key on `CombatRenderKindId`. Reuse can claim any disabled
slot in the matching archetype and must overwrite the kind id from the current
spawn command.

## Lifetime

Render components live on projectile/AOE reusable entities. Render resources live
with the owning combat root and are released on root teardown. The per-kind UV
basis GPU buffer is owned and disposed by `CombatRenderResourceRegistry`.

## Ordering

Render preparation runs after simulation/apply. Batched render submission runs in
presentation, reads `CombatRenderComponent`, and in one active-only scatter pass
fills a single `NativeList<CombatInstanceData>` directly from each entity's own
component. The registry ensures the static per-kind `_UvBasis` table is current,
rebuilding it only when dirty. The system uploads the instance list to
`_InstanceData`, writes the indirect args (`instanceCount` = active count), binds
`_InstanceData` and `_UvBasis` with `Material.SetBuffer` (not
`MaterialPropertyBlock`, which no-ops for indirect draws), and publishes the draw
inputs to a static handoff. A `ScriptableRendererFeature` on `Renderer2D.asset`
then issues one `DrawMeshInstancedIndirect` inside the 2D render pass. It does not
use shared-component filters or `ToComponentDataArray`.

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
