Shader "TianZhang/Jindan/Space Band"
{
    Properties
    {
        _BaseColor("Ink", Color) = (.35,.8,.78,.8)
        _Pattern("0 short ticks, 1 long ticks, 2 outer, 3 inner", Float) = 0
        _Shimmer("Local ink shimmer", Range(0,1)) = .25
        _Opacity("Opacity", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor; float _Pattern, _Shimmer, _Opacity;
            CBUFFER_END
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            float stroke(float distance, float width) { return 1-smoothstep(width,width+max(fwidth(distance),.002),distance); }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv=i.uv; float mask;
                if (_Pattern<1.5)
                {
                    float ticks=stroke(abs(frac(uv.x*8+.5)-.5),.08);
                    float halfLength=_Pattern<.5?.18:.40;
                    mask=max(stroke(abs(uv.y-.5),.025),ticks*(1-smoothstep(halfLength,halfLength+.015,abs(uv.y-.5))));
                }
                else if (_Pattern<2.5)
                {
                    float rails=stroke(abs(abs(uv.y-.5)-.39),.035);
                    float brackets=stroke(min(abs(uv.x-.08),abs(uv.x-.92)),.025)*step(.22,abs(uv.y-.5));
                    mask=max(rails*step(.14,frac(uv.x*5)),brackets);
                }
                else
                {
                    float arrowX=frac(uv.x*3);
                    float chevron=stroke(abs(arrowX-(.78-abs(uv.y-.5)*1.3)),.045)*step(abs(uv.y-.5),.28);
                    mask=max(chevron,stroke(abs(uv.y-.5),.018)*step(.15,arrowX)*step(arrowX,.65));
                }
                float endFade=smoothstep(0,.04,uv.x)*smoothstep(0,.04,1-uv.x);
                float shimmer=1+_Shimmer*.12*sin(uv.x*23+uv.y*7-_Time.y*1.8);
                return half4(_BaseColor.rgb*shimmer,_BaseColor.a*mask*endFade*_Opacity);
            }
            ENDHLSL
        }
    }
}
