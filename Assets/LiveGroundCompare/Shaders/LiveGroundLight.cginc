// LiveGroundCompare (TEST only): the light the Terrain backend shares with the sprites (see LiveGroundLighting.cs).
// _UOAmbient, _UOLightColor, _UOLightPos and _UODead are the globals UOClient.Unity.Lighting sets every frame for
// UOClient/WorldSprite; they are only read here. _LGTerrainToUnity and _LGSunDir are set by TerrainIsoPresenter.
#ifndef LIVEGROUND_LIGHT_INCLUDED
#define LIVEGROUND_LIGHT_INCLUDED

#ifdef UO_ITEM_LIGHTS
#include "Assets/UOClient.Unity/Runtime/Resources/UOItemLights.cginc"
#endif

float4 _UOAmbient;          // rgb: ambient light for the time of day (season tinted)
float4 _UOLightColor;       // rgb: the player's point light color * intensity
float4 _UOLightPos;         // xy: its Unity world position, z: radius
float _UODead;              // 0..1: drained to grey (the player is a ghost)
float4x4 _LGTerrainToUnity; // rows 0, 1: terrain space -> the main camera's Unity world x, y (the sprites' space)
float4 _LGSunDir;           // xyz: the compositor's sun in terrain space (not unit length, as the compositor)

#define LG_MIN_SHADE 0.7
#define LG_MAX_SHADE 1.25
#define LG_HEIGHT_UNIT_PER_Z 0.12856487 // TerrainLandBuilder.HeightUnitPerZ = 4 / (22 * 1.41421356)

// The compositor's shade (relative to flat ground, clamped) for a normal, pulled to the floor by the sun shadow.
float LGShade(float3 n, float visibility)
{
    float shade = clamp(dot(normalize(n), _LGSunDir.xyz) / _LGSunDir.y, LG_MIN_SHADE, LG_MAX_SHADE);
    return LG_MIN_SHADE + (shade - LG_MIN_SHADE) * saturate(visibility);
}

// The player's light alone (no ambient), at the Unity position the sprites would use for this terrain point.
float3 LGLocalLight(float3 terrainWorld)
{
    float2 unityXY = mul(_LGTerrainToUnity, float4(terrainWorld, 1.0)).xy;
    float d = distance(unityXY, _UOLightPos.xy) / max(_UOLightPos.z, 1.0);
    float falloff = saturate(1.0 - d);
    falloff *= falloff;
#ifdef UO_ITEM_LIGHTS
    // Item, static and multi-part lights: the same sum the sprites add at this Unity point (UOItemLights.cginc).
    float3 player = _UOLightColor.rgb * falloff;
    return player + UOItemLightAdd(unityXY, _UOAmbient.rgb + player);
#else
    return _UOLightColor.rgb * falloff;
#endif
}

// Time-of-day ambient plus the player's light, at the Unity position the sprites would use for this terrain point.
float3 LGSpriteLight(float3 terrainWorld)
{
    return _UOAmbient.rgb + LGLocalLight(terrainWorld);
}

// LiveGroundLighting.Lit: the face shade multiplies all light (the compositor's vertex shade), but the sun shadow only
// dims the ambient (sun/sky) term; the player's light is local and is not shadowed by the sun.
float3 LGLitShadowed(float3 n, float visibility, float3 terrainWorld)
{
    return _UOAmbient.rgb * LGShade(n, visibility) + LGLocalLight(terrainWorld) * LGShade(n, 1.0);
}

// The presenter's world-wrapped CPU shadow ceiling (SpriteShadowCeiling: chunk ceilings, loaded-neighbour margins and
// the mobiles/items columns). _LGCeilOn is 0 unless that texture is live, so without it the terrain reads only its mask
// and the water is unshadowed.
sampler2D _LGShadowCeil;
float4 _LGSpriteShadowMap; // w: 1 / wrapped tiles
float _LGCeilOn;

// Sun visibility of a terrain-space ground point from the ceiling (SpriteShadow.Visibility), 1 when off.
float LGCeilVisibility(float3 terrainWorld)
{
    if (_LGCeilOn < 0.5)
        return 1.0;
    float2 tile = float2(terrainWorld.x, -terrainWorld.z);
    float z = terrainWorld.y / LG_HEIGHT_UNIT_PER_Z;
    float ceiling = tex2D(_LGShadowCeil, tile * _LGSpriteShadowMap.w).r;
    return saturate((z + 0.25 - ceiling) / 2.0 + 0.5);
}

// As UOClient/WorldSprite: a ghost sees the world in black and white.
float3 LGGhost(float3 c)
{
    return lerp(c, dot(c, float3(0.299, 0.587, 0.114)).xxx, _UODead);
}

#endif
