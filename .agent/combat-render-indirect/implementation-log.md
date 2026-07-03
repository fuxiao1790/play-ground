# Combat Render Indirect — Implementation Log

## Status

**Complete and verified on-screen (2026-07-03).** Combat sprites (projectiles + AOEs, all kinds)
render in a **single `DrawMeshInstancedIndirect` per update** through the URP 2D Renderer, sampling
one shared atlas, with correct position / rotation / scale / orientation. Confirmed via Frame
Debugger (one "Combat Indirect Sprites" event inside the Renderer2D pass) and visual check.

Getting here required fixing **eight** distinct problems, several of which masked each other — the
chronicle below documents each because most are non-obvious URP/SpriteAtlas gotchas likely to recur.

(This supersedes the earlier "Blocked" snapshot of this file, which was a mid-implementation state
written before the URP-2D-renderer submit path and the atlas/UV debugging.)

## Why this rework existed

The prior `.agent/combat-render-atlas/` plan built a correct atlas foundation (shared unit-quad
mesh, shared material, per-entity `UvRect`, full TRS matrix per entity, Burst prepare system) but
submitted with `Graphics.RenderMeshInstanced` + `MaterialPropertyBlock.SetVectorArray`. That path
is a **functional dead end** on two counts, confirmed against Unity 6000.4 docs:

- `RenderMeshInstanced`'s only per-instance channels are `objectToWorld` / `renderingLayerMask` /
  `prevObjectToWorld`; any other custom struct member is *"ignored by instanced rendering."*
- Its `MaterialPropertyBlock` is documented only *"to supplement L2 data and occlusion data"* — it
  is **uniform** for the draw, not a per-instance array. (Per-instance MPB arrays are a
  `DrawMeshInstanced` feature that was **not** carried into `RenderMeshInstanced`.)

So a per-instance atlas UV rect could never reach the shader — every instance would sample one
broadcast UV. It was also capped at 1023 instances/draw. `RenderMeshIndirect` (via a
`StructuredBuffer` indexed by `SV_InstanceID`) fixes both: arbitrary per-instance data **and** no
1023 cap → all active instances in one command.

## Final architecture

### Data flow

```
CombatRenderPrepareSystem (Burst, PresentationSystemGroup OrderFirst)
  -> writes CombatRenderElement.objectToWorld (full TRS: R/S/T + RenderZ) per active entity
CombatBatchedRenderSystem (PresentationSystemGroup)
  -> CompleteDependency()
  -> Scatter projectileRenderQuery + aoeRenderQuery into ONE NativeList<CombatInstanceData>
     (each = objectToWorld + UV basis, read from CombatRenderElement + CombatRenderComponent)
  -> EnsureInstanceCapacity (grow-by-doubling), instanceBuffer.SetData
  -> populate args GraphicsBuffer (IndirectArguments, IndirectDrawIndexedArgs; instanceCount = active)
  -> SharedMaterial.SetBuffer("_InstanceData", instanceBuffer)
  -> CombatIndirectRenderData.Publish(mesh, material, argsBuffer)   [static handoff]
CombatIndirectRenderFeature : ScriptableRendererFeature (added to Renderer2D.asset)
  -> RecordRenderGraph: raster pass, render attachment = active color, culling disabled
  -> RasterCommandBuffer.DrawMeshInstancedIndirect(mesh, 0, material, 0, argsBuffer)  [ONE call]
Shader Combat/AtlasIndirectSprite (Pass LightMode=Universal2D, #pragma target 4.5)
  -> reads StructuredBuffer<CombatInstanceData>[SV_InstanceID]
  -> worldPos = mul(objectToWorld, posOS); clip = TransformWorldToHClip(worldPos)
  -> uv = uvOriginU.xy + quad.x*uvOriginU.zw + quad.y*uvV.xy ; return SAMPLE(_MainTex, uv)
```

Zero-active / no-mesh frames publish nothing (`HasWork = false`) and issue no draw.

### Key types

