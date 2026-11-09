using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using UOClient.IO.Files;
using UOClient.IO.Formats;

namespace UOClient.IO
{
    /// <summary>
    /// Entry point to a UO client install. Prefers UOP packages and falls back to MUL.
    /// Everything is opened lazily on first use.
    /// </summary>
    public sealed class UOData : IDisposable
    {
        public const int ArtCount = 0x14000;
        public const int StaticOffset = 0x4000;
        public const int GumpCount = 0x10000;

        private readonly Lazy<TileData> _tileData;
        private readonly Lazy<Hues> _hues;
        private readonly Lazy<UOFile> _art;
        private readonly Lazy<UOFile> _gumps;
        private readonly Lazy<UOFile> _texmaps;
        private readonly Lazy<AnimationFile> _animations;
        private readonly Lazy<ushort[]> _radarColors;
        private readonly Dictionary<int, MapFile> _maps = new Dictionary<int, MapFile>();

        public UOData(string directory)
        {
            if (!System.IO.Directory.Exists(directory))
                throw new DirectoryNotFoundException($"UO data directory not found: {directory}");

            Directory = directory;
            _tileData = new Lazy<TileData>(() => new TileData(PathOf("tiledata.mul")));
            _hues = new Lazy<Hues>(() => new Hues(PathOf("hues.mul")));
            _art = new Lazy<UOFile>(() => OpenIndexed("artLegacyMUL.uop", "build/artlegacymul/{0:D8}.tga", ArtCount, false, "art.mul", "artidx.mul"));
            _gumps = new Lazy<UOFile>(() => OpenIndexed("gumpartLegacyMUL.uop", "build/gumpartlegacymul/{0:D8}.tga", GumpCount, true, "gumpart.mul", "gumpidx.mul"));
            _texmaps = new Lazy<UOFile>(() => new MulFile(PathOf("texmaps.mul"), PathOf("texidx.mul")));
            _animations = new Lazy<AnimationFile>(() => new AnimationFile(PathOf("anim.mul"), PathOf("anim.idx")));
            _library = new Lazy<AnimationLibrary>(() => new AnimationLibrary(directory));
            _animData = new Lazy<AnimData>(() => new AnimData(PathOf("animdata.mul")));
            _lightShapes = new Lazy<LightShapes>(() => new LightShapes(PathOf("light.mul"), PathOf("lightidx.mul")));
            _radarColors = new Lazy<ushort[]>(() =>
            {
                byte[] raw = File.ReadAllBytes(PathOf("radarcol.mul"));
                var colors = new ushort[raw.Length / 2];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
                return colors;
            });
        }

        /// <summary>Classic mobile animations (anim.mul).</summary>
        public AnimationFile Animations => _animations.Value;

        /// <summary>All mobile animations, creatures included (anim.mul .. anim6.mul, Bodyconv.def, Body.def).</summary>
        public AnimationLibrary AnimationLibrary => _library.Value;

        private readonly Lazy<AnimationLibrary> _library;

        /// <summary>Animated item art (animdata.mul).</summary>
        public AnimData AnimData => _animData.Value;

        private readonly Lazy<AnimData> _animData;

        /// <summary>The light shapes (light.mul) item and static lights are drawn with.</summary>
        public LightShapes LightShapes => _lightShapes.Value;

        private readonly Lazy<LightShapes> _lightShapes;

        /// <summary>House and boat layouts (multi.mul).</summary>
        public MultiFile Multis => _multis ??= new MultiFile(Directory);

        private MultiFile _multis;

        private Lazy<UOFile> _sounds;

        /// <summary>
        /// A sound effect as 16-bit mono PCM at 22050 Hz, or null. Entries start with a 40-byte header
        /// (the original .wav name and two ints) before the samples.
        /// </summary>
        public short[] GetSound(int id)
        {
            _sounds ??= new Lazy<UOFile>(() => File.Exists(PathOf("soundLegacyMUL.uop"))
                ? new UopFile(PathOf("soundLegacyMUL.uop"), "build/soundlegacymul/{0:D8}.dat", 0x1000, false)
                : (UOFile)new MulFile(PathOf("sound.mul"), PathOf("soundidx.mul")));
            byte[] data;
            lock (_sounds)
                data = _sounds.Value.Read(id);
            int header = _sounds.Value is UopFile ? 40 : 32;
            if (data == null || data.Length <= header + 2)
                return null;
            var samples = new short[(data.Length - header) / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(header + i * 2));
            return samples;
        }

