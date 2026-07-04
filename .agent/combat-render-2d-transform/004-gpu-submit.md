# 004 — GPU submit: 2D shader struct + stride

## Scope
`Assets/Shaders/CombatAtlasIndirectSprite.shader`,
`Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`.

## Change

### Shader
Replace the instance struct + transform:
```hlsl
struct CombatInstanceData
{
    float4 rotation;   // m00, m01, m10, m11
    float3 position;   // world x, world y, renderZ
    uint   renderMeta;
};
```
`vert`:
```hlsl
CombatInstanceData inst = _InstanceData[IN.instanceID];
uint renderId = inst.renderMeta & 0x7FFFFFFFu;
CombatUvBasis basis = _UvBasis[renderId];

float2 os = IN.positionOS.xy;
float3 worldPos;
worldPos.x = inst.rotation.x * os.x + inst.rotation.y * os.y + inst.position.x;
worldPos.y = inst.rotation.z * os.x + inst.rotation.w * os.y + inst.position.y;
worldPos.z = inst.position.z;
OUT.positionHCS = TransformWorldToHClip(worldPos);
OUT.uv = basis.originU.xy + IN.uv.x * basis.originU.zw + IN.uv.y * basis.v.xy;
```
UV path unchanged (still keyed on mesh `IN.uv`, not `positionOS`). `renderMeta`
is `uint` in HLSL vs `int` in C# — raw bit reinterpret via `SetData`, exact.

### CombatBatchedRenderSystem
- `InstanceDataStride` 68 -> 32.
- `_instanceData` stays `NativeList<CombatRenderComponent>`; `WriteDirectJob`
  stays a zero-copy `AddRange` — the component is once again the 32 B wire
  record. No repack.
- `OnCreate` assert `AreEqual(InstanceDataStride, SizeOf<CombatRenderComponent>())`
  now checks 32.

## Critical constraints (must not regress)
- `_InstanceData`/`_UvBasis` bound via `Material.SetBuffer` (not MPB).
- Pass tag `LightMode = Universal2D`; `#pragma target 4.5`.
- Column-major, `mul`-equivalent reconstruction (done by hand above).
- Feature still on `Renderer2D.asset`; shader still in Always Included Shaders.

## Acceptance
- Sprites draw identically to before (position, rotation, scale, z-order, atlas
  UVs). One indirect draw, no 1023 cap.
- Per-instance upload is 32 B; buffer stride matches struct on both sides.

## Depends on
001 (defines the 32 B layout).
