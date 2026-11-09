// LiveGroundCompare natural terrain (-terrainstyle natural): the Terrain backend's land as blended materials instead of
// per-tile art. Same contract and light as LiveGroundCompare/TerrainLit (LiveGroundLight.cginc, sun mask, ceiling),
// plus:
//   _LGWeights0/_LGWeights1: weights of materials 0-3 / 4-7 (TerrainMaterials), _LGWeights2.r: weight of the classic art (_MainTex),
//                    chunk UV like _MainTex (SplatBuilder: 4 samples per tile, borders already wandered by noise).
//   _LGAlbedoArray:  per material sRGB albedo, height in alpha; _LGNormalArray: per material normal map (BC5 / DXT5nm).
//   _LGMatTint[8]:   per material colour multiplier toward UO's own mean colour (TerrainMaterialSet).
// Materials are mapped in world space (terrain space: 1 unit per tile, x = tile x, z = -tile y), not per tile; two
// samples at unrelated scale and rotation, mixed by a large noise, hide the repetition; height-based blending lets grass
// sit in the dips and stones show through where two materials meet.
Shader "LiveGroundCompare/TerrainSplat"
{
    Properties
    {
        _MainTex ("Chunk art", 2D) = "white" {}
        _LGShadowTex ("Sun visibility (1 lit)", 2D) = "white" {}
        _LGWeights0 ("Weights 0-3", 2D) = "black" {}
        _LGWeights1 ("Weights 4-7", 2D) = "black" {}
        _LGWeights2 ("Weight art (r)", 2D) = "red" {}
        _Tiling ("Tiles per texture repeat", Float) = 5
        _NormalStrength ("Normal map strength", Range(0, 2)) = 1.1
        _HeightBlend ("Height blend sharpness", Range(0.02, 1)) = 0.18
        _Macro ("Large-scale variation", Range(0, 0.5)) = 0.22
        _Detail ("Detail contrast", Range(0.5, 3)) = 1.7
        _ScaleB ("Second sample's scale (hides repeats; near 1 keeps one stone size)", Range(1, 3)) = 1.31
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma require 2darray
            #pragma multi_compile __ UO_ITEM_LIGHTS
            #include "UnityCG.cginc"
            #include "../LiveGroundLight.cginc"

            sampler2D _MainTex;
            sampler2D _LGShadowTex;
            sampler2D _LGWeights0, _LGWeights1, _LGWeights2;
            UNITY_DECLARE_TEX2DARRAY(_LGAlbedoArray);
            UNITY_DECLARE_TEX2DARRAY(_LGNormalArray);
            float4 _LGMatTint[8];
            float _Tiling, _NormalStrength, _HeightBlend, _Macro, _Detail, _ScaleB;

            // Explicit gradients: the samples sit in per-pixel branches, where implicit derivatives are undefined.
            float4 SampleA(float2 uv, float2 dx, float2 dy, float k) { return _LGAlbedoArray.SampleGrad(sampler_LGAlbedoArray, float3(uv, k), dx, dy); }
            float4 SampleN(float2 uv, float2 dx, float2 dy, float k) { return _LGNormalArray.SampleGrad(sampler_LGNormalArray, float3(uv, k), dx, dy); }
            float4 SampleMean(float2 uv, float k) { return _LGAlbedoArray.SampleLevel(sampler_LGAlbedoArray, float3(uv, k), 6.0); }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(c), Hash(c + float2(1, 0)), u.x), lerp(Hash(c + float2(0, 1)), Hash(c + 1.0), u.x), u.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float4 s0 = tex2D(_LGWeights0, i.uv), s1 = tex2D(_LGWeights1, i.uv);
                float artW = tex2D(_LGWeights2, i.uv).r;
                float w[8] = { s0.r, s0.g, s0.b, s0.a, s1.r, s1.g, s1.b, s1.a };

                // World-space material UVs: tile (x, y) -> (x, y) / tiling; a second, rotated and slightly rescaled set
                // (_ScaleB near 1: the repeat is hidden but the stones keep one size).
                float2 tile = float2(i.world.x, -i.world.z);
                float2x2 rotB = float2x2(0.8, -0.6, 0.6, 0.8);
                float2 uvA = tile / _Tiling;
                float2 uvB = mul(rotB, tile) / (_Tiling * _ScaleB) + float2(0.31, 0.57);
                float2 dxA = ddx(uvA), dyA = ddy(uvA), dxB = ddx(uvB), dyB = ddy(uvB);
                // Triplanar on slopes: a top-down projection stretches a material up a slope facing the camera, so steep
                // ground also takes it from the side planes (terrain space is isotropic: 1 tile = 1 unit = 7.8 Z).
                float3 gn = normalize(i.normal);
                float3 bw = pow(abs(gn), 4.0);
                bw /= (bw.x + bw.y + bw.z);
                float2 frontP = float2(i.world.x, i.world.y), sideP = float2(-i.world.z, i.world.y);
                float2 uvAf = frontP / _Tiling, uvBf = mul(rotB, frontP) / (_Tiling * _ScaleB) + float2(0.31, 0.57);
                float2 uvAs = sideP / _Tiling, uvBs = mul(rotB, sideP) / (_Tiling * _ScaleB) + float2(0.31, 0.57);
                float2 dxAf = ddx(uvAf), dyAf = ddy(uvAf), dxBf = ddx(uvBf), dyBf = ddy(uvBf);
                float2 dxAs = ddx(uvAs), dyAs = ddy(uvAs), dxBs = ddx(uvBs), dyBs = ddy(uvBs);
                float mix2 = smoothstep(0.25, 0.75, Noise(tile * 0.11 + 7.3));
                float macro = 1.0 + _Macro * (Noise(tile * 0.045) * 2.0 - 1.0) + _Macro * 0.5 * (Noise(tile * 0.19 + 3.1) * 2.0 - 1.0);

                // Height blend: each weight raised by its texture's height, then only the top band survives.
                float4 alb[8];
                float h[8];
                float top = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    alb[k] = 0;
                    h[k] = 0;
                    if (w[k] > 0.004)
                    {
                        float4 a = SampleA(uvA, dxA, dyA, k);
                        float4 b = SampleA(uvB, dxB, dyB, k);
                        alb[k] = lerp(a, b, mix2) * bw.y;
                        if (bw.z > 0.01)
                            alb[k] += lerp(SampleA(uvAf, dxAf, dyAf, k), SampleA(uvBf, dxBf, dyBf, k), mix2) * bw.z;
                        if (bw.x > 0.01)
                            alb[k] += lerp(SampleA(uvAs, dxAs, dyAs, k), SampleA(uvBs, dxBs, dyBs, k), mix2) * bw.x;
                        // More detail contrast around the local average, so clumps and stones read at game zoom.
                        float3 avg = lerp(SampleMean(uvA, k).rgb, SampleMean(uvB, k).rgb, mix2);
                        alb[k].rgb = saturate(avg + (alb[k].rgb - avg) * _Detail);
                        h[k] = w[k] + alb[k].a * 0.5;
                        top = max(top, h[k]);
                    }
                }
                float3 col = 0;
                float3 nts = 0;
                float total = 0;
                [unroll] for (int m = 0; m < 8; m++)
                {
                    if (w[m] > 0.004)
                    {
                        float f = max(h[m] - (top - _HeightBlend), 0.0);
                        col += alb[m].rgb * _LGMatTint[m].rgb * f;
                        float4 na = SampleN(uvA, dxA, dyA, m);
                        float4 nb = SampleN(uvB, dxB, dyB, m);
                        nts += UnpackNormalmapRGorAG(lerp(na, nb, mix2)) * f;
                        total += f;
                    }
                }
                col = total > 0 ? col / total : 0.5;
                nts = total > 0 ? nts / total : float3(0, 0, 1);
                col *= macro;

                // Tangent frame on the terrain: u along +x, v along tile y = -z.
                float3 n0 = normalize(i.normal);
                float3 t = normalize(float3(1, 0, 0) - n0 * n0.x);
                float3 b = normalize(cross(t, n0)); // points along -z (tile +y)
                float3 n = normalize(n0 * nts.z + (t * nts.x + b * nts.y) * _NormalStrength);

                // The classic art where the splat keeps it (floors, brick, caves, water beds...).
                float3 art = tex2D(_MainTex, i.uv).rgb;
                float matW = saturate(1.0 - artW);
                float3 ground = lerp(art, col, matW);
                float3 shadeN = normalize(lerp(n0, n, matW));

                float visibility = min(tex2D(_LGShadowTex, i.uv).r, LGCeilVisibility(i.world));
                float3 rgb = ground * LGLitShadowed(shadeN, visibility, i.world);
                return fixed4(LGGhost(rgb), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
