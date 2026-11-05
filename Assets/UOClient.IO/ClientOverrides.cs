using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UOClient.IO.Formats;

namespace UOClient.IO
{
    /// <summary>
    /// The UO Smallz client's additions on top of the UO data files, read the way its TazUO patches read them
    /// (uosmallz-tazuo-changes.patch, 0005-0010), so both clients show the same items:
    /// <list type="bullet">
    /// <item><c>TileData/*.csv</c>: tiledata rows for the shard's own items, only in 0xFE00-0xFE0D and the
    /// shard block 0x4C00-0x4CFF (stock rows are never replaced).</item>
    /// <item><c>EquipConv/*.csv</c>: how worn shard items (item anim ids 1205-1233) animate and which paperdoll gump they use.</item>
    /// <item><c>ExternalImages/{art,land,gumps,texmaps}/0xNNNN.png</c>, then the HD pack in <c>../HDArt</c> where
    /// ExternalImages has nothing. A PNG exactly 2-4x the stock size is an HD version of it; any other size
    /// replaces the art at its own size.</item>
    /// </list>
    /// Nothing here throws on a bad file or line: it is skipped and noted in <see cref="Problems"/>.
    /// </summary>
    public sealed class ClientOverrides
    {
        public const int TileFirst = 0xFE00, TileLast = 0xFE0D, ShardItemFirst = 0x4C00, ShardItemLast = 0x4CFF;
        public const int AnimFirst = 1205, AnimLast = 1233;

        private readonly Dictionary<int, string> _art = new Dictionary<int, string>();     // static graphic
        private readonly Dictionary<int, string> _land = new Dictionary<int, string>();    // land tile
        private readonly Dictionary<int, string> _gumps = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _texmaps = new Dictionary<int, string>();
        private readonly Dictionary<(int, int), EquipConvEntry> _equipConv = new Dictionary<(int, int), EquipConvEntry>();
        private readonly List<(int Id, StaticTileData Tile)> _tiles = new List<(int, StaticTileData)>();

        public string ClientFolder { get; }
        public readonly List<string> Problems = new List<string>();

        public int ArtCount => _art.Count;
        public int GumpCount => _gumps.Count;
        public int LandCount => _land.Count;
        public int TileRowCount => _tiles.Count;
        public int EquipConvCount => _equipConv.Count;

        public ClientOverrides(string clientFolder, bool includeHdPack)
        {
            ClientFolder = clientFolder;
            string images = Path.Combine(clientFolder, "ExternalImages");
            IndexFolder(Path.Combine(images, "art"), _art, overwrite: true);
            IndexFolder(Path.Combine(images, "land"), _land, overwrite: true, max: 0x4000);
            IndexFolder(Path.Combine(images, "gumps"), _gumps, overwrite: true);
            IndexFolder(Path.Combine(images, "texmaps"), _texmaps, overwrite: true);
            if (includeHdPack)
            {
                string pack = Path.GetFullPath(Path.Combine(clientFolder, "..", "HDArt"));
                IndexFolder(Path.Combine(pack, "art"), _art, overwrite: false);
                IndexFolder(Path.Combine(pack, "land"), _land, overwrite: false, max: 0x4000);
                IndexFolder(Path.Combine(pack, "gumps"), _gumps, overwrite: false);
                IndexFolder(Path.Combine(pack, "texmaps"), _texmaps, overwrite: false);
            }
            ReadTileRows(Path.Combine(clientFolder, "TileData"));
            ReadEquipConv(Path.Combine(clientFolder, "EquipConv"));
        }

        public bool HasArt(int graphic) => _art.ContainsKey(graphic);
        public bool HasGump(int id) => _gumps.ContainsKey(id);

        public string ArtPath(int graphic) => _art.TryGetValue(graphic, out string p) ? p : null;
        public string LandPath(int tile) => _land.TryGetValue(tile, out string p) ? p : null;
        public string GumpPath(int id) => _gumps.TryGetValue(id, out string p) ? p : null;
        public string TexmapPath(int id) => _texmaps.TryGetValue(id, out string p) ? p : null;

        /// <summary>Writes the shard's tiledata rows into the loaded tiledata (stock ids are never touched).</summary>
        public void ApplyTo(TileData tileData)
        {
            foreach (var (id, tile) in _tiles)
                if (id < tileData.Statics.Length)
                    tileData.Statics[id] = tile;
                else
                    Problems.Add($"TileData: 0x{id:X4} is beyond the loaded tiledata ({tileData.Statics.Length} statics)");
        }

        /// <summary>A shard item's equipment conversion for a body, if one is defined.</summary>
        public bool TryEquipConv(int body, int itemAnim, out EquipConvEntry entry) => _equipConv.TryGetValue((body, itemAnim), out entry);

        /// <summary>2, 3 or 4 when a PNG of this size is that multiple of the stock art; 1 for a plain replacement.</summary>
        public static int HdScale(int width, int height, int stockWidth, int stockHeight)
        {
            for (int s = 2; s <= 4; s++)
                if (stockWidth > 0 && width == stockWidth * s && height == stockHeight * s)
                    return s;
            return 1;
        }

