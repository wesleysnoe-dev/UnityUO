// World and character sprites. UO art has 1-bit alpha, so sprites are drawn as opaque cutouts with
// depth: each sprite's Z comes from its draw-order index, which lets every atlas page batch into one
// mesh while still sorting correctly against the others and against moving characters.
// Lighting is global: an ambient color for the time of day plus one soft point light (the player's).
Shader "UOClient/WorldSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Unlit ("Unlit (hover outline)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ LG_SPRITE_SHADOW
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Cutoff;
            float _Unlit;          // 1: drawn as is, neither lit nor greyed (the mouse-over outline)

            float4 _UOAmbient;     // rgb: ambient light for the time of day
            float4 _UOLightColor;  // rgb: point light color * intensity
            float4 _UOLightPos;    // xy: world position, z: radius
            float _UOCutZ;         // upper-floor cut: hide statics at or above this height (+1000); 0 = no cut
            float _UODead;         // 0..1: how far the world is drained to grey (the player is a ghost)
            #ifdef LG_SPRITE_SHADOW
            // LiveGroundCompare (TEST only; LiveGroundLighting.cs, SpriteShadow, mirrors this line for line): sun-shadow
            // receive. Each pixel is put back into the world from its sprite's foot anchor and tested against the CPU
            // shadow ceiling (the UO Z below which a point is in the sun shadow). Without the keyword (the default) the
            // pass compiles to exactly the code outside these blocks.
            sampler2D _LGShadowCeil;   // world-wrapped shadow ceilings in UO Z (texel = 1/4 tile, SpriteShadow.WrapTiles tiles)
            float4 _LGSpriteShadowMap; // xy: world pixels = (unity x + x, y - unity y); z: art scale; w: 1 / wrapped tiles

            float LGSpriteSunShade(float2 world, float4 foot)
            {
                const float h = 22.0, zs = 4.0;
                float s = _LGSpriteShadowMap.z;
                float wx = world.x + _LGSpriteShadowMap.x, wy = _LGSpriteShadowMap.y - world.y;
                float fx = foot.x + _LGSpriteShadowMap.x, fy = _LGSpriteShadowMap.y - foot.y;
                float a = wx / (h * s);                                 // x - y
                // Ground (kind 2): on the ground at the foot's Z.
                float bg = (wy / s + zs * foot.z) / h;
                float2 ground = float2(a + bg, bg - a) * 0.5;
                // Upright (kind 1): above the foot a vertical plane rising 4 px per Z, below it the tile's front half on
                // the ground; kept 1/8 tile (half a texel) inside the foot tile, so the filter never reads a neighbour's
                // texel, whose ceiling includes this tile's own column.
                float af = fx / (h * s), bf = (fy / s + zs * foot.z) / h;
                float2 tile = floor(float2(af + bf, bf - af) * 0.5);
                float up = (fy - wy) / s;
                float b = bf + saturate(-up / h);
                float2 upright = clamp(float2(a + b, b - a) * 0.5, tile + 0.125, tile + 0.875);
                bool flat = foot.w > 1.5;
                float2 p = flat ? ground : upright;
                float z = foot.z + (flat ? 0.0 : max(up, 0.0) / zs);
                float ceiling = tex2D(_LGShadowCeil, p * _LGSpriteShadowMap.w).r;
                float visibility = saturate((z + 0.25 - ceiling) / 2.0 + 0.5);
                // Kind 0 (no foot: effects, meshes without UV3) is never shaded.
                return foot.w < 0.5 ? 1.0 : 0.7 + 0.3 * visibility;
            }
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 height : TEXCOORD1; // x: the sprite's tile height + 1000 (0 = never cut: land, effects)
                float4 color : COLOR;
            #ifdef LG_SPRITE_SHADOW
                float4 foot : TEXCOORD2;   // foot anchor: Unity x, y; UO Z; kind (0 none, 1 upright, 2 ground)
            #endif
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 world : TEXCOORD1;
                float height : TEXCOORD2;
            #ifdef LG_SPRITE_SHADOW
                float4 foot : TEXCOORD3;
            #endif
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.height = v.height.x;
            #ifdef LG_SPRITE_SHADOW
                o.foot = float4(mul(unity_ObjectToWorld, float4(v.foot.xy, v.vertex.z, 1.0)).xy, v.foot.zw);
            #endif
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Inside a building: what is at or above the floor overhead is not drawn (the classic client's max Z).
                if (_UOCutZ > 0.5 && i.height > 0.5 && i.height >= _UOCutZ)
                    discard;
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);
                if (_Unlit > 0.5)
                    return c * i.color;

                float d = distance(i.world, _UOLightPos.xy) / max(_UOLightPos.z, 1.0);
                float falloff = saturate(1.0 - d);
                falloff *= falloff;

            #ifdef LG_SPRITE_SHADOW
                // The sun shade dims only the ambient (LiveGroundLighting.Lit): ambient * shade + player light, written
                // as a factor on the line below so the default path stays as it was.
                float3 lgLit = _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                c.rgb *= (_UOAmbient.rgb * LGSpriteSunShade(i.world, i.foot) + _UOLightColor.rgb * falloff) / max(lgLit, 1e-4);
            #endif
                c.rgb *= _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                c *= i.color;
                // As a ghost the classic client draws the world in black and white.
                c.rgb = lerp(c.rgb, dot(c.rgb, float3(0.299, 0.587, 0.114)).xxx, _UODead);
                return c;
            }
            ENDCG
        }
    }
}
