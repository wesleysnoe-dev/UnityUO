using System;
using System.Collections.Generic;
using UOClient.ArtPipeline;
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
        private readonly HashSet<int> _graphics = new HashSet<int>();

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
            if (!_water[i, j])
                WaterTileCount++;
            _water[i, j] = true;
            _graphics.Add(it.Graphic);
            _mean[i, j] = MeanColor(art, texmap);
            _corner[i, j] = it.Z;
            _corner[i + 1, j] = it.ZRight;
            _corner[i + 1, j + 1] = it.ZBottom;
            _corner[i, j + 1] = it.ZLeft;
        }

        public WaterSurfaceData Finish()
        {
            var d = new WaterSurfaceData { ChunkX0 = _x0, ChunkY0 = _y0, TileCount = WaterTileCount };
            d.Graphics = new List<int>(_graphics);
            d.Graphics.Sort();
            if (WaterTileCount == 0)
                return d;

            int[,] shore = ShoreDistance(_land);
            var index = new int[Tiles + 1, Tiles + 1];
            var x = new List<float>();
            var y = new List<float>();
            var z = new List<float>();
            var s = new List<float>();
            var c = new List<uint>();
            for (int i = 0; i <= Tiles; i++)
                for (int j = 0; j <= Tiles; j++)
                {
                    index[i, j] = -1;
                    if (!Touches(_water, i, j))
                        continue;
                    index[i, j] = x.Count;
                    (float px, float py, float pz) = TerrainLandBuilder.TerrainPoint(_x0 + i, _y0 + j, _corner[i, j] ?? 0);
                    x.Add(px);
                    y.Add(py);
                    z.Add(pz);
                    s.Add((float)shore[i, j] / MaxShore);
                    c.Add(CornerTint(i, j));
                }

            var tri = new List<int>(WaterTileCount * 6);
            for (int i = 0; i < Tiles; i++)
                for (int j = 0; j < Tiles; j++)
                {
                    if (!_water[i, j])
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
        public static int[,] ShoreDistance(bool[,] land)
        {
            const int n = Tiles + 1;
            var dist = new int[n, n];
            var queue = new Queue<(int, int)>();
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    dist[i, j] = MaxShore;
                    if (Touches(land, i, j))
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
            return dist;
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
