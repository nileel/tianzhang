Shader "TianZhang/Jindan/Cast Light"
{
    Properties
    {
        _Tint ("Element", Color) = (1,.25,.025,1)
        _Shape ("Charge, trail, column, ground, or lamp", Float) = 0
        _Family ("Fire, earth, water, space, sun, or moon", Float) = 0
        _Opacity ("Opacity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+18" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Shape, _Family, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                float3 s = frac(p.xyx * .1031);
                s += dot(s, s.yzx + 33.33);
                return frac((s.x + s.y) * s.z);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }

            // A luminous strand with a soft falloff. The pale interior stays legible in daylight.
            float Strand(float distance, float width) { return exp2(-distance * distance / (width * width)); }

            half4 Frag(Varyings i):SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float t = _Time.y, r = length(p), theta = atan2(p.y, p.x);
                float heat = 0, halo = 0;
                if (_Shape < .5)
                {
                    // The seed and a pair of curling wisps meet at the caster's hand.
                    float wrapped = sin(2.0 * theta - 7.0 * r + t * 8.0);
                    float curl = Strand(wrapped, .18) * smoothstep(.12,.33,r) * (1.0-smoothstep(.40,.88,r));
                    heat = exp2(-23.0*r*r) + .75*curl;
                    halo = .7*exp2(-5.7*r*r);
                }
                else if (_Shape < 1.5)
                {
                    float u = i.uv.x;
                    float sweep = sin(u*13.0 - t*14.0) * .16 * (1.0-u);
                    float flow = Noise(float2(u*6.0-t*4.0, p.y*2.0));
                    float y = p.y - sweep;
                    float side = y + .28*sin(u*16.0 - t*9.0);
                    heat = (Strand(y, .18+.08*flow) + .44*Strand(side,.10)) * smoothstep(0,.5,u);
                    halo = Strand(y,.54) * (.52+.4*flow) * smoothstep(0,.33,u);
                }
                else if (_Shape < 2.5)
                {
                    // Elemental silhouettes bloom vertically out of the actual landing point.
                    float y = i.uv.y, x = p.x;
                    float noise = Noise(float2(x*3.0,y*5.2-t*5.0));
                    float width = lerp(.53,.018,y);
                    float curl = .23*sin(y*7.0 - t*6.0) * y + (noise-.5)*.28;
                    float tongues = Strand(x-curl,width) + .55*Strand(abs(x)-(.28+.18*y), width*.37);
                    if (_Family > .5 && _Family < 1.5)
                        tongues = saturate(1.0-abs(x)/width) + .88*saturate(1.0-abs(abs(x)-.35*y)/(.24*(1.01-y)));
                    else if (_Family > 1.5 && _Family < 2.5)
                        tongues = Strand(x-curl*.45,width*.85) + .78*Strand(abs(x)-(.12+.62*y*y),width*.43);
                    else if (_Family > 2.5 && _Family < 3.5)
                        tongues = .85*Strand(abs(x)-.52*(1.0-abs(2.0*y-1.0)),.075) + .72*Strand(x,width*.32);
                    else if (_Family > 3.5 && _Family < 4.5)
                        tongues = Strand(x,width*.53) + .72*Strand(abs(x)-(.06+.42*y),width*.42);
                    else if (_Family > 4.5)
                        tongues = Strand(x-.38*sin(y*3.14),width*.72) + .5*Strand(x+.24*sin(y*5.0),width*.30);
                    float cap = smoothstep(0,.07,y) * (1.0-smoothstep(.68,1.0,y));
                    heat = tongues * cap * (.56+.72*noise);
                    halo = Strand(x, width+.21) * cap * (.45+.24*noise);
                }
                else if (_Shape < 3.5)
                {
                    float eddy = Noise(p*3.5+float2(t*.8,0));
                    float wave = .67 + .07*sin(theta*5.0 + t*3.0) + .09*(eddy-.5);
                    heat = .68*Strand(r-wave,.055) * (.35+.65*eddy) + .55*exp2(-11*r*r);
                    halo = .53*exp2(-4.1*r*r);
                }
                else
                {
                    // Quiet light surrounding the persistent stone lamp.
                    float flicker = .93 + .07*sin(3.8*t);
                    heat = .62*exp2(-26*r*r) * flicker;
                    halo = .67*exp2(-6.3*r*r) * flicker;
                }
                float boundary = 1.0 - smoothstep(.90,1.0,max(abs(p.x),abs(p.y)));
                float opacity = saturate(heat*1.85 + halo*.74) * boundary * _Opacity;
                float3 outer = _Tint.rgb * float3(.33,.25,.32);
                float3 colour = lerp(outer,_Tint.rgb,smoothstep(.005,.34,heat + halo*.55));
                float3 gold = _Family < .5 ? float3(1,.65,.13) : lerp(_Tint.rgb,float3(1,1,1),.42);
                colour = lerp(colour,gold,smoothstep(.22,.62,heat));
                colour = lerp(colour,lerp(gold,float3(1,1,1),.90),smoothstep(.56,.95,heat));
                return half4(colour,opacity);
            }
            ENDHLSL
        }
    }
}
