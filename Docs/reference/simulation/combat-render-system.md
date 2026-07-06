# Combat Render System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed doc for how combat sprite visuals (projectiles + AOEs) are
drawn. Use [index.md](./index.md) for the simulation overview and aspect map, and
[render-batch-data.md](../../contracts/render-batch-data.md) for the ECS render
data contract.

## Summary

Every active projectile and AOE, across every registered sprite kind, is drawn in
**one `DrawMeshInstancedIndirect` per update** using a shared unit-quad mesh, a
shared material, and one shared `SpriteAtlas` page. Per-instance data (a compact
2D transform + packed render metadata) travels to the GPU in a `StructuredBuffer`
indexed by `SV_InstanceID`. The atlas is static, so each registered kind's affine
UV basis is uploaded once to a separate `_UvBasis` `StructuredBuffer` and looked
up by `renderId` in the shader. There is no per-kind mesh/material, no per-kind
draw call, and no 1023-instance cap.

The draw is **recorded inside a URP `ScriptableRendererFeature`**, not issued with
the immediate `Graphics.RenderMesh*` API; the project's 2D Renderer does not
execute immediate render requests (see Critical Constraints).

Non-goals:

- per-projectile / per-AOE `SpriteRenderer` GameObjects
- one draw call per sprite kind
- runtime atlas packing (the atlas is authored + packed in the Editor)

## Data Flow

```text
CombatRenderPrepareSystem (Burst, PresentationSystemGroup, OrderFirst)
  -> writes CombatRenderComponent.Rotation + Position (2x2 basis, position,
     RenderZ) for each active render entity from kinematics + CombatRenderAuthoring

CombatBatchedRenderSystem (PresentationSystemGroup)
  -> CompleteDependency()
  -> scatter projectile + AOE queries into ONE NativeList<CombatInstanceData>
     (Rotation + Position + RenderMeta from CombatRenderComponent)
  -> registry.EnsureUvBasisBuffer() only rebuilds the per-kind UV table when dirty
  -> EnsureInstanceCapacity (grow-by-doubling), instanceBuffer.SetData
  -> populate args GraphicsBuffer (IndirectDrawIndexedArgs; instanceCount = active)
  -> SharedMaterial.SetBuffer("_InstanceData", instanceBuffer)
  -> SharedMaterial.SetBuffer("_UvBasis", uvBasisBuffer)
  -> CombatIndirectRenderData.Publish(mesh, material, argsBuffer)   [static handoff]

CombatIndirectRenderFeature : ScriptableRendererFeature (on Renderer2D.asset)
  -> RecordRenderGraph: raster pass, render attachment = active color texture
  -> RasterCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, 0, args)  [ONE call]

Shader "Combat/AtlasIndirectSprite" (Pass LightMode = Universal2D, #pragma target 4.5)
  -> inst = _InstanceData[SV_InstanceID]
  -> basis = _UvBasis[inst.renderMeta & 0x7FFFFFFF]
  -> world.xy = inst.rotation 2x2 basis * positionOS.xy + inst.position.xy
  -> clip = TransformWorldToHClip(float3(world.xy, inst.position.z))
  -> uv   = basis.originU.xy + quad.x*basis.originU.zw + quad.y*basis.v.xy
  -> sample _MainTex (the atlas page)
```

Zero-active and no-mesh-registered frames publish nothing (`HasWork = false`) and
issue no draw.

## Key Types

- **`CombatInstanceData`** - the per-instance GPU record, defined identically in
  C# (`[StructLayout(Sequential)]`) and HLSL. Stride **32 bytes**:
  `float4 Rotation` (m00, m01, m10, m11) + `float3 Position` (world x, world y,
  RenderZ) + `int/uint RenderMeta` (4). `RenderMeta` stores `renderId` in bits
  0..30 and the CPU-only align-to-velocity flag in bit 31.
  `CombatBatchedRenderSystem.OnCreate` asserts `SizeOf == 32` so a future field
  cannot silently desync the shader stride.
- **`CombatRenderComponent`** - per-entity render inputs: prepared
  compact 2D basis/position plus packed `RenderMeta`. It no longer carries
  `UvOriginU`/`UvV`; the component remains binary-identical to `CombatInstanceData`
  and is uploaded with zero-copy `AddRange`.
