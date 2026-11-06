using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity.UI;
using static UOClient.Unity.UI.ServerGumpWindow;

// usage: gumppreview <uoDataDir> <fixture.json> <outDir> [page]
//
// Renders a saved shard gump (DotNet/UOClient.Tests/Fixtures/Gumps/*.json: its layout and lines) as the Unity
// ServerGumpWindow draws it as it opens, from the UO data alone: no Session, server or Editor. Writes
// <outDir>/<fixture name>[-pN].png. A software copy of ServerGumpWindow.Draw / DrawCommand, DrawHtml, DrawScrollbar,
// DrawEntry's text and UiManager.DrawResizePic / DrawTiled over the same Layout helpers (LabelImage, CroppedImage,
// HtmlImage, EntryImage, HtmlParts, HtmlScrollbar, PicHue, CheckerFaded, GumpControls) and art (UOData.GetGump /
// GetStatic, HueApplier, UniText on unifont1). Not drawn: hover/pressed button art, tooltips, the entry caret and
// selection, HD art, Unity's texture sampling and ui.Scale. Pages switch locally, so [page] picks the one shown (1).
//
// usage: gumppreview <uoDataDir> --ux <outDir>
// Synthetic health bars and the classic status window (UxPreview.cs), renderer-only.
//
// usage: gumppreview <uoDataDir> --spellbook <outDir>
// The classic Magery / Necromancy / Chivalry books from synthetic content bits (SpellbookPreview.cs), renderer-only.
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: gumppreview <uoDataDir> <fixture.json> <outDir> [page]\n       gumppreview <uoDataDir> --ux <outDir>\n"
        + "       gumppreview <uoDataDir> --spellbook <outDir>");
    return 2;
}
if (args[1] == "--ux")
    return UxPreview.Run(args[0], args[2]);
if (args[1] == "--spellbook")
    return SpellbookPreview.Run(args[0], args[2]);
string dataDir = args[0], fixturePath = args[1], outDir = args[2];
int shownPage = args.Length > 3 ? int.Parse(args[3]) : 1;

using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
JsonElement root = doc.RootElement;
var layout = new List<string>();
foreach (JsonElement c in root.GetProperty("layout").EnumerateArray())
    layout.Add(c.GetString());
var lines = new List<string>();
foreach (JsonElement l in root.GetProperty("lines").EnumerateArray())
    lines.Add(l.GetString());
var gump = new ServerGump
{
    X = root.GetProperty("x").GetInt32(),
    Y = root.GetProperty("y").GetInt32(),
    Commands = ServerGump.ParseLayout(string.Concat(layout)),
    Lines = lines.ToArray(),
};

using var data = new UOData(dataDir);
var text = new UniText(data.GetUniFont);
HueEntry[] hues = data.Hues.Entries;
string clilocPath = Path.Combine(data.Directory, "Cliloc.enu");
Cliloc cliloc = File.Exists(clilocPath) ? new Cliloc(clilocPath) : null;
var controls = new GumpControls(gump);
List<GumpCommand> commands = gump.Commands;

// ---- art, as GumpArt.Gump / GumpArt.Item ------------------------------------------------------------------------
var gumpCache = new Dictionary<(int, int), Bitmap32>();
var itemCache = new Dictionary<(int, int), Bitmap32>();
var textCache = new Dictionary<GumpCommand, UniTextImage>();

Bitmap32 Gump(int id, int hue = 0)
{
    hue &= 0x3FFF;
    if (gumpCache.TryGetValue((id, hue), out Bitmap32 bmp))
        return bmp;
    bmp = id >= 0 && id < 0x10000 ? data.GetGump(id) : null;
    if (bmp != null && hue > 0 && hue <= hues.Length)
        bmp = HueApplier.Apply(bmp, hues[hue - 1], false);
    return gumpCache[(id, hue)] = bmp;
}

