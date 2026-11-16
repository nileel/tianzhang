Shader "TianZhang/Jindan/Warm Trace"
{
    Properties
    {
        _BaseColor("Warm trace", Color) = (.85,.41,.12,.25)
        _Opacity("Main rule presence", Range(0,1)) = 1
        _Pulse("Gentle modulation", Range(0,1)) = .12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Opacity, _Pulse;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 p:TEXCOORD0; };
            V Vert(A v) { V o; o.p=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.p); return o; }
            half4 Frag(V i):SV_Target
            {
                float modulation=1-_Pulse*.5+sin(_Time.y*1.6+i.p.x*.3+i.p.z*.4)*_Pulse*.5;
                return half4(_BaseColor.rgb,_BaseColor.a*_Opacity*modulation);
            }
            ENDHLSL
        }
    }
}
