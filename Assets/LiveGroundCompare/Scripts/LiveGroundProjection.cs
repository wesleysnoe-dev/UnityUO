using System;
using UOClient.ArtPipeline;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>
    /// UO's land projection as the production compositor uses it (IsoCompositor.PlacementRect / DrawStretchedLand):
    /// 44px diamonds (TileHalf 22), 4px per Z, positions in UO pixels times the art scale. A tile's four corners are
    /// top (x, y), right (x + 1, y), bottom (x + 1, y + 1) and left (x, y + 1); a sloped tile is split along the
    /// RIGHT–LEFT diagonal into the triangles top-right-left and right-bottom-left.
    /// </summary>
    public static class LiveGroundProjection
    {
        public const int TileHalf = 22;
        public const int ZStep = 4;

        /// <summary>Corner order used by every array here.</summary>
        public const int Top = 0, Right = 1, Bottom = 2, Left = 3;

        /// <summary>The two triangles (corner indices) of a tile, in IsoCompositor.DrawStretchedLand's order.</summary>
        public static readonly int[] Triangles = { Top, Right, Left, Right, Bottom, Left };

        /// <summary>Texmap UVs per corner (u along +x, v along +y; image rows from the top), as DrawStretchedLand.</summary>
        public static readonly float[] TexmapU = { 0f, 1f, 1f, 0f };
        public static readonly float[] TexmapV = { 0f, 0f, 1f, 1f };

        /// <summary>A flat land sprite's diamond corners as fractions of the sprite (44x44 at 1x).</summary>
        public static readonly float[] SpriteU = { 0.5f, 1f, 0.5f, 0f };
        public static readonly float[] SpriteV = { 0f, 0.5f, 1f, 0.5f };

        /// <summary>World-screen position (output pixels at <paramref name="scale"/>, y down) of a tile corner.</summary>
        public static (float X, float Y) Corner(int x, int y, int corner, int z, int scale)
        {
            float ax = (x - y) * TileHalf;
            float ay = (x + y) * TileHalf;
            switch (corner)
            {
                case Top: return (ax * scale, (ay - z * ZStep) * scale);
                case Right: return ((ax + TileHalf) * scale, (ay + TileHalf - z * ZStep) * scale);
                case Bottom: return (ax * scale, (ay + 2 * TileHalf - z * ZStep) * scale);
                case Left: return ((ax - TileHalf) * scale, (ay + TileHalf - z * ZStep) * scale);
                default: throw new ArgumentOutOfRangeException(nameof(corner));
            }
        }

        /// <summary>The corner heights the compositor draws a land item with: its own corners when stretched, else flat at Z.</summary>
        public static void DrawnCorners(in DrawItem it, Span<int> z)
        {
            z[Top] = it.Z;
            z[Right] = it.Stretched ? it.ZRight : it.Z;
            z[Bottom] = it.Stretched ? it.ZBottom : it.Z;
            z[Left] = it.Stretched ? it.ZLeft : it.Z;
        }

        /// <summary>
        /// Height inside a tile at (u, v) in [0,1]² (u along +x, v along +y), on the same RIGHT–LEFT split as the
        /// compositor: triangle top-right-left where u + v ≤ 1, right-bottom-left beyond.
        /// </summary>
        public static float HeightAt(int zTop, int zRight, int zBottom, int zLeft, float u, float v)
        {
            if (u + v <= 1f)
                return zTop + u * (zRight - zTop) + v * (zLeft - zTop);
            return zBottom + (1f - v) * (zRight - zBottom) + (1f - u) * (zLeft - zBottom);
        }

        /// <summary>The classic client's average Z (along the flatter diagonal), as IsoCompositor.CollectItems.</summary>
        public static int AverageZ(int zTop, int zRight, int zBottom, int zLeft) =>
            Math.Abs(zTop - zBottom) <= Math.Abs(zLeft - zRight) ? (zTop + zBottom) >> 1 : (zLeft + zRight) >> 1;

        /// <summary>
        /// Brightness of a face relative to flat ground, lit from the screen's upper left. Same formula as the
        /// compositor's private IsoCompositor.Shade (U/V double as world coordinates, heights in tile-edge units).
        /// </summary>
        public static float Shade(int za, float ua, float va, int zb, float ub, float vb, int zc, float uc, float vc)
        {
            const float toEdge = ZStep / (TileHalf * 1.41421356f);
            float e1x = ub - ua, e1y = vb - va, e1z = (zb - za) * toEdge;
            float e2x = uc - ua, e2y = vc - va, e2z = (zc - za) * toEdge;
            float nx = e1y * e2z - e1z * e2y;
            float ny = e1z * e2x - e1x * e2z;
            float nz = e1x * e2y - e1y * e2x;
            if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }

            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f)
                return 1f;

            const float lx = -0.6f, ly = -0.2f, lz = 0.77f;
            float dot = (nx * lx + ny * ly + nz * lz) / len;
            return Math.Max(0.7f, Math.Min(1.25f, dot / lz));
        }

        /// <summary>Shade of triangle <paramref name="triangle"/> (0 = top-right-left, 1 = right-bottom-left) of a tile.</summary>
        public static float TriangleShade(ReadOnlySpan<int> z, int triangle)
        {
            int a = Triangles[triangle * 3], b = Triangles[triangle * 3 + 1], c = Triangles[triangle * 3 + 2];
            return Shade(z[a], TexmapU[a], TexmapV[a], z[b], TexmapU[b], TexmapV[b], z[c], TexmapU[c], TexmapV[c]);
        }

        /// <summary>
        /// The land placement the compositor would make for a tile with these corners (Stretched when sloped and a
        /// texmap exists), so tests and builders can reproduce its geometry and sort key without the map files.
        /// </summary>
        public static DrawItem LandItem(int x, int y, ushort graphic, int zTop, int zRight, int zBottom, int zLeft, bool hasTexmap)
        {
            bool sloped = zTop != zRight || zTop != zBottom || zTop != zLeft;
            bool stretched = sloped && hasTexmap;
            return new DrawItem
            {
                Kind = DrawKind.Land,
                Graphic = graphic,
                X = x,
                Y = y,
                Z = (sbyte)zTop,
                PriorityZ = (stretched ? AverageZ(zTop, zRight, zBottom, zLeft) : zTop) - 1,
                Stretched = stretched,
                ZRight = (sbyte)zRight,
                ZBottom = (sbyte)zBottom,
                ZLeft = (sbyte)zLeft
            };
        }
    }
}
