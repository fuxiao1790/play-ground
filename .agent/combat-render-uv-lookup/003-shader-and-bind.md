# 003 — Shader UV lookup + render-system stride & bind

## Goal
Shader reads the UV basis from the per-kind `_UvBasis` buffer by the instance's
`renderId`; render system uploads the shrunk 68-byte instances and binds `_UvBasis`.

## Shader ([CombatAtlasIndirectSprite.shader](../../Assets/Shaders/CombatAtlasIndirectSprite.shader))
- `CombatInstanceData` → `{ float4x4 objectToWorld; uint renderMeta; }` (stride 68).
- Add `struct CombatUvBasis { float4 originU; float4 v; };` and
  `StructuredBuffer<CombatUvBasis> _UvBasis;`.
- In `vert`:
  ```hlsl
  CombatInstanceData inst = _InstanceData[IN.instanceID];
  uint id = inst.renderMeta & 0x7FFFFFFFu;      // high bit = align flag, CPU-only
  CombatUvBasis basis = _UvBasis[id];
  float3 worldPos = mul(inst.objectToWorld, float4(IN.positionOS.xyz, 1.0)).xyz;
  OUT.positionHCS = TransformWorldToHClip(worldPos);
  OUT.uv = basis.originU.xy + IN.uv.x * basis.originU.zw + IN.uv.y * basis.v.xy;
  ```
- Keep `LightMode = Universal2D`, `#pragma target 4.5`, blend/ztest unchanged.

## Render system ([CombatBatchedRenderSystem.cs](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs))
- `InstanceDataStride = 68`.
- In `OnUpdate`, after the null/empty guards and before `Submit`, call
  `registry.EnsureUvBasisBuffer()` and hold the returned buffer.
- In `Submit`, in addition to the existing
  `SharedMaterial.SetBuffer("_InstanceData", _instanceBuffer)`, add
  `SharedMaterial.SetBuffer("_UvBasis", uvBasisBuffer)` (cache the property id like
  `InstanceDataProperty`). Must be `Material.SetBuffer` (indirect-draw constraint).
- Scatter (`WriteDirectJob`) is unchanged in shape — still copies the whole component;
  it is now 68 bytes. (Optionally apply the `AddRange(components)` bulk-copy noted
  earlier while here.)

## Acceptance criteria
- Sprites render identically to before (correct atlas region, packing rotation honored)
  in the Editor with the atlas packed.
- `_UvBasis` is bound via `Material.SetBuffer` (not MPB).
- One `DrawMeshInstancedIndirect` per update, unchanged draw count.
- Zero-active / no-mesh frames still publish nothing.

## Dependencies
Requires 001 (`EnsureUvBasisBuffer`) and 002 (68-byte component). Ship all three together.

## Scope
Small. Two files (shader + render system).

## Validation notes
- StructuredBuffer stride 68 is a multiple of 4 (valid). If any target platform rejects
  the non-16-multiple stride, pad `CombatInstanceData`/component to 72 with a trailing
  unused `int` (update all three strides + asserts together). Prefer 68; only pad on a
  concrete failure.
- `renderId == 0` reads `_UvBasis[0]` (zero basis) — safe; such instances are
  degenerate (zero-area) and do not rasterize.
