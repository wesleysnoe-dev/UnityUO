// Ambient life (AmbientLife.cs): fireflies, falling leaves and pollen, each a small quad shaped here. Depth-tested against
// the sprites (each sorts as a character on its tile would) but not writing depth; premultiplied blending, so a firefly's
// glow adds light (alpha 0) while a leaf covers what is behind it. Leaves and pollen take the time-of-day light.
Shader "UOClient/AmbientLife"
{
    SubShader
    {
        Tags { "Queue" = "AlphaTest+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _UOAmbient;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 kind : TEXCOORD1; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.kind = v.kind;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.uv * 2.0 - 1.0;
                float r = length(d);
                float fade = i.kind.y;
                float3 amb = _UOAmbient.rgb;
                amb = dot(amb, 1) <= 0.0001 ? float3(1, 1, 1) : amb;
                if (i.kind.x > 2.5)
                {
                    // A flame's halo: a wide soft warm glow, added as light.
                    float g = saturate(1.0 - r);
                    g = g * g * g;
                    return fixed4(i.color.rgb * g * fade * 0.55, 0);
                }
                if (i.kind.x < 0.5)
                {
                    // Firefly: a bright core in a soft halo, added as light.
                    float core = saturate(1.0 - r * 4.0);
                    float halo = saturate(1.0 - r);
                    float glow = core * 1.6 + halo * halo * halo * 0.9;
                    return fixed4(i.color.rgb * glow * fade * 1.8, 0);
                }
                if (i.kind.x < 1.5)
                {
                    // Leaf: a pointed ellipse with a darker midrib, lit by the time of day.
                    float leaf = 1.0 - smoothstep(0.75, 0.95, length(float2(d.x, d.y * 1.6)) + abs(d.y) * 0.25);
                    if (leaf <= 0.01)
                        discard;
                    float3 c = i.color.rgb * (1.0 - 0.3 * saturate(1.0 - abs(d.y) * 8.0)) * amb;
                    float a = leaf * fade;
                    return fixed4(c * a, a);
                }
                // Pollen: a tiny soft mote.
                float mote = saturate(1.0 - r);
                mote *= mote;
                float a = mote * fade * 0.8;
                return fixed4(i.color.rgb * amb * a, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
