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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float a = tex2D(_MainTex, i.uv).a;
                clip(a - _Cutoff);
                // Daylight: the ambient's brightness (unset reads 0 in some setups: treat as full day).
                float sun = dot(_UOAmbient.rgb, float3(0.3, 0.59, 0.11));
                sun = sun <= 0.0001 ? 1.0 : saturate(sun * 1.25);
                return fixed4(0, 0, 0, _Strength * sun);
            }
            ENDCG
        }
    }
    Fallback Off
}
