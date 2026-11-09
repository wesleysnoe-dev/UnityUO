using System.Globalization;
using UOClient.ArtPipeline;
using UOClient.ArtPipeline.Imaging;
using UOClient.Cli;
using UOClient.IO;
using UOClient.IO.Formats;

// uoclient <command> [args]
// UO data: --uo <dir>, else UO_PATH, else the game's setting (UserSettings/unity-uo-settings.json, --uodata),
// which falls back to the UO Smallz client's own data. The server for `online`: --server host:port, else the setting.
var argList = args.ToList();
string? uoOption = TakeOption("--uo");
var settingArgs = new List<string>();
foreach (string name in new[] { "--server", "--uodata", "--overrides" })
    if (TakeOption(name) is string value)
        settingArgs.AddRange(new[] { name, value });
ClientSettings settings = SettingsFile.Load(settingArgs);
bool noOverrides = argList.Remove("--no-overrides");
string uoPath = uoOption ?? Environment.GetEnvironmentVariable("UO_PATH") ?? settings.ResolvedDataPath;
string? scaleOption = TakeOption("--scale");
bool stretchAll = argList.Remove("--stretch-all");
bool noRoofs = argList.Remove("--no-roofs");
bool coverage = argList.Remove("--coverage");

if (argList.Count == 0)
{
    PrintUsage();
    return 1;
}

// online talks to a server; it only needs the UO install for cliloc text.
if (argList[0] == "online")
    return await Online.Run(argList.Skip(1).ToList(), uoPath, settings);

// upscale works on plain images and needs no UO install.
if (argList[0] == "upscale" && argList.Count >= 4)
{
    Bitmap32 src = Png.Read(argList[1]);
    Bitmap32 up = Resample.Nearest(src, int.Parse(argList[2]));
    Png.Write(argList[3], up);
    Console.WriteLine($"Wrote {argList[3]} ({up.Width}x{up.Height})");
    return 0;
}

using var data = new UOData(uoPath);
// The shard's custom art and items, as the UO Smallz client shows them (--no-overrides: the plain data files).
if (!noOverrides && uoOption == null && settings.ResolvedOverridesPath is string overridesDir)
    data.UseOverrides(new ClientOverrides(overridesDir, settings.hdArt));

