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
    /// On-disk baked placements for one map: atlas pages plus an index from placement to atlas rect.
    ///   placements.bin  - "UOBK" v1: scale, map, page size, page count, then per placement:
    ///                     kind u8, x i32, y i32, z i8, graphic u16, ordinal u8, stretched u8,
    ///                     offsetX i32, offsetY i32 (relative to the tile anchor), width u16, height u16,
    ///                     page u16, atlasX u16, atlasY u16
    ///   page_NNN.png    - RGBA atlas pages (to become BC7 in the client pack)
    ///   report.csv      - per-placement pixel sources
    ///   coverage/page_NNN.png - the same pages tinted by source (green own, cyan other pass, yellow graphic, red original)
    /// This is the shape the Unity client will load: look up by placement key, draw at anchor + offset.
    /// </summary>
    public sealed class BakedRegion
    {
        private const uint Magic = 0x4B424F55; // "UOBK"
        private const int Version = 1;
        private const int Padding = 1;

        private readonly string _dir;
        private readonly Dictionary<PlacementKey, Entry> _index = new Dictionary<PlacementKey, Entry>();
        private readonly Dictionary<int, Bitmap32> _pages = new Dictionary<int, Bitmap32>();
        private readonly Dictionary<PlacementKey, Bitmap32> _cache = new Dictionary<PlacementKey, Bitmap32>();
        private readonly bool _coverage;

        private BakedRegion(string dir, bool coverage)
        {
            _dir = dir;
            _coverage = coverage;
        }

        /// <summary>Every placement that has baked art.</summary>
        public IEnumerable<PlacementKey> Keys => _index.Keys;

        public int Scale { get; private set; }
        public int Map { get; private set; }
        public int Count => _index.Count;

        public static void Save(string dir, int map, int scale, List<BakedPlacement> placements, int pageSize = 4096)
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "coverage"));
            foreach (string old in Directory.GetFiles(dir, "page_*.png"))
                File.Delete(old);

            List<Bitmap32> pages = Pack(placements, pageSize);
            for (int p = 0; p < pages.Count; p++)
            {
                Png.Write(Path.Combine(dir, PageName(p)), pages[p]);
                Png.Write(Path.Combine(dir, "coverage", PageName(p)), CoveragePage(placements, p, pages[p].Width, pages[p].Height));
            }

            using (var bw = new BinaryWriter(File.Create(Path.Combine(dir, "placements.bin"))))
            {
                bw.Write(Magic);
                bw.Write(Version);
                bw.Write(scale);
                bw.Write(map);
                bw.Write(pageSize);
                bw.Write(pages.Count);
                bw.Write(placements.Count);
                foreach (BakedPlacement b in placements)
                {
                    bw.Write((byte)b.Key.Kind);
                    bw.Write(b.Key.X);
                    bw.Write(b.Key.Y);
                    bw.Write(b.Key.Z);
                    bw.Write(b.Key.Graphic);
                    bw.Write(b.Key.Ordinal);
                    bw.Write(b.Stretched);
                    bw.Write(b.OffsetX);
                    bw.Write(b.OffsetY);
                    bw.Write((ushort)b.Image.Width);
                    bw.Write((ushort)b.Image.Height);
                    bw.Write((ushort)b.Page);
                    bw.Write((ushort)b.AtlasX);
                    bw.Write((ushort)b.AtlasY);
                }
            }

            var report = new StringBuilder("kind,graphic,x,y,z,ordinal,stretched,opaque,own,other_pass,graphic_fill,original_fill\n");
            foreach (BakedPlacement b in placements)
            {
                report.Append(string.Format(CultureInfo.InvariantCulture, "{0},0x{1:X4},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}\n",
                    b.Key.Kind, b.Key.Graphic, b.Key.X, b.Key.Y, b.Key.Z, b.Key.Ordinal, b.Stretched ? 1 : 0,
                    b.Opaque, b.FromOwn, b.FromOtherPass, b.FromGraphic, b.FromOriginal));
            }
            File.WriteAllText(Path.Combine(dir, "report.csv"), report.ToString());
        }

        /// <param name="coverage">Serve the coverage-tinted pages instead of the art.</param>
        public static BakedRegion Open(string dir, bool coverage = false)
        {
            var region = new BakedRegion(dir, coverage);
            using (var br = new BinaryReader(File.OpenRead(Path.Combine(dir, "placements.bin"))))
            {
                if (br.ReadUInt32() != Magic || br.ReadInt32() != Version)
                    throw new InvalidDataException("placements.bin is missing or from an incompatible version.");
                region.Scale = br.ReadInt32();
                region.Map = br.ReadInt32();
                br.ReadInt32(); // page size
                br.ReadInt32(); // page count
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var kind = (DrawKind)br.ReadByte();
                    int x = br.ReadInt32(), y = br.ReadInt32();
                    sbyte z = br.ReadSByte();
                    ushort graphic = br.ReadUInt16();
                    byte ordinal = br.ReadByte();
                    br.ReadBoolean(); // stretched
                    var e = new Entry
                    {
                        OffsetX = br.ReadInt32(),
                        OffsetY = br.ReadInt32(),
                        Width = br.ReadUInt16(),
                        Height = br.ReadUInt16(),
                        Page = br.ReadUInt16(),
                        AtlasX = br.ReadUInt16(),
                        AtlasY = br.ReadUInt16()
                    };
                    region._index[new PlacementKey(kind, x, y, z, graphic, ordinal)] = e;
                }
            }
            return region;
        }

        /// <summary>For <see cref="IsoCompositor.PlacementOverride"/>: the baked sprite for this placement, or null.</summary>
        public Bitmap32 Get(DrawItem item)
        {
            PlacementKey key = item.Key;
            if (_cache.TryGetValue(key, out Bitmap32 bmp))
                return bmp;
            if (!_index.TryGetValue(key, out Entry e))
                return null;

            Bitmap32 page = GetPage(e.Page);
            bmp = new Bitmap32(e.Width, e.Height);
            for (int y = 0; y < e.Height; y++)
                Array.Copy(page.Pixels, (e.AtlasY + y) * page.Width + e.AtlasX, bmp.Pixels, y * e.Width, e.Width);
            _cache[key] = bmp;
            return bmp;
        }

        /// <summary>Placement offset from its tile anchor, for checking against the renderer.</summary>
        public bool TryGetOffset(PlacementKey key, out int offsetX, out int offsetY)
        {
            bool found = _index.TryGetValue(key, out Entry e);
            offsetX = e.OffsetX;
            offsetY = e.OffsetY;
            return found;
        }

        private Bitmap32 GetPage(int page)
        {
            if (!_pages.TryGetValue(page, out Bitmap32 bmp))
            {
                string path = _coverage ? Path.Combine(_dir, "coverage", PageName(page)) : Path.Combine(_dir, PageName(page));
                _pages[page] = bmp = Png.Read(path);
            }
            return bmp;
        }

        /// <summary>Shelf packing, tallest first; pages are cropped to their used height.</summary>
        private static List<Bitmap32> Pack(List<BakedPlacement> placements, int pageSize)
        {
            var order = new List<BakedPlacement>(placements);
            order.Sort((a, b) => b.Image.Height != a.Image.Height ? b.Image.Height.CompareTo(a.Image.Height) : b.Image.Width.CompareTo(a.Image.Width));

            var pageHeights = new List<int>();
            int page = 0, x = 0, y = 0, shelf = 0;
            foreach (BakedPlacement b in order)
            {
                int w = b.Image.Width + Padding, h = b.Image.Height + Padding;
                if (w > pageSize || h > pageSize)
                    throw new InvalidOperationException($"{b.Key} is larger than an atlas page.");
                if (x + w > pageSize)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                if (y + h > pageSize)
                {
                    pageHeights.Add(y);
                    page++;
                    x = y = shelf = 0;
                }
                b.Page = page;
                b.AtlasX = x;
                b.AtlasY = y;
                x += w;
                shelf = Math.Max(shelf, h);
            }
            pageHeights.Add(y + shelf);

            var pages = new List<Bitmap32>();
            for (int p = 0; p < pageHeights.Count; p++)
                pages.Add(new Bitmap32(pageSize, Math.Max(1, pageHeights[p])));

            foreach (BakedPlacement b in placements)
            {
                Bitmap32 dst = pages[b.Page];
                for (int row = 0; row < b.Image.Height; row++)
                    Array.Copy(b.Image.Pixels, row * b.Image.Width, dst.Pixels, (b.AtlasY + row) * dst.Width + b.AtlasX, b.Image.Width);
            }
            return pages;
        }

        private static Bitmap32 CoveragePage(List<BakedPlacement> placements, int page, int width, int height)
        {
            var bmp = new Bitmap32(width, height);
            foreach (BakedPlacement b in placements)
            {
                if (b.Page != page)
                    continue;
                for (int i = 0; i < b.Source.Length; i++)
                {
                    uint tint;
                    switch (b.Source[i])
                    {
                        case BakedSource.Own: tint = 0x40FF40; break;
                        case BakedSource.OtherPass: tint = 0x40E0FF; break;
                        case BakedSource.Graphic: tint = 0xFFE040; break;
                        case BakedSource.Original: tint = 0xFF4040; break;
                        default: continue;
                    }
                    uint p = b.Image.Pixels[i];
                    float k = 0.35f + 0.65f * (0.3f * ((p >> 16) & 0xFF) + 0.59f * ((p >> 8) & 0xFF) + 0.11f * (p & 0xFF)) / 255f;
                    uint r = (uint)(((tint >> 16) & 0xFF) * k), g = (uint)(((tint >> 8) & 0xFF) * k), bl = (uint)((tint & 0xFF) * k);
                    int x = b.AtlasX + i % b.Image.Width, y = b.AtlasY + i / b.Image.Width;
                    bmp.Pixels[y * width + x] = 0xFF000000 | (r << 16) | (g << 8) | bl;
                }
            }
            return bmp;
        }

        private static string PageName(int page) => $"page_{page:D3}.png";

        private struct Entry
        {
            public int OffsetX, OffsetY, Width, Height, Page, AtlasX, AtlasY;
        }
    }
}