Bitmap32 Item(int graphic, int hue = 0)
{
    hue &= 0x3FFF;
    if (itemCache.TryGetValue((graphic, hue), out Bitmap32 bmp))
        return bmp;
    bmp = data.GetStatic(graphic);
    if (bmp != null && hue > 0 && hue <= hues.Length)
        bmp = HueApplier.Apply(bmp, hues[hue - 1], (data.TileData.GetStatic(graphic).Flags & TileFlags.PartialHue) != 0);
    return itemCache[(graphic, hue)] = bmp;
}

string ClilocText(int number, string clilocArgs)
{
    if (cliloc == null)
        return $"#{number}";
    return string.IsNullOrEmpty(clilocArgs) ? cliloc.Get(number) ?? $"#{number}" : cliloc.Format(number, clilocArgs);
}

// ServerGumpWindow.TextImage.
UniTextImage TextImage(GumpCommand c)
{
    if (textCache.TryGetValue(c, out UniTextImage img))
        return img;
    switch (c.Name)
    {
        case "text":
            img = LabelImage(text, hues, gump.Line(c.Arg(3)), c.Arg(2));
            break;
        case "croppedtext":
            img = CroppedImage(text, hues, gump.Line(c.Arg(5)), c.Arg(4), c.Arg(2));
            break;
        default:
        {
            (bool background, bool scrollbar, int color) = HtmlParts(c);
            string html = c.Name == "htmlgump" ? gump.Line(c.Arg(4))
                : c.Name == "xmfhtmltok" ? ClilocText(c.Arg(7), c.ClilocArgs)
                : ClilocText(c.Arg(4), null);
            img = HtmlImage(text, hues, html, c.Arg(2), background, scrollbar, color, c.Name == "htmlgump" && c.Arg(7) == 1);
            break;
        }
    }
    return textCache[c] = img;
}

// ServerGumpWindow.Bounds.
(int X, int Y, int W, int H) Bounds(GumpCommand c)
{
    int x = c.Arg(0), y = c.Arg(1);
    switch (c.Name)
    {
        case "resizepic":
        case "picinpic":
        case "picinpichued":
        case "picinpicphued":
            return (x, y, c.Arg(3), c.Arg(4));
        case "gumppictiled":
        case "checkertrans":
        case "htmlgump":
        case "xmfhtmlgump":
        case "xmfhtmlgumpcolor":
        case "xmfhtmltok":
        case "croppedtext":
        case "textentry":
        case "textentrylimited":
            return (x, y, c.Arg(2), c.Arg(3));
        case "gumppic":
        case "gumppichued":
        case "gumppicphued":
        {
            Bitmap32 img = Gump(c.Arg(2));
            return (x, y, img?.Width ?? 0, img?.Height ?? 0);
        }
        case "tilepic":
        case "tilepichue":
        {
            Bitmap32 img = Item(c.Arg(2));
            return (x, y, img?.Width ?? 0, img?.Height ?? 0);
        }
        case "button":
        case "buttontileart":
        case "checkbox":
        case "radio":
        {
            Bitmap32 img = Gump(c.Arg(2));
            return (x, y, img?.Width ?? 20, img?.Height ?? 20);
        }
        case "text":
        {
            UniTextImage img = TextImage(c);
            return (x, y, img.Bitmap == null ? 0 : img.Width, img.Bitmap == null ? 0 : img.Height);
        }
        default:
            return (x, y, 0, 0);
    }
}

// ---- software canvas: gump-local coordinates, clipped, GUI.color's alpha as tint --------------------------------
// ServerGumpWindow.MeasureSize.
int width = 50, height = 50;
foreach (GumpCommand c in commands)
{
    (int x, int y, int w, int h) = Bounds(c);
    if (w == 0 && h == 0)
        continue;
    width = Math.Max(width, x + w);
    height = Math.Max(height, y + h);
}
var canvas = new Bitmap32(width, height);
for (int i = 0; i < canvas.Pixels.Length; i++)
    canvas.Pixels[i] = 0xFF606060; // mid-grey where the world would show