        private readonly UniFont[] _uniFonts = new UniFont[20];

        /// <summary>
        /// Unicode font 0..19 (unifont.mul, unifont1.mul ...), or null when its file is missing; font 1 falls back to
        /// unifont.mul as TazUO's FontsLoader does (FontsLoader.cs:94-104, :178-182). Gumps' labels use font 1.
        /// </summary>
        public UniFont GetUniFont(int index)
        {
            if (index < 0 || index >= _uniFonts.Length)
                return null;
            lock (_uniFonts)
            {
                if (_uniFonts[index] != null)
                    return _uniFonts[index];
                string path = PathOf(index == 0 ? "unifont.mul" : $"unifont{index}.mul");
                if (File.Exists(path))
                    return _uniFonts[index] = new UniFont(path);
            }
            return index == 1 ? GetUniFont(0) : null;
        }

        /// <summary>
        /// The skills window's default groups: skillgrp.mul when the client has a readable one, else TazUO's built-in
        /// groups (SkillsGroupManager.MakeDefault). A fresh list each call; nothing is written.
        /// </summary>
        public List<SkillGroup> GetSkillGroups(int skillCount)
        {
            string path = PathOf("skillgrp.mul");
            List<SkillGroup> groups = null;
            if (File.Exists(path))
            {
                try { groups = Formats.SkillGroups.Parse(File.ReadAllBytes(path), skillCount); }
                catch (IOException) { }
            }
            return groups != null && groups.Count != 0 ? groups : Formats.SkillGroups.BuiltIn(skillCount);
        }

        /// <summary>Minimap color per land tile (0..0x3FFF) followed by statics (0x4000 + graphic), 15-bit RGB.</summary>
        public ushort[] RadarColors => _radarColors.Value;

        public string Directory { get; }
        public TileData TileData => _tileData.Value;
        public Hues Hues => _hues.Value;
        public UOFile ArtFile => _art.Value;
        public UOFile GumpFile => _gumps.Value;

        // ---- the UO Smallz client's overrides (custom art, shard tiledata rows, equipment conversions) ----

        /// <summary>The client additions in use, or null when reading the plain data files.</summary>
        public ClientOverrides Overrides { get; private set; }

        private enum Kind { Land, Static, Gump, Texmap }

        private readonly Dictionary<(Kind, int), (Bitmap32 Shape, Bitmap32 Hd, int Scale)> _overrideCache =
            new Dictionary<(Kind, int), (Bitmap32, Bitmap32, int)>();

        /// <summary>Uses a client folder's ExternalImages/HDArt art, TileData rows and EquipConv lines from now on.</summary>
        public void UseOverrides(ClientOverrides overrides)
        {
            Overrides = overrides;
            overrides?.ApplyTo(TileData);
            lock (_overrideCache)
                _overrideCache.Clear();
        }

        /// <summary>
        /// The override for an art entry: its 1x shape (the PNG itself for a plain replacement, the HD PNG's
        /// stock-size shape otherwise), and the HD PNG with its scale (1 = none). Null shape = no override.
        /// </summary>
        private (Bitmap32 Shape, Bitmap32 Hd, int Scale) Override(Kind kind, int id, Func<Bitmap32> stock)
        {
            ClientOverrides o = Overrides;
            if (o == null)
                return default;
            string path = kind == Kind.Static ? o.ArtPath(id) : kind == Kind.Land ? o.LandPath(id)
                : kind == Kind.Gump ? o.GumpPath(id) : o.TexmapPath(id);
            if (path == null)
                return default;
            lock (_overrideCache)
                if (_overrideCache.TryGetValue((kind, id), out var cached))
                    return cached;

            Bitmap32 png = o.Load(path), original = png == null ? null : stock();
            (Bitmap32, Bitmap32, int) result = default;
            if (png != null)
            {
                int scale = original == null ? 1 : ClientOverrides.HdScale(png.Width, png.Height, original.Width, original.Height);
                if (scale > 1)
                    result = (ClientOverrides.HdShape(png, scale), png, scale);
                else if (kind != Kind.Texmap || original == null || (png.Width == original.Width && png.Height == original.Height))
                    result = (png, null, 1); // texmaps must keep the stock size: they are stretched over land
            }
            lock (_overrideCache)
                _overrideCache[(kind, id)] = result;
            return result;
        }

