// World and character sprites. UO art has 1-bit alpha, so sprites are drawn as opaque cutouts with
// depth: each sprite's Z comes from its draw-order index, which lets every atlas page batch into one
// mesh while still sorting correctly against the others and against moving characters.
// Lighting is global: an ambient color for the time of day plus one soft point light (the player's).
Shader "UOClient/WorldSprite"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Cutoff;

            float4 _UOAmbient;     // rgb: ambient light for the time of day
            float4 _UOLightColor;  // rgb: point light color * intensity
            float4 _UOLightPos;    // xy: world position, z: radius
            float _UOCutZ;         // upper-floor cut: hide statics at or above this height (+1000); 0 = no cut

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 height : TEXCOORD1; // x: the sprite's tile height + 1000 (0 = never cut: land, effects)
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 world : TEXCOORD1;
                float height : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                o.height = v.height.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Inside a building: what is at or above the floor overhead is not drawn (the classic client's max Z).
                if (_UOCutZ > 0.5 && i.height > 0.5 && i.height >= _UOCutZ)
                    discard;
                fixed4 c = tex2D(_MainTex, i.uv);
                clip(c.a - _Cutoff);

                float d = distance(i.world, _UOLightPos.xy) / max(_UOLightPos.z, 1.0);
                float falloff = saturate(1.0 - d);
                falloff *= falloff;

                c.rgb *= _UOAmbient.rgb + _UOLightColor.rgb * falloff;
                return c * i.color;
            }
            ENDCG
        }
    }
}
