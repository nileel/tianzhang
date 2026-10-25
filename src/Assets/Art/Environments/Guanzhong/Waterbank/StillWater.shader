Shader "TianZhang/Guanzhong Still Water"
{
    Properties
    {
        [MainTexture] _BaseMap("Base", 2D) = "white" {}
        [MainColor] _BaseColor("Shallow water", Color) = (.17,.37,.36,1)
        _Smoothness("Smoothness", Range(0,1)) = .68
        _Metallic("Metallic", Range(0,1)) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = .5
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _EmissionColor("Emission", Color) = (0,0,0,0)
        [HideInInspector] _EmissionMap("Emission map", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "UniversalMaterialType"="Lit" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #define InitializeStandardLitSurfaceData InitializeStillWater
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #undef InitializeStandardLitSurfaceData
            void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
            {
                InitializeStillWater(uv,surface);
                // Frozen ripples: no displacement, time integration, depth or occupancy simulation.
                float swell=sin(uv.y*19+sin(uv.x*11)*.6);
                float ripple=pow(saturate(sin(uv.y*53+sin(uv.x*17)*1.5)),18);
                float glint=ripple*smoothstep(.2,.8,sin(uv.x*27+uv.y*5)*.5+.5);
                surface.albedo=_BaseColor.rgb*(.93+.07*swell)+half3(.16,.22,.19)*glint;
                surface.normalTS=normalize(half3(.025*cos(uv.x*17),.018*cos(uv.y*53),1));
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
