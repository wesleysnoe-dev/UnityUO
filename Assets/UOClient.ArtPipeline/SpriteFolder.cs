using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    /// <summary>
    /// A folder of HD sprites: land/XXXX.png and static/XXXX.png (hex graphic id), scale.txt,
    /// report.csv with per-sprite coverage, and coverage/ overlays (green = best instance,
    /// yellow = other instances, red = color-matched fallback). This is the hand-off between
    /// slicing, review and (later) HD pack building.
    /// </summary>
    public sealed class SpriteFolder
    {
        private const string CoverageDir = "coverage";

        private readonly string _dir;
        private readonly bool _coverage;
        private readonly Dictionary<(DrawKind, int), Bitmap32> _cache = new Dictionary<(DrawKind, int), Bitmap32>();

        private SpriteFolder(string dir, int scale, bool coverage)
        {
            _dir = dir;
            _coverage = coverage;
            Scale = scale;
        }

        public int Scale { get; }

        public static void Save(string dir, int scale, IEnumerable<SlicedSprite> sprites)
        {
            string coverageDir = Path.Combine(dir, CoverageDir);
            foreach (string d in new[] { dir, coverageDir })
            {
                Directory.CreateDirectory(Path.Combine(d, "land"));
                Directory.CreateDirectory(Path.Combine(d, "static"));
            }
            File.WriteAllText(Path.Combine(dir, "scale.txt"), scale.ToString(CultureInfo.InvariantCulture));

            var report = new StringBuilder("kind,graphic,instances,opaque,from_best,from_others,fallback,coverage\n");
            foreach (SlicedSprite s in sprites)
            {
                Png.Write(PathFor(dir, s.Kind, s.Graphic), s.Image);
                Png.Write(PathFor(coverageDir, s.Kind, s.Graphic), CoverageOverlay(s));
                report.Append(string.Format(CultureInfo.InvariantCulture, "{0},0x{1:X4},{2},{3},{4},{5},{6},{7:F3}\n",
                    s.Kind, s.Graphic, s.Instances, s.OpaquePixels, s.FromBest, s.FromOthers, s.Fallback, s.Coverage));
            }
            File.WriteAllText(Path.Combine(dir, "report.csv"), report.ToString());
        }

        /// <param name="coverage">Serve the coverage overlays instead of the sprites, to paint a whole region by pixel source.</param>
        public static SpriteFolder Open(string dir, bool coverage = false)
        {
            string scalePath = Path.Combine(dir, "scale.txt");
            if (!File.Exists(scalePath))
                throw new FileNotFoundException("Not a sprite folder (scale.txt missing).", scalePath);
            return new SpriteFolder(dir, int.Parse(File.ReadAllText(scalePath).Trim(), CultureInfo.InvariantCulture), coverage);
        }

        /// <summary>For <see cref="IsoCompositor.SpriteOverride"/>: the HD sprite if this folder has one, else null.</summary>
        public Bitmap32 Get(DrawKind kind, int graphic)
        {
            if (_cache.TryGetValue((kind, graphic), out Bitmap32 bmp))
                return bmp;

            string path = PathFor(_coverage ? Path.Combine(_dir, CoverageDir) : _dir, kind, graphic);
            bmp = File.Exists(path) ? Png.Read(path) : null;
            _cache[(kind, graphic)] = bmp;
            return bmp;
        }

        /// <summary>Sprite luminance tinted by pixel source, so structure stays readable under the colors.</summary>
        private static Bitmap32 CoverageOverlay(SlicedSprite s)
        {
            var bmp = new Bitmap32(s.Image.Width, s.Image.Height);
            for (int i = 0; i < bmp.Pixels.Length; i++)
            {
                uint tint;
                switch (s.Source[i])
                {
                    case PixelSource.Best: tint = 0x40FF40; break;
                    case PixelSource.Others: tint = 0xFFE040; break;
                    case PixelSource.Fallback: tint = 0xFF4040; break;
                    default: continue;
                }

                uint p = s.Image.Pixels[i];
                float luma = (0.3f * ((p >> 16) & 0xFF) + 0.59f * ((p >> 8) & 0xFF) + 0.11f * (p & 0xFF)) / 255f;
                float k = 0.35f + 0.65f * luma;
                uint r = (uint)(((tint >> 16) & 0xFF) * k), g = (uint)(((tint >> 8) & 0xFF) * k), b = (uint)((tint & 0xFF) * k);
                bmp.Pixels[i] = 0xFF000000 | (r << 16) | (g << 8) | b;
            }
            return bmp;
        }

        private static string PathFor(string dir, DrawKind kind, int graphic)
        {
            return Path.Combine(dir, kind == DrawKind.Land ? "land" : "static", graphic.ToString("X4", CultureInfo.InvariantCulture) + ".png");
        }
    }
}
