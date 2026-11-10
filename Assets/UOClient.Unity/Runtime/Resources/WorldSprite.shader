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
        // Blend state: One/Zero (no blending, as always) unless a material asks; Avatar's spectral look sets
        // SrcAlpha/OneMinusSrcAlpha for TazUO's SPECTRAL mode (see-through black, MobileLook.SpectralPixel).
        [HideInInspector] _SrcBlend ("Src blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst blend", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend [_SrcBlend] [_DstBlend]
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ LG_SPRITE_SHADOW
            #pragma multi_compile __ UO_ITEM_LIGHTS
            #include "UnityCG.cginc"
            #ifdef UO_ITEM_LIGHTS
            // Item, static and multi-part lights (ItemLights); the TEST Terrain adds the same sum (LiveGroundLight.cginc).
            #include "Assets/UOClient.Unity/Runtime/Resources/UOItemLights.cginc"
            #endif

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Cutoff;
            float _Unlit;          // 1: drawn as is, neither lit nor greyed (the mouse-over outline)

            float4 _UOAmbient;     // rgb: ambient light for the time of day
            float4 _UOLightColor;  // rgb: point light color * intensity
            float4 _UOLightPos;    // xy: world position, z: radius
            float _UOCutZ;         // upper-floor cut: hide statics at or above this height (+1000); 0 = no cut
            float _UODead;         // 0..1: how far the world is drained to grey (the player is a ghost)
            float _UOFoliageSway;  // plants' lean at full weight, Unity units (FoliageWind); unset 0: still
            float _UOWindBoost;    // WeatherView: rain and storms blow harder (unset 0)
            float _UOFoliageSeason; // leaves' season (FoliageWind.SeasonCode): 0 as drawn, 1 spring, 2 fall, 3 winter, 4 desolation
            float _UOWindowGlow;   // ItemLights: how dark it is (0 day .. 1 night); windows' panes glow by it
            float _UOWaterStill;   // 1: no shimmer and no shore foam (the options' animated water off); unset 0: animated
            float4 _UOSeeThrough;  // SeeThroughTrees: xy the player's middle (Unity), z the circle's radius (0 = off), w the player's depth
            #ifdef LG_SPRITE_SHADOW
            // LiveGroundCompare (TEST only; LiveGroundLighting.cs, SpriteShadow, mirrors this line for line): sun-shadow
            // receive. Each pixel is put back into the world from its sprite's foot anchor and tested against the CPU
            // shadow ceiling (the UO Z below which a point is in the sun shadow). Without the keyword (the default) the
            // pass compiles to exactly the code outside these blocks.
            sampler2D _LGShadowCeil;   // world-wrapped shadow ceilings in UO Z (texel = 1/4 tile, SpriteShadow.WrapTiles tiles)
            float4 _LGSpriteShadowMap; // xy: world pixels = (unity x + x, y - unity y); z: art scale; w: 1 / wrapped tiles
            float _UOSunFade;          // the sun's shadows fade (CharacterShadow.SetSun: low sun, overcast); unset 0 = full

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
                float visibility = lerp(saturate((z + 0.25 - ceiling) / 2.0 + 0.5), 1.0, _UOSunFade);
                // Kind 0 (no foot: effects, meshes without UV3) is never shaded.
                return foot.w < 0.5 ? 1.0 : 0.7 + 0.3 * visibility;
            }

            // The tile point a sprite pixel stands for, as LGSpriteSunShade places it (ground: on the floor; upright: its
            // own foot tile), for item lights that walls stop (UOItemLightAddAt).
            float2 LGSpriteTile(float2 world, float4 foot)
            {
                const float h = 22.0, zs = 4.0;
                float s = _LGSpriteShadowMap.z;
                float wx = world.x + _LGSpriteShadowMap.x, wy = _LGSpriteShadowMap.y - world.y;
                float fx = foot.x + _LGSpriteShadowMap.x, fy = _LGSpriteShadowMap.y - foot.y;
                float a = wx / (h * s);
                float bg = (wy / s + zs * foot.z) / h;
                float2 ground = float2(a + bg, bg - a) * 0.5;
                float af = fx / (h * s), bf = (fy / s + zs * foot.z) / h;
                float2 tile = floor(float2(af + bf, bf - af) * 0.5);
                float up = (fy - wy) / s;
                float b = bf + saturate(-up / h);
                float2 upright = clamp(float2(a + b, b - a) * 0.5, tile + 0.125, tile + 0.875);
                return foot.w > 1.5 ? ground : upright;
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
                float water : TEXCOORD5;   // second UV y: 1 UO water art (shimmer); 2..3 leafy, 4..5 woody, 6..7 evergreen (PlantKind)
                float depth : TEXCOORD6;   // world Z: the draw-order depth (smaller = nearer the camera)
            #ifdef LG_SPRITE_SHADOW
                float4 foot : TEXCOORD3;
            #endif
            };

            // A 4x4 ordered dither threshold (0..1) for a screen pixel.
            float Bayer4(float2 screen)
            {
                int2 p = int2(fmod(floor(screen), 4.0));
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[p.y * 4 + p.x] + 0.5) / 16.0;
            }

            // UO's water art, animated without moving its pixels (that would pull neighbours in from the atlas): slow
            // rolling light swells crossing each other, a finer ripple, and sharp moving glints on the crests.
            // world: Unity units (world pixels at the art scale).
            float3 UOWaterShimmer(float3 rgb, float2 world)
            {
                float t = _Time.y;
                float2 p = world * 0.011;
                float w1 = sin(p.x * 1.7 + p.y * 0.6 + t * 1.05);
                float w2 = sin(-p.x * 0.8 + p.y * 1.9 - t * 0.8);
                float w3 = sin((p.x + p.y) * 3.3 + t * 2.2);
                float swell = (w1 + w2) * 0.5;
                float light = 1.0 + 0.08 * swell + 0.035 * w3;
                // Glints: small sparkles where two fine, fast ripples crest together, brighter on the swells' crests.
                float2 q = world * 0.07;
                float g1 = sin(q.x * 1.3 + q.y * 0.9 + t * 3.1);
                float g2 = sin(-q.x * 0.7 + q.y * 1.6 - t * 2.6);
                float glint = pow(saturate(g1 * g2), 24.0) * (0.25 + 0.2 * saturate(swell)) ;
                return rgb * light + glint * float3(0.8, 0.9, 1.0);
            }

            // A plant's lean (PlantKind): its top moves with slow sweeping gusts and a quicker flutter, its foot stays.
            // The phase comes from the vertex's world position at a long wavelength, so a sprite's two top corners move
            // together (the art keeps its shape) while trees across the screen sway out of step.
            float4 PlantSway(float4 vertex, float code)
            {
                float weight = frac(code);
                if (code < 1.5 || weight <= 0.0 || _UOFoliageSway <= 0.0)
                    return vertex;
                float2 w = mul(unity_ObjectToWorld, vertex).xy;
                float t = _Time.y;
                float gust = 0.55 + 0.45 * sin(w.x * 0.0021 - t * 0.55) * sin(w.y * 0.0017 + t * 0.4);
                float sway = sin(t * 1.3 + (w.x + w.y) * 0.0023) * 0.75 + sin(t * 3.1 + w.x * 0.0041) * 0.25;
                vertex.x += weight * _UOFoliageSway * (1.0 + _UOWindBoost) * gust * sway;
                return vertex;
            }

            // The season on a leafy plant's green pixels (FoliageWind.SeasonCode); trunks, petals and fruit keep their colour.
            // Fall picks gold, orange or red with a slow drift across the world, so neighbouring trees differ.
            float3 FoliageSeason(float3 c, float2 world)
            {
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                float green = saturate((c.g - max(c.r, c.b)) * 7.0 + 0.1);
                float3 t;
                if (_UOFoliageSeason < 1.5)
                    t = c * float3(1.0, 1.12, 0.9) + float3(0.02, 0.035, 0.0);           // spring: fresh, light
                else if (_UOFoliageSeason < 2.5)
                {
                    float n = 0.5 + 0.5 * sin(world.x * 0.0037 + 1.7) * sin(world.y * 0.0043 - 0.6);
                    float3 hue = n < 0.4 ? lerp(float3(1.0, 0.78, 0.28), float3(1.0, 0.56, 0.18), n / 0.4)   // gold to orange
                                         : lerp(float3(1.0, 0.56, 0.18), float3(0.86, 0.3, 0.14), (n - 0.4) / 0.6); // to red
                    t = lum * 1.6 * hue;
                }
                else if (_UOFoliageSeason < 3.5)
                    t = lerp(lum * float3(0.86, 0.9, 1.0), float3(0.9, 0.93, 0.98), 0.4); // winter: frosted
                else
                    t = lum * float3(0.78, 0.7, 0.55);                                      // desolation: withered
                return lerp(c, t, green);
            }

            // A lit window's warmth (second UV y 8, PlantKind.Window): about three windows in four, each flickering a
            // little like a hearth behind it; 0 by day.
            float3 WindowWarmth(float2 world)
            {
                float2 cell = floor(world / 61.0);
                float h = frac(sin(dot(cell, float2(12.9898, 78.233))) * 43758.5453);
                float flicker = 0.88 + 0.12 * sin(_Time.y * (2.0 + h * 3.0) + h * 40.0);
                return float3(1.0, 0.7, 0.36) * step(0.25, h) * flicker * _UOWindowGlow;
            }

            // Glass: a pane's dark, cool pixels (not the frame or the stone around it) glow, added after the light.
            float3 WindowGlow(float3 art, float2 world)
            {
                float lum = dot(art, float3(0.299, 0.587, 0.114));
                float pane = saturate((0.42 - lum) * 5.0) * saturate((art.b - art.r - 0.04) * 10.0);
                return WindowWarmth(world) * pane * 0.95;
            }

            // An opening in a window's art (see-through, with art on all four sides close by): light from inside fills it.
            bool WindowHole(float2 uv)
            {
                float2 t = _MainTex_TexelSize.xy;
                bool l = false, r = false, u = false, d = false;
                // Up to 48 texels each way, two at a time (art at 2x or 3x makes a pane that tall).
                [loop] for (int k = 2; k <= 48; k += 2)
                {
                    l = l || tex2Dlod(_MainTex, float4(uv - float2(t.x * k, 0), 0, 0)).a > 0.5;
                    r = r || tex2Dlod(_MainTex, float4(uv + float2(t.x * k, 0), 0, 0)).a > 0.5;
                    u = u || tex2Dlod(_MainTex, float4(uv + float2(0, t.y * k), 0, 0)).a > 0.5;
                    d = d || tex2Dlod(_MainTex, float4(uv - float2(0, t.y * k), 0, 0)).a > 0.5;
                    if (l && r && u && d)
                        break;
                }
                return l && r && u && d;
            }

            // Foam where water meets the shore (second UV y: 1 + half the nearness of dry land, MeshBuilder.AddQuad): a broken
            // white edge that swells and ebbs as small waves roll in.
            float3 UOShoreFoam(float3 rgb, float2 world, float shore)
            {
                float t = _Time.y;
                float n = sin(world.x * 0.09 + t * 1.3) * sin(world.y * 0.11 - t * 1.1) * 0.5 + 0.5;
                float n2 = sin(world.x * 0.23 - t * 0.7) * sin(world.y * 0.19 + t * 0.9) * 0.5 + 0.5;
                float band = sin(shore * 12.0 - t * 1.8) * 0.5 + 0.5;
                float f = smoothstep(0.35, 0.85, shore + (n - 0.5) * 0.35 + (n2 - 0.5) * 0.15) * (0.6 + 0.4 * band);
                return lerp(rgb, float3(0.86, 0.92, 0.96), saturate(f * 0.8));
            }

            v2f vert(appdata v)
            {
                v2f o;
                v.vertex = PlantSway(v.vertex, v.height.y);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.height = v.height.x;
                o.water = v.height.y;
                o.depth = mul(unity_ObjectToWorld, v.vertex).z;
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
                // A lit window's open pane at night shows the warm room behind it instead of the dark.
                if (c.a < _Cutoff && i.water > 7.5 && i.water < 8.5 && _UOWindowGlow > 0.01 && WindowHole(i.uv))
                    return fixed4(WindowWarmth(i.world) * 0.75 * (1.0 - _UODead), 1.0);
                clip(c.a - _Cutoff);
                // SeeThroughTrees: a plant in front of the player thins out round them (dithered: the sprites stay
                // opaque cutouts, so the draw order holds).
                if (_UOSeeThrough.z > 0.5 && i.water >= 1.5 && i.water < 7.5 && i.depth < _UOSeeThrough.w)
                {
                    float near = 1.0 - smoothstep(0.55, 1.0, distance(i.world, _UOSeeThrough.xy) / _UOSeeThrough.z);
                    clip(Bayer4(i.pos.xy) - near * 0.7);
                }
                if (_Unlit > 0.5)
                    return c * i.color;
                fixed3 art = c.rgb;
                if (i.water > 0.5 && i.water < 1.5)
                    c.rgb = _UOWaterStill < 0.5 ? UOWaterShimmer(c.rgb, i.world) : c.rgb; // animated water off: as drawn
                else if (_UOFoliageSeason > 0.5 && (i.water >= 1.5 && i.water < 3.5 || i.water >= 5.5 && i.water < 7.5 && _UOFoliageSeason > 2.5))
                    c.rgb = FoliageSeason(c.rgb, i.world); // leaves every season; evergreens only winter and desolation
                if (_UOWaterStill < 0.5 && i.water > 1.01 && i.water < 1.5)
                    c.rgb = UOShoreFoam(c.rgb, i.world, (i.water - 1.0) * 2.0);

                float d = distance(i.world, _UOLightPos.xy) / max(_UOLightPos.z, 1.0);
                float falloff = saturate(1.0 - d);
                falloff *= falloff;

            #ifdef LG_SPRITE_SHADOW
                // The sun shade dims only the ambient (LiveGroundLighting.Lit): ambient * shade + player light, written
                // as a factor on the line below so the default path stays as it was.
                float3 lgLit = _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                c.rgb *= (_UOAmbient.rgb * LGSpriteSunShade(i.world, i.foot) + _UOLightColor.rgb * falloff) / max(lgLit, 1e-4);
            #endif
            #ifdef UO_ITEM_LIGHTS
                // The lights near this pixel join the time-of-day ambient and the player's light (written as a factor on
                // the line below, so the path without the keyword stays as it was); with the TEST sun shade, on top of it.
                #ifdef LG_SPRITE_SHADOW
                float3 uoLit = _UOAmbient.rgb * LGSpriteSunShade(i.world, i.foot) + _UOLightColor.rgb * falloff;
                #else
                float3 uoLit = _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                #endif
                #ifdef LG_SPRITE_SHADOW
                // Where the sprite's tile is known, a wall between it and a light stops that light (ItemLights.Trace).
                c.rgb *= (uoLit + (i.foot.w > 0.5 ? UOItemLightAddAt(i.world, LGSpriteTile(i.world, i.foot), uoLit) : UOItemLightAdd(i.world, uoLit))) / max(uoLit, 1e-4);
                #else
                c.rgb *= (uoLit + UOItemLightAdd(i.world, uoLit)) / max(uoLit, 1e-4);
                #endif
            #endif
                c.rgb *= _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                c *= i.color;
                if (i.water > 7.5 && i.water < 8.5 && _UOWindowGlow > 0.01)
                    c.rgb += WindowGlow(art, i.world);
                // As a ghost the classic client draws the world in black and white.
                c.rgb = lerp(c.rgb, dot(c.rgb, float3(0.299, 0.587, 0.114)).xxx, _UODead);
                return c;
            }
            ENDCG
        }
    }
}
