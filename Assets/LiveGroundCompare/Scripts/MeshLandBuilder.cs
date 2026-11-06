using System;
using System.Collections.Generic;
using UOClient.ArtPipeline;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own (the Unity upload is in LiveGroundController).
    /// <summary>
    /// Builds land as a real triangle mesh with the production compositor's exact geometry: each tile's four
    /// corners from <see cref="LiveGroundProjection.Corner"/> (flat at Z unless the compositor stretches it) split
    /// RIGHT–LEFT into top-right-left and right-bottom-left, every vertex at the tile's production sort depth, so
    /// statics, mobiles and roofs sort against it exactly as against the original land sprites. Texmaps map
    /// top (0,0) right (1,0) bottom (1,1) left (0,1); flat tiles map their 44x44 diamond sprite. Faces carry the
    /// compositor's flat shade. Vertices are not shared (per-face shade and per-tile art).
    /// </summary>
    public sealed class MeshLandBuilder
    {
        public const int AtlasWidth = 1024;
        private const int Gutter = 1;

        private readonly int _scale;
        private readonly List<float> _x = new List<float>(), _y = new List<float>(), _depth = new List<float>();
        private readonly List<float> _artU = new List<float>(), _artV = new List<float>(), _shade = new List<float>();
        private readonly List<int> _artOfVertex = new List<int>();
        private readonly List<Bitmap32> _arts = new List<Bitmap32>();
        private readonly Dictionary<Bitmap32, int> _artIndex = new Dictionary<Bitmap32, int>();

        public MeshLandBuilder(int scale)
        {
            if (scale < 1)
                throw new ArgumentOutOfRangeException(nameof(scale));
            _scale = scale;
        }

        public int TileCount { get; private set; }

        /// <param name="it">A land item as IsoCompositor.BuildDrawList made it.</param>
        /// <param name="art">The texmap when <paramref name="texmap"/>, else the flat land sprite.</param>
        /// <param name="depth">The tile's production depth (DepthKey.Relative); every vertex of the tile gets it.</param>
        public void AddTile(in DrawItem it, Bitmap32 art, bool texmap, float depth)
        {
            if (it.Kind != DrawKind.Land)
                throw new ArgumentException("only land goes through the land mesh", nameof(it));
            if (art == null)
                return;
            if (!_artIndex.TryGetValue(art, out int a))
            {
                _artIndex[art] = a = _arts.Count;
                _arts.Add(art);
            }

            Span<int> z = stackalloc int[4];
            LiveGroundProjection.DrawnCorners(it, z);
            float[] us = texmap ? LiveGroundProjection.TexmapU : LiveGroundProjection.SpriteU;
            float[] vs = texmap ? LiveGroundProjection.TexmapV : LiveGroundProjection.SpriteV;
            for (int t = 0; t < 2; t++)
            {
                // Flat sprites are already lit art; only texmapped (stretched) faces get the compositor's shade.
                float shade = texmap ? LiveGroundProjection.TriangleShade(z, t) : 1f;
                for (int k = 0; k < 3; k++)
                {
                    int c = LiveGroundProjection.Triangles[t * 3 + k];
                    (float px, float py) = LiveGroundProjection.Corner(it.X, it.Y, c, z[c], _scale);
                    _x.Add(px);
                    _y.Add(py);
                    _depth.Add(depth);
                    _artU.Add(us[c]);
                    _artV.Add(vs[c]);
                    _shade.Add(shade);
                    _artOfVertex.Add(a);
                }
            }
            TileCount++;
        }

        /// <summary>Packs the arts into one RGBA32 atlas (rows bottom-up, as Unity textures) and resolves the UVs.</summary>
        public LandMeshData Finish()
        {
            var data = new LandMeshData { VertexCount = _x.Count };
            if (_x.Count == 0)
                return data;

            // Shelf-pack with a replicated 1px gutter so point sampling at a UV edge never reads a neighbor.
            var placed = new (int X, int Y)[_arts.Count];
            int x = 0, y = 0, shelf = 0;
            for (int i = 0; i < _arts.Count; i++)
            {
                int w = _arts[i].Width + 2 * Gutter, h = _arts[i].Height + 2 * Gutter;
                if (w > AtlasWidth)
                    throw new InvalidOperationException($"land art {_arts[i].Width} px wide does not fit the {AtlasWidth} px atlas");
                if (x + w > AtlasWidth)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                placed[i] = (x + Gutter, y + Gutter);
                x += w;
                shelf = Math.Max(shelf, h);
            }
            int height = Math.Max(1, y + shelf);
            height = (height + 63) / 64 * 64;

            byte[] rgba = new byte[AtlasWidth * height * 4];
            for (int i = 0; i < _arts.Count; i++)
                Blit(_arts[i], rgba, height, placed[i].X, placed[i].Y);

            int n = _x.Count;
            data.X = _x.ToArray();
            data.Y = _y.ToArray();
            data.Depth = _depth.ToArray();
            data.Shade = _shade.ToArray();
            data.U = new float[n];
            data.V = new float[n];
            for (int v = 0; v < n; v++)
            {
                Bitmap32 art = _arts[_artOfVertex[v]];
                (int px, int py) = placed[_artOfVertex[v]];
                data.U[v] = (px + _artU[v] * art.Width) / AtlasWidth;
                data.V[v] = 1f - (py + _artV[v] * art.Height) / height;
            }
            data.Indices = new int[n];
            for (int i = 0; i < n; i++)
                data.Indices[i] = i;
            data.AtlasRgba = rgba;
            data.AtlasWidth = AtlasWidth;
            data.AtlasHeight = height;
            return data;
        }

        /// <summary>Copies an ARGB bitmap into the bottom-up RGBA atlas at (left, top) image coordinates, gutter replicated.</summary>
        private static void Blit(Bitmap32 b, byte[] rgba, int atlasHeight, int left, int top)
        {
            for (int row = -Gutter; row < b.Height + Gutter; row++)
            {
                int sy = Math.Min(b.Height - 1, Math.Max(0, row));
                int dstRow = atlasHeight - 1 - (top + row);
                for (int col = -Gutter; col < b.Width + Gutter; col++)
                {
                    int sx = Math.Min(b.Width - 1, Math.Max(0, col));
                    uint p = b.Pixels[sy * b.Width + sx];
                    int o = (dstRow * AtlasWidth + left + col) * 4;
                    rgba[o] = (byte)(p >> 16);
                    rgba[o + 1] = (byte)(p >> 8);
                    rgba[o + 2] = (byte)p;
                    rgba[o + 3] = (byte)(p >> 24);
                }
            }
        }
    }

    /// <summary>A finished land mesh: per-vertex world-screen position (output pixels, y down), depth, atlas UV and shade.</summary>
    public sealed class LandMeshData
    {
        public int VertexCount;
        public float[] X, Y, Depth, U, V, Shade;
        public int[] Indices;
        public byte[] AtlasRgba;
        public int AtlasWidth, AtlasHeight;
    }
}
