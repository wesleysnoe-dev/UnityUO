using System;
using System.Collections.Generic;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// Grass that stands up out of the natural terrain (-grass, with -terrainstyle natural): thin blades scattered per chunk
    /// on the worker wherever the ground is painted grass or jungle (a little on forest floor), as thick as the paint,
    /// standing on the terrain's own heights. Unity-free (built on the worker, tested headless); TerrainGrass uploads it,
    /// and the shader (Shaders/Resources/LiveGroundCompareGrass.shader) sways the tips in rolling wind and lights them as
    /// the terrain (time of day, sun mask, the player's light, ghost); the terrain camera draws them right after the
    /// terrain, so hills hide the grass behind them. Blades are generated, not textured; their colour is the grass
    /// material's own (its mean albedo and UO tint). The world editor's in-place repaint does not move them; the rebuild a
    /// moment after a stroke does.
    /// </summary>
    public static class GrassBuilder
    {
        public static readonly bool Enabled = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-grass", StringComparison.OrdinalIgnoreCase));

        public const string ShaderResource = "LiveGroundCompareGrass";

        /// <summary>Blade candidates per tile at full grass weight.</summary>
        public const int BladesPerTile = 56;

        /// <summary>How far below and above the land a floor, step or wall still keeps the grass off its tile (UO Z).</summary>
        public const int CoverBelow = 6, CoverAbove = 24;

        /// <summary>
        /// Whether a static or house part at <paramref name="z"/> keeps grass off its tile: something to walk on (floors,
        /// foundations, steps, bridges) or a wall, at ground level over land at <paramref name="landZ"/>. Plants, water and
        /// anything high above (an upper floor, a roof) leave it be.
        /// </summary>
        public static bool Covers(in StaticTileData td, int z, int landZ) =>
            (td.Flags & (TileFlags.Surface | TileFlags.Wall | TileFlags.Bridge)) != 0
            && (td.Flags & (TileFlags.Foliage | TileFlags.Wet)) == 0
            && z >= landZ - CoverBelow && z <= landZ + CoverAbove;

        /// <summary>Worker: the chunk's tiles [i, j] (x0 + i, y0 + j) a map static covers (<see cref="Covers"/>); null without a map.</summary>
        public static bool[,] CoverFromMap(MapFile map, TileData tileData, int x0, int y0)
        {
            if (map == null || tileData == null)
                return null;
            const int t = TerrainLandBuilder.Tiles, b = MapFile.BlockSize;
            var cover = new bool[t, t];
            for (int by = y0 / b; by < (y0 + t) / b; by++)
                for (int bx = x0 / b; bx < (x0 + t) / b; bx++)
                {
                    StaticCell[] cells = map.ReadStaticBlock(bx, by);
                    if (cells == null)
                        continue;
                    foreach (StaticCell c in cells)
                    {
                        int x = bx * b + c.X, y = by * b + c.Y, i = x - x0, j = y - y0;
                        if ((uint)i >= t || (uint)j >= t || cover[i, j])
                            continue;
                        cover[i, j] = Covers(tileData.GetStatic(c.Graphic), c.Z, map.GetLand(x, y).Z);
                    }
                }
            return cover;
        }

        // TerrainMaterials order (WorldMaterials): grass 0, dirt 1, forest 2, jungle 3 in the first splat texture.
        private const float ForestShare = 0.3f, JungleShare = 1.15f;

        /// <summary>Worker: the chunk's blades, or null with no grass in it.</summary>
        public static GrassData Build(TerrainLandData d)
        {
            if (d?.Splat == null || d.Heights == null || d.Splat.Length < 1)
                return null;
            const int tiles = TerrainLandBuilder.Tiles, size = SplatBuilder.Size;
            int sp = size / tiles; // splat samples per tile
            byte[] w0 = d.Splat[0];
            byte[] wArt = d.Splat.Length > 2 ? d.Splat[2] : null;
            int n = d.HeightmapResolution;
            int hp = (n - 1) / tiles; // heightmap samples per tile
            var (_, posY, _) = d.Position;
            var (_, sizeY, _) = d.Size;

            var data = new GrassData(tiles * tiles * BladesPerTile / 2);
            for (int ty = 0; ty < tiles; ty++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    // Holes (no natural ground here): rows run along +Z like the heights, i.e. tile y descending.
                    if (d.Solid != null)
                    {
                        int rows = d.Solid.GetLength(0), cols = d.Solid.GetLength(1);
                        int row = Math.Min(rows - 1, (tiles - 1 - ty) * rows / tiles), col = Math.Min(cols - 1, tx * cols / tiles);
                        if (!d.Solid[row, col])
                            continue;
                    }
                    if (d.GrassCovered != null && d.GrassCovered[tx, ty])
                        continue; // a floor, step or wall stands here
                    int wx = d.ChunkX0 + tx, wy = d.ChunkY0 + ty;
                    for (int b = 0; b < BladesPerTile; b++)
                    {
                        uint h = Hash(wx, wy, b);
                        float fx = (h & 0xFFFF) / 65536f, fy = (h >> 16) / 65536f;
                        float lx = tx + fx, ly = ty + fy; // chunk-local tile units
                        int sx = Math.Min(size - 1, (int)(lx * sp)), sy = Math.Min(size - 1, (int)(ly * sp));
                        int o = ((size - 1 - sy) * size + sx) * 4; // splat rows bottom-up (PaintedSplat / SplatBuilder)
                        float grass = w0[o] / 255f, forest = w0[o + 2] / 255f, jungle = w0[o + 3] / 255f;
                        float art = wArt != null ? wArt[o] / 255f : 0f;
                        float density = Math.Min(1f, grass + forest * ForestShare + jungle * JungleShare) * (1f - art);
                        uint h2 = Hash(wx * 7 + 3, wy * 13 + 5, b);
                        if ((h2 & 0xFFFF) / 65536f >= density)
                            continue;
                        float y = posY + Height(d.Heights, n, hp, lx, ly) * sizeY;
                        data.AddBlade(d.ChunkX0 + lx, y, -(d.ChunkY0 + ly), h2, jungle > grass, lx / tiles, 1f - ly / tiles);
                    }
                }
            return data.Count == 0 ? null : data;
        }

        /// <summary>
        /// Worker: each vertex's world-screen pixel (at the art scale) and depth, so the grass is drawn by the MAIN camera
        /// among the sprites: a blade takes its tile's land depth (TileDepth, relative to the chunk's base diagonal) a hair
        /// nearer, as a static on that tile would sort. A blade on the row of tiles in front of a character then covers its
        /// feet; blades on the character's own tile and behind stay behind it; hills in front (the depth proxy) hide grass.
        /// Blades on a tile with no land depth are dropped.
        /// </summary>
        public static void Project(GrassData g, float[,] tileDepth, int x0, int y0, int scale)
        {
            if (g == null || tileDepth == null)
                return;
            int n = g.VertexCount;
            g.ScreenX = new float[n];
            g.ScreenY = new float[n];
            g.Depth = new float[n];
            g.Scale = scale;
            int ti = tileDepth.GetLength(0), tj = tileDepth.GetLength(1);
            for (int b = 0; b < g.Count; b++)
            {
                int root = b * 3;
                // Where it was planted: the middle of its two root corners.
                float rx = (g.Positions[root * 3] + g.Positions[(root + 1) * 3]) * 0.5f;
                float rz = (g.Positions[root * 3 + 2] + g.Positions[(root + 1) * 3 + 2]) * 0.5f;
                int i = (int)Math.Floor(rx) - x0, j = (int)Math.Floor(-rz) - y0;
                float depth = i >= 0 && j >= 0 && i < ti && j < tj ? tileDepth[i, j] : float.NaN;
                // A character stands at its tile's centre: grass in the near half of the tile (in front of that centre on
                // screen) sorts one diagonal nearer, so it covers the feet of whoever stands on this tile.
                float within = (rx - (float)Math.Floor(rx)) + (-rz - (float)Math.Floor(-rz));
                if (within > 1f)
                    depth -= DiagonalDepth;
                for (int v = root; v < root + 3; v++)
                {
                    (g.ScreenX[v], g.ScreenY[v]) = TerrainLandBuilder.ToWorldScreen(g.Positions[v * 3], g.Positions[v * 3 + 1], g.Positions[v * 3 + 2], scale);
                    g.Depth[v] = depth - NearerSteps * DepthStep;
                }
            }
        }

        /// <summary>How much nearer than its tile's land a blade sorts (DepthKey steps; a static on the tile is 8 nearer).</summary>
        public const int NearerSteps = 2;

        /// <summary>UOClient.Unity.DepthKey.Step (not referenced: this file is Unity-free).</summary>
        public const float DepthStep = 0.005f;

        /// <summary>One diagonal of depth: DepthKey.PerDiagonal (256 * 3 * 8) * Step.</summary>
        public const float DiagonalDepth = 256 * 3 * 8 * DepthStep;

        /// <summary>Bilinear heightmap value (0..1) at chunk-local tile units; rows run along +Z (tile y descending).</summary>
        private static float Height(float[,] heights, int n, int hp, float lx, float ly)
        {
            float cx = lx * hp, cy = (TerrainLandBuilder.Tiles - ly) * hp;
            int c0 = Math.Max(0, Math.Min(n - 2, (int)cx)), r0 = Math.Max(0, Math.Min(n - 2, (int)cy));
            float u = Math.Max(0f, Math.Min(1f, cx - c0)), v = Math.Max(0f, Math.Min(1f, cy - r0));
            float a = heights[r0, c0] + (heights[r0, c0 + 1] - heights[r0, c0]) * u;
            float b = heights[r0 + 1, c0] + (heights[r0 + 1, c0 + 1] - heights[r0 + 1, c0]) * u;
            return a + (b - a) * v;
        }

        private static uint Hash(int x, int y, int k)
        {
            unchecked
            {
                uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)k * 0xCB1AB31Fu;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }
    }

    /// <summary>One chunk's blades (built on the worker): a triangle per blade in terrain space, as flat arrays.</summary>
    public sealed class GrassData
    {
        /// <summary>x, y, z per vertex (terrain space).</summary>
        public readonly List<float> Positions;

        /// <summary>r, g, b, a per vertex: blade tint; a 255 lush (jungle).</summary>
        public readonly List<byte> Colors;

        /// <summary>Per vertex: 0 root .. 1 tip (sway and gradient), the blade's seed, then the terrain's UV (sun mask).</summary>
        public readonly List<float> Uv, ChunkUv;

        /// <summary>Per vertex, from <see cref="GrassBuilder.Project"/>: world-screen pixels and the main camera's depth (NaN: drop).</summary>
        public float[] ScreenX, ScreenY, Depth;

        /// <summary>The art scale the pixels were projected at.</summary>
        public int Scale = 1;

        public GrassData(int capacity)
        {
            Positions = new List<float>(capacity * 9);
            Colors = new List<byte>(capacity * 12);
            Uv = new List<float>(capacity * 6);
            ChunkUv = new List<float>(capacity * 6);
        }

        public int VertexCount => Positions.Count / 3;

        public int Count => VertexCount / 3;

        public void AddBlade(float x, float y, float z, uint seed, bool lush, float u, float v)
        {
            float r1 = (seed & 0xFF) / 255f, r2 = ((seed >> 8) & 0xFF) / 255f, r3 = ((seed >> 16) & 0xFF) / 255f, r4 = (seed >> 24) / 255f;
            // Height in terrain units (1 tile = 1, 1 UO Z = 0.1286): about 2.5 to 4.5 Z, lush a third taller.
            float height = (0.32f + 0.26f * r1) * (lush ? 1.35f : 1f);
            float width = 0.045f + 0.03f * r2;
            // Facing: mostly across the screen (terrain (1, 0, 1)), so blades never turn edge-on to the UO camera.
            double angle = Math.PI * 0.25 + (r3 - 0.5) * 1.3;
            float dx = (float)Math.Cos(angle) * width * 0.5f, dz = (float)Math.Sin(angle) * width * 0.5f;
            // A lean at rest, any direction.
            double leanAngle = r3 * Math.PI * 2.0;
            float lean = 0.12f * r4;
            float tx = x + (float)Math.Cos(leanAngle) * lean, tz = z + (float)Math.Sin(leanAngle) * lean;
            byte cr = (byte)(200 + 55 * r2), cg = (byte)(205 + 50 * r1), cb = (byte)(180 + 75 * r4), ca = (byte)(lush ? 255 : 0);

            Vertex(x - dx, y - 0.02f, z - dz, 0f, r1, u, v, cr, cg, cb, ca);
            Vertex(x + dx, y - 0.02f, z + dz, 0f, r1, u, v, cr, cg, cb, ca);
            Vertex(tx, y + height, tz, 1f, r1, u, v, cr, cg, cb, ca);
        }

        private void Vertex(float x, float y, float z, float tip, float seed, float u, float v, byte r, byte g, byte b, byte a)
        {
            Positions.Add(x);
            Positions.Add(y);
            Positions.Add(z);
            Uv.Add(tip);
            Uv.Add(seed);
            ChunkUv.Add(u);
            ChunkUv.Add(v);
            Colors.Add(r);
            Colors.Add(g);
            Colors.Add(b);
            Colors.Add(a);
        }
    }
}
