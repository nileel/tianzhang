Shader "TianZhang/Jindan/Space Band"
{
    Properties
    {
        _BaseColor("Energy tint", Color) = (.35,.8,.78,.8)
        _Pattern("0 compression, 1 expansion, 2 outer, 3 inner", Float) = 0
        _Layer("0 ground direction, 1 energy membrane", Float) = 0
        _Shimmer("Local membrane distortion", Range(0,1)) = 1
        _Opacity("Opacity", Range(0,1)) = 1
        _EventTime("Committed visual event time", Float) = -100
        _EventKind("0 deploy, 1 impact, 2 dissolve", Float) = 0
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
                float4 _BaseColor;
                float _Pattern, _Layer, _Shimmer, _Opacity, _EventTime, _EventKind;
            CBUFFER_END
            Varyings Vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
            float stroke(float distance, float width) { return 1-smoothstep(width,width+max(fwidth(distance),.003),distance); }
            float octagon(float2 p) { p=abs(p); return max(max(p.x,p.y),(p.x+p.y)*.70710678); }
            half4 Frag(Varyings i):SV_Target
            {
                float2 uv=i.uv;
                float time=_Time.y;
                float age=max(0,time-_EventTime);
                float eventFade=saturate(1-age/1.05);
                float ink=0,fill=0,flash=0;
                if(_Layer<.5)
                {
                    if(_Pattern<1.5)
                    {
                        float ticks=stroke(abs(frac(uv.x*8+.5)-.5),.065);
                        float halfLength=_Pattern<.5?.18:.40;
                        ink=max(stroke(abs(uv.y-.5),.025),ticks*(1-smoothstep(halfLength,halfLength+.015,abs(uv.y-.5))));
                    }
                    else if(_Pattern<2.5)
                    {
                        float rails=stroke(abs(abs(uv.y-.5)-.39),.028);
                        ink=rails*step(.14,frac(uv.x*5));
                    }
                    else
                    {
                        float arrowX=frac(uv.x*3);
                        ink=stroke(abs(arrowX-(.78-abs(uv.y-.5)*1.3)),.045)*step(abs(uv.y-.5),.28);
                    }
                    ink*=smoothstep(0,.04,uv.x)*smoothstep(0,.04,1-uv.x);
                    return half4(_BaseColor.rgb,_BaseColor.a*ink*_Opacity);
                }
                if(_Pattern<1.5)
                {
                    // Compression converges from both ends; expansion radiates
                    // from the midpoint along the actual affected edge.
                    float bend=.10*sin(uv.y*6-time*1.7)*_Shimmer;
                    float along=abs(uv.x-.5)*2;
                    float travel=_Pattern<.5?time*1.25:-time*1.25;
                    float wave=frac(along*2.8+travel+uv.y*uv.y*.8+bend);
                    float envelope=smoothstep(0,.08,uv.y)*(1-smoothstep(.64,.96,uv.y))
                        *smoothstep(0,.06,uv.x)*smoothstep(0,.06,1-uv.x);
                    ink=(stroke(abs(wave-.5),.035)+stroke(abs(wave-.32),.012)*.35)*envelope;
                    fill=.10*envelope;
                    float ring=abs(length(float2((uv.x-.5)*1.7,uv.y-.38))-age*1.45);
                    flash=stroke(ring,.035)*eventFade*envelope;
                }
                else
                {
                    // This translucent film warps its own pattern only. It does
                    // not sample or refract the camera background.
                    float2 p=uv*2-1;
                    float2 warped=p+.018*_Shimmer*float2(sin(p.y*11-time*2.1),sin(p.x*9+time*1.6));
                    float radius=octagon(warped);
                    float silhouette=1-smoothstep(.96,1.0,radius);
                    float rim=stroke(abs(radius-.91),.025);
                    float rail=stroke(abs(radius-.78),.006)*.35;
                    float cycle=frac(time*.48+(_Pattern<2.5?0:.32));
                    float propagating=stroke(abs(radius-cycle*.91),.020)*(1-cycle)*.75;
                    float facets=stroke(abs(frac(atan2(p.y,p.x)*1.2732395+.5)-.5),.018)
                        *smoothstep(.62,.82,radius)*.22;
                    ink=(rim+rail+propagating+facets)*silhouette;
                    fill=(.17+.035*sin(radius*12-time*1.3))*silhouette;
                    float radiusAtTime=age*1.65;
                    float rings=stroke(abs(radius-radiusAtTime),.025)
                        +stroke(abs(radius-radiusAtTime+.19),.014)*.65
                        +stroke(abs(radius-radiusAtTime+.38),.01)*.4;
                    flash=rings*eventFade*silhouette;
                    if(_EventKind>1.5)fill*=saturate(1-age*2);
                }
                float alpha=saturate(fill+ink*.82+flash*.75)*_BaseColor.a*_Opacity;
                float3 color=lerp(_BaseColor.rgb,float3(.9,1,1),saturate(ink*.36+flash*.7));
                return half4(color,alpha);
            }
            ENDHLSL
        }
    }
}
