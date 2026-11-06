// LiveGroundCompare (TEST only): the light the Terrain backend shares with the sprites (see LiveGroundLighting.cs).
// _UOAmbient, _UOLightColor, _UOLightPos and _UODead are the globals UOClient.Unity.Lighting sets every frame for
// UOClient/WorldSprite; they are only read here. _LGTerrainToUnity and _LGSunDir are set by TerrainIsoPresenter.
#ifndef LIVEGROUND_LIGHT_INCLUDED
#define LIVEGROUND_LIGHT_INCLUDED

float4 _UOAmbient;          // rgb: ambient light for the time of day (season tinted)
float4 _UOLightColor;       // rgb: the player's point light color * intensity
float4 _UOLightPos;         // xy: its Unity world position, z: radius
float _UODead;              // 0..1: drained to grey (the player is a ghost)
float4x4 _LGTerrainToUnity; // rows 0, 1: terrain space -> the main camera's Unity world x, y (the sprites' space)
float4 _LGSunDir;           // xyz: the compositor's sun in terrain space (not unit length, as the compositor)

#define LG_MIN_SHADE 0.7
#define LG_MAX_SHADE 1.25

// The compositor's shade (relative to flat ground, clamped) for a normal, pulled to the floor by the sun shadow.
float LGShade(float3 n, float visibility)
{
    float shade = clamp(dot(normalize(n), _LGSunDir.xyz) / _LGSunDir.y, LG_MIN_SHADE, LG_MAX_SHADE);
    return LG_MIN_SHADE + (shade - LG_MIN_SHADE) * saturate(visibility);
}

// Time-of-day ambient plus the player's light, at the Unity position the sprites would use for this terrain point.
float3 LGSpriteLight(float3 terrainWorld)
{
    float2 unityXY = mul(_LGTerrainToUnity, float4(terrainWorld, 1.0)).xy;
    float d = distance(unityXY, _UOLightPos.xy) / max(_UOLightPos.z, 1.0);
    float falloff = saturate(1.0 - d);
    falloff *= falloff;
    return _UOAmbient.rgb + _UOLightColor.rgb * falloff;
}

// As UOClient/WorldSprite: a ghost sees the world in black and white.
float3 LGGhost(float3 c)
{
    return lerp(c, dot(c, float3(0.299, 0.587, 0.114)).xxx, _UODead);
}

#endif