- **`CombatRenderAuthoring`** - CPU-only per-entity base visual transform
  (`BaseScale`, `BaseSin`, `BaseCos`, stride 16). It is seeded by spawn commands
  and read by render preparation; base rotation/scale is no longer stashed in
  spare matrix cells.
- **`CombatUvBasis`** - per-kind GPU record (`Vector4 originU`, `Vector4 v`,
  stride 32) owned by `CombatRenderResourceRegistry` and exposed as `_UvBasis`.
- **`CombatIndirectRenderData`** - static handoff (`Mesh`, `Material`,
  `GraphicsBuffer ArgsBuffer`, `bool HasWork`, `Publish`/`Clear`). Bridges the ECS
  system (main thread, Update) to the render feature (render loop, same frame).
  Mirrors the `CombatVfxRoot.Instance` handoff pattern.
- **`CombatRenderResourceRegistry`** - owns the shared `Mesh` (unit quad),
  `Material` (`Combat/AtlasIndirectSprite`), the `SpriteAtlas` reference, and the
  registry-owned `_uvBasisBuffer`. `Register(...)` computes each kind's UV basis
  and binds the atlas page as the material's texture. `EnsureUvBasisBuffer()`
  rebuilds the GPU table only when kinds change. `Unregister()` destroys the
  mesh/material and disposes the UV buffer it created (never the atlas asset).

## The UV Basis

The atlas is a pure pixel source. Its packer may rotate a sprite 90 degrees when
packing (`enableRotation`), so an axis-aligned UV rect cannot reproduce a sprite's
orientation. Instead each registered kind has an affine basis:

```text
atlasUV = UvOriginU.xy + quadUV.x * UvOriginU.zw + quadUV.y * UvV.xy
```

That basis now lives in the per-kind `_UvBasis` GPU table, not in each instance.

`CombatRenderResourceRegistry.ComputeUvBasis` derives it from the sprite's actual
vertex-to-UV mapping: it classifies the bottom-left / bottom-right / top-left
corners by local vertex position (BL minimizes x+y, BR maximizes x-y, TL
maximizes y-x; exact for the 4-corner Full Rect sprites this atlas uses) and
reads their real atlas UVs. This reproduces any packing rotation/flip so the
sprite always renders upright.

Two different rotations live in two different spaces and must not be confused:

