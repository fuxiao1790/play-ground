Shader "Combat/AtlasIndirectSprite"
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
            Name "CombatAtlasIndirectUnlit"
            // The active renderer is URP's 2D Renderer (Renderer2D). It only executes passes
            // tagged Universal2D; a pass with no LightMode defaults to SRPDefaultUnlit, which the
            // 2D Renderer skips â€?geometry submits but never draws. This tag is what makes it render.
            Tags { "LightMode" = "Universal2D" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct CombatInstanceData
            {
                float4 rotation; // m00, m01, m10, m11
                float3 position; // world x, world y, renderZ
                uint renderMeta;
            };

            struct CombatUvBasis
            {
                float4 originU; // xy = origin, zw = U axis
                float4 v;       // xy = V axis
            };

            StructuredBuffer<CombatInstanceData> _InstanceData;
            StructuredBuffer<CombatUvBasis> _UvBasis;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                CombatInstanceData inst = _InstanceData[IN.instanceID];
                uint renderId = inst.renderMeta & 0x7FFFFFFFu;
                CombatUvBasis basis = _UvBasis[renderId];

                // Compact 2D basis reconstructs the same mul(M, v) result for unit quad z=0.
                float2 os = IN.positionOS.xy;
                float3 worldPos;
                worldPos.x = inst.rotation.x * os.x + inst.rotation.y * os.y + inst.position.x;
                worldPos.y = inst.rotation.z * os.x + inst.rotation.w * os.y + inst.position.y;
                worldPos.z = inst.position.z;
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                // Affine UV basis: reproduces the sprite's real atlas UVs even if the packer rotated
                // it 90Â°. IN.uv is the unit-quad 0..1 coordinate.
                OUT.uv = basis.originU.xy + IN.uv.x * basis.originU.zw + IN.uv.y * basis.v.xy;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
            }
            ENDHLSL
        }
    }
}
