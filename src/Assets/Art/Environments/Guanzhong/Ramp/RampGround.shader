Shader "TianZhang/Guanzhong Ramp Ground"
{
    Properties
    {
        [MainTexture] _BaseMap("Base", 2D) = "white" {}
        [MainColor] _BaseColor("Earth", Color) = (.43,.35,.25,1)
        _GravelDensity("Gravel density", Range(0,1)) = .18
        _Smoothness("Smoothness", Range(0,1)) = .05
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #define InitializeStandardLitSurfaceData InitializeRampBase
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #undef InitializeStandardLitSurfaceData
            float _GravelDensity;

            float RampHash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453);
            }
            float RampNoise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(RampHash(cell),RampHash(cell+float2(1,0)),f.x),
                    lerp(RampHash(cell+float2(0,1)),RampHash(cell+1),f.x),f.y);
            }
            void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
            {
                InitializeRampBase(uv, surface);
                // Metre UVs stay continuous across the hexes. These are flat colour marks,
                // never displacement: the physical D1 support surface is unchanged.
                float grain = RampNoise(uv*42);
                float mottling = RampNoise(uv*3.2);
                float2 grid = uv*14, cell = floor(grid), local = frac(grid)-.5;
                float h = RampHash(cell);
                local += float2(RampHash(cell+31),RampHash(cell+67))*.28-.14;
                float radius = .10 + RampHash(cell+19)*.23;
                float stone = (1-smoothstep(radius-.03,radius+.025,length(local*float2(1,1.3))))
                    * step(h, _GravelDensity);
                half3 earth = _BaseColor.rgb * (.82 + .22*mottling + .10*grain);
                half3 gravel = lerp(half3(.24,.27,.26), half3(.47,.49,.44), RampHash(cell+11));
                gravel *= .9 + .18 * step(0,local.x-local.y);
                surface.albedo = lerp(earth, gravel, stone);
            }
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