        /// <summary>
        /// An HD PNG's shape at the stock size: a cell takes the first opaque pixel of its scale x scale block.
        /// Used wherever the art is wanted at its own (1x) size; the full PNG is kept for 2x drawing.
        /// </summary>
        public static Bitmap32 HdShape(Bitmap32 png, int scale)
        {
            int w = png.Width / scale, h = png.Height / scale;
            var cells = new Bitmap32(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint found = 0;
                    for (int dy = 0; dy < scale && found == 0; dy++)
                        for (int dx = 0; dx < scale && found == 0; dx++)
                        {
                            uint p = png.Pixels[(y * scale + dy) * png.Width + x * scale + dx];
                            if ((p >> 24) != 0)
                                found = p;
                        }
                    cells.Pixels[y * w + x] = found;
                }
            return cells;
        }

        /// <summary>Reads a PNG, or null (and a note) if it can't be decoded.</summary>
        public Bitmap32 Load(string path)
        {
            try
            {
                return Png.Read(path);
            }
            catch (Exception e)
            {
                lock (Problems)
                    if (Problems.Count < 200)
                        Problems.Add($"{Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        private void IndexFolder(string dir, Dictionary<int, string> into, bool overwrite, int max = int.MaxValue)
        {
            if (!Directory.Exists(dir))
                return;
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.png"))
                {
                    if (!TryNum(Path.GetFileNameWithoutExtension(file), out ulong id) || id >= (ulong)max || id > int.MaxValue)
                        continue;
                    if (overwrite || !into.ContainsKey((int)id))
                        into[(int)id] = file;
                }
            }
            catch (Exception e)
            {
                Problems.Add($"{dir}: {e.Message}");
            }
        }

        private static bool IsShardTile(ulong id) => id >= TileFirst && id <= TileLast || id >= ShardItemFirst && id <= ShardItemLast;

        /// <summary>id,flags,weight,layer,count,animId,hue,lightIndex,height,name per line; '#' comments.</summary>
        private void ReadTileRows(string dir)
        {
            foreach (var (file, n, line) in CsvLines(dir))
            {
                string[] f = line.Split(new[] { ',' }, 10);
                if (f.Length < 10)
                {
                    Problems.Add($"{file} line {n}: {f.Length} fields, want 10");
                    continue;
                }
                var v = new ulong[9];
                ulong[] max = { uint.MaxValue, ulong.MaxValue, byte.MaxValue, byte.MaxValue, int.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, byte.MaxValue };
                bool ok = true;
                for (int i = 0; i < 9 && ok; i++)
                    ok = TryNum(f[i], out v[i]) && v[i] <= max[i];
                if (!ok || !IsShardTile(v[0]))
                {
                    Problems.Add($"{file} line {n}: {(ok ? $"0x{v[0]:X} is outside the shard ranges" : "bad number")}");
                    continue;
                }
                _tiles.Add(((int)v[0], new StaticTileData
                {
                    Flags = (TileFlags)v[1], Weight = (byte)v[2], Layer = (byte)v[3], Count = (int)v[4],
                    AnimId = (ushort)v[5], Hue = (ushort)v[6], LightIndex = (ushort)v[7], Height = (byte)v[8],
                    Name = f[9].Trim()
                }));
            }
        }

        /// <summary>body,itemAnimId,animGraphic,gump,hue per line (gump 0 = itemAnimId).</summary>
        private void ReadEquipConv(string dir)
        {
            foreach (var (file, n, line) in CsvLines(dir))
            {
                string[] f = line.Split(',');
                var v = new ulong[5];
                bool ok = f.Length >= 5;
                for (int i = 0; i < 5 && ok; i++)
                    ok = TryNum(f[i], out v[i]) && v[i] <= ushort.MaxValue;
                if (!ok || v[1] < AnimFirst || v[1] > AnimLast)
                {
                    Problems.Add($"{file} line {n}: {(ok ? $"anim {v[1]} is outside {AnimFirst}-{AnimLast}" : "want 5 numbers")}");
                    continue;
                }
                _equipConv[((int)v[0], (int)v[1])] = new EquipConvEntry((int)v[2], (int)(v[3] == 0 ? v[1] : v[3]), (int)v[4]);
            }
        }

        private List<(string File, int Line, string Text)> CsvLines(string dir)
        {
            var result = new List<(string, int, string)>();
            try
            {
                if (!Directory.Exists(dir))
                    return result;
                string[] files = Directory.GetFiles(dir, "*.csv");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string path in files)
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim().TrimStart('﻿');
                            if (line.Length > 0 && line[0] != '#')
                                result.Add((Path.GetFileName(path), i + 1, line));
                        }
                    }
                    catch (Exception e)
                    {
                        Problems.Add($"{Path.GetFileName(path)}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Problems.Add($"{dir}: {e.Message}");
            }
            return result;
        }

        public static bool TryNum(string s, out ulong v)
        {
            s = s.Trim();
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                : ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }
    }

    /// <summary>A worn item drawn as another animation (Equipconv.def or a shard EquipConv csv).</summary>
    public readonly struct EquipConvEntry
    {
        public readonly int Anim, Gump, Hue;

        public EquipConvEntry(int anim, int gump, int hue)
        {
            Anim = anim;
            Gump = gump;
            Hue = hue;
        }
    }
}
