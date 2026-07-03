# 007 — ScriptableRendererFeature (2D-renderer draw path)

## Why this task exists (discovered during implementation)

Frame Debugger proved that `Graphics.RenderMeshIndirect` — the immediate-mode API task 003 used —
is **never executed by URP's 2D Renderer**. The draw simply does not appear in the
"Renderer2D Pass - Default" event list, even with the matrix, `Material.SetBuffer` binding, and
`LightMode=Universal2D` tag all correct. The 2D renderer does not pick up `Graphics.RenderMesh*`
render requests at all (same class of incompatibility that killed the Entities Graphics attempt).

The reliable way to land an indirect draw in the 2D render loop is to **record it from inside a
`ScriptableRenderPass`** added to `Renderer2D.asset`. `RasterCommandBuffer.DrawMeshInstancedIndirect`
exists and accepts our existing `IndirectDrawIndexedArgs` args buffer unchanged.

This does NOT invalidate tasks 001–004: the shader, the `StructuredBuffer`/`SV_InstanceID` design,
the buffers, and the material were all correct — they just never got drawn. Only the *final submit*
moves from an immediate `Graphics` call to a render-pass recording.

## Design

Handoff mirrors the `CombatVfxRoot.Instance` pattern the VFX system already uses: the ECS system
prepares GPU state on the main thread and publishes it; the render feature consumes it in-pass.

- **`CombatIndirectRenderData`** (static handoff): `Mesh`, `Material`, `GraphicsBuffer ArgsBuffer`,
  `bool HasWork`; `Publish(...)` / `Clear()`.
- **`CombatBatchedRenderSystem`**: unchanged through `PopulateArgs`. `Submit(...)` now binds the
  instance buffer with `Material.SetBuffer` (kept) and calls `CombatIndirectRenderData.Publish(...)`
  instead of `Graphics.RenderMeshIndirect`. Clears the handoff on the two no-draw early-outs
  (no mesh, zero active) and in `OnDestroy`.
- **`CombatIndirectRenderFeature : ScriptableRendererFeature`** + nested `ScriptableRenderPass`
  (RenderGraph, Unity 6000.4): `RecordRenderGraph` adds a raster pass, sets the active color
  texture as the render attachment, and in the render func calls
  `context.cmd.DrawMeshInstancedIndirect(mesh, 0, material, 0, argsBuffer)`. Injected at
  `AfterRenderingTransparents`. `AddRenderPasses` enqueues only when `HasWork`.

## Assembly

`PlayGround.Runtime.asmdef` gains `Unity.RenderPipelines.Universal.Runtime` +
`Unity.RenderPipelines.Core.Runtime` references (needed for `ScriptableRendererFeature`,
`UniversalResourceData`, and the RenderGraph raster-pass API).

## Manual wiring (one Editor step, like assigning the atlas)

Add the feature to the active renderer: select `Assets/Settings/Renderer2D.asset` →
**Add Renderer Feature → Combat Indirect Render Feature**. Without this step nothing draws (the
feature has to live on the renderer to run). This cannot be done reliably by editing files (needs
the script GUID Unity mints on import).

## Acceptance Criteria

- Frame Debugger shows a "Combat Indirect Sprites" event **inside** the Renderer2D pass, drawing
  the `Combat/AtlasIndirectSprite` material — exactly one `DrawMeshInstancedIndirect` per camera.
- Active projectiles/AOEs render at correct transform + atlas sub-rect.
- Zero-active and no-mesh frames issue no draw (handoff `HasWork == false`).

## Open follow-ups

- **Depth/ordering:** the pass attaches color only (ZWrite Off, ZTest LEqual). Ordering among
  combat instances is buffer order; scene depth is not tested. Fine for 2D transparent sprites;
  revisit if combat must depth-interleave with other 2D content.
- **External buffers in RenderGraph:** the instance/args buffers are bound outside the graph
  (material `SetBuffer` + direct args arg). If RenderGraph validation complains, import them via
  `renderGraph.ImportBuffer` + `builder.UseBuffer`. Not needed unless a warning/error appears.

## Dependencies

003 (system) + 001/004 (shader/material). Supersedes 003's `Graphics.RenderMeshIndirect` submit.

## Scope

Medium.
