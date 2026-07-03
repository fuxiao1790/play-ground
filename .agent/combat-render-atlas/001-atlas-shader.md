# Atlas Shader

## Change

Add a new hand-written URP-compatible unlit transparent shader,
`Assets/Shaders/CombatAtlasInstancedSprite.shader`, that samples one shared
atlas texture using a per-instance UV rect. Uses classic Unity GPU instancing
(`UNITY_INSTANCING_BUFFER_START`/`UNITY_DEFINE_INSTANCED_PROP`), the same
mechanism `Graphics.RenderMeshInstanced` already expects via
`RenderParams.matProps` — **not** DOTS/BRG instancing, which is unrelated and
was the reason the prior Entities Graphics attempt was abandoned (URP 2D
Renderer has no BRG integration). Classic GPU instancing works fine with the
2D Renderer since it's a plain draw-call submission, unaffected by that
limitation.

Blend/ZWrite/Cull state mirrors what `CombatRenderResourceRegistry.ConfigureMaterial`
(`CombatRenderComponents.cs:207-226`) forces onto every cloned material today
(alpha blend, no zwrite, cull off, transparent queue), so visual behavior is
unchanged from the current per-kind materials.

Full shader source:

```hlsl
Shader "Combat/AtlasInstancedSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CombatAtlasUnlit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            UNITY_INSTANCING_BUFFER_START(CombatAtlasProps)
                UNITY_DEFINE_INSTANCED_PROP(float4, _UvRect)
            UNITY_INSTANCING_BUFFER_END(CombatAtlasProps)

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);

                float4 uvRect = UNITY_ACCESS_INSTANCED_PROP(CombatAtlasProps, _UvRect);
                OUT.uv = uvRect.xy + IN.uv * uvRect.zw;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return col;
            }
            ENDHLSL
        }
    }
}
```

Notes for the implementer:
- `_UvRect` is `(uOffset, vOffset, uScale, vScale)` — the unit quad's raw
  `[0,1]` UV is remapped to `uvRect.xy + uv * uvRect.zw`, landing inside the
  atlas sub-rect for that instance's kind.
- The mesh's own UVs (set up in task 003 as a plain unit quad, `(0,0)` to
  `(1,1)`) stay `[0,1]` — all atlas addressing happens in the shader via
  `_UvRect`, not by baking sub-rect UVs into the mesh (that baking is exactly
  what today's per-kind system does and what this plan removes).
- No `_Color`/tint property is included — the current `ConfigureMaterial`/`ConfigureProperties`
  path sets `_Color`/`_BaseColor`/`_RendererColor` to `Color.white` unconditionally
  (never actually tints), so there is nothing to preserve. If a future task
  wants per-kind tinting, add a second instanced `float4 _Color` property
  then — out of scope here.
- Create the shader via Unity's Asset menu or by hand-authoring the `.shader`
  file directly under `Assets/Shaders/` (new folder; no existing custom
  shaders in the project). Unity will generate the `.meta` file on next
  import/domain reload.

## Acceptance Criteria

- `Assets/Shaders/CombatAtlasInstancedSprite.shader` exists, compiles in the
  Unity Editor with URP active, and is selectable by `Shader.Find("Combat/AtlasInstancedSprite")`.
  no compile errors/warnings in the Console.
- A `Material` built from this shader with GPU Instancing enabled and a
  `MaterialPropertyBlock` supplying `_UvRect` per instance renders the correct
  atlas sub-rect per instance when submitted via `Graphics.RenderMeshInstanced`
  (verified end-to-end once task 005 wires this up — this task's own
  acceptance is compile + manual single-instance smoke test with a hardcoded
  `_UvRect` of `(0,0,1,1)`, which should render the whole atlas texture
  unmodified on a quad).
- Blend/transparency behavior visually matches today's sprite rendering
  (alpha blended, no unexpected opaque/zwrite artifacts).

## Dependencies

None.

## Scope

Small.
