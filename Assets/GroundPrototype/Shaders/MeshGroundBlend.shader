// GroundPrototype P2 - SAMPLE TEXTURES NOT MAP ART.
// Lit 4-layer blend for the custom-mesh side (built-in RP, Standard lighting, shadows, forward-add point lights).
// Control mask sampling copies Unity Terrain's corner-aligned convention
// (TerrainSplatmapCommon.cginc: splatUV = (tc * (texel.zw - 1) + 0.5) * texel.xy) so both sides read the
// same mask the same way. Normals are the mesh's own; this is NOT the terrain shader.
Shader "UOClient/GroundPrototype/MeshGroundBlend"
{
    Properties
    {
        _Control ("Control (RGBA blend weights)", 2D) = "red" {}
        _Splat0 ("Layer 0 Grass", 2D) = "white" {}
        _Splat1 ("Layer 1 Dirt", 2D) = "white" {}
        _Splat2 ("Layer 2 Sand", 2D) = "white" {}
        _Splat3 ("Layer 3 Stone", 2D) = "white" {}
        _MapOrigin ("Side origin (world)", Vector) = (0, 0, 0, 0)
        _MapSize ("Map size (world xz)", Vector) = (32, 32, 0, 0)
        _TexWorldSize ("Texture repeat (world units)", Float) = 4
        _Smoothness ("Smoothness (shared)", Range(0, 1)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _Control;
        float4 _Control_TexelSize;
        sampler2D _Splat0, _Splat1, _Splat2, _Splat3;
        float4 _MapOrigin;
        float4 _MapSize;
        float _TexWorldSize;
        half _Smoothness;

        struct Input
        {
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 local = IN.worldPos.xz - _MapOrigin.xz;     // local map coordinates (world units)
            float2 tc = local / _MapSize.xy;                     // 0..1 over the sample
            float2 splatUV = (tc * (_Control_TexelSize.zw - 1.0) + 0.5) * _Control_TexelSize.xy;
            half4 w = tex2D(_Control, splatUV);
            w /= (dot(w, half4(1, 1, 1, 1)) + 1e-3);             // same normalisation as the terrain splat path

            float2 uv = local / _TexWorldSize;
            fixed3 c = w.r * tex2D(_Splat0, uv).rgb
                     + w.g * tex2D(_Splat1, uv).rgb
                     + w.b * tex2D(_Splat2, uv).rgb
                     + w.a * tex2D(_Splat3, uv).rgb;

            o.Albedo = c;
            o.Metallic = 0;
            o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