- **World orientation** is in the compact 2x2 rotation*scale basis plus position
  (velocity alignment + the prefab's authored `VisualRotationDegrees`). The
  prefab owns this.
- **Atlas packing orientation** is in the UV basis. The renderer undoes it.

Neither can do the other's job.

## Critical Constraints

- **The `Combat/AtlasIndirectSprite` shader must be in Always Included Shaders (or
  otherwise anchored).** `CombatRenderResourceRegistry.EnsureSharedResources` builds
  the material at runtime with `new Material(Shader.Find("Combat/AtlasIndirectSprite"))`.
  Unity's build stripper only ships assets reachable through the static reference
  graph (a scene/prefab/`.mat`/`Resources/` referencing it by GUID). A `Shader.Find`
  string lookup is invisible to that analysis, no `.mat` asset references this shader,
  and it does not live in `Resources/`; in a player build the shader is stripped,
  `Shader.Find` returns `null`, `EnsureSharedResources` throws, and the first
  `Register(...)` call aborts skill/render setup so nothing spawns or draws. Works fine
  in the Editor (all assets loaded), fails only in builds. Fix: add the shader to
  Project Settings > Graphics > Always Included Shaders (done; see
  `ProjectSettings/GraphicsSettings.asset`, guid `46d20b6f7f8d4e6b9a4fdb80abf1c395`).
  The shader is authored correctly; it is just undiscoverable by static analysis. A more
  precise alternative is to serialize a real `.mat` asset (pins exact variants and puts
  the shader back on the reference graph) instead of `Shader.Find`.
- **URP 2D Renderer requires a RendererFeature + `Universal2D` LightMode.** The
  active renderer is the URP 2D Renderer (`UniversalRP.asset` > `Renderer2D.asset`).
  It only executes shader passes tagged `LightMode = Universal2D`, and it does not
  execute immediate-mode `Graphics.RenderMesh*` render requests at all; the draw must
  be recorded inside a `ScriptableRenderPass`. The `Combat Indirect Render Feature`
  must be added to `Renderer2D.asset` in the Inspector; without it, nothing draws.
- **Indirect StructuredBuffers must be bound with `Material.SetBuffer`.** Both
  `_InstanceData` and `_UvBasis` are set on the material this way. A `GraphicsBuffer`
  set via `MaterialPropertyBlock` silently does not take effect for indirect draws
  (known Unity behavior).
- **The compact basis preserves the previous `mul(M, v)` result for 2D quads.**
  Quad vertices have local `z == 0`, so the shader only needs m00, m01, m10,
  m11, world x/y, and RenderZ.
- **UVs come from `sprite.uv` (+ `sprite.vertices`), never `sprite.rect`.** For a
  packed sprite, `sprite.rect` is in the original texture's space while
  `sprite.texture` is the atlas page; mixing them yields out-of-range UVs.
- **The atlas must be packed at runtime.** If it is not (for example, a stale import
  after changing packing settings), `SpriteAtlas.GetSprite(...)` returns sprites
  pointing at their original source textures; binding one and sampling the rest
  produces corrupted output. Keep `SpritePackerMode` at "Sprite Atlas V2 - Enabled"
  (`5`); reimport the atlas after changing its packing settings. Builds always pack.
- **Single page is required.** One draw call binds one atlas texture, so every combat
  sprite must pack onto one page (extra pages would force per-page draws + bucketing).
  This is enforced by atlas authoring / Pack Preview, not by code; the earlier
  per-registration single-page reference-equality guard was removed because it
  false-failed on the transient unpacked state above.

Also: `RenderMeshInstanced` / `RenderMeshIndirect` (immediate) were both tried and
abandoned here. The former cannot deliver per-instance UV data (its only per-instance
channels are `objectToWorld`/`renderingLayerMask`/`prevObjectToWorld`; its
`MaterialPropertyBlock` is uniform), and neither immediate call is executed by the
2D renderer. This is the same 2D-renderer incompatibility that ruled out Entities
Graphics / BatchRendererGroup earlier.

## Zero Structural Churn

Adding indirect rendering added no ECS component to entities beyond the existing
render components and changed no spawn/despawn/reuse structural behavior. Pool
reuse/despawn still only flips the enableable `Active` (sprite visibility derives
from it, and from `ArmingTag` during arming — there is no separate render gate) and
rewrites value components. The per-frame instance/args GPU buffers are owned by
`CombatBatchedRenderSystem`; the static per-kind UV buffer is owned by
`CombatRenderResourceRegistry`. They are disposed by their owners and reference no
ECS memory.

## Performance Notes

- One `DrawMeshInstancedIndirect` per camera per update for all combat sprites, no
  1023 cap.
- The scatter is a single main-thread active-only pass into one persistent
  `NativeList`; the instance buffer grows by doubling and never shrinks.
- Per-instance upload is 32 bytes per active entity; static per-kind UV data is
  uploaded only when the registry changes.
- The atlas **texture** is not per-frame traffic. Its pixels are GPU-resident once
  Unity loads/packs the atlas, and it is bound to the material a single time at
  registration (`SharedMaterial.mainTexture = packedSprite.texture` in
  `CombatRenderResourceRegistry.Register`) — the shader samples `_MainTex` with no
  per-frame texture upload. Together with the per-kind UV buffer, both static inputs
  (atlas pixels + UV basis) are init-time only; only the compact 2D instance
  records stream each frame.
- The ~13-24 ms idle-frame cost seen after busy scenes is **not** this system: it
  is `CompleteDependency()` syncing the non-shrinking idle simulation pool. See
  performance/idle-cost notes, not the render path.

## Related

- [Render Batch Data contract](../../contracts/render-batch-data.md)
- [VFX System](./vfx-system.md) (the handoff pattern this mirrors)
- [Presentation And Feedback layer](../../layers/presentation-and-feedback.md)
- [Runtime Frame flow](../../flows/runtime-frame.md)