switch (argList[0])
{
    case "info":
        Info();
        return 0;
    case "land" when argList.Count >= 3:
        return Save(data.GetLand(ParseId(argList[1])), argList[2]);
    case "static" when argList.Count >= 3:
        return Save(data.GetStatic(ParseId(argList[1])), argList[2]);
    case "gump" when argList.Count >= 3:
        return Save(data.GetGump(ParseId(argList[1])), argList[2]);
    case "anim" when argList.Count >= 5:
        // body id, or item:<graphic> for a wearable's animation (tiledata AnimId)
        int body = argList[1].StartsWith("item:")
            ? data.TileData.GetStatic(ParseId(argList[1][5..])).AnimId
            : ParseId(argList[1]);
        Console.WriteLine($"body {body} (0x{body:X})");
        return SaveAnim(body, int.Parse(argList[2]), int.Parse(argList[3]), argList[4]);
    case "sound" when argList.Count >= 2:
    {
        using var snd = new UOClient.IO.Files.UopFile(Path.Combine(uoPath, "soundLegacyMUL.uop"), "build/soundlegacymul/{0:D8}.dat", 0x1000, false);
        byte[]? raw = snd.Read(ParseId(argList[1]));
        if (raw == null) { Console.WriteLine("no sound"); return 1; }
        Console.WriteLine($"{raw.Length} bytes; first 48: {BitConverter.ToString(raw, 0, Math.Min(48, raw.Length))}");
        Console.WriteLine("as text: " + new string(raw.Take(40).Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray()));
        return 0;
    }
    case "multi" when argList.Count >= 2:
    {
        using var mul = new UOClient.IO.Files.MulFile(Path.Combine(uoPath, "multi.mul"), Path.Combine(uoPath, "multi.idx"));
        byte[]? raw = mul.Read(ParseId(argList[1]));
        if (raw == null) { Console.WriteLine("no multi"); return 1; }
        Console.WriteLine($"{raw.Length} bytes: /12 = {raw.Length / 12.0}, /16 = {raw.Length / 16.0}");
        for (int i = 0; i < Math.Min(6, raw.Length / 16); i++)
            Console.WriteLine("  " + BitConverter.ToString(raw, i * 16, 16));
        return 0;
    }
    case "bakecheck" when argList.Count >= 3:
    {
        // bakecheck <baked dir> <other UO data dir>: is the baked art still of the same originals in this data set?
        BakedRegion region = BakedRegion.Open(argList[1]);
        using var other = new UOData(argList[2]);
        using var plain = new UOData(uoPath); // stock art, without the client's overrides
        MapFile map = plain.GetMap(region.Map), otherMap = other.GetMap(region.Map);
        int total = 0, artSame = 0, artDiff = 0, onMap = 0, missing = 0;
        var changed = new SortedSet<string>();
        foreach (PlacementKey k in region.Keys)
        {
            total++;
            Bitmap32? a = k.Kind == DrawKind.Land ? plain.GetLand(k.Graphic) : plain.GetStatic(k.Graphic);
            Bitmap32? b = k.Kind == DrawKind.Land ? other.GetLand(k.Graphic) : other.GetStatic(k.Graphic);
            bool same = a != null && b != null && a.Width == b.Width && a.Height == b.Height && a.Pixels.AsSpan().SequenceEqual(b.Pixels);
            if (same) artSame++; else { artDiff++; changed.Add($"{k.Kind} 0x{k.Graphic:X4}"); }
            bool present = k.Kind == DrawKind.Land
                ? map.GetLand(k.X, k.Y).Graphic == k.Graphic && map.GetLand(k.X, k.Y).Z == k.Z
                : map.ReadStaticBlock(k.X >> 3, k.Y >> 3).Any(s => s.X == (k.X & 7) && s.Y == (k.Y & 7) && s.Z == k.Z && s.Graphic == k.Graphic);
            if (present) onMap++; else missing++;
        }
        Console.WriteLine($"{total} baked placements (map {region.Map}, {region.Scale}x)");
        Console.WriteLine($"  art: {artSame} same original art as {argList[2]}, {artDiff} different{(changed.Count > 0 ? ": " + string.Join(", ", changed.Take(12)) : "")}");
        Console.WriteLine($"  map: {onMap} placements still on this data's map, {missing} no longer there");
        return 0;
    }
    case "landtex" when argList.Count >= 3:
    {
        // landtex <name part> <sheet.png>: land graphics whose tiledata name contains the part, their texmap id and size,
        // and a contact sheet of the distinct texmaps (overrides included), each scaled to 128 px, in the listed order.
        string part = argList[1].ToLowerInvariant();
        var texIds = new List<int>();
        LandTileData[] landRows = data.TileData.Land;
        for (int g = 0; g < landRows.Length; g++)
        {
            string name = (landRows[g].Name ?? "").Trim();
            if (!name.ToLowerInvariant().Contains(part))
                continue;
            Bitmap32 tex = data.GetTexmap(landRows[g].TexId);
            Console.WriteLine($"0x{g:X4} '{name}' tex 0x{landRows[g].TexId:X4} {(tex == null ? "none" : $"{tex.Width}x{tex.Height}")}");
            if (tex != null && !texIds.Contains(landRows[g].TexId))
                texIds.Add(landRows[g].TexId);
        }
        const int cell = 128, cols = 8;
        var sheet = new Bitmap32(cols * cell, Math.Max(1, (texIds.Count + cols - 1) / cols) * cell);
        for (int k = 0; k < texIds.Count; k++)
        {
            Bitmap32 tex = data.GetTexmap(texIds[k]);
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                    sheet.Pixels[(k / cols * cell + y) * sheet.Width + k % cols * cell + x] = tex.Pixels[y * tex.Height / cell * tex.Width + x * tex.Width / cell];
        }
        Png.Write(argList[2], sheet);
        Console.WriteLine($"sheet: {string.Join(" ", texIds.Select((t, k) => $"{k}=0x{t:X4}"))}");
        return 0;
    }
    case "landz" when argList.Count >= 5:
    {
        // landz <x0> <y0> <x1> <y1> [map]: each tile's land Z and graphic (rows are y), for reading heights under buildings.
        int lx0 = int.Parse(argList[1]), ly0 = int.Parse(argList[2]), lx1 = int.Parse(argList[3]), ly1 = int.Parse(argList[4]);
        MapFile lm = data.GetMap(argList.Count > 5 ? int.Parse(argList[5]) : 0);
        Console.WriteLine("     " + string.Concat(Enumerable.Range(lx0, lx1 - lx0 + 1).Select(x => $"{x,12}")));
        for (int y = ly0; y <= ly1; y++)
            Console.WriteLine($"{y,5}" + string.Concat(Enumerable.Range(lx0, lx1 - lx0 + 1).Select(x =>
            {
                LandCell c = lm.GetLand(x, y);
                return $"{c.Z,4}:{c.Graphic:X4}  ";
            })));
        return 0;
    }
    case "findname" when argList.Count >= 6:
    {
        // findname <text> <x0> <y0> <x1> <y1> [map]: every static whose tiledata name contains the text, with its tile and Z.
        string want = argList[1].ToLowerInvariant();
        int fx0 = int.Parse(argList[2]), fy0 = int.Parse(argList[3]), fx1 = int.Parse(argList[4]), fy1 = int.Parse(argList[5]);
        MapFile fm = data.GetMap(argList.Count > 6 ? int.Parse(argList[6]) : 0);
        for (int bx = fx0 >> 3; bx <= fx1 >> 3; bx++)
            for (int by = fy0 >> 3; by <= fy1 >> 3; by++)
                foreach (StaticCell s in fm.ReadStaticBlock(bx, by))
                {
                    int x = bx * 8 + s.X, y = by * 8 + s.Y;
                    string nm = data.TileData.GetStatic(s.Graphic).Name ?? "";
                    if (x >= fx0 && x <= fx1 && y >= fy0 && y <= fy1 && nm.ToLowerInvariant().Contains(want))
                        Console.WriteLine($"{x},{y},{s.Z} 0x{s.Graphic:X4} {nm}");
                }
        return 0;
    }
    case "multiparts" when argList.Count >= 2:
    {
        // multiparts <hex id>: a house or boat's parts (offset, Z, graphic, tiledata name, height and the flags that order them).
        int mid = Convert.ToInt32(argList[1], 16);
        foreach (var p in data.Multis.Get(mid).OrderBy(p => p.X + p.Y).ThenBy(p => p.Z))
        {
            var td = data.TileData.GetStatic(p.Graphic);
            Console.WriteLine($"{p.X,3},{p.Y,3} z{p.Z,3} 0x{p.Graphic:X4} {td.Name,-20} h{td.Height,2} {(p.Fixed ? "" : "(not fixed) ")}{td.Flags & (TileFlags.Background | TileFlags.Wall | TileFlags.Surface | TileFlags.Roof | TileFlags.Door | TileFlags.Window)}");
        }
        return 0;
    }
    case "landhist" when argList.Count >= 5:
    {
        // landhist <x0> <y0> <x1> <y1> [map]: every land graphic in the rectangle, how many, and its tiledata name (what the
        // natural terrain style classifies it by, TerrainMaterials.Classify).
        int hx0 = int.Parse(argList[1]), hy0 = int.Parse(argList[2]), hx1 = int.Parse(argList[3]), hy1 = int.Parse(argList[4]);
        MapFile hm = data.GetMap(argList.Count > 5 ? int.Parse(argList[5]) : 0);
        var counts = new Dictionary<int, int>();
        for (int x = hx0; x <= hx1; x++)
            for (int y = hy0; y <= hy1; y++)
            {
                int g = hm.GetLand(x, y).Graphic & 0x3FFF;
                counts[g] = counts.TryGetValue(g, out int n) ? n + 1 : 1;
            }
        foreach (var kv in counts.OrderByDescending(k => k.Value))
            Console.WriteLine($"0x{kv.Key:X4} {kv.Value,6}  '{data.TileData.Land[kv.Key].Name}'");
        return 0;
    }
    case "area" when argList.Count >= 5:
    {
        // area <x0> <y0> <x1> <y1> [map]: every static graphic in the rectangle with its tiledata, how many, and how many
        // stand on a tile next to water land (a quick survey of what lines a coast or a river).
        int ax0 = int.Parse(argList[1]), ay0 = int.Parse(argList[2]), ax1 = int.Parse(argList[3]), ay1 = int.Parse(argList[4]);
        MapFile am = data.GetMap(argList.Count > 5 ? int.Parse(argList[5]) : 0);
        bool Wet(int x, int y) => (data.TileData.Land[am.GetLand(x, y).Graphic & 0x3FFF].Flags & TileFlags.Wet) != 0;
        var seen = new Dictionary<int, (int All, int Shore, int MinZ, int MaxZ)>();
        for (int x = ax0; x <= ax1; x++)
            for (int y = ay0; y <= ay1; y++)
            {
                bool shore = Wet(x - 1, y) || Wet(x + 1, y) || Wet(x, y - 1) || Wet(x, y + 1) || Wet(x, y);
                foreach (StaticCell s in am.ReadStaticBlock(x >> 3, y >> 3).Where(s => s.X == (x & 7) && s.Y == (y & 7)))
                {
                    seen.TryGetValue(s.Graphic, out var v);
                    seen[s.Graphic] = (v.All + 1, v.Shore + (shore ? 1 : 0), v.All == 0 ? s.Z : Math.Min(v.MinZ, s.Z), v.All == 0 ? s.Z : Math.Max(v.MaxZ, s.Z));
                }
            }
        foreach (var kv in seen.OrderByDescending(k => k.Value.All))
        {
            StaticTileData sd = data.TileData.GetStatic(kv.Key);
            Console.WriteLine($"0x{kv.Key:X4} x{kv.Value.All,-4} shore {kv.Value.Shore,-4} z {kv.Value.MinZ}..{kv.Value.MaxZ} '{sd.Name}' h {sd.Height} {sd.Flags}");
        }
        // Land beside any water (wet land or a wet static on a neighbouring tile), by graphic.
        bool AnyWet(int x, int y) => Wet(x, y) || am.ReadStaticBlock(x >> 3, y >> 3)
            .Any(s => s.X == (x & 7) && s.Y == (y & 7) && (data.TileData.GetStatic(s.Graphic).Flags & TileFlags.Wet) != 0);
        var land = new Dictionary<int, int>();
        for (int x = ax0; x <= ax1; x++)
            for (int y = ay0; y <= ay1; y++)
                if (!AnyWet(x, y) && (AnyWet(x - 1, y) || AnyWet(x + 1, y) || AnyWet(x, y - 1) || AnyWet(x, y + 1)))
                {
                    int g = am.GetLand(x, y).Graphic & 0x3FFF;
                    land[g] = land.TryGetValue(g, out int n) ? n + 1 : 1;
                }
        // A character map: S ocean/water static, w water land, . other wet static, n no-draw land, # other land.
        Console.WriteLine("-- map (S water static, w water land, n no-draw land, # land)");
        for (int y = ay0; y <= ay1; y++)
        {
            var row = new System.Text.StringBuilder();
            for (int x = ax0; x <= ax1; x++)
            {
                int g = am.GetLand(x, y).Graphic & 0x3FFF;
                bool st = am.ReadStaticBlock(x >> 3, y >> 3).Any(s => s.X == (x & 7) && s.Y == (y & 7) && s.Graphic >= 0x1796 && s.Graphic <= 0x17B2);
                bool nodraw = g < 3 || g == 0x1DB || (g >= 0x1AE && g <= 0x1B5);
                row.Append(st ? 'S' : Wet(x, y) ? 'w' : nodraw ? 'n' : '#');
            }
            Console.WriteLine($"{y} {row}");
        }
        Console.WriteLine("-- land beside water");
        foreach (var kv in land.OrderByDescending(k => k.Value))
            Console.WriteLine($"land 0x{kv.Key:X4} x{kv.Value,-4} '{data.TileData.Land[kv.Key].Name}' {data.TileData.Land[kv.Key].Flags}");
        return 0;
    }
    case "tile" when argList.Count >= 3:
    {
        // tile <x> <y> [map]: what stands on a tile (land, then statics with their tiledata).
        int tx = int.Parse(argList[1]), ty = int.Parse(argList[2]), tm = argList.Count > 3 ? int.Parse(argList[3]) : 0;
        MapFile mf = data.GetMap(tm);
        LandCell lc = mf.GetLand(tx, ty);
        LandTileData ld = data.TileData.Land[lc.Graphic & 0x3FFF];
        Console.WriteLine($"land 0x{lc.Graphic:X4} '{ld.Name}' z {lc.Z} flags {ld.Flags}");
        foreach (StaticCell s in mf.ReadStaticBlock(tx >> 3, ty >> 3).Where(s => s.X == (tx & 7) && s.Y == (ty & 7)))
        {
            StaticTileData sd = data.TileData.GetStatic(s.Graphic);
            Console.WriteLine($"static 0x{s.Graphic:X4} '{sd.Name}' z {s.Z} height {sd.Height} flags {sd.Flags}");
        }
        return 0;
    }
    case "hue":
        // hue <n>...: a hue's 32-step ramp (dark to bright), as 0xRRGGBB.
        foreach (string s in argList.Skip(1))
        {
            int h = ParseId(s);
            var ramp = data.Hues.Entries[h - 1].Colors.Select(c => (Color16.ToArgb32(c) & 0xFFFFFF).ToString("X6"));
            Console.WriteLine($"hue {h} (0x{h:X}): {string.Join(" ", ramp)}");
        }
        return 0;
    case "animdata":
        // animdata <graphic>...: the frames an animated item (or effect) cycles through.
        foreach (string s in argList.Skip(1))
        {
            int g = ParseId(s);
            AnimDataEntry a = data.AnimData.Get(g);
            StaticTileData td = data.TileData.GetStatic(g);
            Console.WriteLine($"0x{g:X4} '{td.Name}' flags {td.Flags}: {(a.Offsets == null ? "no frames" : $"{a.Offsets.Length} frames, interval {a.Interval}: " + string.Join(" ", a.Offsets.Select(o => $"0x{g + o:X4}'{data.TileData.GetStatic(g + o).Name}'")))}");
        }
        return 0;
    case "overrides":
    {
        // What the UO Smallz client adds on top of the data files, and a few of the shard's items through it.
        ClientOverrides? o = data.Overrides;
        Console.WriteLine($"UO data:   {data.Directory}");
        Console.WriteLine($"Overrides: {o?.ClientFolder ?? "(none)"}");
        if (o == null)
            return 1;
        Console.WriteLine($"  {o.ArtCount} art, {o.LandCount} land, {o.GumpCount} gumps, {o.TileRowCount} tiledata rows, {o.EquipConvCount} equipment conversions");
        foreach (string p in o.Problems.Take(10))
            Console.WriteLine($"  problem: {p}");
        foreach (int id in new[] { 0x4C00, 0x4C08, 0x4C0A, 0x4C38, 0x4C39, 0xFE00, 0xFE10, 0x1F2E, 0x0F78 })
        {
            StaticTileData td = data.TileData.GetStatic(id);
            Bitmap32? art = data.GetStatic(id), hd = data.GetStaticHd(id, 2);
            string conv = td.AnimId != 0 && data.TryEquipConv(400, td.AnimId, out EquipConvEntry e) ? $", worn as anim {e.Anim} gump {e.Gump + 50000}" : "";
            Console.WriteLine($"  0x{id:X4} '{td.Name}' anim {td.AnimId}: art {(art == null ? "none" : $"{art.Width}x{art.Height}")}{(hd != null ? $", HD {hd.Width}x{hd.Height}" : "")}{conv}");
        }
        return 0;
    }
    case "bodies":
        // Which bodies have art (after Bodyconv/Body.def), and what mount items ride.
        foreach (string s in argList.Skip(1))
        {
            if (s.StartsWith("item:"))
            {
                int g = ParseId(s[5..]);
                StaticTileData td = data.TileData.GetStatic(g);
                Console.WriteLine($"item 0x{g:X4} '{td.Name}' anim {td.AnimId} (0x{td.AnimId:X})");
                continue;
            }
            int b = ParseId(s);
            var (file, real, hue) = data.AnimationLibrary.Resolve(b);
            AnimGroup group = data.AnimationLibrary.GroupOf(b);
            var (walk, run, stand) = data.AnimationLibrary.Actions(b);
            int frames = data.AnimationLibrary.Read(b, stand, 1).Length;
            string source = file < 0 ? "AnimationFrame*.uop" : "anim" + (file == 0 ? "" : (file + 1).ToString());
            Console.WriteLine($"body {b} (0x{b:X}): {source} as {real}, {group}, hue {hue}, walk/run/stand {walk}/{run}/{stand}, stand frames {frames}");
        }
        return 0;
    case "compose" when argList.Count >= 7:
        Compose(Ints(1, 5), argList[6]);
        return 0;
    case "slice" when argList.Count >= 3:
        return Slice(argList[1], argList.Skip(2).ToList());
    case "bake" when argList.Count >= 3:
        return Bake(argList[1], argList.Skip(2).ToList());
    case "reassemble" when argList.Count >= 8:
        return Reassemble(Ints(1, 5), argList[6], argList[7]);
    default:
        PrintUsage();
        return 1;
}

