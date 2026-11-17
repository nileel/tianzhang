Shader "TianZhang/Jindan/Visibility Domain Range"
{
    Properties
    {
        _BaseColor ("Domain tint", Color) = (1,.75,.3,1)
        _Moon ("Broken inner moon line", Float) = 0
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
                float _Moon;
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
            half4 Frag(Output input) : SV_Target
            {
                // UV is the fixed cell-local XZ, never a character or observation coordinate.
                float2 p = input.uv;
                float edge = .5 - max(abs(p.x), max(abs(.5*p.x+.8660254*p.y),abs(.5*p.x-.8660254*p.y)));
                float moon = step(.5,_Moon);
                float inset = lerp(.045,.095,moon);
                float aa = max(fwidth(edge),.002);
                float stroke = 1-smoothstep(.009,.009+aa,abs(edge-inset));
                float angle = atan2(p.y,p.x);
                float dashes = smoothstep(-.15,.15,cos(angle*12));
                stroke *= lerp(1,dashes,moon);
                float flow = pow(.5+.5*sin(dot(input.world,float2(.7,1.1))-_Time.y*1.4+moon*2),8);
                float fill = smoothstep(0,.035,edge)*(.018+.025*flow);
                float alpha = max(fill,stroke*(.62+.2*flow));
                return half4(_BaseColor.rgb,alpha*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
