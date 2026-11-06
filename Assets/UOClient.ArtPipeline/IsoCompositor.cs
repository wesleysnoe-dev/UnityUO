using System;
using System.Collections.Generic;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    public enum DrawKind : byte
    {
        Land = 0,
        Static = 1,

        /// <summary>Characters and creatures; never produced from map data, used by renderers to sort mobiles in.</summary>
        Mobile = 2
    }

    public struct DrawItem
    {
        public DrawKind Kind;
        public ushort Graphic;
        public int X;
        public int Y;
        public sbyte Z;
        public ushort Hue;
        public int PriorityZ;
        public int Sequence;

        /// <summary>
        /// Disambiguates identical statics (same tile, z and graphic) in map order. Together with
        /// Kind/X/Y/Z/Graphic it identifies a placement across passes; see <see cref="PlacementKey"/>.
        /// </summary>
        public byte Ordinal;

        public PlacementKey Key => new PlacementKey(Kind, X, Y, Z, Graphic, Ordinal);

        /// <summary>Land drawn as a texmap quad because its corners differ in height. Z is the top corner.</summary>
        public bool Stretched;
        public sbyte ZRight;
        public sbyte ZBottom;
        public sbyte ZLeft;

        /// <summary>Top-left of the sprite in composite image pixels (already multiplied by the composite's scale).</summary>
        public int Left;
        public int Top;
        public int Width;
        public int Height;
    }

    /// <summary>Identity of one map placement (a land tile or one static), stable across render passes.</summary>
    public readonly struct PlacementKey : IEquatable<PlacementKey>
    {
        public readonly DrawKind Kind;
        public readonly int X;
        public readonly int Y;
        public readonly sbyte Z;
        public readonly ushort Graphic;
        public readonly byte Ordinal;

        public PlacementKey(DrawKind kind, int x, int y, sbyte z, ushort graphic, byte ordinal)
        {
            Kind = kind;
            X = x;
            Y = y;
            Z = z;
            Graphic = graphic;
            Ordinal = ordinal;
        }

        public bool Equals(PlacementKey o) =>
            Kind == o.Kind && X == o.X && Y == o.Y && Z == o.Z && Graphic == o.Graphic && Ordinal == o.Ordinal;

        public override bool Equals(object obj) => obj is PlacementKey o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = h * 397 ^ X;
                h = h * 397 ^ Y;
                h = h * 397 ^ Z;
                h = h * 397 ^ Graphic;
                return h * 397 ^ Ordinal;
            }
        }

        public override string ToString() => $"{Kind} 0x{Graphic:X4} ({X},{Y},{Z})#{Ordinal}";
    }

    /// <summary>A rendered region plus, per pixel, the index of the <see cref="DrawItem"/> that drew it (-1 = empty).</summary>
    public sealed class Composite
    {
        public Composite(Bitmap32 image, int[] owner, List<DrawItem> items)
        {
            Image = image;
            Owner = owner;
            Items = items;
        }

        public Bitmap32 Image { get; }
        public int[] Owner { get; }
        public List<DrawItem> Items { get; }

        public int Map { get; set; }
        public int RegionX { get; set; }
        public int RegionY { get; set; }
        public int RegionWidth { get; set; }
        public int RegionHeight { get; set; }

        /// <summary>Pixels per original UO pixel. 1 for composites sent to ComfyUI.</summary>
        public int Scale { get; set; } = 1;

        /// <summary>World-screen position (at <see cref="Scale"/>) of the image's top-left pixel.</summary>
        public int OriginX { get; set; }
        public int OriginY { get; set; }
    }

    /// <summary>
    /// CPU isometric renderer for a rectangular map region, using UO's projection
    /// (44px diamonds, 4px per Z) and classic-client draw ordering. It is the reference
    /// the Unity renderer is checked against, and it produces the composites and ownership
    /// maps for the ComfyUI restyle-and-slice pipeline.
    /// Not yet handled: hues, animated/translucent art, multis.
    /// </summary>
    public sealed class IsoCompositor
    {
        private const int TileHalf = 22;
        private const int ZStep = 4;

        private readonly UOData _data;
        private readonly Dictionary<int, Bitmap32> _landCache = new Dictionary<int, Bitmap32>();
        private readonly Dictionary<int, Bitmap32> _staticCache = new Dictionary<int, Bitmap32>();
        private readonly Dictionary<int, Bitmap32> _texmapCache = new Dictionary<int, Bitmap32>();
        private readonly Dictionary<long, LandCell[]> _blockCache = new Dictionary<long, LandCell[]>();
        private MapFile _map;
        private int _scale = 1;
        private Func<DrawKind, int, Bitmap32> _spriteOverride;

        public IsoCompositor(UOData data)
        {
            _data = data;
        }

        /// <summary>Debug: draw every land tile as a texmap quad, to check texture orientation against the flat art.</summary>
        public bool StretchAllLand { get; set; }

        /// <summary>
        /// Leave out statics flagged Roof, as the client does when the player is indoors. A roofless
        /// pass exposes interiors (floors, inner walls, furniture) that a rooftop view never shows.
        /// </summary>
        public bool HideRoofs { get; set; }

        /// <summary>Output pixels per UO pixel. Original art is upscaled nearest-neighbor unless overridden.</summary>
        public int Scale
        {
            get => _scale;
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _scale = value;
                ClearSpriteCaches();
            }
        }

        /// <summary>
        /// Supplies replacement art (e.g. sliced HD sprites) already at <see cref="Scale"/>, i.e. original size × Scale.
        /// Return null to use the original art.
        /// </summary>
        public Func<DrawKind, int, Bitmap32> SpriteOverride
        {
            get => _spriteOverride;
            set
            {
                _spriteOverride = value;
                ClearSpriteCaches();
            }
        }

        /// <summary>One placement ready to draw: its item (rect in world-screen pixels at <see cref="Scale"/>) and art.</summary>
        public struct DrawEntry
        {
            public DrawItem Item;

            /// <summary>The art: a sprite, a baked placement, or for unbaked stretched land the texmap to rasterize. Null if missing.</summary>
            public Bitmap32 Sprite;

            /// <summary>Sprite came from <see cref="PlacementOverride"/> (so stretched land is a finished image, not a texmap).</summary>
            public bool Baked;
        }

        /// <summary>
        /// The region's placements in draw order, each with its art and world-screen rect. This is what a
        /// real-time renderer consumes; <see cref="Compose"/> flattens the same list into one image.
        /// </summary>
        public List<DrawEntry> BuildDrawList(int mapIndex, int x0, int y0, int width, int height)
        {
            MapFile map = _data.GetMap(mapIndex);
            if (_map != map)
            {
                _map = map;
                _blockCache.Clear();
            }

            TileData tileData = _data.TileData;
            List<DrawItem> items = CollectItems(tileData, x0, y0, width, height);
            items.Sort(CompareDrawOrder);

            int s = _scale;
            var entries = new List<DrawEntry>(items.Count);
            foreach (DrawItem item in items)
            {
                DrawItem it = item;
                Bitmap32 bmp = PlacementOverride?.Invoke(it);
                bool baked = bmp != null;
                if (bmp == null)
                {
                    bmp = it.Stretched ? GetTexmap(tileData.Land[it.Graphic & 0x3FFF].TexId)
                        : it.Kind == DrawKind.Land ? GetLand(it.Graphic)
                        : GetStatic(it.Graphic);
                }
                if (bmp != null)
                    (it.Left, it.Top, it.Width, it.Height) = PlacementRect(it, s, bmp.Width / s, bmp.Height / s);
                entries.Add(new DrawEntry { Item = it, Sprite = bmp, Baked = baked });
            }
            return entries;
        }

        public Composite Compose(int mapIndex, int x0, int y0, int width, int height)
        {
            List<DrawEntry> entries = BuildDrawList(mapIndex, x0, y0, width, height);
            int s = _scale;

            // Bounds in world-screen space, then shift everything into image space.
            var items = new List<DrawItem>(entries.Count);
            var sprites = new Bitmap32[entries.Count];
            var baked = new bool[entries.Count];
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                DrawItem it = entries[i].Item;
                items.Add(it);
                sprites[i] = entries[i].Sprite;
                baked[i] = entries[i].Baked;
                if (sprites[i] == null)
                    continue;

                minX = Math.Min(minX, it.Left);
                minY = Math.Min(minY, it.Top);
                maxX = Math.Max(maxX, it.Left + it.Width);
                maxY = Math.Max(maxY, it.Top + it.Height);
            }

            Composite result;
            if (minX == int.MaxValue)
            {
                result = new Composite(new Bitmap32(1, 1), new[] { -1 }, items);
            }
            else
            {
                var image = new Bitmap32(maxX - minX, maxY - minY);
                var owner = new int[image.Width * image.Height];
                for (int i = 0; i < owner.Length; i++)
                    owner[i] = -1;

                for (int i = 0; i < items.Count; i++)
                {
                    Bitmap32 bmp = sprites[i];
                    if (bmp == null)
                        continue;

                    DrawItem it = items[i];
                    it.Left -= minX;
                    it.Top -= minY;
                    items[i] = it;

                    if (it.Stretched && !baked[i])
                        DrawStretchedLand(bmp, image, owner, it, minX, minY, s, i);
                    else
                        Blit(bmp, image, owner, it.Left, it.Top, i);
                }

                result = new Composite(image, owner, items) { OriginX = minX, OriginY = minY };
            }

            result.Map = mapIndex;
            result.RegionX = x0;
            result.RegionY = y0;
            result.RegionWidth = width;
            result.RegionHeight = height;
            result.Scale = s;
            return result;
        }

        /// <summary>
        /// World-screen rectangle (in output pixels at <paramref name="scale"/>) of a placement.
        /// <paramref name="spriteWidth"/>/<paramref name="spriteHeight"/> are the original (1x) art size;
        /// stretched land ignores them. Positions are computed in UO pixels and multiplied by the scale,
        /// so a 2x render lines up exactly with a 2x nearest upscale of the 1x render.
        /// </summary>
        public static (int Left, int Top, int Width, int Height) PlacementRect(in DrawItem it, int scale, int spriteWidth, int spriteHeight)
        {
            int anchorX = (it.X - it.Y) * TileHalf;
            int baseY = (it.X + it.Y) * TileHalf;
            int anchorY = baseY - it.Z * ZStep;
            int s = scale;

            if (it.Stretched)
            {
                int top = Math.Min(Math.Min(baseY - it.Z * ZStep, baseY + TileHalf - it.ZRight * ZStep),
                                   Math.Min(baseY + 2 * TileHalf - it.ZBottom * ZStep, baseY + TileHalf - it.ZLeft * ZStep));
                int bottom = Math.Max(Math.Max(baseY - it.Z * ZStep, baseY + TileHalf - it.ZRight * ZStep),
                                      Math.Max(baseY + 2 * TileHalf - it.ZBottom * ZStep, baseY + TileHalf - it.ZLeft * ZStep));
                // One pixel of padding: the rasterizer's crack tolerance can reach just past the corners.
                return ((anchorX - TileHalf) * s - 1, top * s - 1, TileHalf * 2 * s + 2, (bottom - top) * s + 2);
            }

            if (it.Kind == DrawKind.Land)
                return ((anchorX - TileHalf) * s, anchorY * s, spriteWidth * s, spriteHeight * s);

            return ((anchorX - spriteWidth / 2) * s, (anchorY + TileHalf * 2 - spriteHeight) * s, spriteWidth * s, spriteHeight * s);
        }

        /// <summary>World-screen anchor (tile top corner at its Z) of a placement, in output pixels at <paramref name="scale"/>.</summary>
        public static (int X, int Y) Anchor(in DrawItem it, int scale)
        {
            return ((it.X - it.Y) * TileHalf * scale, ((it.X + it.Y) * TileHalf - it.Z * ZStep) * scale);
        }

        /// <summary>
        /// The placement's original art alone, at <see cref="Scale"/>, sized to its <see cref="PlacementRect"/>.
        /// Used to fill pixels of baked placements that no render pass ever showed.
        /// </summary>
        public Bitmap32 RenderPlacementAlone(DrawItem it)
        {
            int s = _scale;
            if (!it.Stretched)
                return it.Kind == DrawKind.Land ? GetLand(it.Graphic) : GetStatic(it.Graphic);

            Bitmap32 tex = GetTexmap(_data.TileData.Land[it.Graphic & 0x3FFF].TexId);
            if (tex == null)
                return null;
            (int left, int top, int w, int h) = PlacementRect(it, s, 0, 0);
            var bmp = new Bitmap32(w, h);
            DrawStretchedLand(tex, bmp, new int[w * h], it, left, top, s, 0);
            return bmp;
        }

        /// <summary>
        /// Per-placement art (e.g. baked HD sprites), checked before <see cref="SpriteOverride"/>.
        /// Must be sized like <see cref="PlacementRect"/> at <see cref="Scale"/>; stretched land is blitted, not rasterized.
        /// </summary>
        public Func<DrawItem, Bitmap32> PlacementOverride { get; set; }

        private List<DrawItem> CollectItems(TileData tileData, int x0, int y0, int width, int height)
        {
            var items = new List<DrawItem>();
            var ordinals = new Dictionary<(int, int, sbyte, ushort), byte>();
            int seq = 0;

            for (int by = y0 >> 3; by <= (y0 + height - 1) >> 3; by++)
            {
                for (int bx = x0 >> 3; bx <= (x0 + width - 1) >> 3; bx++)
                {
                    LandCell[] land = GetLandBlock(bx, by);
                    for (int cy = 0; cy < 8; cy++)
                    {
                        for (int cx = 0; cx < 8; cx++)
                        {
                            int x = (bx << 3) + cx, y = (by << 3) + cy;
                            if (!InRegion(x, y, x0, y0, width, height))
                                continue;

                            LandCell cell = land[(cy << 3) | cx];
                            if (IsNoDrawLand(cell.Graphic))
                                continue;

                            // Corner heights: top is this tile, right/bottom/left come from the neighbors.
                            sbyte zTop = cell.Z;
                            sbyte zRight = GetLandZ(x + 1, y);
                            sbyte zBottom = GetLandZ(x + 1, y + 1);
                            sbyte zLeft = GetLandZ(x, y + 1);
                            bool sloped = zTop != zRight || zTop != zBottom || zTop != zLeft;

                            bool stretched = (sloped || StretchAllLand)
                                && GetTexmap(tileData.Land[cell.Graphic & 0x3FFF].TexId) != null;

                            // Same choice as the classic client: average along the flatter diagonal.
                            int averageZ = Math.Abs(zTop - zBottom) <= Math.Abs(zLeft - zRight)
                                ? (zTop + zBottom) >> 1
                                : (zLeft + zRight) >> 1;

                            items.Add(new DrawItem
                            {
                                Kind = DrawKind.Land,
                                Graphic = cell.Graphic,
                                X = x,
                                Y = y,
                                Z = zTop,
                                PriorityZ = (stretched ? averageZ : zTop) - 1,
                                Sequence = seq++,
                                Stretched = stretched,
                                ZRight = zRight,
                                ZBottom = zBottom,
                                ZLeft = zLeft
                            });
                        }
                    }

                    foreach (StaticCell st in _map.ReadStaticBlock(bx, by))
                    {
                        int x = (bx << 3) + st.X, y = (by << 3) + st.Y;
                        if (!InRegion(x, y, x0, y0, width, height) || IsNoDrawStatic(st.Graphic))
                            continue;

                        // Counted before the roof filter so ordinals match between roof and roofless passes.
                        var dupKey = (x, y, st.Z, st.Graphic);
                        ordinals.TryGetValue(dupKey, out byte ordinal);
                        ordinals[dupKey] = (byte)Math.Min(255, ordinal + 1);

                        StaticTileData td = tileData.GetStatic(st.Graphic);
                        if (HideRoofs && td.IsRoof)
                            continue;

                        int priorityZ = st.Z;
                        if (td.IsBackground)
                            priorityZ--;
                        if (td.Height > 0)
                            priorityZ++;

                        items.Add(new DrawItem
                        {
                            Kind = DrawKind.Static,
                            Graphic = st.Graphic,
                            X = x,
                            Y = y,
                            Z = st.Z,
                            Hue = st.Hue,
                            PriorityZ = priorityZ,
                            Sequence = seq++,
                            Ordinal = ordinal
                        });
                    }
                }
            }

            return items;
        }

        /// <summary>Classic draw order: diagonal (x + y), then priority Z, then land before statics before mobiles.</summary>
        public static int CompareDrawOrder(DrawItem a, DrawItem b)
        {
            int c = (a.X + a.Y).CompareTo(b.X + b.Y);
            if (c != 0) return c;
            c = a.PriorityZ.CompareTo(b.PriorityZ);
            if (c != 0) return c;
            c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            return a.Sequence.CompareTo(b.Sequence);
        }

        private static void Blit(Bitmap32 src, Bitmap32 dst, int[] owner, int left, int top, int id)
        {
            for (int y = 0; y < src.Height; y++)
            {
                int dy = top + y;
                if ((uint)dy >= (uint)dst.Height)
                    continue;

                int srcRow = y * src.Width;
                int dstRow = dy * dst.Width;
                for (int x = 0; x < src.Width; x++)
                {
                    uint p = src.Pixels[srcRow + x];
                    int dx = left + x;
                    if ((p >> 24) == 0 || (uint)dx >= (uint)dst.Width)
                        continue;

                    dst.Pixels[dstRow + dx] = p;
                    owner[dstRow + dx] = id;
                }
            }
        }

        /// <summary>
        /// Maps a texmap onto the tile's four corners: top (u0,v0), right (u1,v0), bottom (u1,v1), left (u0,v1),
        /// as two triangles with flat per-face shading from a light at the upper left.
        /// </summary>
        private static void DrawStretchedLand(Bitmap32 tex, Bitmap32 dst, int[] owner, DrawItem it, int originX, int originY, int scale, int id)
        {
            float ax = (it.X - it.Y) * TileHalf;
            float ay = (it.X + it.Y) * TileHalf;

            var top = new Vertex(ax, ay - it.Z * ZStep, 0, 0, it.Z, scale, originX, originY);
            var right = new Vertex(ax + TileHalf, ay + TileHalf - it.ZRight * ZStep, 1, 0, it.ZRight, scale, originX, originY);
            var bottom = new Vertex(ax, ay + 2 * TileHalf - it.ZBottom * ZStep, 1, 1, it.ZBottom, scale, originX, originY);
            var left = new Vertex(ax - TileHalf, ay + TileHalf - it.ZLeft * ZStep, 0, 1, it.ZLeft, scale, originX, originY);

            RasterTriangle(tex, dst, owner, id, top, right, left, Shade(top, right, left), scale);
            RasterTriangle(tex, dst, owner, id, right, bottom, left, Shade(right, bottom, left), scale);
        }

        private readonly struct Vertex
        {
            public readonly float X, Y, U, V;
            public readonly float Height;

            /// <param name="x">Screen position in original UO pixels; converted to image pixels using scale and origin.</param>
            public Vertex(float x, float y, float u, float v, int z, int scale, int originX, int originY)
            {
                X = x * scale - originX;
                Y = y * scale - originY;
                U = u;
                V = v;
                // Height in tile-edge units: Z is 4px steps on a 31px tile edge.
                Height = z * ZStep / (TileHalf * 1.41421356f);
            }
        }

        /// <summary>Brightness of a face relative to flat ground (1.0), lit from the screen's upper left.</summary>
        private static float Shade(in Vertex a, in Vertex b, in Vertex c)
        {
            // U/V double as world coordinates: tile edges are 1 unit.
            float e1x = b.U - a.U, e1y = b.V - a.V, e1z = b.Height - a.Height;
            float e2x = c.U - a.U, e2y = c.V - a.V, e2z = c.Height - a.Height;
            float nx = e1y * e2z - e1z * e2y;
            float ny = e1z * e2x - e1x * e2z;
            float nz = e1x * e2y - e1y * e2x;
            if (nz < 0) { nx = -nx; ny = -ny; nz = -nz; }

            float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-6f)
                return 1f;

            // Light from -U (screen upper left) and above.
            const float lx = -0.6f, ly = -0.2f, lz = 0.77f;
            float dot = (nx * lx + ny * ly + nz * lz) / len;
            // Clamp kept gentle: steep faces away from the light (riverbanks, cliffs) stay readable.
            return Math.Max(0.7f, Math.Min(1.25f, dot / lz));
        }

        private static void RasterTriangle(Bitmap32 tex, Bitmap32 dst, int[] owner, int id, in Vertex a, in Vertex b, in Vertex c, float shade, int scale)
        {
            float area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (Math.Abs(area) < 1e-3f)
                return;

            int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            int maxX = Math.Min(dst.Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            int maxY = Math.Min(dst.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));

            // Grow coverage by half an output pixel so neighboring quads and flat art tiles meet without cracks.
            const float eps = 0.5f;
            float absArea = Math.Abs(area);
            float lenA = Length(b, c), lenB = Length(c, a), lenC = Length(a, b);
            int size = tex.Width;

            for (int py = minY; py <= maxY; py++)
            {
                float y = py + 0.5f;
                for (int px = minX; px <= maxX; px++)
                {
                    float x = px + 0.5f;
                    float w0 = ((c.X - b.X) * (y - b.Y) - (c.Y - b.Y) * (x - b.X)) / area;
                    float w1 = ((a.X - c.X) * (y - c.Y) - (a.Y - c.Y) * (x - c.X)) / area;
                    float w2 = 1f - w0 - w1;

                    // weight * |area| / edge length is the pixel distance to that edge.
                    if (w0 * absArea / lenA < -eps || w1 * absArea / lenB < -eps || w2 * absArea / lenC < -eps)
                        continue;

                    float u = w0 * a.U + w1 * b.U + w2 * c.U;
                    float v = w0 * a.V + w1 * b.V + w2 * c.V;
                    int tx = Clamp((int)(u * size), 0, size - 1);
                    int ty = Clamp((int)(v * size), 0, size - 1);

                    int i = py * dst.Width + px;
                    dst.Pixels[i] = ApplyShade(tex.Pixels[ty * size + tx], shade);
                    owner[i] = id;
                }
            }
        }

        private static float Length(in Vertex a, in Vertex b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Max(1e-3f, (float)Math.Sqrt(dx * dx + dy * dy));
        }

        private static uint ApplyShade(uint p, float s)
        {
            uint r = (uint)Math.Min(255, (int)(((p >> 16) & 0xFF) * s));
            uint g = (uint)Math.Min(255, (int)(((p >> 8) & 0xFF) * s));
            uint b = (uint)Math.Min(255, (int)((p & 0xFF) * s));
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private LandCell[] GetLandBlock(int bx, int by)
        {
            long key = ((long)bx << 32) | (uint)by;
            if (!_blockCache.TryGetValue(key, out LandCell[] cells))
                _blockCache[key] = cells = _map.ReadLandBlock(bx, by);
            return cells;
        }

        private sbyte GetLandZ(int x, int y)
        {
            x = Clamp(x, 0, _map.Width - 1);
            y = Clamp(y, 0, _map.Height - 1);
            return GetLandBlock(x >> 3, y >> 3)[((y & 7) << 3) | (x & 7)].Z;
        }

        private static bool InRegion(int x, int y, int x0, int y0, int w, int h)
        {
            return x >= x0 && y >= y0 && x < x0 + w && y < y0 + h;
        }

        // Matches the classic client's list of land/static graphics that are never drawn.
        private static bool IsNoDrawLand(ushort g) => g < 3 || g == 0x1DB || (g >= 0x1AE && g <= 0x1B5);

        private static bool IsNoDrawStatic(ushort g)
        {
            switch (g)
            {
                case 0x0001:
                case 0x21BC:
                case 0x63D3:
                case 0x9E4C:
                case 0x9E64:
                case 0x9E65:
                case 0x9E7D:
                    return true;
                default:
                    return g >= 0x2198 && g <= 0x21A4;
            }
        }

        private void ClearSpriteCaches()
        {
            _landCache.Clear();
            _staticCache.Clear();
        }

        private Bitmap32 GetLand(int g)
        {
            if (!_landCache.TryGetValue(g, out Bitmap32 bmp))
                _landCache[g] = bmp = _spriteOverride?.Invoke(DrawKind.Land, g) ?? _data.GetLandHd(g, _scale) ?? Upscaled(_data.GetLand(g));
            return bmp;
        }

        /// <summary>A static graphic's sprite at <see cref="Scale"/>, as placements get it (cached; null if there is no art).</summary>
        public Bitmap32 StaticSprite(int graphic) => GetStatic(graphic);

        // Baked art first, then the client's HD art at exactly this scale, then the 1x art upscaled.
        private Bitmap32 GetStatic(int g)
        {
            if (!_staticCache.TryGetValue(g, out Bitmap32 bmp))
                _staticCache[g] = bmp = _spriteOverride?.Invoke(DrawKind.Static, g) ?? _data.GetStaticHd(g, _scale) ?? Upscaled(_data.GetStatic(g));
            return bmp;
        }

        private Bitmap32 Upscaled(Bitmap32 bmp) => bmp == null ? null : Resample.Nearest(bmp, _scale);

        /// <summary>Texmaps stay at their native size; the rasterizer samples them at any scale.</summary>
        private Bitmap32 GetTexmap(int texId)
        {
            if (texId == 0)
                return null;
            if (!_texmapCache.TryGetValue(texId, out Bitmap32 bmp))
                _texmapCache[texId] = bmp = _data.GetTexmap(texId);
            return bmp;
        }
    }
}