void Info()
{
    Console.WriteLine($"UO data: {data.Directory}");
    Console.WriteLine($"Art:     {data.ArtFile.GetType().Name} {Path.GetFileName(data.ArtFile.FilePath)}, {data.ArtFile.Count} slots");
    Console.WriteLine($"Gumps:   {data.GumpFile.GetType().Name} {Path.GetFileName(data.GumpFile.FilePath)}, {data.GumpFile.Count} slots");
    TileData td = data.TileData;
    Console.WriteLine($"Tiledata: {(td.IsNewFormat ? "64-bit flags" : "32-bit flags")}, {td.Land.Length} land, {td.Statics.Length} statics");
    Console.WriteLine($"Hues:    {data.Hues.Entries.Length}");
    for (int i = 0; i < 6; i++)
    {
        try
        {
            MapFile m = data.GetMap(i);
            long expected = (long)m.BlockWidth * m.BlockHeight * 196;
            Console.WriteLine($"Map {i}:   {m.Width}x{m.Height}, land data {m.LandDataLength:N0} bytes (expected {expected:N0})");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Map {i}:   unavailable ({e.Message})");
        }
    }
}

int Save(Bitmap32? bmp, string path)
{
    if (bmp == null)
    {
        Console.Error.WriteLine("No art at that index.");
        return 2;
    }
    Png.Write(path, bmp);
    Console.WriteLine($"Wrote {path} ({bmp.Width}x{bmp.Height})");
    return 0;
}