        public Bitmap32 GetLand(int graphic) =>
            Override(Kind.Land, graphic & 0x3FFF, () => StockLand(graphic)).Shape ?? StockLand(graphic);

        public Bitmap32 GetStatic(int graphic) =>
            Override(Kind.Static, graphic, () => StockStatic(graphic)).Shape ?? StockStatic(graphic);

        /// <summary>
        /// A static exactly as art.mul has it, ignoring the client's overrides: for art whose pixels carry data an
        /// upscaled copy blurs away (the cursors' green hotspot markers).
        /// </summary>
        public Bitmap32 GetStockStatic(int graphic) => StockStatic(graphic);

        /// <summary>The HD art for a static when one exists at exactly <paramref name="scale"/>x (else null: upscale the 1x art).</summary>
        public Bitmap32 GetStaticHd(int graphic, int scale)
        {
            var o = Override(Kind.Static, graphic, () => StockStatic(graphic));
            return o.Scale == scale ? o.Hd : null;
        }

        public Bitmap32 GetLandHd(int graphic, int scale)
        {
            var o = Override(Kind.Land, graphic & 0x3FFF, () => StockLand(graphic));
            return o.Scale == scale ? o.Hd : null;
        }

        /// <summary>A gump's HD picture and its scale over the stock size, or (null, 1).</summary>
        public (Bitmap32 Hd, int Scale) GetGumpHd(int id)
        {
            var o = Override(Kind.Gump, id, () => StockGump(id));
            return o.Scale > 1 ? (o.Hd, o.Scale) : (null, 1);
        }

        /// <summary>A worn item drawn with another animation and paperdoll gump for this body (Equipconv.def, then the shard's csv lines).</summary>
        public bool TryEquipConv(int body, int itemAnim, out EquipConvEntry entry)
        {
            if (Overrides != null && Overrides.TryEquipConv(body, itemAnim, out entry))
                return true;
            return StockEquipConv.TryGetValue((body, itemAnim), out entry);
        }

        private Dictionary<(int, int), EquipConvEntry> _stockEquipConv;

