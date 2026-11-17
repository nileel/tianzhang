Shader "TianZhang/Jindan/Public Altar"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,0.75,0.35,1)
        _Moon ("Moon instead of sun", Float) = 0
        _Halo ("Local halo", Float) = 0
        _Strength ("Strength", Range(0,1)) = 1
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
                float _Moon, _Halo, _Strength;
            CBUFFER_END
            struct Input { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Output Vert(Input input)
            {
                Output output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Output input) : SV_Target
            {
                float2 p = (input.uv - .5) * 2;
                float radius = length(p);
                float aa = max(fwidth(radius), .012);
                float sun = 1 - smoothstep(.32-aa, .32+aa, radius);
                float ring = 1 - smoothstep(.025, .025+aa, abs(radius-.48));
                float angle = atan2(p.y, p.x);
                float ray = pow(abs(cos(angle*4)), 20) * smoothstep(.58,.62,radius) * (1-smoothstep(.83,.87,radius));
                float moon = (1-smoothstep(.72-aa,.72+aa,radius)) * smoothstep(.63-aa,.63+aa,length(p-float2(.32,.17)));
                float glyph = lerp(max(sun,max(ring,ray)), moon, step(.5,_Moon));
                float halo = pow(saturate(1-radius), 3) * .28;
                float pulse = .94 + .06*sin(_Time.y*1.8);
                float alpha = lerp(glyph,halo,step(.5,_Halo))*_Strength*_BaseColor.a;
                return half4(_BaseColor.rgb * lerp(1,pulse,step(.5,_Halo)),alpha);
            }
            ENDHLSL
        }
    }
}
