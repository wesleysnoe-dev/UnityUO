using System;
using UOClient.ArtPipeline;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own (the Terrain upload is in TerrainLandChunk).
    /// <summary>
    /// Builds the data for one chunk's real UnityEngine.Terrain from the land the streamer diverted: a heightmap from
    /// the map's corner heights, holes where no land was diverted, and one baked texture with each tile's classic art.
    ///
    /// Terrain space: 1 unit per tile in X and Z, <see cref="HeightUnitPerZ"/> units per UO Z in Y (the compositor's
    /// shade uses the same ratio, ZStep / (TileHalf·√2) tile edges per Z). Tile y runs along -Z, which keeps the UO
    /// screen projection orientation-preserving for a camera (see <see cref="ToWorldScreen"/>), so the terrain's top
    /// faces stay front faces. A tile corner (x, y) at height z is terrain point (x, z·HeightUnitPerZ, -y).
    ///
    /// Heightmap: <see cref="SamplesPerTile"/> samples per tile edge, each on the compositor's RIGHT–LEFT split
    /// (<see cref="LiveGroundProjection.HeightAt"/>). Unity's own cell triangulation and LOD decide what is drawn
    /// between samples; this builder only guarantees the samples.
    /// </summary>
    public sealed class TerrainLandBuilder
    {
        public const int Tiles = 16;
        public const int SamplesPerTile = 2;
        public const int HeightmapResolution = Tiles * SamplesPerTile + 1; // 33: Unity's minimum, 2^n + 1
        public const int HolesResolution = HeightmapResolution - 1;
        public const int MinZ = -128, ZRange = 256;

        /// <summary>Terrain Y units per UO Z: ZStep / (TileHalf·√2), the compositor's tile-edge height ratio.</summary>
        public const float HeightUnitPerZ = LiveGroundProjection.ZStep / (LiveGroundProjection.TileHalf * 1.41421356f);

        private readonly int _x0, _y0, _texelsPerTile;
        private readonly bool[,] _present = new bool[Tiles, Tiles];
        private readonly Bitmap32[,] _art = new Bitmap32[Tiles, Tiles];
        private readonly bool[,] _texmap = new bool[Tiles, Tiles];
        private readonly int?[,] _corner = new int?[Tiles + 1, Tiles + 1];

        /// <param name="x0">First tile x of the chunk.</param>
        /// <param name="y0">First tile y of the chunk.</param>
        /// <param name="texelsPerTile">Baked texture pixels per tile edge.</param>
        public TerrainLandBuilder(int x0, int y0, int texelsPerTile)
        {
            if (texelsPerTile < 1 || texelsPerTile * Tiles > 4096)
                throw new ArgumentOutOfRangeException(nameof(texelsPerTile));
            _x0 = x0;
            _y0 = y0;
            _texelsPerTile = texelsPerTile;
        }

        public int TileCount { get; private set; }

        /// <summary>Texels per tile for an art scale: 32 per scale step, capped so the chunk texture stays ≤ 1024.</summary>
        public static int TexelsFor(int scale) => Math.Max(16, Math.Min(64, 32 * Math.Max(1, scale)));

        /// <param name="it">A land item as IsoCompositor.BuildDrawList made it (its four corner heights are the map's).</param>
        /// <param name="art">The texmap when <paramref name="texmap"/>, else the flat 44x44 land sprite.</param>
        public void AddTile(in DrawItem it, Bitmap32 art, bool texmap)
        {
            if (it.Kind != DrawKind.Land)
                throw new ArgumentException("only land goes into the terrain", nameof(it));
            int i = it.X - _x0, j = it.Y - _y0;
            if (i < 0 || j < 0 || i >= Tiles || j >= Tiles)
                throw new ArgumentOutOfRangeException(nameof(it), $"tile {it.X},{it.Y} is outside chunk {_x0},{_y0}");
            if (art == null)
                return;
            if (!_present[i, j])
                TileCount++;
            _present[i, j] = true;
            _art[i, j] = art;
            _texmap[i, j] = texmap;
            // The map's corner heights (also for tiles the compositor draws flat: a heightfield cannot step).
            _corner[i, j] = it.Z;
            _corner[i + 1, j] = it.ZRight;
            _corner[i + 1, j + 1] = it.ZBottom;
            _corner[i, j + 1] = it.ZLeft;
        }

        public TerrainLandData Finish()
        {
            var d = new TerrainLandData
            {
                ChunkX0 = _x0,
                ChunkY0 = _y0,
                TileCount = TileCount,
                CornerZ = new int[Tiles + 1, Tiles + 1],
                Heights = new float[HeightmapResolution, HeightmapResolution],
                Solid = new bool[HolesResolution, HolesResolution],
                TextureSize = Tiles * _texelsPerTile
            };
            if (TileCount == 0)
                return d;

            // Corners no diverted tile touched (around holes) take the lowest known height.
            int fill = int.MaxValue;
            foreach (int? z in _corner)
                if (z.HasValue)
                    fill = Math.Min(fill, z.Value);
            for (int i = 0; i <= Tiles; i++)
                for (int j = 0; j <= Tiles; j++)
                    d.CornerZ[i, j] = _corner[i, j] ?? fill;

            // Heights[row, col]: row along terrain +Z (= tile y descending), col along +X (tile x).
            for (int row = 0; row < HeightmapResolution; row++)
            {
                float ty = Tiles - (float)row / SamplesPerTile;
                for (int col = 0; col < HeightmapResolution; col++)
                {
                    float tx = (float)col / SamplesPerTile;
                    d.Heights[row, col] = Normalize(SampleZ(d.CornerZ, tx, ty));
                }
            }

            for (int row = 0; row < HolesResolution; row++)
                for (int col = 0; col < HolesResolution; col++)
                    d.Solid[row, col] = _present[col / SamplesPerTile, Tiles - 1 - row / SamplesPerTile];

            d.Rgba = BakeTexture(d.TextureSize);
            return d;
        }

        /// <summary>Height (UO Z, fractional) at chunk-local tile coordinates on the compositor's RIGHT–LEFT split.</summary>
        public static float SampleZ(int[,] cornerZ, float tx, float ty)
        {
            int i = Math.Min(Tiles - 1, Math.Max(0, (int)Math.Floor(tx)));
            int j = Math.Min(Tiles - 1, Math.Max(0, (int)Math.Floor(ty)));
            return LiveGroundProjection.HeightAt(cornerZ[i, j], cornerZ[i + 1, j], cornerZ[i + 1, j + 1], cornerZ[i, j + 1], tx - i, ty - j);
        }

        /// <summary>A UO Z as a Unity heightmap value in [0, 1] over <see cref="TerrainLandData.Size"/>.Y.</summary>
        public static float Normalize(float z) => Math.Max(0f, Math.Min(1f, (z - MinZ) / ZRange));

        /// <summary>Terrain-space point of a tile corner (x, y) at height z.</summary>
        public static (float X, float Y, float Z) TerrainPoint(float tileX, float tileY, float z) =>
            (tileX, z * HeightUnitPerZ, -tileY);

        /// <summary>
        /// The UO world-screen pixel (at <paramref name="scale"/>, y down) a terrain-space point projects to: the same
        /// map as <see cref="LiveGroundProjection.Corner"/>. The presenter's camera matrix is this plus the streamer's origin.
        /// </summary>
        public static (float X, float Y) ToWorldScreen(float x, float y, float z, int scale)
        {
            float tileY = -z, uoZ = y / HeightUnitPerZ;
            return ((x - tileY) * LiveGroundProjection.TileHalf * scale,
                    ((x + tileY) * LiveGroundProjection.TileHalf - uoZ * LiveGroundProjection.ZStep) * scale);
        }

        /// <summary>
        /// Nearness to the viewer in UO pixels (unscaled): grows toward the screen bottom and with height. Constant along
        /// no screen pixel's line of sight, so a camera depth linear in it resolves the terrain's self-occlusion.
        /// </summary>
        public static float Nearness(float x, float y, float z) =>
            (x - z) * LiveGroundProjection.TileHalf + y / HeightUnitPerZ * LiveGroundProjection.ZStep;

        /// <summary>
        /// One texture for the chunk, rows bottom-up (as Unity textures): Terrain UV (0,0) is the chunk's tile-y-max,
        /// tile-x-min corner, so image row 0 (top) is tile y0. A texmap fills its tile as the compositor maps it
        /// (u along x, v along y); a flat sprite's diamond is unrotated into the square. No diverted tile: transparent.
        /// </summary>
        private byte[] BakeTexture(int size)
        {
            int p = _texelsPerTile;
            byte[] rgba = new byte[size * size * 4];
            for (int iy = 0; iy < size; iy++)
            {
                int j = iy / p;
                float v = (iy % p + 0.5f) / p;
                int dstRow = size - 1 - iy;
                for (int ix = 0; ix < size; ix++)
                {
                    int i = ix / p;
                    if (!_present[i, j])
                        continue;
                    float u = (ix % p + 0.5f) / p;
                    uint px = SampleArt(_art[i, j], _texmap[i, j], u, v);
                    int o = (dstRow * size + ix) * 4;
                    rgba[o] = (byte)(px >> 16);
                    rgba[o + 1] = (byte)(px >> 8);
                    rgba[o + 2] = (byte)px;
                    rgba[o + 3] = 255; // the terrain's material is opaque; sprite corners outside the diamond are never sampled
                }
            }
            return rgba;
        }

        /// <summary>The ARGB art pixel at tile (u, v): texmap directly, or the sprite at the same spot the mesh maps.</summary>
        public static uint SampleArt(Bitmap32 art, bool texmap, float u, float v)
        {
            float sx, sy;
            if (texmap)
            {
                sx = u;
                sy = v;
            }
            else
            {
                // Bilinear in the sprite's corners: top (½,0), right (1,½), bottom (½,1), left (0,½).
                sx = 0.5f + 0.5f * u - 0.5f * v;
                sy = 0.5f * u + 0.5f * v;
            }
            int x = Math.Min(art.Width - 1, Math.Max(0, (int)(sx * art.Width)));
            int y = Math.Min(art.Height - 1, Math.Max(0, (int)(sy * art.Height)));
            return art.Pixels[y * art.Width + x];
        }
    }

    /// <summary>A finished chunk terrain: what TerrainLandChunk uploads into TerrainData and a texture.</summary>
    public sealed class TerrainLandData
    {
        public int ChunkX0, ChunkY0, TileCount;

        /// <summary>The map's corner heights, [i, j] = corner (x0 + i, y0 + j).</summary>
        public int[,] CornerZ;

        /// <summary>Unity SetHeights layout: [row along +Z, col along +X], normalized over <see cref="Size"/>.Y.</summary>
        public float[,] Heights;

        /// <summary>Unity SetHoles layout: true where land was diverted (solid), false for a hole.</summary>
        public bool[,] Solid;

        public byte[] Rgba;
        public int TextureSize;

        /// <summary>Terrain size in terrain-space units (tiles, Z range, tiles).</summary>
        public (float X, float Y, float Z) Size =>
            (TerrainLandBuilder.Tiles, TerrainLandBuilder.ZRange * TerrainLandBuilder.HeightUnitPerZ, TerrainLandBuilder.Tiles);

        /// <summary>The terrain's position: its local (0, 0, 0) is tile (x0, y0 + 16) at Z -128.</summary>
        public (float X, float Y, float Z) Position =>
            (ChunkX0, TerrainLandBuilder.MinZ * TerrainLandBuilder.HeightUnitPerZ, -(ChunkY0 + TerrainLandBuilder.Tiles));
    }
}
