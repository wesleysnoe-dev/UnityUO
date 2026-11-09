// Item, static and multi-part lights (UOClient.Unity.ItemLights sets these each frame; LightSources says which art
// gives light). Compiled only under UO_ITEM_LIGHTS, which ItemLights enables while at least one light is on; without it
// UOClient/WorldSprite and the LiveGroundCompare Terrain run exactly their old code. Shared by both, so sprites and the
// TEST Terrain take the same light at the same Unity point.
#ifndef UO_ITEM_LIGHTS_INCLUDED
#define UO_ITEM_LIGHTS_INCLUDED

#define UO_MAX_ITEM_LIGHTS 32

float4 _UOItemLights[UO_MAX_ITEM_LIGHTS];      // xy: Unity world position, z: radius (Unity units), w: brightness 0-1
float4 _UOItemLightColors[UO_MAX_ITEM_LIGHTS]; // rgb: the light's colour, already scaled by how dark it is
float _UOItemLightCount;

// Walls block light (ItemLights.Visibility): per light, which tiles of a 24-tile window around it it reaches past walls,
// packed 8 x 4 windows into one R8 texture. xy: the window's first tile; z: 1 when the window is ready (else lit).
sampler2D _UOLightVis;
float4 _UOItemLightTiles[UO_MAX_ITEM_LIGHTS];
#define UO_LIGHT_VIS_TILES 24.0

// How much of light k reaches tile point p (continuous tile coordinates), bilinear between tiles so a wall's shadow edge
// is soft; outside the window it is lit (the light's own falloff ends before it).
float UOLightVisibility(int k, float2 p)
{
    float4 t = _UOItemLightTiles[k];
    if (t.z < 0.5)
        return 1.0;
    float2 local = p - t.xy;
    if (local.x < 0.0 || local.y < 0.0 || local.x > UO_LIGHT_VIS_TILES || local.y > UO_LIGHT_VIS_TILES)
        return 1.0;
    local = clamp(local, 0.5, UO_LIGHT_VIS_TILES - 0.5);
    float2 block = float2(fmod((float)k, 8.0), floor((float)k / 8.0));
    float2 uv = (block * UO_LIGHT_VIS_TILES + local) / float2(8.0 * UO_LIGHT_VIS_TILES, 4.0 * UO_LIGHT_VIS_TILES);
    return tex2Dlod(_UOLightVis, float4(uv, 0, 0)).r;
}

// The lights at a Unity point: each light.mul shape as a radial falloff (1 - d / radius)^2 at its peak, in its colour.
// Capped so the light on a point never passes white (TazUO adds the lights into an 8-bit light target over the ambient).
float3 UOItemLightAdd(float2 world, float3 lightSoFar)
{
    float3 sum = 0;
    int n = (int)_UOItemLightCount;
    for (int k = 0; k < UO_MAX_ITEM_LIGHTS; k++)
    {
        if (k >= n)
            break;
        float4 l = _UOItemLights[k];
        float f = saturate(1.0 - distance(world, l.xy) / max(l.z, 1.0));
        sum += _UOItemLightColors[k].rgb * (l.w * f * f);
    }
    return min(sum, max(1.0 - lightSoFar, 0.0));
}

// As UOItemLightAdd, where the tile the pixel stands for is known (tile point p): a wall between a light and p stops it.
float3 UOItemLightAddAt(float2 world, float2 p, float3 lightSoFar)
{
    float3 sum = 0;
    int n = (int)_UOItemLightCount;
    for (int k = 0; k < UO_MAX_ITEM_LIGHTS; k++)
    {
        if (k >= n)
            break;
        float4 l = _UOItemLights[k];
        float f = saturate(1.0 - distance(world, l.xy) / max(l.z, 1.0));
        if (f <= 0.0)
            continue;
        sum += _UOItemLightColors[k].rgb * (l.w * f * f * UOLightVisibility(k, p));
    }
    return min(sum, max(1.0 - lightSoFar, 0.0));
}

#endif
