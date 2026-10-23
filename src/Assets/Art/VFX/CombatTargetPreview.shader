Shader "TianZhang/CombatTargetPreview"
{
    Properties
    {
        _Color ("Sweep color", Color) = (1, 0.64, 0.27, 0.3)
        _Sweep ("World center and half width", Vector) = (0, 0.1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Sweep;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float heightWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.heightWS = world.y;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float distance = abs(input.heightWS - _Sweep.x) / max(_Sweep.y, 0.0001);
                half band = 1 - smoothstep(0.15, 1, distance);
                return half4(_Color.rgb, _Color.a * band);
            }
            ENDHLSL
        }
    }
}
