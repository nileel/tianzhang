Shader "TianZhang/Jindan/Tide Front"
{
    Properties
    {
        _FoamMap("Unity sample foam", 2D) = "gray" {}
        _BaseColor("Low foam tint", Color) = (.55,.77,.72,.65)
        _Progress("Travel 0 to 1", Range(0,1)) = 0
        _Flow("World XZ travel direction", Vector) = (0,1,0,0)
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
            TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _Flow; float _Progress;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; };
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 local:TEXCOORD1; };
            V Vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.world);
                o.local=o.world.xz-TransformObjectToWorld(float3(0,0,0)).xz; return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 direction=normalize(_Flow.xy+float2(.00001,0));
                float along=dot(i.local,direction);
                float foam=SAMPLE_TEXTURE2D(_FoamMap,sampler_FoamMap,i.world.xz*1.9-direction*_Progress*.4).r;
                float wave=along-lerp(-.66,.66,_Progress)+(.5-foam)*.055;
                float front=exp(-wave*wave*300);
                float tail=exp(-pow(wave+.14,2)*80)*.22;
                float fade=smoothstep(0,.12,_Progress)*(1-smoothstep(.8,1,_Progress));
                return half4(_BaseColor.rgb,(front+tail)*(.3+.7*foam)*fade*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
