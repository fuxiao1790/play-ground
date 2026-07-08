# Render Batch Data

## Purpose

Define ECS render data used to batch projectile and AOE sprite visuals.

## Produced By

[Combat Bridge](../layers/combat-bridge.md) registers resources.
[ECS Simulation](../layers/ecs-simulation.md) writes runtime render components
and prepared compact 2D transforms.

## Consumed By

[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Current render data includes:

- `CombatRenderComponent` (the prepared compact 2D transform: `Rotation`
  float4, `Position` float3, plus packed `RenderMeta`; render id in bits 0..30,
  align-to-velocity in bit 31)
- `CombatRenderAuthoring` (CPU-only base visual scale/sin/cos, stride 16; seeded
  by spawn and read by render preparation)
- `CombatRenderKindId`
- `CombatInstanceData` - the per-instance GPU record (`Rotation` + `Position` +
  `RenderMeta`, stride 32), copied directly from `CombatRenderComponent` with
  `AddRange` and uploaded to `_InstanceData`
- `CombatUvBasis` - the per-kind GPU record (`OriginU` + `V`, stride 32),
  uploaded by `CombatRenderResourceRegistry` to `_UvBasis` and indexed by render id
- one shared, manually assembled `SpriteAtlas` asset
- one registry-owned capacity-baked mesh, material, `MeshFilter`, and
  `MeshRenderer`

See [Combat Render System](../reference/simulation/combat-render-system.md) for
the full submission design.

`CombatRenderKindId` is a plain `IComponentData` int, copied from the spawn
command's `RenderTypeId`. It is a kind identifier only; it does not partition
chunks and does not partition spawn pools. The same render id is packed into
`CombatRenderComponent.RenderMeta`, and the shader masks it out to index the
static per-kind `_UvBasis` table.

## Guarantees

The combat sprite atlas is one manually assembled `UnityEngine.U2D.SpriteAtlas`
asset, assigned via a serialized field on `CombatRoot` and threaded into
`CombatRenderResourceRegistry.ConfigureAtlas(...)`. There is no runtime packing:
`Register(...)` resolves the atlas's packed copy of the sprite via
`SpriteAtlas.GetSprite(name)`, binds that sprite's atlas page as the shared
material's texture, and computes each kind's affine UV basis from the packed
sprite's `uv`/`vertices`.

Every active projectile/AOE entity across every kind draws through one
registry-owned `MeshRenderer` with one shared capacity-baked mesh and one shared
material. The mesh contains repeated quads. Each quad carries its `_InstanceData`
slot in UV1 (`TEXCOORD1.x`), and `CombatBatchedRenderSystem` controls the active
range with `Mesh.SetSubMesh(0, activeCount * 6)`. There is no per-kind draw call
and no 1023-instance cap.

Sorting Layer order is part of the contract: combat VFX use `CombatVfx`, combat
sprites use `CombatSprites`, and actors remain on `Default`. The combat sprite
batch is one atomic renderer, so `CombatSprites` should not be used for content
that expects to interleave with individual projectile/AOE sprites.

## Restrictions

Render state must not define gameplay domain or faction by itself. Domain still
comes from `ProjectileTag` or `AoeTag`; faction comes from `CombatFaction`.

Spawn pooling must not key on `CombatRenderKindId`. Reuse can claim any disabled
slot in the matching archetype and must overwrite the kind id from the current
spawn command.

## Lifetime

Render components live on projectile/AOE reusable entities. Render resources live
with the owning combat root and are released on root teardown. The capacity mesh,
renderer GameObject, material, and per-kind UV basis GPU buffer are owned and
disposed by `CombatRenderResourceRegistry`. The per-frame `_InstanceData` buffer
is owned by `CombatBatchedRenderSystem`.

## Ordering

Render preparation runs after simulation/apply. Batched render submission runs in
presentation, reads `CombatRenderComponent`, and in one scatter pass fills a
single `NativeList<CombatRenderComponent>` directly from each entity's own
component. The registry ensures the static per-kind `_UvBasis` table is current
and that the baked mesh capacity can cover the current active count. The system
uploads the instance list to `_InstanceData`, binds `_InstanceData` and `_UvBasis`
with `Material.SetBuffer`, and sets the mesh submesh index count to
`activeCount * 6`. URP 2D then discovers and sorts the registry-owned
`MeshRenderer` through its normal renderer pass.

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
