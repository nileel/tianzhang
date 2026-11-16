Shader "TianZhang/Jindan/Low Fire"
{
    Properties
    {
        _BaseColor("Flame colour", Color) = (1,.34,.055,1)
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
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; };
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 c=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(c),Hash(c+float2(1,0)),f.x),lerp(Hash(c+float2(0,1)),Hash(c+1),f.x),f.y);
            }
            V Vert(A v) { V o; o.world=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world); o.uv=v.uv; return o; }
            half4 Frag(V i):SV_Target
            {
                float2 p=i.uv;
                float phase=_Time.y*2.1+i.world.x*1.7+i.world.z*2.3;
                float n=Noise(float2(p.x*6+i.world.x, p.y*7-phase*1.5));
                float detail=Noise(float2(p.x*15,p.y*13-phase*2));
                float lean=sin(p.y*8-phase*2)*p.y*.13+(n-.5)*p.y*.38;
                float side=abs(p.x-.5+lean);
                float tip=.72+.26*Noise(float2(p.x*3+i.world.z,phase*.6));
                float height=p.y/tip;
                // Curved, broken silhouette: the source cards must never read as solid triangular fins.
                float width=pow(saturate(1-height),.72)*.34*(.55+.65*n);
                float shape=(1-smoothstep(width*.45,width+.04,side))*(1-smoothstep(.72,1,height));
                float breakup=smoothstep(.23,.56,n*.65+detail*.35+(1-p.y)*.18);
                float core=(1-smoothstep(.025,.23,side+p.y*.17))*breakup;
                half3 c=lerp(_BaseColor.rgb,half3(1,.84,.36),core);
                float a=shape*breakup*smoothstep(0,.045,p.y)*_Opacity*_Strength*.92;
                return half4(c,a);
            }
            ENDHLSL
        }
    }
}
