# 001 — Indirect Shader

## Change

Add a new shader `Assets/Shaders/CombatAtlasIndirectSprite.shader` (shader name
`"Combat/AtlasIndirectSprite"`) that reads per-instance data from a `StructuredBuffer`
indexed by `SV_InstanceID`, instead of the instancing-buffer / `unity_ObjectToWorld` path.
Leave the old `CombatAtlasInstancedSprite.shader` in place (unused after task 004; not deleted
here — asset deletion is a manual editor action, and the atlas plan is left intact).

## Why a rewrite, not an edit

The old shader depends on two things `RenderMeshIndirect` does not provide:
- `unity_ObjectToWorld` (via `TransformObjectToHClip`) — not populated by indirect.
- `UNITY_DEFINE_INSTANCED_PROP(_UvRect)` — the instanced-prop channel does not carry
  per-instance data under `RenderMeshInstanced`/indirect.

Both must be replaced by an explicit buffer read.

## Shader shape

Keep the render state identical to the current shader (this is a 2D transparent unlit sprite):
`Tags { RenderType=Transparent, Queue=Transparent, RenderPipeline=UniversalPipeline }`,
`Blend SrcAlpha OneMinusSrcAlpha`, `ZWrite Off`, `ZTest LEqual`, `Cull Off`, `_MainTex` = atlas.

```hlsl
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 4.5   // StructuredBuffer in the vertex stage

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct CombatInstanceData
{
    float4x4 objectToWorld;
    float4   uvRect;        // xy = origin (0..1), zw = size (0..1)
};
StructuredBuffer<CombatInstanceData> _InstanceData;

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

struct Attributes
{
    float4 positionOS : POSITION;
    float2 uv         : TEXCOORD0;
    uint   instanceID : SV_InstanceID;
};

struct Varyings
{
    float4 positionHCS : SV_POSITION;
    float2 uv          : TEXCOORD0;
};

Varyings vert(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    CombatInstanceData inst = _InstanceData[IN.instanceID];

    float3 worldPos = mul(inst.objectToWorld, float4(IN.positionOS.xyz, 1.0)).xyz;
    OUT.positionHCS = TransformWorldToHClip(worldPos);
    OUT.uv = inst.uvRect.xy + IN.uv * inst.uvRect.zw;
    return OUT;
}

half4 frag(Varyings IN) : SV_Target
{
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
}
ENDHLSL
```

## Correctness checks (do not assume — verify on screen)

- **Matrix convention (top risk).** `objectToWorld` is a Unity `Matrix4x4` (column-major)
  uploaded raw into an HLSL `float4x4`. Baseline `mul(m, v)`. If sprites render sheared,
  transposed, or mis-positioned, switch to `transpose(inst.objectToWorld)` or `mul(v, m)` and
  re-verify. This must be confirmed with a real on-screen sprite, not reasoned away.
- **`#pragma target 4.5`** (or higher) is required — `StructuredBuffer` reads in the vertex
  stage are not available at the default target level. Confirm the target platform supports SM5.
- **UV remap parity.** `uvRect.xy + uv * uvRect.zw` matches the old shader's mapping and the
  registry's `uvRect = (rect.x/w, rect.y/h, rect.width/w, rect.height/h)` — a full-quad `uv`
  0..1 maps to the sprite's sub-rect. No change to `Register(...)`'s UV math.
- **Z / depth.** The matrix's `m23 = RenderZ` flows through `objectToWorld`, so per-entity
  Z-ordering is preserved without any extra shader work.

## Acceptance Criteria

- A material using `Combat/AtlasIndirectSprite`, bound to a populated `_InstanceData` buffer and
  drawn via `RenderMeshIndirect`, renders each instance at its own matrix and its own atlas
  sub-rect (verified by two instances showing two different sprites — the exact regression the
  instanced path could not do).
- No `UNITY_INSTANCING_BUFFER`, no `unity_ObjectToWorld`, no `MaterialPropertyBlock` per-instance
  array anywhere in the shader.
- Render state (blend/ZWrite/ZTest/cull/queue) matches the old shader; no visual regression in
  transparency or draw order versus the pre-rework look.

## Dependencies

Data Contract in `index.md` (struct layout must match 002/003 exactly). Otherwise independent.

## Scope

Small–medium (one shader file; the risk is the matrix convention, which needs a live check).
