// LiveGroundCompare (TEST only): the Terrain backend's occlusion proxy (TerrainDepthProxy).
// Drawn by the MAIN camera, before the sprites (Geometry < AlphaTest), into depth only: no colour, no light. Each vertex
// carries its tile's production depth (the land sprite quad's DepthKey), so statics, mobiles and items are hidden by the
// terrain exactly where original land would hide them. UOClient/WorldSprite is not changed by this.
Shader "LiveGroundCompare/DepthKey"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
    Fallback Off
}
