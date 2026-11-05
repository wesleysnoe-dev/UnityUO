using System;
using System.Collections.Generic;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    public enum PixelSource : byte
    {
        Transparent = 0,
        Best = 1,
        Others = 2,
        Fallback = 3
    }

    public sealed class SlicedSprite
    {
        public DrawKind Kind;
        public int Graphic;
        public Bitmap32 Image;

        /// <summary>Where each pixel of <see cref="Image"/> came from; drives coverage overlays in review.</summary>
        public PixelSource[] Source;

        /// <summary>Placed copies of this graphic across all composites.</summary>
        public int Instances;

        /// <summary>Opaque output pixels (the original silhouette at scale).</summary>
        public int OpaquePixels;

        /// <summary>Pixels taken from the best single instance.</summary>
        public int FromBest;

        /// <summary>Pixels the best instance missed, filled from the average of the other instances.</summary>
        public int FromOthers;

        /// <summary>Pixels never visible in any composite, filled from color-matched original art.</summary>
        public int Fallback => OpaquePixels - FromBest - FromOthers;

        public float Coverage => OpaquePixels == 0 ? 0 : (float)(FromBest + FromOthers) / OpaquePixels;
    }

    /// <summary>
    /// Cuts restyled composites back into per-graphic HD sprites using the ownership map.
    /// Each graphic's base is the instance with the most visible pixels (keeps detail crisp);
    /// pixels it lacks come from the average of the other instances, and anything never visible
    /// (always behind something) from the original art, color-matched to the restyled pixels.
    /// The original silhouette is always re-applied so sprites still fit UO's tiles exactly.
    /// Stretched land is not sliced here; its HD form will be a texmap.
    /// </summary>
    public sealed class Slicer
    {
        private readonly UOData _data;
        private readonly Dictionary<(DrawKind, int), Accumulator> _sprites = new Dictionary<(DrawKind, int), Accumulator>();

        public Slicer(UOData data, int scale)
        {
            if (scale < 1)
                throw new ArgumentOutOfRangeException(nameof(scale));
            _data = data;
            Scale = scale;
        }

        public int Scale { get; }

        /// <summary>Stretched land placements skipped so far.</summary>
        public int SkippedStretched { get; private set; }

        /// <summary>Restyled images that had to be resized to composite × scale.</summary>
        public int Resized { get; private set; }

        /// <summary>Scale implied by a restyled image's size, rounded; for picking a default.</summary>
        public static int GuessScale(Composite c, Bitmap32 restyled)
        {
            return Math.Max(1, (int)Math.Round((double)restyled.Width / c.Image.Width));
        }

        public void Add(Composite c, Bitmap32 restyled)
        {
            if (c.Scale != 1)
                throw new ArgumentException("Slice composites rendered at scale 1.", nameof(c));

            int s = Scale;
            int rw = c.Image.Width * s, rh = c.Image.Height * s;
            if (restyled.Width != rw || restyled.Height != rh)
            {
                restyled = Resample.Bilinear(restyled, rw, rh);
                Resized++;
            }

            int cw = c.Image.Width, ch = c.Image.Height;
            for (int index = 0; index < c.Items.Count; index++)
            {
                DrawItem it = c.Items[index];
                if (it.Stretched)
                {
                    SkippedStretched++;
                    continue;
                }

                Accumulator acc = GetAccumulator(it.Kind, it.Graphic);
                if (acc == null)
                    continue;

                Bitmap32 orig = acc.Original;
                int ow = acc.Width;
                var instance = new uint[acc.Pixels];
                var mask = new bool[acc.Pixels];
                int covered = 0;

                for (int oy = 0; oy < orig.Height; oy++)
                {
                    int cy = it.Top + oy;
                    if ((uint)cy >= (uint)ch)
                        continue;
                    for (int ox = 0; ox < orig.Width; ox++)
                    {
                        int cx = it.Left + ox;
                        if ((uint)cx >= (uint)cw || (orig[ox, oy] >> 24) == 0)
                            continue;
                        if (c.Owner[cy * cw + cx] != index)
                            continue; // hidden behind something drawn later

                        for (int dy = 0; dy < s; dy++)
                        {
                            int outRow = (oy * s + dy) * ow;
                            int srcRow = (cy * s + dy) * rw;
                            for (int dx = 0; dx < s; dx++)
                            {
                                int o = outRow + ox * s + dx;
                                uint p = restyled.Pixels[srcRow + cx * s + dx] | 0xFF000000;
                                instance[o] = p;
                                mask[o] = true;
                                acc.SumR[o] += (p >> 16) & 0xFF;
                                acc.SumG[o] += (p >> 8) & 0xFF;
                                acc.SumB[o] += p & 0xFF;
                                acc.Count[o]++;
                                covered++;
                            }
                        }
                    }
                }

                acc.Instances++;
                if (covered > acc.BestCovered)
                {
                    acc.BestCovered = covered;
                    acc.Best = instance;
                    acc.BestMask = mask;
                }
            }
        }

        public List<SlicedSprite> Build()
        {
            var result = new List<SlicedSprite>(_sprites.Count);
            foreach (KeyValuePair<(DrawKind, int), Accumulator> pair in _sprites)
                result.Add(Build(pair.Key.Item1, pair.Key.Item2, pair.Value));
            result.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.Graphic.CompareTo(b.Graphic));
            return result;
        }

        private SlicedSprite Build(DrawKind kind, int graphic, Accumulator acc)
        {
            Bitmap32 original = Resample.Nearest(acc.Original, Scale);
            var image = new Bitmap32(acc.Width, acc.Height);
            var source = new PixelSource[acc.Pixels];
            var sprite = new SlicedSprite { Kind = kind, Graphic = graphic, Image = image, Source = source, Instances = acc.Instances };

            // First pass: take known pixels and gather color statistics for the fallback.
            var stats = new ColorTransfer();
            for (int i = 0; i < acc.Pixels; i++)
            {
                uint o = original.Pixels[i];
                if ((o >> 24) == 0)
                    continue;
                sprite.OpaquePixels++;

                uint p;
                if (acc.BestMask != null && acc.BestMask[i])
                {
                    p = acc.Best[i];
                    source[i] = PixelSource.Best;
                    sprite.FromBest++;
                }
                else if (acc.Count[i] > 0)
                {
                    int n = acc.Count[i];
                    p = 0xFF000000
                        | ((uint)(acc.SumR[i] / n + 0.5f) << 16)
                        | ((uint)(acc.SumG[i] / n + 0.5f) << 8)
                        | (uint)(acc.SumB[i] / n + 0.5f);
                    source[i] = PixelSource.Others;
                    sprite.FromOthers++;
                }
                else
                {
                    source[i] = PixelSource.Fallback;
                    continue;
                }

                image.Pixels[i] = p;
                stats.Add(o, p);
            }

            // Second pass: never-visible pixels get the original art, shifted to the restyle's colors.
            for (int i = 0; i < acc.Pixels; i++)
            {
                if (source[i] == PixelSource.Fallback)
                    image.Pixels[i] = stats.Apply(original.Pixels[i]);
            }

            return sprite;
        }

        private Accumulator GetAccumulator(DrawKind kind, int graphic)
        {
            if (_sprites.TryGetValue((kind, graphic), out Accumulator acc))
                return acc;

            Bitmap32 original = kind == DrawKind.Land ? _data.GetLand(graphic) : _data.GetStatic(graphic);
            acc = original == null ? null : new Accumulator(original, Scale);
            if (acc != null)
                _sprites[(kind, graphic)] = acc;
            return acc;
        }

        private sealed class Accumulator
        {
            public Accumulator(Bitmap32 original, int scale)
            {
                Original = original;
                Width = original.Width * scale;
                Height = original.Height * scale;
                Pixels = Width * Height;
                SumR = new float[Pixels];
                SumG = new float[Pixels];
                SumB = new float[Pixels];
                Count = new int[Pixels];
            }

            public readonly Bitmap32 Original;
            public readonly int Width, Height, Pixels;
            public readonly float[] SumR, SumG, SumB;
            public readonly int[] Count;
            public int Instances;
            public uint[] Best;
            public bool[] BestMask;
            public int BestCovered;
        }
    }
}
