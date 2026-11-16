Shader "TianZhang/Jindan/Water Surface"
{
    Properties
    {
        _BaseColor("Water colour", Color) = (.17,.37,.36,1)
        _MudColor("Wet shore colour", Color) = (.19,.215,.16,1)
        _FoamMap("Unity sample foam", 2D) = "gray" {}
        [Normal] _NormalMap("Unity sample water normal", 2D) = "bump" {}
        _Depth("Wet 0 / shallow 1 / deep 2", Range(0,2)) = 1
        _Opacity("Surface presence", Range(0,1)) = 1
        _Flow("World XZ direction and speed", Vector) = (0,1,.12,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor, _MudColor, _Flow;
            float _Depth, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A v)
            {
                V o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float water=saturate(_Depth);
                float2 p=i.positionWS.xz;
                float2 scroll=_Flow.xy*_Time.y*_Flow.z;
                // Same two scrolling normal samples as the official water approach; no scene/depth texture or displacement.
                half3 n1=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,p*.8-scroll));
                half3 n2=UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap,sampler_NormalMap,p*1.31-scroll*.63+float2(.37,.2)));
                half3 n=normalize(i.normalWS+half3(n1.x+n2.x,0,n1.y+n2.y)*lerp(.035,.11,water));
                float foam=SAMPLE_TEXTURE2D(_FoamMap,sampler_FoamMap,p*.65-scroll*.35).r;
                float grain=SAMPLE_TEXTURE2D(_FoamMap,sampler_FoamMap,p*7.5).r;
                half3 mud=_MudColor.rgb*(.8+.22*foam+.12*grain);
                half3 base=lerp(mud,_BaseColor.rgb*(.92+.08*foam),water);
                float crest=smoothstep(.67,.9,foam)*water*.09;
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lighting=SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation;
                half3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                half spec=pow(saturate(dot(n,normalize(view+light.direction))),lerp(55,95,water));
                half3 color=(base+crest*half3(.5,.64,.61))*lighting;
                color+=light.color*spec*lerp(.13,.26,water)*light.shadowAttenuation;
                // Wetness remains a patchy thin ground layer; depth is a rule state, never a raised water volume.
                float alpha=lerp(.72+.2*foam,.97,water)*_Opacity;
                return half4(MixFog(color,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
