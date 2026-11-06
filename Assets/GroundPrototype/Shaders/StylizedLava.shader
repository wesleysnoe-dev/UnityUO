// GroundPrototype P2 - SAMPLE, NOT MAP ART.
// Stylized lava: matte dark crust + scrolling, domain-warped emission. Emission only brightens the lava
// surface itself; it does not light anything (no GI, no bloom). Neighbour lighting comes solely from the
// separate, toggleable real point light created by FluidSurfaceBuilder.
Shader "UOClient/GroundPrototype/StylizedLava"
{
    Properties
    {
        _CrustColor ("Crust albedo", Color) = (0.13, 0.08, 0.06, 1)
        [HDR] _HotColor ("Hot emission", Color) = (1.0, 0.55, 0.12, 1)
        _CoolColor ("Cool emission", Color) = (0.55, 0.08, 0.02, 1)
        _EmissionStrength ("Emission strength", Float) = 1.25
        _FlowSpeed ("Flow speed", Float) = 0.18
        _CellScale ("Pattern scale", Float) = 1.3
        _MapOrigin ("Side origin (world)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" }
        LOD 200
        Offset -1, -1   // wins depth ties where a ground corner sits exactly at surfaceZ (no height change)

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        fixed4 _CrustColor, _HotColor, _CoolColor;
        float _EmissionStrength, _FlowSpeed, _CellScale;
        float4 _MapOrigin;

        struct Input
        {
            float3 worldPos;
        };

        float Hash21 (float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise (float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        float Fbm (float2 p)
        {
            float s = 0.0, a = 0.5;
            for (int k = 0; k < 4; k++) { s += a * Noise(p); p = p * 2.03 + 17.1; a *= 0.5; }
            return s / 0.9375;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = (IN.worldPos.xz - _MapOrigin.xz) * _CellScale;   // local map coordinates -> same pattern both sides
            float t = _Time.y * _FlowSpeed;
            float2 warp = float2(Fbm(p * 0.6 + float2(t, 0.0)), Fbm(p * 0.6 + float2(5.2, -t * 0.7)));
            float n = Fbm(p + warp * 1.6 + float2(t * 0.5, t * 0.3));

            float crust = smoothstep(0.48, 0.64, n);                     // 1 = cooled crust, 0 = molten
            float molten = 1.0 - crust;
            float pulse = 0.85 + 0.15 * sin(_Time.y * 1.3 + n * 6.2831);

            o.Albedo = lerp(_CoolColor.rgb * 0.35, _CrustColor.rgb, crust);
            o.Emission = lerp(_CoolColor.rgb, _HotColor.rgb, saturate(molten * 1.4)) * molten * _EmissionStrength * pulse;
            o.Metallic = 0;
            o.Smoothness = 0;                                            // matte
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
