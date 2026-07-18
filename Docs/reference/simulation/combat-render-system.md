# Combat Render System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed doc for how combat sprite visuals (projectiles + AOEs) are
drawn. Use [index.md](./index.md) for the simulation overview and aspect map, and
[render-batch-data.md](../../contracts/render-batch-data.md) for the ECS render
data contract.

## Summary

Every active projectile and AOE, across every registered sprite kind, is drawn by
one persistent `MeshRenderer` using a capacity-baked quad mesh, a shared material,
and one shared `SpriteAtlas` page. Per-instance data (a compact 2D transform and
packed render metadata) travels to the GPU in a `StructuredBuffer`; each baked
quad carries its instance slot in `TEXCOORD1.x`, which the shader uses to index
that buffer.

The atlas is static, so each registered kind's affine UV basis is uploaded once
to a separate `_UvBasis` `StructuredBuffer` and looked up by `renderId` in the
shader. There is no per-kind mesh/material, no per-kind draw call, and no
1023-instance cap.

The combat batch participates in normal URP 2D object sorting because it is a
real `MeshRenderer`. Its Sorting Layer is `CombatSprites`, below actor sprites on
`Default` and above combat VFX on `CombatVfx`.

Non-goals:

- per-projectile / per-AOE `SpriteRenderer` GameObjects
- one draw call per sprite kind
- runtime atlas packing (the atlas is authored + packed in the Editor)

## Data Flow

```text
CombatRenderPrepareSystem (Burst, PresentationSystemGroup, OrderFirst)
  -> writes CombatRenderComponent.Rotation + Position for each render entity

CombatBatchedRenderSystem (PresentationSystemGroup)
  -> CompleteDependency()
  -> scatter projectile + AOE queries into ONE NativeList<CombatInstanceData>
  -> registry.EnsureUvBasisBuffer() only rebuilds the per-kind UV table when dirty
  -> EnsureInstanceCapacity (grow-by-doubling), instanceBuffer.SetData
  -> registry.EnsureMeshCapacity(active) (grow-by-doubling)
  -> SharedMaterial.SetBuffer("_InstanceData", instanceBuffer)
  -> SharedMaterial.SetBuffer("_UvBasis", uvBasisBuffer)
  -> SharedMesh.SetSubMesh(0, SubMeshDescriptor(0, active * 6))

URP 2D DrawRenderer2DPass
  -> discovers the registry-owned MeshRenderer
  -> sorts it by Renderer.sortingLayerID / sortingOrder
  -> submits the active submesh range as one renderer draw

Shader "Combat/AtlasIndirectSprite" (Pass LightMode = Universal2D, #pragma target 4.5)
  -> slot = round(IN.slotIndex.x)
  -> inst = _InstanceData[slot]
  -> basis = _UvBasis[inst.renderMeta & 0x7FFFFFFF]
  -> world.xy = inst.rotation 2x2 basis * positionOS.xy + inst.position.xy
  -> clip = TransformWorldToHClip(float3(world.xy, inst.position.z))
  -> uv = basis.originU.xy + quad.x*basis.originU.zw + quad.y*basis.v.xy
  -> sample _MainTex (the atlas page)
```

Zero-active frames set the mesh submesh index count to zero. No-mesh-registered
frames have no registry-owned renderer to draw.

## Key Types

- **`CombatInstanceData`** - the per-instance GPU record, defined identically in
  C# (`[StructLayout(Sequential)]`) and HLSL. Stride **32 bytes**:
  `float4 Rotation` + `float3 Position` + `int/uint RenderMeta`.
  `CombatBatchedRenderSystem.OnCreate` asserts `SizeOf == 32`.
- **`CombatRenderComponent`** - per-entity render inputs: prepared compact 2D
  basis/position plus packed `RenderMeta`. It remains binary-identical to
  `CombatInstanceData` and is uploaded with zero-copy `AddRange`.
- **`CombatRenderAuthoring`** - CPU-only per-entity base visual transform seeded
  by spawn commands and read by render preparation.
- **`CombatUvBasis`** - per-kind GPU record (`Vector4 originU`, `Vector4 v`,
  stride 32) owned by `CombatRenderResourceRegistry` and exposed as `_UvBasis`.
- **`CombatRenderResourceRegistry`** - owns the shared capacity-baked `Mesh`,
  `Material` (`Combat/AtlasIndirectSprite`), persistent `MeshFilter` /
  `MeshRenderer` GameObject, the `SpriteAtlas` reference, and `_uvBasisBuffer`.
  `EnsureMeshCapacity(...)` grows the baked mesh by doubling and never shrinks
  it. `SetActiveInstanceCount(...)` sets the active submesh index range.
  `Unregister()` destroys the owned renderer, mesh/material, and UV buffer it
  created (never the atlas asset).