- `CombatInstanceData` (C# `[StructLayout(Sequential)]` **and** HLSL, stride **96**):
  `Matrix4x4 objectToWorld` (64) + `Vector4 uvOriginU` (16: origin.xy, uAxis.xy) +
  `Vector4 uvV` (16: vAxis.xy, 0, 0). `OnCreate` asserts `SizeOf == 96`.
- `CombatRenderComponent.UvOriginU` / `.UvV` (float4×2) — per-entity affine atlas-UV basis
  (replaced the old single `float4 UvRect`). Computed once at `Register` time, copied onto entities
  by existing apply plumbing, read in `Scatter`.
- `CombatIndirectRenderData` (static) — `Mesh` / `Material` / `GraphicsBuffer ArgsBuffer` /
  `bool HasWork`; `Publish(...)` / `Clear()`. Bridge between the ECS system (main thread, runs in
  Update) and the render feature (render loop, same frame). Mirrors the `CombatVfxRoot.Instance`
  handoff pattern already used by VFX.

### Files changed / added

- **Added** `Assets/Shaders/CombatAtlasIndirectSprite.shader` — indirect/StructuredBuffer shader,
  `LightMode=Universal2D`, `#pragma target 4.5`, affine UV basis. (Old
  `CombatAtlasInstancedSprite.shader` left in place, unused/dead.)
- **Added** `Assets/Scripts/System/Common/CombatIndirectRenderFeature.cs` — `CombatIndirectRenderData`
  static handoff + `CombatIndirectRenderFeature` (RenderGraph raster pass) + platform-agnostic wiring.
- **`Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`** — rewritten submit: scatter both
  queries into one instance list, GraphicsBuffers (instance Structured grow-by-doubling; args
  IndirectArguments), `Material.SetBuffer`, publish to handoff, `supportsIndirectArgumentsBuffer`
  guard. Deleted `MaterialPropertyBlock`/`_uvRectScratch`/`MaxInstancesPerDraw` 1023-chunk loop.
- **`Assets/Scripts/System/Common/CombatRenderComponents.cs`** — `CombatInstanceData` struct;
  `UvOriginU`/`UvV` on `CombatRenderComponent` and `CombatRenderResourceEntry`; `ComputeUvBasis`;
  `Register` computes the UV basis from `sprite.uv`+`sprite.vertices`, binds `packedSprite.texture`;
  removed the single-page guard + `AtlasTexture` field; loads `Combat/AtlasIndirectSprite`; dropped
  `enableInstancing`.
- **`Assets/Scripts/PlayGround.Runtime.asmdef`** — added `Unity.RenderPipelines.Core.Runtime` +
  `Unity.RenderPipelines.Universal.Runtime`.
- **Manual, in Editor:** added `Combat Indirect Render Feature` to `Assets/Settings/Renderer2D.asset`.

## Debugging chronicle (the eight problems, in the order hit)

Each of these alone produced "nothing renders" or visibly wrong output; they had to be cleared in
sequence because each masked the next.

1. **Matrix convention (self-inflicted false fix).** Initially changed the vertex transform to
   `mul(v, M)` on a shaky "StructuredBuffer loads transposed" assumption, untestable at the time
   because the draw wasn't executing yet. **Truth:** Unity `Matrix4x4` and HLSL are *both*
   column-major, so a `float4x4` read from a `StructuredBuffer` loads **untransposed** → the
   conventional `mul(M, v)` is correct. Reverted. Lesson: verify on-screen, don't reason about
   matrix packing in the abstract.

2. **`MaterialPropertyBlock.SetBuffer` no-ops for indirect draws.** Binding the instance
   `StructuredBuffer` via `matProps.SetBuffer` silently does nothing for indirect draws (known Unity
   issue). **Fix:** `Material.SetBuffer` directly, rebound every frame (survives buffer growth +
   material rebuild).

3. **Missing `LightMode = Universal2D` pass tag.** The project's active renderer is the URP **2D
   Renderer** (`UniversalRP.asset` → `Renderer2D.asset`). It only executes passes tagged
   `Universal2D`; a pass with no `LightMode` defaults to `SRPDefaultUnlit`, which the 2D renderer
   skips. **Fix:** add `Tags { "LightMode" = "Universal2D" }`. (The old atlas shader lacked this too
   — the atlas plan's "RenderMeshInstanced already works on this renderer" was untested; it never
   rendered.)

4. **`Graphics.RenderMeshIndirect` is not executed by the 2D Renderer at all.** Even with the tag,
   matrix, and buffer correct, the draw never appeared in the Frame Debugger's Renderer2D pass. The
   immediate-mode `Graphics.RenderMesh*` render-request API isn't picked up by the 2D renderer (same
   class of incompatibility that killed the earlier Entities Graphics attempt). **Fix:** record the
   draw inside a `ScriptableRendererFeature` + RenderGraph raster pass on `Renderer2D.asset`
   (`RasterCommandBuffer.DrawMeshInstancedIndirect`), fed by the `CombatIndirectRenderData` handoff.
   *(Misstep: assumed the VFX indirect draws visible in the same Frame Debugger were a copyable
   project pattern — they're Unity Visual Effect Graph output, not applicable.)*

5. **UV rect computed from `sprite.rect` / atlas dimensions (wrong for packed sprites).** After the
   draw finally executed, sprites were invisible. A shader bisection (fixed clip-space red quad →
   red at correct positions → sample-with-forced-alpha → black → visualize UV → saturated) proved the
   UVs were ≥1. For a `SpriteAtlas`-packed sprite, `sprite.rect` is in the **original texture's**
   pixel space while `sprite.texture` is the atlas page — dividing `rect` by atlas dimensions yields
   garbage. **Fix (interim):** use `sprite.uv` (real normalized atlas UVs).

6. **Atlas packer rotation broke a plain UV rect.** With `enableRotation` on, the packer rotates
   some sprites 90° in the atlas; a `sprite.uv` **min/max axis-aligned rect** loses that rotation, so
   those sprites rendered turned/mirrored ("some facing the other way"). Rejected the "disable atlas
   rotation" shortcut — the atlas is a pure pixel source, orientation belongs to the renderer/prefab,
   and disabling rotation also risks multi-page overflow. **Fix:** replace the UV rect with an
   **affine UV basis** (`origin + U-axis + V-axis`) from the sprite's actual vertex→UV mapping
   (`ComputeUvBasis`: classify BL/BR/TL corners by local vertex position, read their atlas UVs). This
   reproduces any packing rotation/flip so the sprite always renders upright; the entity matrix
   independently handles world orientation. (Object rotation lives in the 4×4 matrix — world space;
   packing rotation lives in the UV basis — texture space. Different spaces; neither can do the
   other's job.)

7. **Single-page guard misfired.** `Register` compared `packedSprite.texture` reference-equality
   across sprites and threw "atlas has grown to multiple pages." With 6 sprites at ≤64px that's
   impossible — the real trigger was the atlas being momentarily **unpacked** (see #8), where
   `GetSprite` returns sprites on their differing *source* textures. The guard was a proxy for
   "single page" that's trivially true when packed and falsely fails when unpacked. **Fix:** removed
   the guard and the `AtlasTexture` field; bind `packedSprite.texture` idempotently (last wins).
   Single-page is an atlas-authoring property (Pack Preview), not worth re-asserting per-sprite.

8. **Silent corruption from an unpacked atlas (root cause of #7).** After removing the guard,
   sprites rendered corrupted. A diagnostic log proved `GetSprite` was returning the **source**
   textures (`tilemap` 305×186, `tilemap_packed` 288×176) with source-space UVs — the atlas was
   **not packed at runtime** (toggling `enableRotation` left the imported atlas stale). Because the
   combat sprites come from 3 source textures, we bound one and every sprite from the others sampled
   the wrong image. **Fix:** reimport the atlas (right-click → Reimport) so it repacks; `GetSprite`
   then returns the single atlas page. **Standing requirement:** the atlas must be packed at runtime
   (`SpritePackerMode = 5`, "Sprite Atlas V2 - Enabled"; builds always pack).

## Constraints / gotchas for the future

- **URP 2D Renderer:** custom draws need `LightMode=Universal2D` AND must be recorded in a
  `ScriptableRendererFeature` — the immediate `Graphics.RenderMesh*` API is ignored. See memory
  `reference_urp_2d_renderer_lightmode`.
- **Indirect StructuredBuffer:** bind with `Material.SetBuffer`, never `MaterialPropertyBlock`.
- **Matrix in StructuredBuffer:** Unity `Matrix4x4` → HLSL `float4x4` is untransposed (both
  column-major); use `mul(M, v)`.
- **SpriteAtlas UVs:** use `sprite.uv` (+ `sprite.vertices` for the affine basis), never
  `sprite.rect`/atlas-dimension math. Handle packer rotation in UV space, not the matrix.
- **Atlas must be packed at runtime** for `GetSprite` to return atlas-page sprites; a stale/unpacked
  atlas resolves to source textures → corruption. Reimport after changing packing settings.
- **Single page is required** for the one-draw-call design (one bound texture; extra pages force
  per-page draws + bucketing) — enforced by atlas authoring, not by code.
- **Feature must be on `Renderer2D.asset`** (manual Inspector step); without it nothing draws.

## Verified vs remaining

Verified on-screen: single indirect draw in the 2D pass; correct transform + orientation + atlas
sampling across kinds; no corruption after atlas reimport. Prior static checks: `dotnet build` of
Runtime + PlayMode csprojs green (pre-URP-reference; re-verify after Unity regenerates projects).

Remaining (hygiene, not behavior):
- Update plan tasks `002`/`003` (UV basis + 96-byte stride) and `005` ("single page" not "single
  texture reference"); task `006` docs/tests.
- Update `Docs/contracts/render-batch-data.md` for the indirect/single-draw/RendererFeature design.
- Optional: re-add a **clear** "atlas not packed / sprite resolved to non-atlas texture" fail-fast
  (the removed guard's absence turned the config issue in #8 into silent corruption).
