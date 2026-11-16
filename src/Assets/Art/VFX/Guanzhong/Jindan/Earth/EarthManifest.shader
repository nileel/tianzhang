Shader "TianZhang/Jindan/Earth Manifest"
{
    Properties
    {
        [MainTexture] _BaseMap("Source texture", 2D) = "white" {}
        [MainColor] _BaseColor("Earth / stone", Color) = (.43,.35,.25,1)
        _Progress("Form 0 to 1 / dissolve 1 to 0", Range(0,1)) = 1
        _MinY("Mesh bottom (object space)", Float) = 0
        _Height("Mesh height (object space)", Float) = 1
        _EarthGrain("Use original ramp grain", Float) = 0
        _GravelDensity("Gravel density", Range(0,1)) = .15
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BaseColor;
        float _Progress, _MinY, _Height, _EarthGrain, _GravelDensity;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionOS : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half fog : TEXCOORD4;
        };
        float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float Noise(float2 p)
        {
            float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
        }
        float Mask(float3 p)
        {
            // Object-space gradient: all six headings and existing Y adaptation retain their anchors.
            float h=(p.y-_MinY)/max(_Height,.00001);
            float grain=Noise(p.xz/max(_Height,.00001)*23 + h*9);
            float threshold=h*.82 + grain*.16 + .01;
            clip(_Progress-threshold);
            return _Progress-threshold;
        }
        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionOS=v.positionOS.xyz;
            o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);
            o.uv=TRANSFORM_TEX(v.uv,_BaseMap);
            o.fog=ComputeFogFactor(o.positionCS.z);
            return o;
        }
        half3 Albedo(float2 uv)
        {
            half3 stone=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb*_BaseColor.rgb;
            half luminance=dot(stone,half3(.2126,.7152,.0722));
            stone=lerp(luminance.xxx,stone,.40);
            // Original RampGround colour marks; no displacement or new geometry.
            float grain=Noise(uv*42), mottling=Noise(uv*3.2);
            float2 grid=uv*14, cell=floor(grid), local=frac(grid)-.5;
            local+=float2(Hash(cell+31),Hash(cell+67))*.28-.14;
            float radius=.10+Hash(cell+19)*.23;
            float pebble=(1-smoothstep(radius-.03,radius+.025,length(local*float2(1,1.3))))*step(Hash(cell),_GravelDensity);
            half3 earth=_BaseColor.rgb*(.82+.22*mottling+.10*grain);
            half3 gravel=lerp(half3(.24,.27,.26),half3(.47,.49,.44),Hash(cell+11))*(.9+.18*step(0,local.x-local.y));
            return lerp(stone,lerp(earth,gravel,pebble),_EarthGrain);
        }
        half4 Forward(Varyings i) : SV_Target
        {
            float edge=Mask(i.positionOS);
            half3 n=normalize(i.normalWS);
            Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
            half3 lighting=SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation;
            half3 color=Albedo(i.uv)*lighting;
            // Narrow warm soil edge, never a bright full-surface flash.
            color+=half3(.10,.065,.025)*(1-smoothstep(0,.045,edge))*step(_Progress,.995);
            return half4(MixFog(color,i.fog),1);
        }
        half4 Depth(Varyings i) : SV_Target { Mask(i.positionOS); return i.positionCS.z; }
        half4 Normals(Varyings i) : SV_Target { Mask(i.positionOS); return half4(normalize(i.normalWS),0); }
        float3 _LightDirection;
        float3 _LightPosition;
        Varyings ShadowVert(Attributes v)
        {
            Varyings o=Vert(v);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 direction=normalize(_LightPosition-o.positionWS);
            #else
                float3 direction=_LightDirection;
            #endif
            o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction)));
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Forward
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment Depth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Depth
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Normals
            ENDHLSL
        }
    }
}
