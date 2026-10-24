Shader "TianZhang/Guanzhong Terrace Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Original Tile Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Warm Tint", Color) = (0.94, 0.90, 0.80, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.06
        _Metallic("Metallic", Range(0, 1)) = 0
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _EmissionColor("Emission", Color) = (0, 0, 0, 0)
        [HideInInspector] _EmissionMap("Emission Map", 2D) = "white" {}
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            // Keep URP's Lit material buffer, lighting, normals, shadows and depth passes.
            // Only this Tile derivative reduces albedo chroma; original texture/UVs stay intact.
            #define InitializeStandardLitSurfaceData InitializeOriginalTileSurface
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #undef InitializeStandardLitSurfaceData
            void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
            {
                InitializeOriginalTileSurface(uv, surface);
                half luminance = dot(surface.albedo, half3(0.2126h, 0.7152h, 0.0722h));
                surface.albedo = lerp(luminance.xxx, surface.albedo, 0.40h);
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