// anim <body> <action> <uoDirection 0-7> <out.png>: all frames side by side, anchors marked
int SaveAnim(int body, int action, int direction, string path)
{
    int fileDir = AnimationFile.FileDirection(direction, out bool mirror);
    AnimationFrame[] frames = data.Animations.Read(body, action, fileDir);
    if (frames.Length == 0)
    {
        Console.Error.WriteLine($"No animation for body {body} action {action} direction {direction}.");
        return 2;
    }

    int cell = frames.Max(f => Math.Max(f.Image.Width, f.Image.Height)) + 8;
    var sheet = new Bitmap32(cell * frames.Length, cell);
    for (int i = 0; i < frames.Length; i++)
    {
        AnimationFrame f = frames[i];
        int ox = i * cell + 4, oy = 4;
        for (int y = 0; y < f.Image.Height; y++)
            for (int x = 0; x < f.Image.Width; x++)
            {
                uint p = f.Image[mirror ? f.Image.Width - 1 - x : x, y];
                if ((p >> 24) != 0)
                    sheet[ox + x, oy + y] = p;
            }
        // Anchor (where the mobile stands) in red.
        int ax = ox + (mirror ? f.Image.Width - f.CenterX : f.CenterX), ay = oy + f.Image.Height + f.CenterY;
        for (int k = -2; k <= 2; k++)
        {
            if ((uint)(ax + k) < (uint)sheet.Width && (uint)ay < (uint)sheet.Height) sheet[ax + k, ay] = 0xFFFF0000;
            if ((uint)ax < (uint)sheet.Width && (uint)(ay + k) < (uint)sheet.Height) sheet[ax, ay + k] = 0xFFFF0000;
        }
    }
    Png.Write(path, sheet);
    Console.WriteLine($"Wrote {path}: {frames.Length} frames, file direction {fileDir}{(mirror ? " mirrored" : "")}");
    return 0;
}

