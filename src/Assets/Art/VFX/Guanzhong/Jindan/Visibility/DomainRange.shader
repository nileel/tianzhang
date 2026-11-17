Shader "TianZhang/Jindan/Visibility Domain Range"
{
    Properties
    {
        _BaseColor ("Domain tint", Color) = (1,.75,.3,1)
        _Moon ("Moon haze instead of sunlight", Float) = 0
        _Opacity ("Presentation fade", Range(0,1)) = 0
        _EventTime ("Public domain opening time", Float) = -100
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Moon, _Opacity, _EventTime;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 world : TEXCOORD1; };
            Output Vert(Input input)
            {
                Output output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.world = TransformObjectToWorld(input.positionOS.xyz).xz;
                output.uv = input.uv;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),
                    lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Output input) : SV_Target
            {
                // Every covered cell keeps a soft ground wash beneath the continuous motion.
                float t=_Time.y;
                float2 p=input.world;
                float coarse=Noise(p*.67+float2(-t*.18,t*.09));
                float fine=Noise(p*1.43+float2(t*.11,-t*.15));
                float haze=smoothstep(.20,.83,coarse*.72+fine*.28);
                float rays=pow(saturate(.5+.5*sin(p.x*3.6+p.y*1.1-t*1.3+coarse*2.5)),4);
                float cellWash=1-smoothstep(.18,.55,length(input.uv));
                float opening=exp(-max(0,t-_EventTime)*2.4);
                float alpha=lerp(.14+.12*cellWash+.20*rays,.16+.10*cellWash+.16*haze,step(.5,_Moon));
                alpha+=opening*lerp(.14,.08,step(.5,_Moon))*haze;
                return half4(_BaseColor.rgb,alpha*_BaseColor.a*_Opacity);
            }
            ENDHLSL
        }
    }
}
