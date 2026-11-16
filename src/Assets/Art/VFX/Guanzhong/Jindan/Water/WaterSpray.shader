Shader "TianZhang/Jindan/Water Spray"
{
    Properties { _BaseColor("Droplet tint", Color) = (.65,.94,1,.9) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+1" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            V Vert(A v) { V o; o.vertex=TransformObjectToHClip(v.vertex.xyz); o.uv=v.uv; o.color=v.color; return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv*2-1; p.x*=1.35;
                float drop=1-smoothstep(.48,1,dot(p,p));
                return half4(_BaseColor.rgb*i.color.rgb,_BaseColor.a*i.color.a*drop);
            }
            ENDHLSL
        }
    }
}