void Compose(int[] r, string outDir)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    Composite c = new IsoCompositor(data) { StretchAllLand = stretchAll, HideRoofs = noRoofs }.Compose(r[0], r[1], r[2], r[3], r[4]);
    CompositeFile.Save(outDir, c);

    Console.WriteLine($"Composed map {r[0]} ({r[1]},{r[2]}) {r[3]}x{r[4]} tiles -> {c.Image.Width}x{c.Image.Height}px, {c.Items.Count} sprites in {sw.ElapsedMilliseconds} ms");
    Console.WriteLine($"Output: {Path.GetFullPath(outDir)}");
    Console.WriteLine($"Restyle {CompositeFile.ImageName} and save the result as {Path.Combine(outDir, CompositeFile.RestyledName)}, then run slice.");
}

// <compositeDir>[=<restyled.png>] ... -> loaded passes and the scale to use
(List<(Composite Composite, Bitmap32 Restyled, string Name)> Passes, int Scale)? LoadPasses(List<string> inputs)
{
    var loaded = new List<(Composite Composite, Bitmap32 Restyled, string Name)>();
    foreach (string input in inputs)
    {
        int eq = input.IndexOf('=', 2); // skip a drive letter's colon
        string dir = eq >= 0 ? input[..eq] : input;
        string restyledPath = eq >= 0 ? input[(eq + 1)..] : Path.Combine(dir, CompositeFile.RestyledName);
        if (!File.Exists(restyledPath))
        {
            Console.Error.WriteLine($"Missing restyled image: {restyledPath}");
            return null;
        }
        loaded.Add((CompositeFile.Load(dir), Png.Read(restyledPath), dir));
    }

    int scale = scaleOption != null ? int.Parse(scaleOption) : Slicer.GuessScale(loaded[0].Composite, loaded[0].Restyled);
    foreach (var (composite, restyled, name) in loaded)
    {
        double ratioX = (double)restyled.Width / composite.Image.Width, ratioY = (double)restyled.Height / composite.Image.Height;
        if (Math.Abs(ratioX - scale) > 0.02 * scale || Math.Abs(ratioY - scale) > 0.02 * scale)
            Console.WriteLine($"warning: {name}: restyled is {ratioX:F2}x/{ratioY:F2}x the composite, expected {scale}x; it will be resized");
    }
    return (loaded, scale);
}

