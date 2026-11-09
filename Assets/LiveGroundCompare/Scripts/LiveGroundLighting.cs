using System;
using System.Collections.Generic;
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

        /// <summary>The sun's ground direction (unit, toward the sun: -x, -y) and its rise in UO Z per tile, as every ray here uses them.</summary>
        public static (float Ux, float Uy, float Rise) SunRay
        {
            get
            {
                float hl = (float)Math.Sqrt(SunX * SunX + SunY * SunY);
                return (SunX / hl, SunY / hl, SunUp / hl / TerrainLandBuilder.HeightUnitPerZ);
            }
        }

        /// <summary>
        /// Light of a receiver with sun shade <paramref name="sunShade"/>: the sun shadow dims only the time-of-day
        /// ambient (the sun/sky term); the player's light is local and added unshadowed. Per channel; the shaders mirror it.
        /// </summary>
        public static float Lit(float ambient, float localLight, float sunShade) => ambient * sunShade + localLight;

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

        public static bool Casts(in StaticTileData td) =>
            td.Height >= MinHeight && (td.Flags & TileFlags.Wet) == 0 && !(OutlineCasters && ShadowRules.CastsOutline(td));

        /// <summary>
        /// Set while free-standing objects cast their own outline shadow (the client's CharacterShadow, ShadowRules): they
        /// then make no column here, so they are not shadowed twice. Off by default (and in the tests).
        /// </summary>
        public static volatile bool OutlineCasters;

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

    /// <summary>
    /// TEST-only dynamic moving approximation: mobiles and server items cast into <see cref="LiveGroundLighting.ShadowCeiling"/>
    /// as the same tile columns map statics are (<see cref="StaticOccluders"/>), stamped on the CPU when they appear,
    /// move to another tile or go away. Not a Unity shadow map: a mobile is a <see cref="MobileHeight"/> column on the
    /// tile its feet are drawn in (it jumps a tile at a time, no silhouette), an item a column of its tiledata height.
    /// Also the cross-chunk margin: the rays of a chunk continued into its loaded west/north neighbours.
    /// </summary>
    public static class DynamicShadow
    {
        /// <summary>A standing character's height in Z: TazUO Constants.DEFAULT_CHARACTER_HEIGHT, ModernUO PersonHeight.</summary>
        public const int MobileHeight = 16;

        /// <summary>How far below its own foot a column still shades (a receiver downhill); bounds the stamp's reach.</summary>
        public const int DownhillZ = 8;

        /// <summary>The farthest a dynamic column's shadow is stamped, in tiles along the ground.</summary>
        public const float MaxReachTiles = 8f;

        /// <summary>Ground distance (tiles) over which a column of <paramref name="height"/> Z can reach a receiver at most <see cref="DownhillZ"/> below its foot.</summary>
        public static float Reach(int height) =>
            Math.Min(MaxReachTiles, (Math.Max(0, height) + DownhillZ) / LiveGroundLighting.SunRay.Rise);

        /// <summary>The tile a mobile's feet are drawn in: its walking anchor (world-screen pixels at <paramref name="scale"/>) at <paramref name="footZ"/>, as <see cref="SpriteShadow.PixelPoint"/> finds the foot tile.</summary>
        public static (int X, int Y) FootTile(float anchorX, float anchorY, float footZ, int scale)
        {
            const float h = LiveGroundProjection.TileHalf, zs = LiveGroundProjection.ZStep;
            float af = anchorX / (h * scale), bf = (anchorY / scale + zs * footZ) / h;
            return ((int)Math.Floor((af + bf) / 2f), (int)Math.Floor((bf - af) / 2f));
        }

        /// <summary>Tiles [X0, X1) x [Y0, Y1) a column on tile (x, y) can shade: its own tile and away from the sun (+x, +y).</summary>
        public static (int X0, int Y0, int X1, int Y1) Footprint(int x, int y, int height)
        {
            (float ux, float uy, _) = LiveGroundLighting.SunRay;
            float r = Reach(height);
            return (x, y, x + 1 + (int)Math.Ceiling(r * -ux), y + 1 + (int)Math.Ceiling(r * -uy));
        }

        /// <summary>
        /// Raises a chunk's ceilings (<see cref="LiveGroundLighting.ShadowCeiling"/> layout, chunk at tile x0, y0) where a
        /// column on tile (cx, cy) with top Z <paramref name="top"/> blocks the sun: the same ray steps and rule as
        /// <see cref="LiveGroundLighting.CeilingAt"/> (the texel's own tile is skipped, so a mobile does not shade itself).
        /// Returns the texels its footprint covered in this chunk.
        /// </summary>
        public static int Stamp(float[] ceiling, int x0, int y0, int cx, int cy, int top, int height)
        {
            const int n = LiveGroundLighting.ShadowSize, k = LiveGroundLighting.ShadowTexelsPerTile;
            (int fx0, int fy0, int fx1, int fy1) = Footprint(cx, cy, height);
            int c0 = Math.Max(0, (fx0 - x0) * k), c1 = Math.Min(n, (fx1 - x0) * k);
            int r0 = Math.Max(0, (fy0 - y0) * k), r1 = Math.Min(n, (fy1 - y0) * k);
            if (c0 >= c1 || r0 >= r1)
                return 0;
            (float ux, float uy, float rise) = LiveGroundLighting.SunRay;
            float reach = Reach(height);
            int lx = cx - x0, ly = cy - y0; // chunk-local, so the arithmetic is CeilingAt's
            for (int r = r0; r < r1; r++)
            {
                float ty = (r + 0.5f) / k;
                for (int c = c0; c < c1; c++)
                {
                    float tx = (c + 0.5f) / k;
                    if ((int)Math.Floor(tx) == lx && (int)Math.Floor(ty) == ly)
                        continue;
                    for (float s = LiveGroundLighting.ShadowStep; s <= reach; s += LiveGroundLighting.ShadowStep)
                    {
                        float px = tx + ux * s, py = ty + uy * s;
                        if ((int)Math.Floor(px) != lx || (int)Math.Floor(py) != ly)
                            continue;
                        float v = top - rise * s; // the first step inside the column is its highest
                        int i = r * n + c;
                        if (v > ceiling[i])
                            ceiling[i] = v;
                        break;
                    }
                }
            }
            return (c1 - c0) * (r1 - r0);
        }

        /// <summary>A loaded chunk's corner heights and static columns, as its east/south neighbours' margins read them.</summary>
        public sealed class Heights
        {
            public int[,] CornerZ;      // [17, 17]
            public int[,] OccluderTop;  // [16, 16] or null (terrain only)
            public int MaxZ;

            public static Heights Of(int[,] cornerZ, int[,] occluderTop)
            {
                if (cornerZ == null)
                    return null;
                int maxZ = int.MinValue;
                foreach (int z in cornerZ)
                    maxZ = Math.Max(maxZ, z);
                if (occluderTop != null)
                    foreach (int z in occluderTop)
                        maxZ = Math.Max(maxZ, z);
                return new Heights { CornerZ = cornerZ, OccluderTop = occluderTop, MaxZ = maxZ };
            }
        }

        /// <summary>
        /// The cross-chunk margin: per texel, the highest ceiling the sun ray picks up after it leaves its chunk into the
        /// west (<paramref name="west"/>), north or north-west neighbour (rays go toward -x, -y only), from those chunks'
        /// heights and static columns. A ray stops (lit from there on) where it enters a neighbour that is not loaded
        /// (null) or leaves the three. <paramref name="own"/> (the chunk's own ceiling) only ends rays early. Writes
        /// <paramref name="margin"/> (NoCeiling where nothing); returns false when no neighbour is loaded.
        /// </summary>
        public static bool Margin(float[] own, Heights west, Heights north, Heights northWest, float[] margin)
        {
            const int n = LiveGroundLighting.ShadowSize, k = LiveGroundLighting.ShadowTexelsPerTile, t = TerrainLandBuilder.Tiles;
            for (int i = 0; i < margin.Length; i++)
                margin[i] = LiveGroundLighting.NoCeiling;
            if (west == null && north == null && northWest == null)
                return false;
            int maxZ = Math.Max(west?.MaxZ ?? int.MinValue, Math.Max(north?.MaxZ ?? int.MinValue, northWest?.MaxZ ?? int.MinValue));
            (float ux, float uy, float rise) = LiveGroundLighting.SunRay;
            for (int row = 0; row < n; row++)
            {
                float ty = (row + 0.5f) / k;
                for (int col = 0; col < n; col++)
                {
                    float tx = (col + 0.5f) / k;
                    int idx = row * n + col;
                    float best = Math.Max(own[idx], LiveGroundLighting.NoCeiling), found = LiveGroundLighting.NoCeiling;
                    for (float s = LiveGroundLighting.ShadowStep; ; s += LiveGroundLighting.ShadowStep)
                    {
                        float px = tx + ux * s, py = ty + uy * s;
                        if (px >= 0f && py >= 0f)
                            continue; // still in the chunk: its own ceiling has it
                        if (px < -t || py < -t)
                            break;
                        float drop = rise * s;
                        if (maxZ - drop <= Math.Max(best, found))
                            break;
                        Heights h = px < 0f ? (py < 0f ? northWest : west) : north;
                        if (h == null)
                            break; // not loaded: lit beyond (fail-safe), never guessed
                        float lx = px < 0f ? px + t : px, ly = py < 0f ? py + t : py;
                        found = Math.Max(found, TerrainLandBuilder.SampleZ(h.CornerZ, lx, ly) - drop);
                        if (h.OccluderTop != null)
                        {
                            int i = Math.Min(t - 1, (int)lx), j = Math.Min(t - 1, (int)ly);
                            if (h.OccluderTop[i, j] != StaticOccluders.None)
                                found = Math.Max(found, h.OccluderTop[i, j] - drop);
                        }
                    }
                    margin[idx] = found;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// TEST-only, Unity-free bookkeeping behind SpriteShadowCeiling: the attached chunks' ceilings (own + margin), the
    /// dynamic casters, and which chunks must be recomposed. Main thread only. Bounded per frame: a caster that keeps
    /// its tile costs one lookup; one that moves, appears or goes away dirties only the chunks its footprint touches
    /// (at most 2x2); margins are recomputed for at most <see cref="MarginsPerFrame"/> chunks a frame (the rest wait,
    /// lit at their west/north edge meanwhile, and are counted).
    /// </summary>
    public sealed class ShadowCeilingCore
    {
        private const int T = TerrainLandBuilder.Tiles, N = LiveGroundLighting.ShadowSize * LiveGroundLighting.ShadowSize;

        public sealed class Chunk
        {
            public int X0, Y0;
            public object Owner;
            public float[] Own;       // the chunk's own ceiling (worker-built)
            public float[] Base;      // max(Own, margin)
            public float[] Settled;   // Base + item stamps (items rarely move, so mobiles are stamped on top of this)
            public float[] Composite; // Settled + mobile stamps: what the texture shows
            public DynamicShadow.Heights Heights;
            public bool HasMargin;
        }

        private sealed class Caster
        {
            public int X, Y, Z, Height, Seen;
            public bool Mobile;
            public bool Casts => Height > 0;
        }

        private readonly Dictionary<(int, int), Chunk> _chunks = new Dictionary<(int, int), Chunk>();
        private readonly Dictionary<object, Caster> _casters = new Dictionary<object, Caster>();
        private readonly HashSet<(int, int)> _dirty = new HashSet<(int, int)>();        // Composite to redo
        private readonly HashSet<(int, int)> _settledDirty = new HashSet<(int, int)>(); // Settled (and Composite) to redo
        private readonly List<(int, int)> _marginQueue = new List<(int, int)>();
        private readonly HashSet<(int, int)> _marginQueued = new HashSet<(int, int)>();
        private readonly List<object> _gone = new List<object>();
        private readonly float[] _margin = new float[N];
        private int _generation;

        public int MarginsPerFrame { get; set; } = 1;

        public int ChunkCount => _chunks.Count;
        public int CasterCount => _casters.Count;
        public int CastingCount { get; private set; }
        public int PendingMargins => _marginQueue.Count;

        /// <summary>Last <see cref="Frame"/>: chunks recomposed, margins computed, casters swept, texels stamped.</summary>
        public int LastDirty { get; private set; }
        public int LastMargins { get; private set; }
        public int LastSwept { get; private set; }
        public int LastStamped { get; private set; }

        public bool TryGet(int x0, int y0, out Chunk chunk) => _chunks.TryGetValue((x0, y0), out chunk);

        /// <summary>A chunk attached: its own ceiling now, its margin (and its east/south neighbours') queued.</summary>
        public Chunk Add(int x0, int y0, float[] own, int[,] cornerZ, int[,] occluderTop, object owner)
        {
            var c = new Chunk
            {
                X0 = x0, Y0 = y0, Owner = owner, Own = own,
                Base = (float[])own.Clone(), Settled = new float[N], Composite = new float[N],
                Heights = DynamicShadow.Heights.Of(cornerZ, occluderTop)
            };
            _chunks[(x0, y0)] = c;
            _settledDirty.Add((x0, y0));
            QueueMargin(x0, y0);
            QueueNeighbours(x0, y0);
            return c;
        }

        /// <summary>A chunk detached (only if <paramref name="owner"/> still has it); its east/south neighbours lose its margin.</summary>
        public bool Remove(int x0, int y0, object owner)
        {
            if (!_chunks.TryGetValue((x0, y0), out Chunk c) || !ReferenceEquals(c.Owner, owner))
                return false;
            _chunks.Remove((x0, y0));
            _dirty.Remove((x0, y0));
            _settledDirty.Remove((x0, y0));
            QueueNeighbours(x0, y0);
            return true;
        }

        private void QueueNeighbours(int x0, int y0)
        {
            QueueMargin(x0 + T, y0);
            QueueMargin(x0, y0 + T);
            QueueMargin(x0 + T, y0 + T);
        }

        private void QueueMargin(int x0, int y0)
        {
            if (_chunks.ContainsKey((x0, y0)) && _marginQueued.Add((x0, y0)))
                _marginQueue.Add((x0, y0));
        }

        /// <summary>
        /// A caster seen this frame: a column of <paramref name="height"/> Z (0 or less: tracked, casts nothing) standing
        /// on tile (x, y) at Z <paramref name="z"/>. Casters not reported between two <see cref="Frame"/> calls are removed.
        /// <paramref name="mobile"/>: stamped over the settled (item) layer, so its steps never restamp the items.
        /// </summary>
        public void Report(object key, int x, int y, int z, int height, bool mobile = false)
        {
            if (_casters.TryGetValue(key, out Caster c))
            {
                c.Seen = _generation;
                if (c.X == x && c.Y == y && c.Z == z && c.Height == height && c.Mobile == mobile)
                    return;
                Touch(c);
                c.X = x; c.Y = y; c.Z = z; c.Height = height; c.Mobile = mobile;
            }
            else
                _casters[key] = c = new Caster { X = x, Y = y, Z = z, Height = height, Mobile = mobile, Seen = _generation };
            Touch(c);
        }

        /// <summary>Marks the chunks a casting column's footprint touches dirty (an item: their settled layer too).</summary>
        private void Touch(Caster c)
        {
            if (!c.Casts)
                return;
            (int x0, int y0, int x1, int y1) = DynamicShadow.Footprint(c.X, c.Y, c.Height);
            for (int cy = Floor(y0); cy <= Floor(y1 - 1); cy += T)
                for (int cx = Floor(x0); cx <= Floor(x1 - 1); cx += T)
                    if (_chunks.ContainsKey((cx, cy)))
                        (c.Mobile ? _dirty : _settledDirty).Add((cx, cy));
        }

        private static int Floor(int v) => (int)Math.Floor(v / (double)T) * T;

        /// <summary>
        /// Once per frame: drops casters not reported since the last call, computes queued margins (budgeted) and
        /// recomposes every dirty chunk (Base, then every casting column whose footprint reaches it). Appends the chunks
        /// whose <see cref="Chunk.Composite"/> changed to <paramref name="changed"/>.
        /// </summary>
        public void Frame(List<Chunk> changed)
        {
            _gone.Clear();
            foreach (KeyValuePair<object, Caster> kv in _casters)
                if (kv.Value.Seen != _generation)
                    _gone.Add(kv.Key);
            foreach (object key in _gone)
            {
                Touch(_casters[key]);
                _casters.Remove(key);
            }
            LastSwept = _gone.Count;
            _gone.Clear();
            _generation++;

            int margins = 0;
            while (_marginQueue.Count > 0 && margins < MarginsPerFrame)
            {
                (int x0, int y0) = _marginQueue[0];
                _marginQueue.RemoveAt(0);
                _marginQueued.Remove((x0, y0));
                if (!_chunks.TryGetValue((x0, y0), out Chunk c))
                    continue;
                ComputeBase(c);
                _settledDirty.Add((x0, y0));
                margins++;
            }
            LastMargins = margins;

            int casting = 0, stamped = 0;
            foreach (KeyValuePair<object, Caster> kv in _casters)
                if (kv.Value.Casts)
                    casting++;
            CastingCount = casting;
            LastDirty = 0;
            // Items: Settled = Base + every casting item column reaching the chunk (only when an item or the base changed).
            foreach ((int x0, int y0) in _settledDirty)
            {
                if (!_chunks.TryGetValue((x0, y0), out Chunk c))
                    continue;
                Array.Copy(c.Base, c.Settled, N);
                stamped += StampAll(c, false);
                _dirty.Add((x0, y0));
            }
            _settledDirty.Clear();
            // Mobiles: Composite = Settled + every mobile column reaching the chunk.
            foreach ((int x0, int y0) in _dirty)
            {
                if (!_chunks.TryGetValue((x0, y0), out Chunk c))
                    continue;
                Array.Copy(c.Settled, c.Composite, N);
                stamped += StampAll(c, true);
                changed.Add(c);
                LastDirty++;
            }
            _dirty.Clear();
            LastStamped = stamped;
        }

        private int StampAll(Chunk c, bool mobiles)
        {
            int stamped = 0;
            foreach (KeyValuePair<object, Caster> kv in _casters)
            {
                Caster k = kv.Value;
                if (!k.Casts || k.Mobile != mobiles)
                    continue;
                (int fx0, int fy0, int fx1, int fy1) = DynamicShadow.Footprint(k.X, k.Y, k.Height);
                if (fx1 <= c.X0 || fy1 <= c.Y0 || fx0 >= c.X0 + T || fy0 >= c.Y0 + T)
                    continue;
                stamped += DynamicShadow.Stamp(mobiles ? c.Composite : c.Settled, c.X0, c.Y0, k.X, k.Y, k.Z + k.Height, k.Height);
            }
            return stamped;
        }

        /// <summary>Base = max(own, margin from the loaded west/north/north-west neighbours).</summary>
        private void ComputeBase(Chunk c)
        {
            _chunks.TryGetValue((c.X0 - T, c.Y0), out Chunk w);
            _chunks.TryGetValue((c.X0, c.Y0 - T), out Chunk nn);
            _chunks.TryGetValue((c.X0 - T, c.Y0 - T), out Chunk nw);
            c.HasMargin = DynamicShadow.Margin(c.Own, w?.Heights, nn?.Heights, nw?.Heights, _margin);
            for (int i = 0; i < N; i++)
                c.Base[i] = Math.Max(c.Own[i], _margin[i]);
        }

        public void Clear()
        {
            _chunks.Clear();
            _casters.Clear();
            _dirty.Clear();
            _settledDirty.Clear();
            _marginQueue.Clear();
            _marginQueued.Clear();
            CastingCount = 0;
        }
    }
}
