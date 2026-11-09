using System;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>
    /// The natural terrain's materials (TerrainSplat): which ground a UO land tile is, from its tiledata name. Natural
    /// ground (grass, dirt, forest floor, jungle, sand, rock, snow) and cobblestone get real textures blended across tile
    /// edges; anything else (brick, planks, marble, tile, cave, lava, void, unnamed) keeps the classic art
    /// (<see cref="Art"/>). Index order matches the texture files (E:/UOOverhaul/tools/terrain_materials_prep.py).
    /// </summary>
    public static class TerrainMaterials
    {
        public const int Grass = 0, Dirt = 1, Forest = 2, Jungle = 3, Sand = 4, Rock = 5, Snow = 6, Cobble = 7;

        /// <summary>Keep the tile's classic art (the splat's ninth weight).</summary>
        public const int Art = 8;

        /// <summary>Real materials (texture array layers).</summary>
        public const int Count = 8;

        public static readonly string[] Names = { "grass", "dirt", "forest", "jungle", "sand", "rock", "snow", "cobble" };

        /// <summary>The material for a land tile's tiledata name ("decimated_" variants count as their base).</summary>
        public static int Classify(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.StartsWith("decimated_"))
                n = n.Substring("decimated_".Length);
            switch (n)
            {
                case "grass":
                    return Grass;
                case "dirt":
                case "embank":
                case "furrows":
                    return Dirt;
                case "forest":
                case "tree":
                case "leaves":
                    return Forest;
                case "jungle":
                    return Jungle;
                case "sand":
                case "sand stone":
                    return Sand;
                case "rock":
                    return Rock;
                case "snow":
                case "snow dirt path":
                case "snow grass path":
                case "snow stone path":
                case "snow sand path":
                    return Snow;
                // Town paving is the natural cobble (light, mossy), as the world editor paints it; its splat shader keeps
                // one stone size (a near second scale) and does not stretch it up slopes (triplanar).
                case "cobblestones":
                case "cobbleston":
                case "stone":
                case "stone moss":
                case "stone moss2":
                case "flagstone":
                case "stone_paver":
                    return Cobble;
                default:
                    return Art;
            }
        }

        /// <summary>One material per land graphic (index = graphic).</summary>
        public static byte[] Table(LandTileData[] land)
        {
            var table = new byte[land?.Length ?? 0];
            for (int i = 0; i < table.Length; i++)
                table[i] = (byte)Classify(land[i].Name);
            return table;
        }

        public static int Lookup(byte[] table, int graphic) =>
            table != null && (uint)graphic < (uint)table.Length ? table[graphic] : Art;

        /// <summary>
        /// How far (tiles) a material blends into its neighbour: natural ground wanders a long way, built surfaces
        /// (cobblestone, the classic art) keep a firm edge so streets and floors stay crisp.
        /// </summary>
        public static float Softness(int material) => material == Cobble || material == Art ? 0.22f : 0.9f;
    }

    /// <summary>
    /// A chunk's splat weights (Unity-free): per sample, how much of each material, from the map's materials around it.
    /// Samples are <see cref="SamplesPerTile"/> per tile edge; each is moved by smooth value noise before it looks at the
    /// tiles, so material borders wander instead of following the tile diamonds; a tile's material reaches past its edge
    /// as far as the softer of the two materials allows (<see cref="TerrainMaterials.Softness"/>), and only as far as the
    /// harder one where a road meets grass. The map around the chunk is read too, so there is no seam at chunk borders.
    /// Output: three RGBA images (materials 0-3, 4-7, then art in red), rows bottom-up like the chunk art texture
    /// (row 0 of the image's top half is tile y0 at the top).
    /// </summary>
    public static class SplatBuilder
    {
        public const int Tiles = TerrainLandBuilder.Tiles;
        public const int SamplesPerTile = 4;
        public const int Size = Tiles * SamplesPerTile;
        private const int Reach = 2;

        public static byte[][] Build(int x0, int y0, Func<int, int, int> materialAt)
        {
            var images = new[] { new byte[Size * Size * 4], new byte[Size * Size * 4], new byte[Size * Size * 4] };
            // The chunk's materials plus a margin, read once.
            const int m = Reach + 1, span = Tiles + 2 * m;
            var mat = new int[span, span];
            for (int i = 0; i < span; i++)
                for (int j = 0; j < span; j++)
                    mat[i, j] = materialAt(x0 + i - m, y0 + j - m);

            var w = new float[TerrainMaterials.Art + 1];
            for (int sy = 0; sy < Size; sy++)
                for (int sx = 0; sx < Size; sx++)
                {
                    float wx = x0 + (sx + 0.5f) / SamplesPerTile, wy = y0 + (sy + 0.5f) / SamplesPerTile;
                    int ownI = (int)Math.Floor(wx) - x0 + m, ownJ = (int)Math.Floor(wy) - y0 + m;
                    float ownSoft = TerrainMaterials.Softness(mat[ownI, ownJ]);
                    // Wander: two octaves of value noise, scaled by how soft the ground here is.
                    float amp = Math.Min(1f, ownSoft / 0.9f);
                    float px = wx + amp * (0.42f * Signed(Noise(wx * 0.85f, wy * 0.85f, 11)) + 0.18f * Signed(Noise(wx * 2.3f, wy * 2.3f, 23)));
                    float py = wy + amp * (0.42f * Signed(Noise(wx * 0.85f, wy * 0.85f, 37)) + 0.18f * Signed(Noise(wx * 2.3f, wy * 2.3f, 41)));

                    Array.Clear(w, 0, w.Length);
                    int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
                    for (int tx = cx - Reach; tx <= cx + Reach; tx++)
                        for (int ty = cy - Reach; ty <= cy + Reach; ty++)
                        {
                            int i = tx - x0 + m, j = ty - y0 + m;
                            if (i < 0 || j < 0 || i >= span || j >= span)
                                continue;
                            int k = mat[i, j];
                            float soft = Math.Min(TerrainMaterials.Softness(k), ownSoft);
                            float dx = Math.Max(0f, Math.Max(tx - px, px - (tx + 1))), dy = Math.Max(0f, Math.Max(ty - py, py - (ty + 1)));
                            float d = (float)Math.Sqrt(dx * dx + dy * dy);
                            if (d >= soft)
                                continue;
                            float f = 1f - d / soft;
                            w[k] += f * f * (3f - 2f * f); // smoothstep falloff
                        }
                    float sum = 0f;
                    foreach (float v in w)
                        sum += v;
                    if (sum <= 0f)
                    {
                        w[mat[ownI, ownJ]] = 1f;
                        sum = 1f;
                    }
                    int o = ((Size - 1 - sy) * Size + sx) * 4;
                    for (int k = 0; k <= TerrainMaterials.Art; k++)
                    {
                        byte b = (byte)Math.Round(255f * w[k] / sum);
                        images[k / 4][o + k % 4] = b;
                    }
                }
            return images;
        }

        private static float Signed(float n) => n * 2f - 1f;

        /// <summary>Smooth value noise in [0, 1], deterministic per world position and seed (the same on every machine).</summary>
        public static float Noise(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
