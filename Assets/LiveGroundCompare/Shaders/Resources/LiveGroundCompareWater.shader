// LiveGroundCompare (TEST only): the Terrain backend's water surface over real-map water land tiles.
// Drawn by TerrainIsoPresenter's camera on layer 31 after the opaque terrain, alpha-blended over the classic water art
// the terrain already shows. Built-in pipeline, no Unity lights: lit like the sprites and the terrain
// (LiveGroundLight.cginc): the compositor's sun shade on the wave normal, times the time-of-day ambient and the player's
// light, ghost grey; the sun highlight is scaled by the ambient. No emission.
// Sun shadow: the presenter's world CPU ceiling (LGCeilVisibility, as TerrainLit and the sprites) dims only the ambient
// term and the sun highlight (LGLitShadowed); the player's light is not shadowed. Without the ceiling (_LGCeilOn 0)
// visibility is 1 and the water is lit exactly as before.
//
// Mesh contract (TerrainWater): world space = terrain space (1 unit per tile, +Y up, tile y along -Z);
// vertex colour rgb = mean colour of that tile's classic art; uv.x = shore distance / MaxShore (0 shoreline, 1 open).
// Motion: analytic wave normals and a shore foam band driven by _Time.y only (no textures, no scripts per frame).
Shader "LiveGroundCompare/Water"
{
    Properties
    {
        _EyeDir ("Direction to the viewer (terrain space)", Vector) = (0.5, 0.7071, -0.5, 0)
        _WaveAmp ("Wave normal strength", Range(0, 1)) = 0.35
        _WaveSpeed ("Wave speed", Range(0, 4)) = 1
        _ShoreAlpha ("Alpha at the shoreline", Range(0, 1)) = 0.35
        _OpenAlpha ("Alpha in open water", Range(0, 1)) = 0.7
        _DeepShade ("Open-water darkening", Range(0, 1)) = 0.2
        _Foam ("Shore foam strength", Range(0, 1)) = 0.55
        _Gloss ("Specular strength", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1 // coplanar with the terrain at the same corner heights

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "../LiveGroundLight.cginc"

            float4 _EyeDir;
            float _WaveAmp, _WaveSpeed, _ShoreAlpha, _OpenAlpha, _DeepShade, _Foam, _Gloss;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float shore : TEXCOORD1;
                fixed3 tint : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.shore = saturate(v.uv.x);
                o.tint = v.color.rgb;
                return o;
            }

            // One travelling wave: adds d(height)/d(x, z) for direction dir, wavelength (tiles), speed (tiles/s).
            float2 Wave(float2 p, float2 dir, float wavelength, float speed, float t)
            {
                float k = 6.2831853 / wavelength;
                float phase = k * (dot(p, dir) - speed * t);
                return dir * (k * cos(phase));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = i.world.xz;

                float2 g = 0;
                g += 0.050 * Wave(p, normalize(float2(1.0, 0.35)), 2.3, 0.35, t);
                g += 0.035 * Wave(p, normalize(float2(-0.4, 1.0)), 1.45, 0.28, t);
                g += 0.020 * Wave(p, normalize(float2(0.8, -0.7)), 0.85, 0.22, t);
                g += 0.012 * Wave(p, normalize(float2(-1.0, -0.2)), 0.55, 0.18, t);
                float3 n = normalize(float3(-g.x * _WaveAmp * 6.0, 1.0, -g.y * _WaveAmp * 6.0));

                float3 l = normalize(_LGSunDir.xyz);
                float3 v = normalize(_EyeDir.xyz);
                float visibility = LGCeilVisibility(i.world); // 1 without the ceiling
                float spec = pow(saturate(dot(n, normalize(l + v))), 48.0) * _Gloss * visibility; // no sun glint in shadow

                // The map's own water colour, slightly darker away from the shore (shore distance, not real depth).
                float open = i.shore;
                float3 baseCol = i.tint * lerp(1.0, 1.0 - _DeepShade, open);

                // Foam: bands drifting toward the shoreline, only near it, broken up by the wave field.
                float band = sin((open * 4.0 + t * 0.6) * 6.2831853) * 0.5 + 0.5;
                float breakup = saturate(0.5 + (g.x + g.y) * 4.0);
                float foam = _Foam * saturate(1.0 - open * 2.5) * smoothstep(0.55, 0.95, band * breakup + (1.0 - open) * 0.35);

                float3 lit = lerp(baseCol, float3(0.92, 0.95, 0.97), foam) * LGLitShadowed(n, visibility, i.world);
                lit += spec * _UOAmbient.rgb; // the sun's highlight dims with the time of day; the player light adds none
                float alpha = saturate(lerp(_ShoreAlpha, _OpenAlpha, open) + foam * 0.5 + spec * 0.3);
                return fixed4(LGGhost(lit), alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
