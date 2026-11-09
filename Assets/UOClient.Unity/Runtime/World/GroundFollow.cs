using System;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>
    /// Characters follow the drawn ground (WORLD_EDITOR.md step 3). The server stands a character on land at the tile's
    /// average land Z (UO's rule); a smooth ground (the natural terrain, painted or not) can be a few Z above or below that
    /// at the tile's centre, and between tiles a step is a straight line while the ground curves, so characters floated or
    /// sank. While a renderer that draws such ground is on, it sets <see cref="DrawnZ"/> and <see cref="LandAverageZ"/>;
    /// a character standing on land (its Z is the tile's average land Z) is then drawn at the drawn ground's height,
    /// sampled continuously along its step. On a floor, a bridge, stairs or a roof (any other Z) nothing changes. Drawing
    /// only: the server, walking and sorting are untouched, and the offset is clamped to <see cref="MaxOffset"/>.
    /// </summary>
    public static class GroundFollow
    {
        /// <summary>The drawn ground's height (UO Z) at a world point in tile units (tile corners at integers), or null.</summary>
        public static Func<float, float, float?> DrawnZ;

        /// <summary>The server's Z for a character on the land of tile (x, y) (UO's average land Z), or null if unknown.</summary>
        public static Func<int, int, int?> LandAverageZ;

        /// <summary>The most a character is moved from its server Z (UO Z units).</summary>
        public const float MaxOffset = 8f;

        public static bool On => DrawnZ != null && LandAverageZ != null;

        /// <summary>Whether a character at Z <paramref name="z"/> on tile (x, y) stands on its land.</summary>
        public static bool OnLand(int x, int y, int z) => LandAverageZ?.Invoke(x, y) is int avg && Math.Abs(avg - z) <= 1;

        /// <summary>
        /// The Z to draw a character at, standing or stepping: <paramref name="tile"/> is its interpolated tile position,
        /// <paramref name="z"/> its interpolated server Z, <paramref name="fromLand"/> / <paramref name="toLand"/> whether
        /// each end of the step is on land, <paramref name="t"/> the step's progress (1 when standing).
        /// </summary>
        public static float Z(Vector2 tile, float z, bool fromLand, bool toLand, float t)
        {
            if (!On || !fromLand && !toLand)
                return z;
            if (DrawnZ(tile.x + 0.5f, tile.y + 0.5f) is not float drawn)
                return z;
            float weight = Mathf.Lerp(fromLand ? 1f : 0f, toLand ? 1f : 0f, t);
            return z + Mathf.Clamp(drawn - z, -MaxOffset, MaxOffset) * weight;
        }
    }
}
