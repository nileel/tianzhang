Shader "TianZhang/Jindan/Low Fire"
{
    Properties
    {
        _BaseColor("Outer orange-red flame", Color) = (1,.19,.025,1)
        _Opacity("Presence driven by surface", Range(0,1)) = 1
        _Strength("Main 1 / paid tail .65", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
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
            float _Opacity, _Strength;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float2 seed:TEXCOORD1; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float seed:TEXCOORD1; };
            // Signed distance to a tapering, curved tongue. Broad coherent lobes keep the
            // silhouette readable at the default camera; there is no granular noise mask.
            float Tongue(float2 p, float origin, float height, float width, float bend, float phase)
            {
                float y=saturate(p.y/height);
                float curve=origin+bend*(sin(y*5.2-phase)*y*y+.55*y*y);
                float radius=width*pow(saturate(1-y),.75)*(1+.17*sin(y*8-phase));
                return min(radius-abs(p.x-curve), min(p.y,height-p.y));
            }
            V Vert(A v)
            {
                V o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv;
                float3 origin=TransformObjectToWorld(float3(0,0,0));
                o.seed=v.seed.x+origin.x*1.7+origin.z*2.3; return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv;
                float phase=_Time.y*3.1+i.seed;
                float height=.76+.065*sin(phase*.83);
                float body=Tongue(p,.48,height,.235,.24,phase);
                body=max(body,Tongue(p,.27,.48+.07*sin(phase+1),.16,-.24,phase+.8));
                body=max(body,Tongue(p,.69,.57+.06*cos(phase),.15,.21,phase+2));
                // A crescent cut into the upper lobe makes the tip curl rather than
                // reading as a straight cone; its motion stays coherent with the tongue.
                float2 notch=float2(.66+.06*sin(phase),.56+.035*cos(phase));
                float cut=length((p-notch)*float2(1,1.15))-.115;
                body=min(body,cut);
                float layer=Tongue(float2(p.x,p.y+.015),.47,height*.76,.155,.15,phase+.15);
                float core=Tongue(p,.46,height*.48,.075,.08,phase+.25);
                float cycle=frac(_Time.y*.72+i.seed*.17);
                float2 ember=float2(.52+.13*sin(phase*.6),.72+cycle*.25);
                float detached=(.047*(1-cycle))-length((p-ember)*float2(1, .65));
                float distance=max(body,detached);
                float aa=max(fwidth(distance),.003);
                float a=smoothstep(-aa,aa,distance)*smoothstep(0,.025,p.y)*_Opacity*lerp(.82,1,_Strength);
                half3 c=lerp(_BaseColor.rgb,half3(1,.58,.065),smoothstep(-aa,aa,layer));
                c=lerp(c,half3(1,.93,.52),smoothstep(-aa,aa,core));
                c=lerp(c,half3(1,.65,.12),step(body,detached));
                c*=lerp(.82,1,_Strength);
                return half4(c,a);
            }
            ENDHLSL
        }
    }
}