## The UV Basis

The atlas is a pure pixel source. Its packer may rotate a sprite 90 degrees when
packing, so an axis-aligned UV rect cannot reproduce a sprite's orientation.
Instead each registered kind has an affine basis:

```text
atlasUV = UvOriginU.xy + quadUV.x * UvOriginU.zw + quadUV.y * UvV.xy
```

That basis lives in the per-kind `_UvBasis` GPU table, not in each instance.
`CombatRenderResourceRegistry.ComputeUvBasis` derives it from the sprite's actual
vertex-to-UV mapping so rotated/packed sprites still render upright.

Two different rotations live in two different spaces and must not be confused:

- **World orientation** is in the compact 2x2 rotation*scale basis plus position.
- **Atlas packing orientation** is in the UV basis.

## Critical Constraints

- **The `Combat/AtlasIndirectSprite` shader must be in Always Included Shaders
  (or otherwise anchored).** The material is built at runtime with
  `Shader.Find("Combat/AtlasIndirectSprite")`, which build stripping cannot see.
- **URP 2D Renderer still requires `Universal2D` LightMode.** The combat batch is
  now a real `MeshRenderer`, so the normal 2D renderer pass discovers it through
  culling and Sorting Layer filtering. The shader pass still needs
  `LightMode = Universal2D`; without that tag, Renderer2D skips the pass.
- **StructuredBuffers are bound with `Material.SetBuffer`.** Both `_InstanceData`
  and `_UvBasis` are set on the shared material. There is one combat sprite
  renderer, so material-level binding remains the simplest source of truth.
- **The compact basis preserves the previous 2D quad transform.** Quad vertices
  have local `z == 0`, so the shader only needs m00, m01, m10, m11, world x/y,
  and RenderZ.
- **UVs come from `sprite.uv` and `sprite.vertices`, never `sprite.rect`.**
- **Registered sprites must have mesh vertices at their rect corners.** Import
  them as Full Rect, or ensure the art fills the rect. `ComputeUvBasis` treats
  the extreme vertices as rect corners; a Tight mesh with transparent margins
  produces a wrong basis and visibly distorted sprites. See
  [combat-atlas-tight-mesh-uv-distortion.md](./combat-atlas-tight-mesh-uv-distortion.md).
- **The atlas must be packed at runtime.** `SpriteAtlas.GetSprite(...)` must
  return sprites from the atlas page.
- **Single page is required.** One draw call binds one atlas texture.

`RenderMeshInstanced` / `RenderMeshIndirect` immediate calls remain rejected
because they are not scene `Renderer`s and are not executed by the 2D renderer.
The current baked-mesh approach is different because it submits through a real
`MeshRenderer`.

## Sorting Layer Contract

`CombatVfx`, `CombatSprites`, and `Default` are ordered bottom-to-top. The
`CombatSprites` layer is expected to contain this one batch renderer. Other
content should not be added to `CombatSprites` expecting per-object interleaving
with individual projectiles or AOEs, because the whole combat sprite batch is one
atomic `Renderer` from Unity's sorter point of view.

## Zero Structural Churn

Batched rendering adds no ECS component to entities beyond the existing render
components and changes no spawn/despawn/reuse structural behavior. Pool
reuse/despawn still only flips enableable state and rewrites value components.
The per-frame instance GPU buffer is owned by `CombatBatchedRenderSystem`; the
capacity mesh and static per-kind UV buffer are owned by
`CombatRenderResourceRegistry`. They are disposed by their owners and reference
no ECS memory.

## Performance Notes

- One `MeshRenderer` draw for all combat sprites, no 1023 cap.
- The scatter is a single main-thread active-only pass into one persistent
  `NativeList`; the instance buffer grows by doubling and never shrinks.
- The baked mesh grows by doubling and never shrinks; zero active entities use a
  zero-index submesh.
- Per-instance upload is 32 bytes per active entity; static per-kind UV data is
  uploaded only when the registry changes.
- The atlas texture is not per-frame traffic. Its pixels are GPU-resident once
  Unity loads/packs the atlas.
- The known idle-frame cost after busy scenes is simulation pool sync work, not
  this render path.

## Related

- [Render Batch Data contract](../../contracts/render-batch-data.md)
- [Tight-mesh UV distortion pitfall](./combat-atlas-tight-mesh-uv-distortion.md)
- [VFX System](./vfx-system.md)
- [Presentation And Feedback layer](../../layers/presentation-and-feedback.md)
- [Runtime Frame flow](../../flows/runtime-frame.md)
