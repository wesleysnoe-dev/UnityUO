// Item, static and multi-part lights (UOClient.Unity.ItemLights sets these each frame; LightSources says which art
// gives light). Compiled only under UO_ITEM_LIGHTS, which ItemLights enables while at least one light is on; without it
// UOClient/WorldSprite and the LiveGroundCompare Terrain run exactly their old code. Shared by both, so sprites and the
// TEST Terrain take the same light at the same Unity point.
#ifndef UO_ITEM_LIGHTS_INCLUDED
#define UO_ITEM_LIGHTS_INCLUDED

#define UO_MAX_ITEM_LIGHTS 16

float4 _UOItemLights[UO_MAX_ITEM_LIGHTS];      // xy: Unity world position, z: radius (Unity units), w: brightness 0-1
float4 _UOItemLightColors[UO_MAX_ITEM_LIGHTS]; // rgb: the light's colour, already scaled by how dark it is
float _UOItemLightCount;

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

#endif