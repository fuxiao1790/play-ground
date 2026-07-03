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
                float4x4 objectToWorld;
                float4 uvRect;
            };

            StructuredBuffer<CombatInstanceData> _InstanceData;

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
        }
    }
}