// bake <bakedDir> <compositeDir>[=<restyled.png>] ...   (primary pass first, e.g. roofs, then --no-roofs)
int Bake(string outDir, List<string> inputs)
{
    var passes = LoadPasses(inputs);
    if (passes == null)
        return 2;
    var (loaded, scale) = passes.Value;

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var baker = new Baker(data, scale);
    foreach (var (composite, restyled, _) in loaded)
        baker.Add(composite, restyled);
    List<BakedPlacement> placements = baker.Build();
    BakedRegion.Save(outDir, baker.Map, scale, placements);

    long opaque = placements.Sum(p => (long)p.Opaque);
    double Pct(Func<BakedPlacement, int> f) => 100.0 * placements.Sum(p => (long)f(p)) / Math.Max(1, opaque);
    long bytes = Directory.GetFiles(outDir, "page_*.png").Sum(f => new FileInfo(f).Length);
    Console.WriteLine($"Baked {placements.Count} placements ({placements.Count(p => p.Key.Kind == DrawKind.Land)} land, {placements.Count(p => p.Key.Kind == DrawKind.Static)} static) at {scale}x from {loaded.Count} pass(es) in {sw.ElapsedMilliseconds} ms");
    Console.WriteLine($"Pixels: {Pct(p => p.FromOwn):F1}% painted in place, {Pct(p => p.FromOtherPass):F1}% from a later pass, {Pct(p => p.FromGraphic):F1}% from the per-graphic slice, {Pct(p => p.FromOriginal):F1}% color-matched original");
    Console.WriteLine($"Atlas: {Directory.GetFiles(outDir, "page_*.png").Length} page(s), {bytes / 1024.0:F0} KB as PNG");
    Console.WriteLine($"Output: {Path.GetFullPath(outDir)}");
    return 0;
}

