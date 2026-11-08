using System;
using System.Collections.Generic;
using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own (the Unity upload is in TerrainWater).
    /// <summary>
    /// Which LAND graphics are water, from tiledata only. A land row is water when it has <see cref="TileFlags.Wet"/>
    /// AND is named "water". Wet alone is not enough: in the shipped tiledata the Wet land rows are 0xA8–0xAB and
    /// 0x136–0x137 ("water"), but also the Ter Mur "acid" rows (0x2E0E–0x2E5F) and three "TerrainFallback" rows.
    /// Static water (0x1797… shore and lake statics) is not land and stays on the classic sprite path.
    /// </summary>
    public static class WaterTiles
    {
        public const string WaterName = "water";

        public static bool IsWater(TileFlags flags, string name) =>
            (flags & TileFlags.Wet) != 0 && string.Equals(name?.Trim(), WaterName, StringComparison.OrdinalIgnoreCase);

        public static bool IsWater(in LandTileData row) => IsWater(row.Flags, row.Name);

        /// <summary>One flag per land graphic (index = graphic). Rows past the table read as not water.</summary>
        public static bool[] Table(LandTileData[] land)
        {
            var table = new bool[land?.Length ?? 0];
            for (int i = 0; i < table.Length; i++)
                table[i] = IsWater(land[i]);
            return table;
        }

        public static bool Lookup(bool[] table, int graphic) => table != null && (uint)graphic < (uint)table.Length && table[graphic];

        /// <summary>The flat open-water statics (0x1796–0x17B2) the map lays over coasts, docks and lakes.</summary>
        public const int OceanStaticFirst = 0x1796, OceanStaticLast = 0x17B2;

        /// <summary>
        /// Whether a STATIC is open water the surface takes over: one of the flat ocean statics, and tiledata agrees
        /// (Wet, named "water"). The other statics named "water" (0x346E…, 0x55F0…) are shoreline and fall edges whose art
        /// is part water, part rock or foam: they stay classic sprites.
        /// </summary>
        public static bool IsOceanStatic(int graphic, TileFlags flags, string name) =>
            graphic >= OceanStaticFirst && graphic <= OceanStaticLast && IsWater(flags, name);

        /// <summary>One flag per static graphic (index = graphic): <see cref="IsOceanStatic"/>.</summary>
        public static bool[] StaticTable(StaticTileData[] statics)
        {
            var table = new bool[statics?.Length ?? 0];
            for (int i = 0; i < table.Length; i++)
                table[i] = IsOceanStatic(i, statics[i].Flags, statics[i].Name);
            return table;
        }

        /// <summary>
        /// The land rows of the streamer's own data (<c>WorldStreamer.Data</c>, overrides included). Fails closed:
        /// no data, a tiledata that does not load, or no land rows give false and a reason, never an empty table that
        /// would silently read as "no water".
        /// </summary>
        public static bool TryLand(UOData data, out LandTileData[] land, out string error)
        {
            land = null;
            error = null;
            if (data == null)
            {
                error = "no tiledata (WorldStreamer.Data is null)";
                return false;
            }
            try
            {
                land = data.TileData?.Land;
            }
            catch (Exception e)
            {
                error = $"tiledata did not load ({e.GetType().Name}: {e.Message})";
                return false;
            }
            if (land == null || land.Length == 0)
            {
                land = null;
                error = "tiledata has no land rows";
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Builds one chunk's water surface for the Terrain backend from the land the streamer diverted: a mesh over every
    /// water tile, in terrain space (<see cref="TerrainLandBuilder.TerrainPoint"/>) at the map's corner heights, with
    /// the compositor's RIGHT–LEFT split. Per vertex it carries the mean colour of that tile's classic art (so the
    /// surface takes the map's own water colour) and a shore distance.
    ///
    /// Shore distance is NOT water depth: UO has no water depth (ocean land sits at one Z). It is the corner-grid
    /// distance, in tiles, to the nearest corner that touches non-water land in this chunk, capped at
    /// <see cref="MaxShore"/>; 0 = on the shoreline. Neighbouring chunks are not visible here, so a shore that runs
    /// exactly along a chunk border reads as open water on that side (no foam there).
    /// </summary>
    public sealed class WaterSurfaceBuilder
    {
        public const int Tiles = TerrainLandBuilder.Tiles;
        public const int MaxShore = 4;

        private readonly int _x0, _y0;
        private readonly bool[,] _water = new bool[Tiles, Tiles];
        private readonly bool[,] _land = new bool[Tiles, Tiles]; // diverted, not water
        private readonly uint[,] _mean = new uint[Tiles, Tiles];
        private readonly int?[,] _corner = new int?[Tiles + 1, Tiles + 1];
        private readonly bool[,] _static = new bool[Tiles, Tiles]; // water from an ocean static: its flat Z wins the corners
        private readonly HashSet<int> _graphics = new HashSet<int>();
        private Func<int, int, bool> _outsideLand;

        /// <summary>
        /// What lies around the chunk, for the shore distance: (world x, world y) → non-water land there. Without it a
        /// shore running along the chunk border reads as open water on this side, and the foam stops at the border.
        /// </summary>
        public void SetOutside(Func<int, int, bool> landAt) => _outsideLand = landAt;

        public WaterSurfaceBuilder(int x0, int y0)
        {
            _x0 = x0;
            _y0 = y0;
        }

        public int WaterTileCount { get; private set; }

        public void AddTile(in DrawItem it, Bitmap32 art, bool texmap, bool water)
        {
            if (it.Kind != DrawKind.Land)
                throw new ArgumentException("only land goes into the water surface", nameof(it));
            int i = it.X - _x0, j = it.Y - _y0;
            if (i < 0 || j < 0 || i >= Tiles || j >= Tiles)
                throw new ArgumentOutOfRangeException(nameof(it), $"tile {it.X},{it.Y} is outside chunk {_x0},{_y0}");
            if (art == null)
                return;
            if (!water)
            {
                _land[i, j] = true;
                return;
            }
            if (_static[i, j])
                return; // an ocean static already made this tile water at its own height
            if (!_water[i, j])
                WaterTileCount++;
            _water[i, j] = true;
            _graphics.Add(it.Graphic);
            _mean[i, j] = MeanColor(art, texmap);
            SetCorner(i, j, it.Z);
            SetCorner(i + 1, j, it.ZRight);
            SetCorner(i + 1, j + 1, it.ZBottom);
            SetCorner(i, j + 1, it.ZLeft);
        }

        // A corner an ocean static set keeps its flat height (land water around it shares the corner).
        private void SetCorner(int ci, int cj, int z)
        {
            if (!Touches(_static, ci, cj))
                _corner[ci, cj] = z;
        }

        /// <summary>
        /// An ocean static (<see cref="WaterTiles.IsOceanStatic"/>): the tile becomes water, flat at the static's Z, in
        /// its art's mean colour, whatever land lies under it (sand under a dock's water, the sea floor).
        /// </summary>
        public void AddStaticWater(in DrawItem it, Bitmap32 art)
        {
            if (it.Kind != DrawKind.Static)
                throw new ArgumentException("AddStaticWater takes a static", nameof(it));
            int i = it.X - _x0, j = it.Y - _y0;
            if (i < 0 || j < 0 || i >= Tiles || j >= Tiles)
                throw new ArgumentOutOfRangeException(nameof(it), $"tile {it.X},{it.Y} is outside chunk {_x0},{_y0}");
            if (art == null)
                return;
            if (!_water[i, j])
                WaterTileCount++;
            _water[i, j] = true;
            _static[i, j] = true;
            _graphics.Add(it.Graphic);
            _mean[i, j] = MeanColor(art, false);
            _corner[i, j] = it.Z;
            _corner[i + 1, j] = it.Z;
            _corner[i + 1, j + 1] = it.Z;
            _corner[i, j + 1] = it.Z;
        }

        /// <summary>
        /// Every water tile (world x, y) with the colour the ground under it should bake as: its art's mean colour,
        /// darkened as a water bed, so only the water surface's own waves and foam show on top.
        /// </summary>
        public void ForEachBed(Action<int, int, uint> bed)
        {
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                    if (_water[i, j])
                        bed(_x0 + i, _y0 + j, Bed(_mean[i, j]));
        }

        /// <summary>A water tile's mean colour as the bed under the surface: 70% brightness.</summary>
        public static uint Bed(uint mean)
        {
            uint r = ((mean >> 16) & 0xFF) * 7 / 10, g = ((mean >> 8) & 0xFF) * 7 / 10, b = (mean & 0xFF) * 7 / 10;
            return 0xFF000000u | r << 16 | g << 8 | b;
        }

        public WaterSurfaceData Finish()
        {
            var d = new WaterSurfaceData { ChunkX0 = _x0, ChunkY0 = _y0, TileCount = WaterTileCount };
            d.Graphics = new List<int>(_graphics);
            d.Graphics.Sort();
            if (WaterTileCount == 0 && _flatLevel == null) // flat: a skirt can reach in from water in the next chunk
                return d;

            // Shore: drawn ground that no water (land or ocean static) covers; around the chunk, what the map says.
            var shoreLand = new bool[Tiles, Tiles];
            for (int a = 0; a < Tiles; a++)
                for (int b = 0; b < Tiles; b++)
                    shoreLand[a, b] = _land[a, b] && !_water[a, b];
            Func<int, int, bool> outside = _outsideLand;
            int[,] shore = ShoreDistance(shoreLand, outside == null ? null : (a, b) => outside(_x0 + a, _y0 + b));
            var index = new int[Tiles + 1, Tiles + 1];
            var x = new List<float>();
            var y = new List<float>();
            var z = new List<float>();
            var s = new List<float>();
            var c = new List<uint>();

            // Which tiles the mesh covers: the water tiles, and with a flat level also a one-tile skirt over the banks
            // (the terrain hides it wherever the bank is above the water, so the shoreline follows the ground).
            Func<int, int, (bool Water, int Level)> flat = _flatLevel;
            var mesh = new bool[Tiles, Tiles];
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                {
                    mesh[i, j] = _water[i, j];
                    if (flat != null && !mesh[i, j])
                        for (int di = -1; di <= 1 && !mesh[i, j]; di++)
                            for (int dj = -1; dj <= 1 && !mesh[i, j]; dj++)
                                mesh[i, j] = flat(_x0 + i + di, _y0 + j + dj).Water;
                }
            uint chunkTint = MeanTint();
            for (int i = 0; i <= Tiles; i++)
                for (int j = 0; j <= Tiles; j++)
                {
                    index[i, j] = -1;
                    if (!Touches(mesh, i, j))
                        continue;
                    float level;
                    if (flat != null)
                    {
                        float? l = FlatLevel(flat, _x0 + i, _y0 + j);
                        if (l == null)
                            continue;
                        level = l.Value;
                    }
                    else
                        level = _corner[i, j] ?? 0;
                    index[i, j] = x.Count;
                    (float px, float py, float pz) = TerrainLandBuilder.TerrainPoint(_x0 + i, _y0 + j, level);
                    x.Add(px);
                    y.Add(py);
                    z.Add(pz);
                    bool wet = Touches(_water, i, j);
                    s.Add(wet ? (float)shore[i, j] / MaxShore : 0f);
                    c.Add(wet ? CornerTint(i, j) : chunkTint);
                }

            var tri = new List<int>(WaterTileCount * 6);
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                {
                    if (!mesh[i, j] || index[i, j] < 0 || index[i + 1, j] < 0 || index[i + 1, j + 1] < 0 || index[i, j + 1] < 0)
                        continue;
                    int top = index[i, j], right = index[i + 1, j], bottom = index[i + 1, j + 1], left = index[i, j + 1];
                    // RIGHT–LEFT split, wound so cross(b - a, c - a) is terrain +Y (Unity's front face seen from above).
                    tri.Add(top); tri.Add(right); tri.Add(left);
                    tri.Add(right); tri.Add(bottom); tri.Add(left);
                }

            d.X = x.ToArray();
            d.Y = y.ToArray();
            d.Z = z.ToArray();
            d.Shore = s.ToArray();
            d.Tint = c.ToArray();
            d.Indices = tri.ToArray();
            return d;
        }

        private Func<int, int, (bool Water, int Level)> _flatLevel;

        /// <summary>
        /// Natural terrain: the surface is flat at the water's level (a little under it, <see cref="WaterBed.SurfaceDrop"/>)
        /// instead of following the tile corners, and reaches one tile past the water over the banks. With the bed lowered
        /// under it (<see cref="WaterBed.Corner"/>) and the terrain's smooth heights, the bank's own slope cuts the
        /// shoreline. <paramref name="tileWater"/>: any world tile → (water there, its level).
        /// </summary>
        public void UseFlatLevel(Func<int, int, (bool Water, int Level)> tileWater) => _flatLevel = tileWater;

        /// <summary>The flat surface's height at world corner (x, y): the mean level of the water tiles around it, else of
        /// those within two tiles (a skirt corner); null when there is no water near.</summary>
        public static float? FlatLevel(Func<int, int, (bool Water, int Level)> tileWater, int x, int y)
        {
            for (int reach = 1; reach <= 2; reach++)
            {
                int sum = 0, n = 0;
                for (int tx = x - reach; tx < x + reach; tx++)
                    for (int ty = y - reach; ty < y + reach; ty++)
                    {
                        (bool water, int level) = tileWater(tx, ty);
                        if (!water)
                            continue;
                        sum += level;
                        n++;
                    }
                if (n > 0)
                    return (float)sum / n - WaterBed.SurfaceDrop;
            }
            return null;
        }

        /// <summary>The mean colour of the chunk's water tiles (for skirt corners, which touch none).</summary>
        private uint MeanTint()
        {
            long r = 0, g = 0, b = 0;
            int n = 0;
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                    if (_water[i, j])
                    {
                        uint m = _mean[i, j];
                        r += (m >> 16) & 0xFF;
                        g += (m >> 8) & 0xFF;
                        b += m & 0xFF;
                        n++;
                    }
            return n == 0 ? 0xFF204060u : 0xFF000000u | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
        }

        /// <summary>Whether any of the (up to four) tiles around corner (ci, cj) is set in <paramref name="tiles"/>.</summary>
        private static bool Touches(bool[,] tiles, int ci, int cj)
        {
            for (int di = -1; di <= 0; di++)
                for (int dj = -1; dj <= 0; dj++)
                {
                    int i = ci + di, j = cj + dj;
                    if (i >= 0 && j >= 0 && i < Tiles && j < Tiles && tiles[i, j])
                        return true;
                }
            return false;
        }

        /// <summary>
        /// Per corner: Chebyshev steps (in tiles) to the nearest corner touching non-water land, capped at
        /// <see cref="MaxShore"/>. A corner touching land is 0; one with no land in reach is <see cref="MaxShore"/>.
        /// </summary>
        public static int[,] ShoreDistance(bool[,] land) => ShoreDistance(land, null);

        /// <summary>
        /// <see cref="ShoreDistance(bool[,])"/> with the land around the chunk too: <paramref name="outsideLand"/>
        /// (chunk-relative tile, from -<see cref="MaxShore"/> to <see cref="Tiles"/> + <see cref="MaxShore"/> - 1) says
        /// whether a tile outside the chunk is non-water land, so the distance (and the foam) runs on across chunk
        /// borders instead of restarting at each one. Null: nothing outside counts as land.
        /// </summary>
        public static int[,] ShoreDistance(bool[,] land, Func<int, int, bool> outsideLand)
        {
            const int m = MaxShore, t = Tiles + 2 * m, n = t + 1;
            var ext = new bool[t, t];
            for (int i = -m; i < Tiles + m; i++)
                for (int j = -m; j < Tiles + m; j++)
                {
                    bool inside = i >= 0 && j >= 0 && i < Tiles && j < Tiles;
                    ext[i + m, j + m] = inside ? land[i, j] : outsideLand != null && outsideLand(i, j);
                }
            var dist = new int[n, n];
            var queue = new Queue<(int, int)>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    dist[i, j] = MaxShore;
                    if (TouchesIn(ext, t, i, j))
                    {
                        dist[i, j] = 0;
                        queue.Enqueue((i, j));
                    }
                }
            while (queue.Count > 0)
            {
                (int i, int j) = queue.Dequeue();
                int next = dist[i, j] + 1;
                if (next >= MaxShore)
                    continue;
                for (int di = -1; di <= 1; di++)
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= n || b >= n || dist[a, b] <= next)
                            continue;
                        dist[a, b] = next;
                        queue.Enqueue((a, b));
                    }
            }
            var chunk = new int[Tiles + 1, Tiles + 1];
            for (int i = 0; i <= Tiles; i++)
                for (int j = 0; j <= Tiles; j++)
                    chunk[i, j] = dist[i + m, j + m];
            return chunk;
        }

        private static bool TouchesIn(bool[,] tiles, int size, int ci, int cj)
        {
            for (int di = -1; di <= 0; di++)
                for (int dj = -1; dj <= 0; dj++)
                {
                    int i = ci + di, j = cj + dj;
                    if (i >= 0 && j >= 0 && i < size && j < size && tiles[i, j])
                        return true;
                }
            return false;
        }

        /// <summary>Mean of the water tiles touching a corner (ARGB, alpha 255).</summary>
        private uint CornerTint(int ci, int cj)
        {
            int r = 0, g = 0, b = 0, count = 0;
            for (int di = -1; di <= 0; di++)
                for (int dj = -1; dj <= 0; dj++)
                {
                    int i = ci + di, j = cj + dj;
                    if (i < 0 || j < 0 || i >= Tiles || j >= Tiles || !_water[i, j])
                        continue;
                    uint m = _mean[i, j];
                    r += (int)(m >> 16) & 0xFF;
                    g += (int)(m >> 8) & 0xFF;
                    b += (int)m & 0xFF;
                    count++;
                }
            if (count == 0)
                return 0xFF000000;
            return 0xFF000000u | (uint)(r / count) << 16 | (uint)(g / count) << 8 | (uint)(b / count);
        }

        /// <summary>Mean ARGB (alpha 255) of a tile's art over the tile, sampled where the terrain bake samples it.</summary>
        public static uint MeanColor(Bitmap32 art, bool texmap)
        {
            const int grid = 8;
            long r = 0, g = 0, b = 0;
            int count = 0;
            for (int a = 0; a < grid; a++)
                for (int c = 0; c < grid; c++)
                {
                    uint px = TerrainLandBuilder.SampleArt(art, texmap, (a + 0.5f) / grid, (c + 0.5f) / grid);
                    if (px >> 24 == 0)
                        continue;
                    r += (px >> 16) & 0xFF;
                    g += (px >> 8) & 0xFF;
                    b += px & 0xFF;
                    count++;
                }
            if (count == 0)
                return 0xFF000000;
            return 0xFF000000u | (uint)(r / count) << 16 | (uint)(g / count) << 8 | (uint)(b / count);
        }
    }

    /// <summary>
    /// Natural terrain: the ground under water as a bed below the surface, so the water has depth and the shoreline is
    /// cut by the bank's slope, not by tile edges (Unity-free).
    /// </summary>
    public static class WaterBed
    {
        /// <summary>How far (UO Z) the bed sits under the surface away from the banks.</summary>
        public const int Depth = 8;

        /// <summary>How far (UO Z) the flat surface sits under the water's own level, so flat beaches at that level stay dry.</summary>
        public const float SurfaceDrop = 0.4f;

        /// <summary>How far (UO Z) a bank corner touching water dips under the level, so the water reaches it.</summary>
        public const int BankDip = 2;

        /// <summary>
        /// A world corner's height for the drawn terrain: under open water (all four tiles round it water) the bed, the
        /// mean level less <see cref="Depth"/>; on a bank (some of them water) just under the lowest level touching it;
        /// elsewhere the map's own height. Never raised.
        /// </summary>
        public static float Corner(int mapZ, Func<int, int, (bool Water, int Level)> tileWater, int x, int y)
        {
            int n = 0, sum = 0, min = int.MaxValue;
            for (int tx = x - 1; tx <= x; tx++)
                for (int ty = y - 1; ty <= y; ty++)
                {
                    (bool water, int level) = tileWater(tx, ty);
                    if (!water)
                        continue;
                    n++;
                    sum += level;
                    min = Math.Min(min, level);
                }
            if (n == 4)
                return Math.Min(mapZ, (float)sum / n - Depth);
            if (n > 0)
                return Math.Min(mapZ, min - BankDip);
            return mapZ;
        }
    }

    /// <summary>A finished chunk water surface in terrain space (what TerrainWater uploads as a mesh).</summary>
    public sealed class WaterSurfaceData
    {
        public int ChunkX0, ChunkY0, TileCount;

        /// <summary>The distinct land graphics classified as water in this chunk (for the log).</summary>
        public List<int> Graphics;

        /// <summary>Vertex positions in terrain space.</summary>
        public float[] X, Y, Z;

        /// <summary>Per vertex shore distance / <see cref="WaterSurfaceBuilder.MaxShore"/>: 0 on the shoreline, 1 open water.</summary>
        public float[] Shore;

        /// <summary>Per vertex mean classic-art colour (ARGB).</summary>
        public uint[] Tint;

        public int[] Indices;

        public int VertexCount => X?.Length ?? 0;
    }
}
