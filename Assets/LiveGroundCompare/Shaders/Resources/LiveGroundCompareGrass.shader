// LiveGroundCompare grass (-grass with -terrainstyle natural): generated blades over the natural terrain (GrassBuilder,
// TerrainGrass). Drawn by the MAIN camera among the sprites: each vertex is already at its projected Unity position and
// its tile's depth key (a hair nearer than that tile's land), so blades in front of a character cover its feet, those
// behind stay behind, and the terrain's depth proxy hides grass behind hills. Cutout-opaque like WorldSprite (ZWrite).
// Colour: the grass material's own mean albedo and UO tint (_LGAlbedoArray / _LGMatTint, globals from
// TerrainMaterialSet), darker at the root, lighter at the tip; jungle (vertex alpha) deeper.
// Wind: tips sway across the screen (_WindVec, Unity per terrain unit) in rolling waves with gusts, from _Time only.
// Light: the terrain's (LiveGroundLight.cginc) at the blade's terrain position (UV3): ambient, sun mask and shadow
// ceiling, player and item lights, ghost.
Shader "LiveGroundCompare/Grass"
{
    Properties
    {
        _LGShadowTex ("Sun visibility (1 lit)", 2D) = "white" {}
        _Sway ("Sway (terrain units at the tip)", Range(0, 0.5)) = 0.13
        _WaveSpeed ("Wave speed", Range(0, 4)) = 1.4
        _WaveScale ("Wave length (1 / tiles)", Range(0.02, 1)) = 0.22
        _RootDark ("Root darkness", Range(0, 1)) = 0.55
        _TipLight ("Tip brightness", Range(0.5, 2)) = 1.3
        [HideInInspector] _WindVec ("Unity per terrain unit across the screen", Vector) = (1, 0, 0, 0)
        [HideInInspector] _UpVec ("Unity per terrain height unit", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest-20" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile __ UO_ITEM_LIGHTS
            #include "UnityCG.cginc"
            #include "../LiveGroundLight.cginc"

            sampler2D _LGShadowTex;
            UNITY_DECLARE_TEX2DARRAY(_LGAlbedoArray);
            float4 _LGMatTint[8];
            float _Sway, _WaveSpeed, _WaveScale, _RootDark, _TipLight;
            float4 _WindVec, _UpVec;
            float _UOWindBoost; // WeatherView: rain and storms bend the grass harder and faster (unset 0)
            sampler2D _LGGrassCover; // GrassCover: house parts' tiles (r 1), world-wrapped every GrassCover.Wrap tiles
            float _LGGrassCoverOn;

            struct appdata
            {
                float4 vertex : POSITION;   // Unity position and depth key (TerrainGrass)
                float4 color : COLOR;
                float2 uv : TEXCOORD0;      // x: 0 root .. 1 tip, y: blade seed
                float2 uv2 : TEXCOORD1;     // the chunk's terrain UV (sun mask)
                float3 terrain : TEXCOORD2; // terrain-space position (wind phase, light)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 chunkUv : TEXCOORD1;
                float3 world : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 w = v.terrain;
                // Waves roll across the screen along the wind (terrain (1, 0, 1)).
                float along = (w.x + w.z) * 0.70710678;
                float t = _Time.y * _WaveSpeed * (1.0 + _UOWindBoost * 0.6);
                float wave = sin(along * _WaveScale * 6.2831 - t * 2.2) * 0.55 + 0.45;
                float gust = sin(along * 0.05 - _Time.y * 0.35) * 0.5 + 0.5;
                gust *= gust;
                float flutter = sin(t * 7.0 + v.uv.y * 40.0) * 0.12;
                float bend = v.uv.x * v.uv.x;
                float sway = (wave * (0.6 + gust * 0.9) + flutter) * _Sway * bend * (1.0 + _UOWindBoost);
                float4 p = v.vertex;
                p.xy += _WindVec.xy * sway - _UpVec.xy * abs(sway) * 0.35 * v.uv.x; // a bent blade is a little shorter
                o.pos = UnityObjectToClipPos(p);
                o.world = w;
                o.color = v.color;
                o.uv = v.uv;
                o.chunkUv = v.uv2;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // A house's floor, step or wall stands on this tile (GrassCover): no blade shows through it.
                if (_LGGrassCoverOn > 0.5)
                {
                    float2 coverUv = (floor(float2(i.world.x, -i.world.z)) + 0.5) / 256.0; // GrassCover.Wrap
                    if (tex2Dlod(_LGGrassCover, float4(coverUv, 0, 0)).r > 0.5)
                        discard;
                }
                float lush = i.color.a;
                float3 grass = UNITY_SAMPLE_TEX2DARRAY_LOD(_LGAlbedoArray, float3(0.5, 0.5, 0), 8).rgb * _LGMatTint[0].rgb;
                float3 jungle = UNITY_SAMPLE_TEX2DARRAY_LOD(_LGAlbedoArray, float3(0.5, 0.5, 3), 8).rgb * _LGMatTint[3].rgb;
                float3 base = lerp(grass, jungle * 1.1, lush);
                float3 tip = base * _TipLight + float3(0.05, 0.05, 0.0);
                float3 col = lerp(base * (1.0 - _RootDark), tip, saturate(i.uv.x * 1.15)) * i.color.rgb;

                float visibility = min(tex2D(_LGShadowTex, i.chunkUv).r, LGCeilVisibility(i.world));
                float3 rgb = col * LGLitShadowed(float3(0, 1, 0), visibility, i.world);
                return fixed4(LGGhost(rgb), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
