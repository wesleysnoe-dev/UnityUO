// A character's sun shadow (CharacterShadow.cs): its frame laid on the ground, drawn as darkness through the frame's
// alpha. After the ground and grass, before every sprite (Queue AlphaTest-10, no depth test, no depth write), so the
// grass under it is shaded and whatever is drawn in its place afterwards (the character first) stays on top.
// Strength fades with the time-of-day ambient (_UOAmbient, the sprites' global): no sun, no shadow.
Shader "UOClient/CharacterShadow"
{
    Properties
    {
        _MainTex ("Atlas page", 2D) = "white" {}
        _Strength ("Darkness", Range(0, 1)) = 0.42
        _Cutoff ("Frame alpha cutoff", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Strength, _Cutoff;
            float4 _UOAmbient;
            // The moving sun (CharacterShadow.SetSun): Unity units per pixel of height to add to a sun shadow's corner,
            // and 1 - the sun's shadow strength. Unset (0): the reference sun at full strength.
            float4 _UOSunShift;
            float _UOSunFade;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 height : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0));
                world.xy += v.height.x * _UOSunShift.xy * v.color.g; // sun shadows only (g 1); a lamp's stays
                o.pos = mul(UNITY_MATRIX_VP, world);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;
                clip(a - _Cutoff);
                // Daylight: the ambient's brightness (unset reads 0 in some setups: treat as full day).
                float sun = dot(_UOAmbient.rgb, float3(0.3, 0.59, 0.11));
                sun = sun <= 0.0001 ? 1.0 : saturate(sun * 1.25);
                // Vertex colour (CharacterShadow.Tint): a the shadow's strength; g 1 a sun shadow (fades with daylight),
                // 0 a lamp's indoor shadow (by day and night alike).
                sun *= 1.0 - _UOSunFade; // the sun low on the horizon, or the moon handing over
                return fixed4(0, 0, 0, _Strength * i.color.a * lerp(1.5, sun, i.color.g)); // a lamp shadow, close to its flame, reads darker
            }
            ENDCG
        }
    }
    Fallback Off
}
