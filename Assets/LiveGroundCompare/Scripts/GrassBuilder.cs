using System;
using System.Collections.Generic;

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