        /// <summary>Equipconv.def: body, item anim, anim to draw, gump (0 = item + 50000, -1 = converted + 50000, else the gump), hue.</summary>
        private Dictionary<(int, int), EquipConvEntry> StockEquipConv
        {
            get
            {
                if (_stockEquipConv != null)
                    return _stockEquipConv;
                var table = new Dictionary<(int, int), EquipConvEntry>();
                string path = PathOf("Equipconv.def");
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim().Trim('"');
                        if (line.Length == 0 || line[0] == '#')
                            continue;
                        string[] f = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length < 3 || !int.TryParse(f[0], out int body) || !int.TryParse(f[1], out int item) || !int.TryParse(f[2], out int to))
                            continue;
                        int gump = f.Length > 3 && int.TryParse(f[3], out int g) ? g : 0;
                        int hue = f.Length > 4 && int.TryParse(f[4], out int h) ? h : 0;
                        // Stored as the number the paperdoll adds 50000 (male) / 60000 (female) to.
                        int gumpBase = gump == 0 ? item : gump == -1 ? to : gump - 50000;
                        table[(body, item)] = new EquipConvEntry(to, gumpBase, hue);
                    }
                }
                return _stockEquipConv = table;
            }
        }

        private Bitmap32 StockLand(int graphic)
        {
            byte[] data = ArtFile.Read(graphic & 0x3FFF);
            return data == null ? null : ArtDecoder.DecodeLand(data);
        }

        private Bitmap32 StockStatic(int graphic)
        {
            byte[] data = ArtFile.Read(StaticOffset + graphic);
            return data == null ? null : ArtDecoder.DecodeStatic(data);
        }

        public Bitmap32 GetGump(int id) => Override(Kind.Gump, id, () => StockGump(id)).Shape ?? StockGump(id);

        // gump.def: a gump with no data drawn as the first one in its list that has data (TazUO's GumpsLoader), e.g. the
        // paperdoll pictures of newer clothes (the Arcane set, fur boots) drawn as older ones. The line's hue is not applied.
        private Dictionary<int, int[]> _gumpDef;

        private Dictionary<int, int[]> GumpDef
        {
            get
            {
                if (_gumpDef != null)
                    return _gumpDef;
                var def = new Dictionary<int, int[]>();
                string path = System.IO.Path.Combine(Directory, "gump.def");
                if (System.IO.File.Exists(path))
                    foreach (string raw in System.IO.File.ReadAllLines(path))
                    {
                        string line = raw.Split('#')[0];
                        int open = line.IndexOf('{'), close = line.IndexOf('}');
                        if (open < 0 || close < open || !int.TryParse(line.Substring(0, open).Trim(), out int from))
                            continue;
                        var to = new List<int>();
                        foreach (string part in line.Substring(open + 1, close - open - 1).Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(part, out int t))
                                to.Add(t);
                        if (to.Count > 0)
                            def[from] = to.ToArray();
                    }
                return _gumpDef = def;
            }
        }

        private Bitmap32 StockGump(int id) => StockGumpOnly(id) ?? GumpDefReplacement(id);

        private Bitmap32 GumpDefReplacement(int id)
        {
            if (!GumpDef.TryGetValue(id, out int[] to))
                return null;
            foreach (int t in to)
                if (StockGumpOnly(t) is Bitmap32 b)
                    return b;
            return null;
        }

        private Bitmap32 StockGumpOnly(int id)
        {
            if (!GumpFile.TryGetEntry(id, out FileEntry entry))
                return null;
            byte[] data = GumpFile.Read(entry);
            if (data == null)
                return null;

            if (entry.Compression == CompressionType.None)
                return GumpDecoder.Decode(data, (entry.Extra >> 16) & 0xFFFF, entry.Extra & 0xFFFF);

            // Compressed UOP gumps: int32 width, int32 height, then the rows.
            if (data.Length < 8)
                return null;
            int width = BinaryPrimitives.ReadInt32LittleEndian(data);
            int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            return GumpDecoder.Decode(data.AsSpan(8).ToArray(), width, height);
        }

        /// <summary>Land texture used for sloped ground; <paramref name="texId"/> comes from tiledata.</summary>
        public Bitmap32 GetTexmap(int texId) => Override(Kind.Texmap, texId, () => StockTexmap(texId)).Shape ?? StockTexmap(texId);

        private Bitmap32 StockTexmap(int texId)
        {
            byte[] data = _texmaps.Value.Read(texId);
            return data == null ? null : TexmapDecoder.Decode(data);
        }

        public MapFile GetMap(int index)
        {
            lock (_maps)
            {
                if (!_maps.TryGetValue(index, out MapFile map))
                {
                    map = new MapFile(Directory, index);
                    _maps[index] = map;
                }
                return map;
            }
        }

        private string PathOf(string file) => Path.Combine(Directory, file);

        private UOFile OpenIndexed(string uop, string nameFormat, int count, bool hasExtra, string mul, string idx)
        {
            if (File.Exists(PathOf(uop)))
                return new UopFile(PathOf(uop), nameFormat, count, hasExtra);
            return new MulFile(PathOf(mul), PathOf(idx));
        }

        public void Dispose()
        {
            if (_art.IsValueCreated) _art.Value.Dispose();
            if (_gumps.IsValueCreated) _gumps.Value.Dispose();
            if (_texmaps.IsValueCreated) _texmaps.Value.Dispose();
            if (_animations.IsValueCreated) _animations.Value.Dispose();
            if (_library.IsValueCreated) _library.Value.Dispose();
            if (_sounds != null && _sounds.IsValueCreated) _sounds.Value.Dispose();
            _multis?.Dispose();
            lock (_maps)
            {
                foreach (MapFile map in _maps.Values)
                    map.Dispose();
                _maps.Clear();
            }
        }
    }
}
