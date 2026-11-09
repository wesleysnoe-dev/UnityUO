// Colour grading (Fx/FxGrade.cs), the last pass on the world camera: a gentle S-curve for depth, a touch more saturation,
// warm highlights and cool shadows (split toning), and a soft vignette. Strength 0 returns the image unchanged.
Shader "UOClient/FxGrade"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength;   // 0..1
            float _Contrast;   // S-curve amount
            float _Saturation; // 1 = unchanged
            float _Vignette;   // 0..1 edge darkening
            float4 _Warm, _Cool; // highlight and shadow tints (rgb), strength in w

            fixed4 frag(v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float3 src = c;
                // S-curve around mid grey: deeper shadows, brighter highlights, the ends kept.
                float3 s = c * c * (3.0 - 2.0 * c);
                c = lerp(c, s, _Contrast);
                float lum = dot(c, float3(0.299, 0.587, 0.114));
                c = lerp(lum.xxx, c, _Saturation);
                // Split toning by luminance.
                c += _Warm.rgb * _Warm.w * smoothstep(0.45, 1.0, lum);
                c += _Cool.rgb * _Cool.w * (1.0 - smoothstep(0.0, 0.45, lum));
                // Vignette: an oval darkening toward the corners.
                float2 d = (i.uv - 0.5) * float2(1.0, 0.8);
                c *= 1.0 - _Vignette * smoothstep(0.25, 0.75, length(d));
                return fixed4(lerp(src, saturate(c), _Strength), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
