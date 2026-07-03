# Combat Render Indirect

## End Goal

One `Graphics.RenderMeshIndirect` call per update that draws **every** active combat
render entity (all projectiles + all AOEs, all kinds) in a **single draw call**, sampling
one shared atlas that contains **every** sprite any ECS combat entity may need. Per-instance
data (object-to-world matrix + atlas UV rect) is delivered to the shader through a
`StructuredBuffer` indexed by `SV_InstanceID` — the only channel that actually carries
per-instance custom data under the indirect path.

This supersedes `.agent/combat-render-atlas/` for the *submission* half only. That plan is
left in place, untouched; this one is a separate, additive effort that replaces its task-005
render system and its shader with an indirect design.

## Why This Plan Exists (what the atlas plan got wrong)

The atlas plan built the shared atlas correctly (shared mesh, shared material, per-entity
`UvRect`, full TRS matrix per entity) but submitted with `Graphics.RenderMeshInstanced` +
`MaterialPropertyBlock.SetVectorArray("_UvRect", …)`. That is a **functional dead end**,
confirmed against Unity 6000.4 docs:

- `Graphics.RenderMeshInstanced`'s only per-instance channels are the `objectToWorld` matrix
  and (optionally) `renderingLayerMask` / `prevObjectToWorld`. Any other custom struct member
  is *"ignored by instanced rendering."*
- Its `MaterialPropertyBlock` is documented only *"to supplement L2 data and occlusion data"* —
  it is **uniform** for the draw, not a per-instance array (the per-instance-MPB-array trick is
  a `DrawMeshInstanced` feature, not carried into `RenderMeshInstanced`).

So the current code cannot deliver a per-instance UV rect at all; every instance would sample
one broadcast UV. It is also capped at 1023 instances/draw, forcing a chunk loop — the opposite
of the single-draw-call goal.

`RenderMeshIndirect` fixes both at once: an arbitrary per-instance `StructuredBuffer` **and** no
1023 cap (bounded only by buffer size), so all active instances submit in one command.

Sources:
- Graphics.RenderMeshInstanced — Unity 6000.4 docs
- Graphics.RenderMeshIndirect — Unity 6000.4 docs

## What Already Exists And Is Reused Unchanged

- `CombatRenderElement.objectToWorld` — full TRS matrix: rotation (velocity-aligned cos/sin),
  scale (`VisualScale`, native sprite size folded), position (`m03`/`m13`), depth
  (`m23 = RenderZ`). Built by `CombatRenderMatrixUtility.ElementFor` in the parallel Burst
  `CombatRenderPrepareSystem` (and the apply systems). **Not touched.**
- `CombatRenderComponent.UvRect` (`float4`) — per-entity atlas UV rect, computed once at
  `Register(...)` time, carried on the entity. **Not touched.**
- `CombatRenderResourceRegistry` — `SharedMesh` (unit quad), `SharedMaterial`, `Atlas`,
  `Register(...)`/UV computation, single-page guard, `ConfigureAtlas(SpriteAtlas)`. Reused; only
  the shader it loads and the `enableInstancing` flag change (task 004).
- `CombatRenderPrepareSystem` (OrderFirst, active-only, Burst parallel) — unchanged.
- Active-only scatter over `projectileRenderQuery` + `aoeRenderQuery`, `CompleteDependency()`
  sync pattern — reused (both queries scatter into **one** combined buffer → one draw).
- Zero structural churn on spawn/despawn/reuse — unchanged; this plan touches only presentation
  submit + the shader. No component is added, removed, or re-shaped on any entity.

## What This Plan Introduces

- `CombatInstanceData` — a blittable struct `{ float4x4 objectToWorld; float4 uvRect; }` shared
  by C# (`[StructLayout(Sequential)]`) and HLSL (`struct` in the shader), stride **80 bytes**.
  See "Data Contract" below — 001/002/003 must agree on it exactly.
- Two `GraphicsBuffer`s owned by the render system:
  - instance buffer (`Target.Structured`, stride 80), grown when active count exceeds capacity;
  - indirect args buffer (`Target.IndirectArguments`, one `IndirectDrawIndexedArgs`).
- A rewritten shader (`CombatAtlasIndirectSprite.shader`) that reads
  `StructuredBuffer<CombatInstanceData>` by `SV_InstanceID`, builds clip position manually, and
  remaps UV from the per-instance rect — replacing the `UNITY_INSTANCING_BUFFER` /
  `unity_ObjectToWorld` shader.
- A single `Graphics.RenderMeshIndirect(rp, SharedMesh, argsBuffer, 1)` submit.

## What This Plan Removes (vs the atlas plan's implementation)

- `MaterialPropertyBlock.SetVectorArray("_UvRect", …)` per-instance delivery (non-functional).
- `_uvRectScratch`, `MaxInstancesPerDraw`, and the 1023-chunked `for` submit loop.
- `enableInstancing = true` on the shared material (irrelevant to manual-buffer indirect).
- `UNITY_INSTANCING_BUFFER` / `UNITY_ACCESS_INSTANCED_PROP` / `unity_ObjectToWorld` reliance in
  the shader.

