using System;
using System.Collections.Generic;
using UOClient.IO.Formats;

namespace UOClient.LiveGroundCompare
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>
    /// A bounded, read-only copy of real map land: tile graphics and the corner-height grid (one more row and
    /// column than tiles, since a tile's right/bottom/left corners are its neighbors' heights). Out-of-map corners
    /// clamp to the edge exactly like IsoCompositor.GetLandZ. Never writes anything.
    /// </summary>
    public sealed class RealMapPatch
    {
        /// <summary>Largest patch side in tiles: comparisons stay a bounded slice, not a facet.</summary>
        public const int MaxSide = 256;

        private readonly ushort[] _graphics;
        private readonly sbyte[] _corners;

        public int Map { get; }
        public int X0 { get; }
        public int Y0 { get; }
        public int Width { get; }
        public int Height { get; }

        /// <param name="landAt">The land cell at a map tile (coordinates already clamped into the map).</param>
        public RealMapPatch(int map, int x0, int y0, int width, int height, int mapWidth, int mapHeight, Func<int, int, LandCell> landAt)
        {
            if (landAt == null)
                throw new ArgumentNullException(nameof(landAt));
            if (width < 1 || height < 1 || width > MaxSide || height > MaxSide)
                throw new ArgumentOutOfRangeException(nameof(width), $"patch must be 1..{MaxSide} tiles a side, got {width}x{height}");
            if (mapWidth < 1 || mapHeight < 1 || x0 < 0 || y0 < 0 || x0 + width > mapWidth || y0 + height > mapHeight)
                throw new ArgumentOutOfRangeException(nameof(x0), $"patch {x0},{y0} {width}x{height} is outside the {mapWidth}x{mapHeight} map");

            Map = map;
            X0 = x0;
            Y0 = y0;
            Width = width;
            Height = height;
            _graphics = new ushort[width * height];
            _corners = new sbyte[(width + 1) * (height + 1)];

            for (int cy = 0; cy <= height; cy++)
                for (int cx = 0; cx <= width; cx++)
                {
                    int mx = Math.Min(x0 + cx, mapWidth - 1), my = Math.Min(y0 + cy, mapHeight - 1);
                    LandCell cell = landAt(mx, my);
                    _corners[cy * (width + 1) + cx] = cell.Z;
                    if (cx < width && cy < height)
                        _graphics[cy * width + cx] = cell.Graphic;
                }
        }

        /// <summary>Reads a patch from a facet's land (block reads cached for the copy only).</summary>
        public static RealMapPatch FromMap(MapFile map, int x0, int y0, int width, int height)
        {
            var blocks = new Dictionary<long, LandCell[]>();
            return new RealMapPatch(map.Index, x0, y0, width, height, map.Width, map.Height, (x, y) =>
            {
                long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
                if (!blocks.TryGetValue(key, out LandCell[] cells))
                    blocks[key] = cells = map.ReadLandBlock(x >> 3, y >> 3);
                return cells[((y & 7) << 3) | (x & 7)];
            });
        }

        /// <summary>Corner height at patch-relative corner (cx, cy), 0..Width / 0..Height.</summary>
        public sbyte CornerZ(int cx, int cy)
        {
            if ((uint)cx > (uint)Width || (uint)cy > (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(cx));
            return _corners[cy * (Width + 1) + cx];
        }

        public ushort Graphic(int tx, int ty)
        {
            if ((uint)tx >= (uint)Width || (uint)ty >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(tx));
            return _graphics[ty * Width + tx];
        }

        /// <summary>A tile's corners (top, right, bottom, left) at patch-relative tile (tx, ty).</summary>
        public (sbyte Top, sbyte Right, sbyte Bottom, sbyte Left) Corners(int tx, int ty)
        {
            if ((uint)tx >= (uint)Width || (uint)ty >= (uint)Height)
                throw new ArgumentOutOfRangeException(nameof(tx));
            return (CornerZ(tx, ty), CornerZ(tx + 1, ty), CornerZ(tx + 1, ty + 1), CornerZ(tx, ty + 1));
        }

        /// <summary>Ground height at a fractional map position inside the patch, on the compositor's RIGHT–LEFT split.</summary>
        public float HeightAt(float mapX, float mapY)
        {
            float px = mapX - X0, py = mapY - Y0;
            if (px < 0 || py < 0 || px > Width || py > Height)
                throw new ArgumentOutOfRangeException(nameof(mapX));
            int tx = Math.Min((int)px, Width - 1), ty = Math.Min((int)py, Height - 1);
            (sbyte t, sbyte r, sbyte b, sbyte l) = Corners(tx, ty);
            return LiveGroundProjection.HeightAt(t, r, b, l, px - tx, py - ty);
        }
    }
}
