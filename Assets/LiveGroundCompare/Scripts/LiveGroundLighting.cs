using System;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>
    /// The light model the Terrain backend shares with the sprites, and the terrain's sun shadow.
    ///
    /// Sprites (UOClient/WorldSprite) are lit by globals only: <c>_UOAmbient</c> (time of day, season) plus one soft point
    /// light at the player (<c>_UOLightColor</c>, <c>_UOLightPos</c> in Unity world XY), then drained to grey by
    /// <c>_UODead</c>. Original land also carries the compositor's per-face shade (<see cref="LiveGroundProjection.Shade"/>)
    /// as its vertex colour. The terrain and water shaders read those same globals and use the same shade formula with
    /// smooth normals, so the only terrain-only term is <see cref="SunVisibility"/>, which scales the shade toward
    /// <see cref="MinShade"/> (the compositor's floor for a face turned away from the sun).
    /// </summary>
    public static class LiveGroundLighting
    {
        /// <summary>The compositor's light, from the screen's upper left: x along +tile x, y along +tile y, up. Not unit length (as the compositor).</summary>
        public const float SunX = -0.6f, SunY = -0.2f, SunUp = 0.77f;

        /// <summary>The compositor's shade clamp.</summary>
        public const float MinShade = 0.7f, MaxShade = 1.25f;

        /// <summary>Shadow mask texels per tile edge (bilinear-filtered by the GPU).</summary>
        public const int ShadowTexelsPerTile = 4;

        public const int ShadowSize = TerrainLandBuilder.Tiles * ShadowTexelsPerTile;

        /// <summary>Height bias (UO Z) a shadow ray starts above the surface, against self-shadowing on its own face.</summary>
        public const float ShadowBiasZ = 0.25f;

        /// <summary>Ray step in tiles.</summary>
        public const float ShadowStep = 0.25f;

        /// <summary>The sun direction in terrain space (x, up, -tile y): what the shaders get as <c>_LGSunDir</c>.</summary>
        public static (float X, float Y, float Z) SunTerrain => (SunX, SunUp, -SunY);

        /// <summary>
        /// Shade of a terrain-space normal (any length) relative to flat ground: the compositor's formula and clamp,
        /// then pulled toward <see cref="MinShade"/> by how much of the sun reaches the point (1 lit, 0 in shadow).
        /// </summary>
        public static float Shade(float nx, float ny, float nz, float visibility)
        {
            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f)
                return 1f;
            (float lx, float ly, float lz) = SunTerrain;
            float dot = (nx * lx + ny * ly + nz * lz) / len;
            float shade = Math.Max(MinShade, Math.Min(MaxShade, dot / SunUp));
            float v = Math.Max(0f, Math.Min(1f, visibility));
            return MinShade + (shade - MinShade) * v;
        }

        /// <summary>The player light's falloff at a distance, as UOClient/WorldSprite: (1 - d / radius)², clamped.</summary>
        public static float PointFalloff(float distance, float radius)
        {
            float f = Math.Max(0f, Math.Min(1f, 1f - distance / Math.Max(radius, 1f)));
            return f * f;
        }

        /// <summary>
        /// The two rows (x, y, z, 1 → Unity world x / y) that map a terrain-space point to the main camera's world:
        /// WorldStreamer.ToUnity(<see cref="TerrainLandBuilder.ToWorldScreen"/>), where (<paramref name="originX"/>,
        /// <paramref name="originY"/>) = ToUnity(0, 0). The presenter's view matrix and the shaders' player-light
        /// distance both use these rows, so terrain pixels and sprites are lit at the same Unity position.
        /// </summary>
        public static (float[] RowX, float[] RowY) TerrainToUnityRows(int scale, float originX, float originY)
        {
            const float h = LiveGroundProjection.TileHalf, zs = LiveGroundProjection.ZStep;
            float s = scale, hz = 1f / TerrainLandBuilder.HeightUnitPerZ;
            return (new[] { h * s, 0f, h * s, originX }, new[] { -h * s, zs * hz * s, h * s, originY });
        }

        /// <summary>
        /// How much sun reaches chunk-local tile point (<paramref name="tx"/>, <paramref name="ty"/>) on the chunk's own
        /// surface: 0 when the heightfield blocks the ray toward the sun, else 1. Rays that leave the chunk count as lit
        /// (the builder does not see neighbouring chunks).
        /// </summary>
        public static float SunVisibility(int[,] cornerZ, float tx, float ty, int maxZ)
        {
            const int t = TerrainLandBuilder.Tiles;
            float hl = (float)Math.Sqrt(SunX * SunX + SunY * SunY);
            float ux = SunX / hl, uy = SunY / hl;
            float risePerTile = SunUp / hl / TerrainLandBuilder.HeightUnitPerZ; // UO Z per tile along the ray
            float z0 = TerrainLandBuilder.SampleZ(cornerZ, tx, ty) + ShadowBiasZ;
            for (float s = ShadowStep; ; s += ShadowStep)
            {
                float px = tx + ux * s, py = ty + uy * s;
                if (px < 0f || py < 0f || px > t || py > t)
                    return 1f;
                float rayZ = z0 + risePerTile * s;
                if (rayZ > maxZ)
                    return 1f;
                if (TerrainLandBuilder.SampleZ(cornerZ, px, py) > rayZ)
                    return 0f;
            }
        }

        /// <summary>
        /// The chunk's sun-visibility mask, <see cref="ShadowSize"/>² bytes (255 lit, 0 shadow), rows bottom-up like the
        /// chunk texture (row 0 = tile y max), so terrain UVs address it directly. Computed from the map's corner
        /// heights each time the chunk is built; nothing is painted.
        /// </summary>
        public static byte[] ShadowMask(int[,] cornerZ)
        {
            const int n = ShadowSize, t = TerrainLandBuilder.Tiles;
            int maxZ = int.MinValue;
            foreach (int z in cornerZ)
                maxZ = Math.Max(maxZ, z);
            var mask = new byte[n * n];
            for (int row = 0; row < n; row++)
            {
                float ty = t - (row + 0.5f) / ShadowTexelsPerTile;
                for (int col = 0; col < n; col++)
                {
                    float tx = (col + 0.5f) / ShadowTexelsPerTile;
                    mask[row * n + col] = SunVisibility(cornerZ, tx, ty, maxZ) > 0.5f ? (byte)255 : (byte)0;
                }
            }
            return mask;
        }
    }
}