## Data Contract (single source of truth for 001/002/003)

```
C# (Assets/.../CombatRenderComponents.cs or the render system):

    [StructLayout(LayoutKind.Sequential)]
    struct CombatInstanceData
    {
        public Matrix4x4 objectToWorld; // 64 bytes, column-major (Unity native)
        public Vector4   uvRect;        // 16 bytes: xy = atlas origin (0..1), zw = size (0..1)
    }                                   // stride = 80

HLSL (shader):

    struct CombatInstanceData
    {
        float4x4 objectToWorld;
        float4   uvRect;
    };
    StructuredBuffer<CombatInstanceData> _InstanceData;
```

**Matrix convention is the top correctness risk.** Unity `Matrix4x4` is column-major in memory;
reinterpreted as an HLSL `float4x4` from a `StructuredBuffer`, the multiply order must match or
sprites render sheared/rotated/offset. Baseline: `mul(inst.objectToWorld, float4(posOS, 1))`.
If a smoke test shows wrong orientation, the fix is a `transpose(...)` (or swapping to
`mul(v, m)`) — task 001 must verify this on-screen, not assume it.

## Tasks

- [001-indirect-shader.md](001-indirect-shader.md) — new `CombatAtlasIndirectSprite.shader`
  reading `StructuredBuffer<CombatInstanceData>` by `SV_InstanceID`.
- [002-instance-and-args-buffers.md](002-instance-and-args-buffers.md) — `CombatInstanceData`
  struct + the two `GraphicsBuffer`s (lifecycle, growth, indirect-args population).
- [003-render-system-rewrite.md](003-render-system-rewrite.md) — rewrite
  `CombatBatchedRenderSystem` to scatter into one instance list, upload, and issue **one**
  `RenderMeshIndirect`.
- [004-registry-material.md](004-registry-material.md) — registry loads the new shader, drops
  `enableInstancing`, keeps atlas `mainTexture`; decide buffer-binding site.
- [005-atlas-completeness.md](005-atlas-completeness.md) — the single-page atlas must contain
  **every** combat sprite the ECS may spawn (content + single-page enforcement).
- [006-docs-and-tests.md](006-docs-and-tests.md) — rewrite the render-batch contract for the
  indirect/single-draw design; adjust PlayMode fixtures.

Dependencies: 002 is foundational (defines the struct). 001 depends on the Data Contract only
(parallel with 002). 003 depends on 001 + 002. 004 depends on 001. 005 is independent content
work (can proceed any time; gates real gameplay, not compilation). 006 depends on 003/004.

## Constraints And Invariants

- **Exactly one draw call per update.** Both `projectileRenderQuery` and `aoeRenderQuery`
  scatter into one shared instance buffer; a single `RenderMeshIndirect` draws the union. No
  per-kind, per-domain, or per-faction splitting (domain/faction identity must never move onto
  render data — same restriction as the atlas plan's `render-batch-data.md`).
- **No 1023 cap.** The `RenderMeshInstanced` limit is gone; capacity is the instance buffer
  size, grown on demand. If active count is 0, skip the submit entirely.
- **Single-page atlas holding every combat sprite.** `Register(...)`'s existing throw (sprite
  not a packable) and single-page guard (different packed texture) are the enforcement; task 005
  is the content side that keeps them from firing in real scenes.
- **Zero structural churn / prepare stays active-only.** Unchanged from the atlas plan — this
  plan adds no ECS component and does not touch prepare or the apply systems.
- **GraphicsBuffer lifetime owned by the render system**, disposed in `OnDestroy` (mirrors how
  the registry owns `SharedMesh`/`SharedMaterial`). Buffers reference no ECS memory.
- **Platform support required:** `RenderMeshIndirect` needs
  `SystemInfo.supportsIndirectArgumentsBuffer`. Assumed true on target (desktop/URP); task 003
  notes a guarded early-out if it ever isn't.

## Explicitly Out Of Scope

- The ~24 ms idle-frame cost. That is `CompleteDependency()` syncing the non-shrinking idle
  simulation pool (see `.agent/idle-combat-system-cost/`), **not** the submit path — this rework
  will not change it. Called out so nobody expects indirect to fix idle stalls.
- Burst-job buffer filling. Task 003 fills the instance list on the main thread and uploads via
  `SetData` (correctness first, still one draw call). A job-written persistent compute buffer is
  a later optimization, noted in 003 but not built here.
- Deleting the old `combat-render-atlas` plan or its `RenderMeshInstanced` shader file — left in
  place per user direction; task 004 just points the registry at the new shader.

## Open Questions

1. **Buffer bind site:** `RenderParams.matProps.SetBuffer(...)` (per-frame, leaves shared
   material clean) vs `SharedMaterial.SetBuffer(...)` (once on (re)create). Task 004 leans
   `matProps` for cleanliness; either is correct. Rebind is mandatory whenever the buffer is
   recreated on growth.
2. **Instance buffer growth policy:** dispose+recreate at next-power-of-two on overflow vs a
   fixed generous cap. Task 002 proposes grow-by-doubling from a small initial capacity.
