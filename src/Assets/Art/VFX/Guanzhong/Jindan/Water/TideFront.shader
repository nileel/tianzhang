Shader "TianZhang/Jindan/Tide Front"
{
    Properties
    {
        _FoamMap("Unity sample foam", 2D) = "gray" {}
        _BaseColor("Wave water tint", Color) = (.045,.48,.62,.88)
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
            struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float2 local:TEXCOORD1; float3 wave:TEXCOORD2; };
            V Vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.positionOS.xyz);
                o.local=o.world.xz-TransformObjectToWorld(float3(0,0,0)).xz;
                float2 direction=normalize(_Flow.xy+float2(.00001,0));
                float along=dot(o.local,direction);
                float across=dot(o.local,float2(-direction.y,direction.x));
                float travel=lerp(-.78,.78,_Progress);
                float phase=(along-travel+.24)/.48;
                float theta=saturate(phase)*6.2831853;
                float envelope=smoothstep(0,.10,_Progress)*(1-smoothstep(.76,1,_Progress));
                float rim=saturate((.52-max(abs(o.local.x),abs(o.local.x)*.5+abs(o.local.y)*.8660254))/.07);
                // A rolling cross-section: the lip folds over the face; only the effect moves, never the support mesh.
                float height=.145*(1-cos(theta))*envelope*rim*(.91+.09*sin(across*21+_Progress*9));
                o.world.y+=height;
                o.world.xz+=direction*(-.115*sin(theta))*envelope*rim;
                o.wave=float3(phase,height,envelope);
                o.positionCS=TransformWorldToHClip(o.world); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 direction=normalize(_Flow.xy+float2(.00001,0));
                float foam=SAMPLE_TEXTURE2D(_FoamMap,sampler_FoamMap,i.world.xz*2.4-direction*_Progress*.6).b;
                float phase=i.wave.x;
                float body=smoothstep(0,.10,phase)*(1-smoothstep(.83,1,phase));
                float lip=1-smoothstep(.06,.15,abs(phase-.5)+(.5-foam)*.08);
                float streak=pow(saturate(sin(dot(i.local,float2(-direction.y,direction.x))*39+foam*5)),9);
                float lace=smoothstep(.42,.76,foam);
                half3 water=lerp(half3(.015,.18,.28),_BaseColor.rgb,saturate(phase*1.5));
                water+=half3(.10,.32,.35)*streak*body*.5;
                half3 color=lerp(water,half3(.83,1,.96),saturate(lip*(.75+.25*lace)));
                float trail=exp(-pow(phase+.12,2)*24)*lace*.35;
                return half4(color,saturate(body*.86+lip*.1+trail)*i.wave.z*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
