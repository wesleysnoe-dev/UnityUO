// LiveGroundCompare (TEST only): the Terrain backend's land material, lit like the sprites.
// Drawn by TerrainIsoPresenter's camera on layer 31 (a Unity Terrain's materialTemplate, drawInstanced off, so the
// patches carry ordinary positions, normals and the chunk UV). Built-in pipeline, no Unity lights:
//   colour = chunk art * (ambient * shade(normal, sun shadow) + player light * shade(normal)), ghost grey
// with the same globals UOClient/WorldSprite reads (LiveGroundLight.cginc). The sun shadow dims only the ambient. No emission.
// _LGShadowTex: the chunk's sun-visibility mask (LiveGroundLighting.ShadowMask), same UV as the chunk art; with the
// sprite receive live it is also min'd with the world ceiling (loaded-neighbour margins, mobile/item columns).
Shader "LiveGroundCompare/TerrainLit"
{
    Properties
    {
        _MainTex ("Chunk art", 2D) = "white" {}
        _LGShadowTex ("Sun visibility (1 lit)", 2D) = "white" {}
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
            #pragma target 3.0
            #pragma multi_compile __ UO_ITEM_LIGHTS
            #include "UnityCG.cginc"
            #include "../LiveGroundLight.cginc"

            sampler2D _MainTex;
            sampler2D _LGShadowTex;

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

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                // The chunk's own mask, and the world ceiling (neighbour margins, mobiles/items) when the receive is live.
                float visibility = min(tex2D(_LGShadowTex, i.uv).r, LGCeilVisibility(i.world));
                float3 rgb = c.rgb * LGLitShadowed(i.normal, visibility, i.world);
                return fixed4(LGGhost(rgb), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