var full = (X: 0, Y: 0, W: width, H: height);
float tint = 1f;

(int X, int Y, int W, int H) Intersect((int X, int Y, int W, int H) a, (int X, int Y, int W, int H) b)
{
    int x0 = Math.Max(a.X, b.X), y0 = Math.Max(a.Y, b.Y);
    int x1 = Math.Min(a.X + a.W, b.X + b.W), y1 = Math.Min(a.Y + a.H, b.Y + b.H);
    return (x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
}

void Blend(int dx, int dy, uint s, float alpha)
{
    uint a = (uint)((s >> 24) * alpha + 0.5f);
    if (a == 0)
        return;
    uint d = canvas.Pixels[dy * width + dx];
    uint Mix(int sh) => ((((s >> sh) & 0xFF) * a + ((d >> sh) & 0xFF) * (255 - a)) / 255) << sh;
    canvas.Pixels[dy * width + dx] = 0xFF000000 | Mix(16) | Mix(8) | Mix(0);
}

// The (sx, sy, w, h) part of src at (x, y), source-over, inside clip.
void BlitPart(Bitmap32 src, int x, int y, int sx, int sy, int w, int h, (int X, int Y, int W, int H) clip)
{
    if (src == null)
        return;
    clip = Intersect(clip, full);
    for (int py = 0; py < h; py++)
        for (int px = 0; px < w; px++)
        {
            int dx = x + px, dy = y + py, ix = sx + px, iy = sy + py;
            if (dx < clip.X || dy < clip.Y || dx >= clip.X + clip.W || dy >= clip.Y + clip.H)
                continue;
            if (ix < 0 || iy < 0 || ix >= src.Width || iy >= src.Height)
                continue;
            Blend(dx, dy, src.Pixels[iy * src.Width + ix], tint);
        }
}

void Blit(Bitmap32 src, int x, int y, (int X, int Y, int W, int H) clip)
{
    if (src != null)
        BlitPart(src, x, y, 0, 0, src.Width, src.Height, clip);
}

// UiManager.DrawTiled: tiles from the corner, cut to the w x h box.
void Tiled(Bitmap32 img, int x, int y, int w, int h, (int X, int Y, int W, int H) clip)
{
    if (img == null || w <= 0 || h <= 0 || img.Width <= 0 || img.Height <= 0)
        return;
    var box = Intersect(clip, (x, y, w, h));
    for (int ty = 0; ty < h; ty += img.Height)
        for (int tx = 0; tx < w; tx += img.Width)
            Blit(img, x + tx, y + ty, box);
}

// UiManager.DrawResizePic.
void ResizePic(int id, int x, int y, int w, int h, (int X, int Y, int W, int H) clip)
{
    Bitmap32 tl = Gump(id), t = Gump(id + 1), tr = Gump(id + 2);
    Bitmap32 l = Gump(id + 3), m = Gump(id + 4), rr = Gump(id + 5);
    Bitmap32 bl = Gump(id + 6), b = Gump(id + 7), br = Gump(id + 8);
    if (tl == null || br == null)
    {
        Blit(tl, x, y, clip);
        return;
    }
    int left = tl.Width, top = tl.Height, right = br.Width, bottom = br.Height;
    Tiled(m, x + left, y + top, w - left - right, h - top - bottom, clip);
    Tiled(t, x + left, y, w - left - (tr?.Width ?? right), top, clip);
    Tiled(b, x + (bl?.Width ?? left), y + h - bottom, w - (bl?.Width ?? left) - right, bottom, clip);
    Tiled(l, x, y + top, left, h - top - (bl?.Height ?? bottom), clip);
    Tiled(rr, x + w - right, y + (tr?.Height ?? top), right, h - (tr?.Height ?? top) - bottom, clip);
    Blit(tl, x, y, clip);
    Blit(tr, x + w - (tr?.Width ?? 0), y, clip);
    Blit(bl, x, y + h - (bl?.Height ?? 0), clip);
    Blit(br, x + w - right, y + h - bottom, clip);
}

// ServerGumpWindow.ScrollParts.
HtmlScrollParts ScrollParts(HtmlScrollbarKind kind, int h)
{
    if (kind == HtmlScrollbarKind.Flag)
    {
        Bitmap32 up = Gump(ScrollFlagUp), down = Gump(ScrollFlagDown), flag = Gump(ScrollFlagThumb);
        return new HtmlScrollParts(up?.Width ?? 0, up?.Height ?? 0, down?.Width ?? 0, down?.Height ?? 0,
            flag?.Width ?? 0, flag?.Height ?? 0, flag?.Width ?? 0, h);
    }
    Bitmap32 u = Gump(ScrollBarUp), d = Gump(ScrollBarDown), s = Gump(ScrollBarSlider), tp = Gump(ScrollBarTop);
    return new HtmlScrollParts(u?.Width ?? 0, u?.Height ?? 0, d?.Width ?? 0, d?.Height ?? 0,
        s?.Width ?? 0, s?.Height ?? 0, tp?.Width ?? 0, h);
}

// ServerGumpWindow.DrawScrollbar (ox, oy: the html control's corner).
void Scrollbar(HtmlScroll scroll, in HtmlScrollParts p, int x, int ox, int oy, (int X, int Y, int W, int H) clip)
{
    if (scroll.Kind == HtmlScrollbarKind.Flag)
    {
        if (scroll.Max != 0)
            Blit(Gump(ScrollFlagThumb), ox + x, oy + scroll.ThumbY(p), clip);
        return;
    }
    int h = p.Height;
    if (h <= 0)
        return;
    Bitmap32 top = Gump(ScrollBarTop), middle = Gump(ScrollBarMiddle), bottom = Gump(ScrollBarBottom);
    int topH = top?.Height ?? 0, bottomH = bottom?.Height ?? 0;
    int middleH = h - p.UpH - p.DownH - topH - bottomH;
    if (middleH > 0)
    {
        Blit(top, ox + x, oy + p.UpH, clip);
        Tiled(middle, ox + x, oy + p.UpH + topH, p.BarW, middleH, clip);
        Blit(bottom, ox + x, oy + h - p.DownH - bottomH, clip);
    }
    else
    {
        middleH = h - p.UpH - p.DownH;
        Tiled(middle, ox + x, oy + p.UpH, p.BarW, middleH, clip);
    }
    Blit(Gump(ScrollBarUp), ox + x, oy, clip);
    Blit(Gump(ScrollBarDown), ox + x, oy + h - p.DownH, clip);
    if (scroll.Max > 0 && middleH > 0)
        Blit(Gump(ScrollBarSlider), ox + x + ((p.BarW - p.ThumbW) >> 1), oy + scroll.ThumbY(p), clip);
}

// ServerGumpWindow.DrawHtml as it opens: scrolled to the top.
void Html(GumpCommand c)
{
    (bool background, _, _) = HtmlParts(c);
    UniTextImage img = TextImage(c);
    var r = (X: c.Arg(0), Y: c.Arg(1), W: c.Arg(2), H: c.Arg(3));
    var scroll = new HtmlScroll(HtmlScrollbar(c));
    bool hasBar = scroll.Kind != HtmlScrollbarKind.None;
    scroll.SetMax(img.Bitmap == null ? 0 : HtmlScrollMax(img.Height, r.H, background));
    HtmlScrollParts parts = ScrollParts(scroll.Kind, r.H);
    if (background)
        ResizePic(HtmlBackgroundGump, r.X, r.Y, r.W - (hasBar ? HtmlScrollbarWidth : 0), r.H, r);
    if (hasBar)
        Scrollbar(scroll, parts, HtmlScrollbarX(r.W), r.X, r.Y, r);
    if (img.Bitmap != null)
    {
        int off = HtmlTextOffset(background);
        Blit(img.Bitmap, r.X + off, r.Y + off - scroll.Value, Intersect(r, (r.X + off, r.Y + off, r.W - off, r.H - off)));
    }
}

// ServerGumpWindow.Draw / DrawCommand, as the gump opens (nothing pressed, hovered or focused).
HashSet<int> faded = CheckerFaded(commands, Bounds);
int page = 0, drawn = 0;
for (int i = 0; i < commands.Count; i++)
{
    GumpCommand c = commands[i];
    if (c.Name == "page")
    {
        page = c.Arg(0);
        continue;
    }
    if (page != 0 && page != shownPage)
        continue;
    tint = faded.Contains(i) ? CheckerAlpha : 1f;
    int x = c.Arg(0), y = c.Arg(1);
    switch (c.Name)
    {
        case "resizepic":
            ResizePic(c.Arg(2), x, y, c.Arg(3), c.Arg(4), full);
            break;
        case "gumppic":
        case "gumppichued":
        case "gumppicphued":
            Blit(Gump(c.Arg(2), PicHue(c.Named("hue"))), x, y, full);
            break;
        case "picinpic":
        case "picinpichued":
        case "picinpicphued":
            if (c.Arg(3) > 0 && c.Arg(4) > 0)
                BlitPart(Gump(c.Arg(2)), x, y, c.Arg(5), c.Arg(6), c.Arg(3), c.Arg(4), full);
            break;
        case "gumppictiled":
            Tiled(Gump(c.Arg(4)), x, y, c.Arg(2), c.Arg(3), full);
            break;
        case "tilepic":
        case "tilepichue":
            Blit(Item(c.Arg(2), c.Name == "tilepichue" ? c.Arg(3) : 0), x, y, full);
            break;
        case "checkertrans":
            // 50% black over the box; what it covers was faded by CheckerFaded.
            for (int py = Math.Max(0, y); py < Math.Min(height, y + c.Arg(3)); py++)
                for (int px = Math.Max(0, x); px < Math.Min(width, x + c.Arg(2)); px++)
                    Blend(px, py, 0xFF000000, CheckerAlpha);
            break;
        case "text":
        case "croppedtext":
            Blit(TextImage(c).Bitmap, x, y, full);
            break;
        case "htmlgump":
        case "xmfhtmlgump":
        case "xmfhtmlgumpcolor":
        case "xmfhtmltok":
            Html(c);
            break;
        case "button":
        case "buttontileart":
        {
            Bitmap32 up = Gump(c.Arg(2));
            if (up == null)
                break;
            Blit(up, x, y, full);
            if (c.Name == "buttontileart")
                Blit(Item(c.Arg(7), c.Arg(8)), x + c.Arg(9), y + c.Arg(10), full);
            break;
        }
        case "checkbox":
        case "radio":
            Blit(Gump(controls.IsChecked(i) ? c.Arg(3) : c.Arg(2)), x, y, full);
            break;
        case "textentry":
        case "textentrylimited":
            Blit(EntryImage(text, hues, controls.Text(i), c.Arg(4), c.Arg(2)).Bitmap, x, y, (x, y, c.Arg(2), c.Arg(3)));
            break;
        default:
            continue;
    }
    drawn++;
}

Directory.CreateDirectory(outDir);
string name = Path.GetFileNameWithoutExtension(fixturePath) + (shownPage == 1 ? "" : $"-p{shownPage}");
string outPath = Path.Combine(outDir, name + ".png");
Png.Write(outPath, canvas);
Console.WriteLine($"{outPath}: {width}x{height}, page {shownPage}, {drawn} of {commands.Count} commands drawn, " +
                  $"cliloc {(cliloc == null ? "missing" : "loaded")}");
return 0;
