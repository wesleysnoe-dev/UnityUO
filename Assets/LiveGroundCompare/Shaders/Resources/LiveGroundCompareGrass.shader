// LiveGroundCompare grass (-grass with -terrainstyle natural): generated blades over the natural terrain (TerrainGrass.cs).
// Colour: the grass material's own mean albedo and UO tint (_LGAlbedoArray / _LGMatTint, globals from TerrainMaterialSet),
// darker at the root, lighter and warmer at the tip; jungle (vertex alpha) deeper and more saturated.
// Wind: the tips sway along one wind direction in long rolling waves with slow gusts, from _Time only (no script per frame).
// Light: the terrain's (LiveGroundLight.cginc): time-of-day ambient, the chunk's sun mask and the shadow ceiling, the
// player's and item lights, the ghost grey.
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
    }

    SubShader
    {
        Tags { "Queue" = "Geometry+10" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

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

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;  // x: 0 root .. 1 tip, y: blade seed
                float2 uv2 : TEXCOORD1; // the chunk's terrain UV
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
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                // Wind along terrain (+x, -z) (screen right and a little down), in waves rolling across the field.
                float2 dir = normalize(float2(1.0, -0.45));
                float along = dot(w.xz, dir);
                float t = _Time.y * _WaveSpeed;
                float wave = sin(along * _WaveScale * 6.2831 - t * 2.2) * 0.55 + 0.45;
                float gust = sin(along * 0.05 - _Time.y * 0.35) * 0.5 + 0.5;
                gust *= gust;
                float flutter = sin(t * 7.0 + v.uv.y * 40.0) * 0.12;
                float bend = v.uv.x * v.uv.x;
                float sway = (wave * (0.6 + gust * 0.9) + flutter) * _Sway * bend;
                w.xz += dir * sway;
                w.y -= abs(sway) * 0.35 * v.uv.x; // a bent blade is a little shorter
                o.pos = mul(UNITY_MATRIX_VP, float4(w, 1.0));
                o.world = w;
                o.color = v.color;
                o.uv = v.uv;
                o.chunkUv = v.uv2;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // The ground's own grass colour (layer 0, jungle 3), its average at a low mip, tinted toward UO's.
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