// slice <outDir> <compositeDir>[=<restyled.png>] ...
int Slice(string outDir, List<string> inputs)
{
    var passes = LoadPasses(inputs);
    if (passes == null)
        return 2;
    var (loaded, scale) = passes.Value;

    var slicer = new Slicer(data, scale);
    foreach (var (composite, restyled, _) in loaded)
        slicer.Add(composite, restyled);

    List<SlicedSprite> sprites = slicer.Build();
    SpriteFolder.Save(outDir, scale, sprites);

    int opaque = sprites.Sum(s => s.OpaquePixels);
    int fallback = sprites.Sum(s => s.Fallback);
    Console.WriteLine($"Sliced {sprites.Count} sprites ({sprites.Count(s => s.Kind == DrawKind.Land)} land, {sprites.Count(s => s.Kind == DrawKind.Static)} static) at {scale}x from {loaded.Count} composite(s)");
    Console.WriteLine($"Coverage: {100.0 * (opaque - fallback) / Math.Max(1, opaque):F1}% restyled, {100.0 * fallback / Math.Max(1, opaque):F1}% color-matched fallback (hidden in every composite)");
    if (slicer.SkippedStretched > 0)
        Console.WriteLine($"Skipped {slicer.SkippedStretched} sloped land placements (HD texmaps not sliced yet)");
    if (slicer.Resized > 0)
        Console.WriteLine($"Resized {slicer.Resized} restyled image(s) to match the composite");
    Console.WriteLine($"Output: {Path.GetFullPath(outDir)} (see report.csv)");
    return 0;
}

