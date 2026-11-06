using System;
using UOClient.IO.Formats;

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
        public static float SunVisibility(int[,] cornerZ, float tx, float ty, int maxZ) => SunVisibility(cornerZ, null, tx, ty, maxZ);

        /// <summary>
        /// As above, and a ray also stops where it passes below the top of a static column
        /// (<paramref name="occluderTop"/>[i, j], <see cref="StaticOccluders.None"/> = no column). The column is the
        /// tile's whole footprint from the ground up to its highest casting static (<see cref="StaticOccluders"/>), so a
        /// point on a column's own tile is in its shadow.
        /// </summary>
        public static float SunVisibility(int[,] cornerZ, int[,] occluderTop, float tx, float ty, int maxZ)
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
                if (occluderTop != null)
                {
                    int i = Math.Min(t - 1, (int)px), j = Math.Min(t - 1, (int)py);
                    if (occluderTop[i, j] > rayZ)
                        return 0f;
                }
            }
        }

        /// <summary>
        /// The chunk's sun-visibility mask, <see cref="ShadowSize"/>² bytes (255 lit, 0 shadow), rows bottom-up like the
        /// chunk texture (row 0 = tile y max), so terrain UVs address it directly. Computed from the map's corner
        /// heights each time the chunk is built; nothing is painted.
        /// </summary>
        public static byte[] ShadowMask(int[,] cornerZ) => ShadowMask(cornerZ, null);

        /// <summary>As above, with the chunk's own static columns also casting (null: terrain only).</summary>
        public static byte[] ShadowMask(int[,] cornerZ, int[,] occluderTop)
        {
            const int n = ShadowSize, t = TerrainLandBuilder.Tiles;
            int maxZ = int.MinValue;
            foreach (int z in cornerZ)
                maxZ = Math.Max(maxZ, z);
            if (occluderTop != null)
                foreach (int z in occluderTop)
                    maxZ = Math.Max(maxZ, z);
            var mask = new byte[n * n];
            for (int row = 0; row < n; row++)
            {
                float ty = t - (row + 0.5f) / ShadowTexelsPerTile;
                for (int col = 0; col < n; col++)
                {
                    float tx = (col + 0.5f) / ShadowTexelsPerTile;
                    mask[row * n + col] = SunVisibility(cornerZ, occluderTop, tx, ty, maxZ) > 0.5f ? (byte)255 : (byte)0;
                }
            }
            return mask;
        }

        /// <summary>A <see cref="ShadowCeiling"/> texel no sun ray of this chunk is blocked from (and what unloaded ground reads).</summary>
        public const float NoCeiling = -1000f;

        /// <summary>
        /// The shadow ceiling at chunk-local tile point (<paramref name="tx"/>, <paramref name="ty"/>): the UO Z below which
        /// a point standing there is in the sun shadow. A ray toward the sun from height z is blocked at step s when the
        /// surface or a column there is above z + rise·s, so the point is shadowed exactly when z is below
        /// max over s of (occluder height − rise·s). That one number per texel answers the same question as
        /// <see cref="SunVisibility(int[,], int[,], float, float, int)"/> for any height, so sprites (walls, trees,
        /// mobiles) can test every pixel against it in O(1). The column on the point's own tile is skipped: a sprite on a
        /// column tile is that column, and must not shade itself. Rays that leave the chunk count as lit (as the mask).
        /// </summary>
        public static float CeilingAt(int[,] cornerZ, int[,] occluderTop, float tx, float ty, int maxZ)
        {
            const int t = TerrainLandBuilder.Tiles;
            float hl = (float)Math.Sqrt(SunX * SunX + SunY * SunY);
            float ux = SunX / hl, uy = SunY / hl;
            float risePerTile = SunUp / hl / TerrainLandBuilder.HeightUnitPerZ;
            int ownI = Math.Min(t - 1, (int)tx), ownJ = Math.Min(t - 1, (int)ty);
            float ceiling = NoCeiling;
            for (float s = ShadowStep; ; s += ShadowStep)
            {
                float px = tx + ux * s, py = ty + uy * s;
                if (px < 0f || py < 0f || px > t || py > t)
                    return ceiling;
                float drop = risePerTile * s;
                if (maxZ - drop <= ceiling)
                    return ceiling; // nothing further along can raise it
                ceiling = Math.Max(ceiling, TerrainLandBuilder.SampleZ(cornerZ, px, py) - drop);
                if (occluderTop != null)
                {
                    int i = Math.Min(t - 1, (int)px), j = Math.Min(t - 1, (int)py);
                    if ((i != ownI || j != ownJ) && occluderTop[i, j] != StaticOccluders.None)
                        ceiling = Math.Max(ceiling, occluderTop[i, j] - drop);
                }
            }
        }

        /// <summary>
        /// The chunk's shadow ceilings (<see cref="CeilingAt"/>), <see cref="ShadowSize"/>² floats, row r = tile y
        /// (r + ½) / <see cref="ShadowTexelsPerTile"/> from the chunk's first row (top-down in tile y, unlike the mask),
        /// column c = tile x likewise. This is the layout of the presenter's world-wrapped ceiling texture.
        /// </summary>
        public static float[] ShadowCeiling(int[,] cornerZ, int[,] occluderTop)
        {
            const int n = ShadowSize;
            int maxZ = int.MinValue;
            foreach (int z in cornerZ)
                maxZ = Math.Max(maxZ, z);
            if (occluderTop != null)
                foreach (int z in occluderTop)
                    maxZ = Math.Max(maxZ, z);
            var ceiling = new float[n * n];
            for (int row = 0; row < n; row++)
            {
                float ty = (row + 0.5f) / ShadowTexelsPerTile;
                for (int col = 0; col < n; col++)
                    ceiling[row * n + col] = CeilingAt(cornerZ, occluderTop, (col + 0.5f) / ShadowTexelsPerTile, ty, maxZ);
            }
            return ceiling;
        }

        /// <summary>Bilinear read of a <see cref="ShadowCeiling"/> at chunk-local tile point (x, y), as the GPU filters it (edges clamp).</summary>
        public static float SampleCeiling(float[] ceiling, float x, float y)
        {
            const int n = ShadowSize;
            float fx = x * ShadowTexelsPerTile - 0.5f, fy = y * ShadowTexelsPerTile - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float ax = fx - x0, ay = fy - y0;
            float At(int c, int r) => ceiling[Math.Max(0, Math.Min(n - 1, r)) * n + Math.Max(0, Math.Min(n - 1, c))];
            float top = At(x0, y0) + (At(x0 + 1, y0) - At(x0, y0)) * ax;
            float bottom = At(x0, y0 + 1) + (At(x0 + 1, y0 + 1) - At(x0, y0 + 1)) * ax;
            return top + (bottom - top) * ay;
        }
    }

    /// <summary>
    /// TEST-only sun-shadow receive for sprites (UOClient/WorldSprite under the LG_SPRITE_SHADOW keyword). A sprite only
    /// knows its screen position, so each vertex carries its foot anchor (Unity x, y of the tile centre or the walking
    /// anchor, the foot's UO Z, and a kind) in UV3. Every pixel is then put back into the world: an upright sprite (walls,
    /// trees, mobiles, items) as a vertical plane through the foot, each pixel above the foot rising 4 px per Z, and the
    /// pixels below the foot (the tile's front half) on the ground at the foot's Z; a ground sprite (land, floors) wholly
    /// on the ground. The point is tested against <see cref="LiveGroundLighting.ShadowCeiling"/>, so a mobile next to a
    /// wall can have dark feet and a lit head. This mirrors the shader line for line (tested); it is a labelled
    /// approximation of billboards, not a shadow map.
    /// </summary>
    public static class SpriteShadow
    {
        /// <summary>UV3.w: no receive (what every mesh without the channel reads), upright, ground.</summary>
        public const int KindNone = 0, KindUpright = 1, KindGround = 2;

        /// <summary>Upright points stay this far inside the foot tile, so its own (skipped) column's neighbours never read in.</summary>
        public const float Inset = 0.5f / LiveGroundLighting.ShadowTexelsPerTile;

        /// <summary>Z over which a pixel goes from shadow to lit across the ceiling (soft edge, about ¼ tile of sun travel).</summary>
        public const float SoftZ = 2f;

        /// <summary>Tiles the presenter's world-wrapped ceiling texture spans (the loaded window must stay narrower).</summary>
        public const int WrapTiles = 256;

        /// <summary>Kind of a static by its tiledata height: flat ones (floors, rugs) lie on the ground.</summary>
        public static int StaticKind(int height) => height <= 1 ? KindGround : KindUpright;

        /// <summary>World-screen pixel (at <paramref name="scale"/>, y down) of a tile's centre at height z: the sprites' foot anchor.</summary>
        public static (float X, float Y) TileAnchor(int x, int y, int z, int scale) =>
            ((x - y) * LiveGroundProjection.TileHalf * scale,
             ((x + y) * LiveGroundProjection.TileHalf + LiveGroundProjection.TileHalf - z * LiveGroundProjection.ZStep) * (float)scale);

        /// <summary>
        /// The world point (tile x, tile y, UO Z) a sprite pixel at world-screen (<paramref name="wx"/>, <paramref name="wy"/>)
        /// stands for, given its foot anchor (same space) and kind. Tile coordinates are continuous: integers are tile corners.
        /// </summary>
        public static (float X, float Y, float Z) PixelPoint(float wx, float wy, float footX, float footY, float footZ, int kind, int scale)
        {
            const float h = LiveGroundProjection.TileHalf, zs = LiveGroundProjection.ZStep;
            float s = scale;
            float a = wx / (h * s); // x - y
            if (kind == KindGround)
            {
                float bg = (wy / s + zs * footZ) / h; // x + y on the ground at the foot's height
                return ((a + bg) / 2f, (bg - a) / 2f, footZ);
            }
            float af = footX / (h * s), bf = (footY / s + zs * footZ) / h;
            float tx = (float)Math.Floor((af + bf) / 2f), ty = (float)Math.Floor((bf - af) / 2f);
            float up = (footY - wy) / s; // unscaled pixels above the foot
            float z = footZ + Math.Max(up, 0f) / zs;
            float b = bf + Math.Max(0f, Math.Min(1f, -up / h));
            float x = Math.Max(tx + Inset, Math.Min(tx + 1f - Inset, (a + b) / 2f));
            float y = Math.Max(ty + Inset, Math.Min(ty + 1f - Inset, (b - a) / 2f));
            return (x, y, z);
        }

        /// <summary>Sun visibility (0 shadow, 1 lit) of a point at UO Z <paramref name="z"/> under a ceiling.</summary>
        public static float Visibility(float z, float ceiling) =>
            Math.Max(0f, Math.Min(1f, (z + LiveGroundLighting.ShadowBiasZ - ceiling) / SoftZ + 0.5f));

        /// <summary>The factor the sprite's light is multiplied by: the compositor's flat shade (1) pulled to its floor in shadow, as the terrain.</summary>
        public static float Shade(float visibility) => LiveGroundLighting.MinShade + (1f - LiveGroundLighting.MinShade) * visibility;
    }

    /// <summary>
    /// Building (static) shadows for the Terrain sun mask, as a labelled approximation: every map static whose tiledata
    /// <see cref="StaticTileData.Height"/> is at least <see cref="MinHeight"/> (and that is not water) makes its tile a
    /// solid column from the ground up to Z + Height; the mask's sun rays stop at the highest column per tile. This is
    /// a projected silhouette from known heights, computed on the CPU when the chunk is built: not a Unity shadow map,
    /// no overhangs (a roof shades as a solid block), only the chunk's own map statics (no neighbouring chunks, no
    /// server items or multis such as player houses). Sprites receive it through <see cref="LiveGroundLighting.ShadowCeiling"/>.
    /// </summary>
    public static class StaticOccluders
    {
        public const int None = int.MinValue;

        /// <summary>Lower statics (rugs, papers, low fences) would darken their whole tile for a sliver of real shadow.</summary>
        public const int MinHeight = 3;

        public static bool Casts(in StaticTileData td) => td.Height >= MinHeight && (td.Flags & TileFlags.Wet) == 0;

        public static int[,] Empty()
        {
            const int t = TerrainLandBuilder.Tiles;
            var top = new int[t, t];
            for (int i = 0; i < t; i++)
                for (int j = 0; j < t; j++)
                    top[i, j] = None;
            return top;
        }

        /// <summary>Raises chunk-local tile (i, j) to the top of a static at <paramref name="z"/> if it casts.</summary>
        public static void Add(int[,] top, int i, int j, int z, in StaticTileData td)
        {
            const int t = TerrainLandBuilder.Tiles;
            if (top == null || (uint)i >= t || (uint)j >= t || !Casts(td))
                return;
            top[i, j] = Math.Max(top[i, j], z + td.Height);
        }

        /// <summary>The chunk's columns from the map's static blocks (the 2x2 8x8 blocks under a 16x16 chunk).</summary>
        public static int[,] FromMap(MapFile map, TileData tileData, int x0, int y0)
        {
            if (map == null || tileData == null)
                return null;
            const int t = TerrainLandBuilder.Tiles, b = MapFile.BlockSize;
            int[,] top = Empty();
            for (int by = y0 / b; by < (y0 + t) / b; by++)
                for (int bx = x0 / b; bx < (x0 + t) / b; bx++)
                {
                    StaticCell[] cells = map.ReadStaticBlock(bx, by);
                    if (cells == null)
                        continue;
                    foreach (StaticCell s in cells)
                        Add(top, bx * b + s.X - x0, by * b + s.Y - y0, s.Z, tileData.GetStatic(s.Graphic));
                }
            return top;
        }

        public static int Count(int[,] top)
        {
            if (top == null)
                return 0;
            int n = 0;
            foreach (int z in top)
                if (z != None)
                    n++;
            return n;
        }
    }
}
