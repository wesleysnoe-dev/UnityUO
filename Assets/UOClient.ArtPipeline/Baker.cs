using System;
using System.Collections.Generic;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    public enum BakedSource : byte
    {
        Transparent = 0,

        /// <summary>Painted for this placement in the primary pass.</summary>
        Own = 1,

        /// <summary>Painted for this placement in a later pass (e.g. roofless).</summary>
        OtherPass = 2,

        /// <summary>Never visible here; taken from the per-graphic slice (other placements of the same art).</summary>
        Graphic = 3,

        /// <summary>Never visible anywhere; original art, color-matched to this placement's painted pixels.</summary>
        Original = 4
    }

    /// <summary>One map placement's own HD sprite, positioned relative to its tile anchor.</summary>
    public sealed class BakedPlacement
    {
        public PlacementKey Key;
        public bool Stretched;

        /// <summary>Sprite top-left relative to <see cref="IsoCompositor.Anchor"/> at the bake scale.</summary>
        public int OffsetX;
        public int OffsetY;

        public Bitmap32 Image;
        public BakedSource[] Source;

        public int Opaque, FromOwn, FromOtherPass, FromGraphic, FromOriginal;

        // Filled in by atlas packing.
        public int Page, AtlasX, AtlasY;
    }

    /// <summary>
    /// Bakes restyled composites into per-placement HD sprites: every land tile and static on the
    /// map keeps exactly the pixels ComfyUI painted for it, so there are no repetition seams.
    /// Passes are added in priority order (normal first, then roofless). Pixels a placement never
    /// showed come from the per-graphic slice, then from color-matched original art.
    /// </summary>
    public sealed class Baker
    {
        private readonly UOData _data;
        private readonly Slicer _slicer;
        private readonly Dictionary<PlacementKey, Accumulator> _placements = new Dictionary<PlacementKey, Accumulator>();
        private readonly Dictionary<(DrawKind, int), Bitmap32> _originals = new Dictionary<(DrawKind, int), Bitmap32>();
        private int _passes;

        public Baker(UOData data, int scale)
        {
            _data = data;
            Scale = scale;
            _slicer = new Slicer(data, scale);
        }

        public int Scale { get; }

        public int Map { get; private set; } = -1;

        public void Add(Composite c, Bitmap32 restyled)
        {
            if (c.Scale != 1)
                throw new ArgumentException("Bake composites rendered at scale 1.", nameof(c));
            if (Map >= 0 && Map != c.Map)
                throw new ArgumentException($"All passes must come from one map (got {c.Map} after {Map}).", nameof(c));
            Map = c.Map;

            int s = Scale;
            int rw = c.Image.Width * s, rh = c.Image.Height * s;
            if (restyled.Width != rw || restyled.Height != rh)
                restyled = Resample.Bilinear(restyled, rw, rh);

            _slicer.Add(c, restyled);
            BakedSource source = _passes == 0 ? BakedSource.Own : BakedSource.OtherPass;
            _passes++;

            int cw = c.Image.Width, ch = c.Image.Height;
            for (int index = 0; index < c.Items.Count; index++)
            {
                DrawItem it = c.Items[index];
                Bitmap32 original = it.Stretched ? null : GetOriginal(it.Kind, it.Graphic);
                if (!it.Stretched && original == null)
                    continue;

                (int left, int top, int w, int h) = IsoCompositor.PlacementRect(it, s,
                    original?.Width ?? 0, original?.Height ?? 0);

                if (!_placements.TryGetValue(it.Key, out Accumulator acc))
                {
                    (int ax, int ay) = IsoCompositor.Anchor(it, s);
                    acc = new Accumulator(it, w, h, left - ax, top - ay);
                    _placements[it.Key] = acc;
                }

                // Placement rect in this restyled image's pixels.
                int ix = left - c.OriginX * s, iy = top - c.OriginY * s;
                for (int py = 0; py < h; py++)
                {
                    int y = iy + py;
                    if ((uint)y >= (uint)rh)
                        continue;
                    int ownerRow = (y / s) * cw;
                    for (int px = 0; px < w; px++)
                    {
                        int x = ix + px;
                        if ((uint)x >= (uint)rw || c.Owner[ownerRow + x / s] != index)
                            continue;

                        int o = py * w + px;
                        if (acc.Source[o] != BakedSource.Transparent)
                            continue; // an earlier pass already painted it
                        acc.Pixels[o] = restyled.Pixels[y * rw + x] | 0xFF000000;
                        acc.Source[o] = source;
                    }
                }
            }
        }

        public List<BakedPlacement> Build()
        {
            var sliced = new Dictionary<(DrawKind, int), SlicedSprite>();
            foreach (SlicedSprite s in _slicer.Build())
                sliced[(s.Kind, s.Graphic)] = s;

            var compositor = new IsoCompositor(_data) { Scale = Scale };
            var result = new List<BakedPlacement>(_placements.Count);

            foreach (Accumulator acc in _placements.Values)
            {
                Bitmap32 silhouette = compositor.RenderPlacementAlone(acc.Item);
                if (silhouette == null || silhouette.Width != acc.Width || silhouette.Height != acc.Height)
                    continue;

                // Slices are flat art; a sloped tile's quad has a different shape, so it never borrows from them.
                SlicedSprite graphic = null;
                if (!acc.Item.Stretched)
                    sliced.TryGetValue((acc.Item.Kind, acc.Item.Graphic), out graphic);
                var image = new Bitmap32(acc.Width, acc.Height);
                var source = new BakedSource[acc.Width * acc.Height];
                var bp = new BakedPlacement
                {
                    Key = acc.Item.Key,
                    Stretched = acc.Item.Stretched,
                    OffsetX = acc.OffsetX,
                    OffsetY = acc.OffsetY,
                    Image = image,
                    Source = source
                };

                // The original silhouette decides which pixels exist; painted pixels are kept, the rest filled.
                var colors = new ColorTransfer();
                for (int i = 0; i < source.Length; i++)
                {
                    uint o = silhouette.Pixels[i];
                    if ((o >> 24) == 0)
                        continue;
                    bp.Opaque++;

                    BakedSource src = acc.Source[i];
                    if (src == BakedSource.Own || src == BakedSource.OtherPass)
                    {
                        image.Pixels[i] = acc.Pixels[i];
                        source[i] = src;
                        colors.Add(o, acc.Pixels[i]);
                        if (src == BakedSource.Own) bp.FromOwn++; else bp.FromOtherPass++;
                    }
                    else if (graphic != null && graphic.Source[i] != PixelSource.Fallback && graphic.Source[i] != PixelSource.Transparent)
                    {
                        image.Pixels[i] = graphic.Image.Pixels[i];
                        source[i] = BakedSource.Graphic;
                        bp.FromGraphic++;
                    }
                    else
                    {
                        source[i] = BakedSource.Original;
                        bp.FromOriginal++;
                    }
                }

                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == BakedSource.Original)
                        image.Pixels[i] = colors.Samples > 0 || graphic == null
                            ? colors.Apply(silhouette.Pixels[i])
                            : graphic.Image.Pixels[i]; // nothing painted here at all: use the slice's own color match
                }

                result.Add(bp);
            }

            result.Sort((a, b) =>
            {
                int c = (a.Key.X + a.Key.Y).CompareTo(b.Key.X + b.Key.Y);
                return c != 0 ? c : a.Key.GetHashCode().CompareTo(b.Key.GetHashCode());
            });
            return result;
        }

        private Bitmap32 GetOriginal(DrawKind kind, int graphic)
        {
            if (!_originals.TryGetValue((kind, graphic), out Bitmap32 bmp))
                _originals[(kind, graphic)] = bmp = kind == DrawKind.Land ? _data.GetLand(graphic) : _data.GetStatic(graphic);
            return bmp;
        }

        private sealed class Accumulator
        {
            public Accumulator(DrawItem item, int width, int height, int offsetX, int offsetY)
            {
                Item = item;
                Width = width;
                Height = height;
                OffsetX = offsetX;
                OffsetY = offsetY;
                Pixels = new uint[width * height];
                Source = new BakedSource[width * height];
            }

            public readonly DrawItem Item;
            public readonly int Width, Height, OffsetX, OffsetY;
            public readonly uint[] Pixels;
            public readonly BakedSource[] Source;
        }
    }
}