// reassemble <map> <x> <y> <w> <h> <spritesDir|bakedDir> <out.png>
int Reassemble(int[] r, string artDir, string outPath)
{
    var compositor = new IsoCompositor(data) { HideRoofs = noRoofs };
    string kind;
    if (File.Exists(Path.Combine(artDir, "placements.bin")))
    {
        BakedRegion region = BakedRegion.Open(artDir, coverage);
        compositor.Scale = region.Scale;
        compositor.PlacementOverride = region.Get;
        kind = $"{region.Count} baked placements";
    }
    else
    {
        SpriteFolder folder = SpriteFolder.Open(artDir, coverage);
        compositor.Scale = folder.Scale;
        compositor.SpriteOverride = folder.Get;
        kind = "per-graphic sprites";
    }

    Composite c = compositor.Compose(r[0], r[1], r[2], r[3], r[4]);
    Png.Write(outPath, c.Image);
    Console.WriteLine($"Reassembled map {r[0]} ({r[1]},{r[2]}) {r[3]}x{r[4]} at {compositor.Scale}x from {kind} -> {outPath} ({c.Image.Width}x{c.Image.Height})");
    return 0;
}

string? TakeOption(string name)
{
    int i = argList.IndexOf(name);
    if (i < 0 || i + 1 >= argList.Count)
        return null;
    string value = argList[i + 1];
    argList.RemoveRange(i, 2);
    return value;
}

int[] Ints(int start, int count) => argList.Skip(start).Take(count).Select(int.Parse).ToArray();

static int ParseId(string s)
{
    return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? int.Parse(s.AsSpan(2), NumberStyles.HexNumber)
        : int.Parse(s);
}

static void PrintUsage()
{
    Console.WriteLine("""
        uoclient [--uo <dir>] <command>
          info                                         Summarize the UO install
          land   <id> <out.png>                        Export a land tile (id may be 0x hex)
          static <id> <out.png>                        Export a static
          gump   <id> <out.png>                        Export a gump
          compose <map> <x> <y> <w> <h> <outDir>       Render a region + ownership map for restyling
                  [--no-roofs]                         Leave roofs out to expose interiors (slice both passes)
                  [--stretch-all]                      Debug: draw all land as texmap quads
          slice <spritesDir> <compositeDir>[=<restyled.png>] ...
                  [--scale N]                          Cut restyled composites into per-graphic HD sprites
                                                       (default restyled image: <compositeDir>/restyled.png,
                                                        default scale: restyled width / composite width)
          bake <bakedDir> <compositeDir>[=<restyled.png>] ...
                  [--scale N]                          Bake restyled passes into per-placement HD sprites + atlas
                                                       (primary pass first, then e.g. the --no-roofs pass)
          reassemble <map> <x> <y> <w> <h> <spritesDir|bakedDir> <out.png>
                  [--coverage] [--no-roofs]            Re-render a region from sliced sprites to check seams;
                                                       --coverage paints pixels green/yellow/red by source
          upscale <in.png> <factor> <out.png>          Nearest-neighbor upscale (a stand-in "restyle" for testing)
        """);
}
