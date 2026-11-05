using System.Text.Json;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Server gump text drawn as TazUO's Label, CroppedText and HtmlControl draw it (UniText, the FontsLoader port), in the
/// install's unicode font 1: picture sizes, line counts, crops, glyph and border pixels and colours, on the labels of
/// the saved StableGump, PetLoreGump and AuctionGump fixtures. The HTML line counts were checked against a plain greedy
/// word wrap over the same widths.
/// </summary>
[Collection("UO install")]
public sealed class UniTextTests
{
    private readonly UniFont _font;
    private readonly UniText _text;
    private readonly HueEntry[] _hues;

    public UniTextTests(UOInstallFixture fixture)
    {
        _font = fixture.Data.GetUniFont(1);
        _text = new UniText(fixture.Data.GetUniFont);
        _hues = fixture.Data.Hues.Entries;
    }

    private static (List<GumpCommand> Commands, string[] Lines) Fixture(string name)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Gumps", name + ".json")));
        string[] lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetString()!).ToArray();
        string layout = string.Concat(doc.RootElement.GetProperty("layout").EnumerateArray().Select(l => l.GetString()));
        return (ServerGump.ParseLayout(layout), lines);
    }

    private static readonly string[] TextFixtures =
    {
        "stable-live-all", "stable-live-breeding-claim-first", "stable-live-breeding-courting", "stable-live-breeding-due",
        "pet-lore-care-courting", "auction-browse", "auction-sell",
    };

    public static IEnumerable<object[]> TextFixtureNames => TextFixtures.Select(n => new object[] { n });

    private static (int Left, int Right) DrawnColumns(Bitmap32 b)
    {
        int left = int.MaxValue, right = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b[x, y] != 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
        return (left, right);
    }

    // ---- Label ----

    [Fact]
    public void LabelPutsTheGlyphOnePixelInWithABorderAllRound()
    {
        // 'I': OffsetX 0, OffsetY 4, 2 x 10, every pixel set.
        UniTextImage img = ServerGumpWindow.LabelImage(_text, _hues, "I", 1152);
        uint white = ServerGumpWindow.TextArgb(_hues, 1153);
        Assert.Equal((3 + 4, 14 + 4, 1), (img.Width, img.Height, img.Lines));
        Bitmap32 b = img.Bitmap;
        for (int y = 4; y < 14; y++)
        {
            Assert.Equal(white, b[1, y]);
            Assert.Equal(white, b[2, y]);
            Assert.Equal(UniText.BlackArgb, b[0, y]);
            Assert.Equal(UniText.BlackArgb, b[3, y]);
            Assert.Equal(0u, b[4, y]);
        }
        for (int x = 0; x <= 3; x++)
        {
            Assert.Equal(UniText.BlackArgb, b[x, 3]);
            Assert.Equal(UniText.BlackArgb, b[x, 14]);
            Assert.Equal(0u, b[x, 2]);
            Assert.Equal(0u, b[x, 15]);
        }
    }

    [Fact]
    public void LabelColoursFollowTheHueShadeAndBorderRules()
    {
        // Hue 53 (server) is client 54's shade 30, bordered.
        Bitmap32 gold = ServerGumpWindow.LabelImage(_text, _hues, "I", 53).Bitmap;
        Assert.Equal(ServerGumpWindow.TextArgb(_hues, 54), gold[1, 4]);
        Assert.Equal(UniText.BlackArgb, gold[0, 4]);
        // Client hue 0 is GetPolygoneColor's near-black: no border.
        Bitmap32 black = ServerGumpWindow.LabelImage(_text, _hues, "I", 0xFFFF).Bitmap;
        Assert.Equal(ServerGumpWindow.TextBlackArgb, black[1, 4]);
        Assert.Equal(0u, black[0, 4]);
        // Client hue 0xFFFF is FontsLoader's unhued white (alpha 0xFE), bordered.
        Bitmap32 unhued = ServerGumpWindow.LabelImage(_text, _hues, "I", 0xFFFE).Bitmap;
        Assert.Equal(UniText.UnhuedArgb, unhued[1, 4]);
        Assert.Equal(UniText.BlackArgb, unhued[0, 4]);
    }

    [Fact]
    public void EmptyLabelDrawsNothing()
    {
        Assert.Null(ServerGumpWindow.LabelImage(_text, _hues, "", 1152).Bitmap);
        Assert.Null(ServerGumpWindow.CroppedImage(_text, _hues, "", 1152, 100).Bitmap);
        Assert.Null(ServerGumpWindow.LabelImage(new UniText(_ => null), _hues, "Stable", 1152).Bitmap);
    }

    [Fact]
    public void MultilineLabelStacksItsLineHeights()
    {
        UniTextImage img = ServerGumpWindow.LabelImage(_text, _hues, "a\nbbb", 1152);
        Assert.Equal((18 + 4, 14 + 14 + 4, 2), (img.Width, img.Height, img.Lines));
        Assert.Equal(_font.Height("Ag\nAg") + 4, ServerGumpWindow.LabelImage(_text, _hues, "Ag\nAg", 1152).Height);
    }

    [Theory]
    // StableGump (live), text commands: width = font 1 width + 4, height = lowest glyph bottom + 4.
    [InlineData("stable-live-all", "Stable", 39, 18)]
    [InlineData("stable-live-all", "Name / type", 82, 20)]
    [InlineData("stable-live-all", "*", 14, 17)]
    [InlineData("stable-live-all", "Select with the gem.  * = bonded", 212, 20)]
    [InlineData("stable-live-all", "Page 1 / 1", 71, 20)]
    [InlineData("stable-live-breeding-claim-first", "Stable All Followers", 120, 18)]
    [InlineData("stable-live-breeding-claim-first", "Requirements", 78, 20)]
    // PetLoreGump (live).
    [InlineData("pet-lore-care-courting", "Animal Lore", 72, 18)]
    // AuctionGump.
    [InlineData("auction-browse", "bank 12,345 · pack 678", 150, 20)]
    [InlineData("auction-browse", "My auctions", 73, 20)]
    [InlineData("auction-browse", "Bulk Orders", 73, 18)]
    public void FixtureLabelPictureSizes(string fixture, string line, int width, int height)
    {
        (List<GumpCommand> commands, string[] lines) = Fixture(fixture);
        GumpCommand c = commands.First(c => (c.Name == "text" && lines[c.Arg(3)] == line) || (c.Name == "croppedtext" && lines[c.Arg(5)] == line));
        UniTextImage img = c.Name == "text"
            ? ServerGumpWindow.LabelImage(_text, _hues, line, c.Arg(2))
            : ServerGumpWindow.CroppedImage(_text, _hues, line, c.Arg(4), c.Arg(2));
        if (c.Name == "text")
            Assert.Equal((width, height, 1), (img.Width, img.Height, img.Lines));
        else
            Assert.Equal((c.Arg(2) + 4, UniText.HtmlLineHeight + 4, 1), (img.Width, img.Height, img.Lines));
        Assert.Equal(width, _font.Measure(line) + 4);
    }

    [Theory]
    [MemberData(nameof(TextFixtureNames))]
    public void EveryFixtureLabelIsItsUniFontSize(string fixture)
    {
        (List<GumpCommand> commands, string[] lines) = Fixture(fixture);
        foreach (GumpCommand c in commands.Where(c => c.Name == "text"))
        {
            string line = lines[c.Arg(3)];
            UniTextImage img = ServerGumpWindow.LabelImage(_text, _hues, line, c.Arg(2));
            Assert.True((_font.Measure(line) + 4, _font.Height(line) + 4) == (img.Width, img.Height), $"'{line}': {img.Width} x {img.Height}");
            // The glyphs start one pixel in, the border one before them.
            (int left, int right) = DrawnColumns(img.Bitmap);
            Assert.Equal(0, left);
            Assert.True(right <= _font.Measure(line), $"'{line}' draws to {right}");
        }
    }

    // ---- CroppedText ----

    [Theory]
    [MemberData(nameof(TextFixtureNames))]
    public void EveryFixtureCroppedTextFitsItsBoxOnOneLine(string fixture)
    {
        // The shards Fit their strings to the box with the same font, so TazUO never crops them further: one HTML line,
        // the box's width + 4, glyphs no further right than the text's own width.
        (List<GumpCommand> commands, string[] lines) = Fixture(fixture);
        foreach (GumpCommand c in commands.Where(c => c.Name == "croppedtext"))
        {
            string line = lines[c.Arg(5)];
            int width = c.Arg(2);
            Assert.True(_font.Measure(line) <= width, $"'{line}' is {_font.Measure(line)} in {width}");
            UniTextImage img = ServerGumpWindow.CroppedImage(_text, _hues, line, c.Arg(4), width);
            Assert.Equal((width + 4, 22, 1), (img.Width, img.Height, img.Lines));
            Assert.True(DrawnColumns(img.Bitmap).Right <= _font.Measure(line));
        }
    }

    [Fact]
    public void CropReproducesTheShardsFitString()
    {
        // stable-live-all's mood box (croppedtext 622 140 96 18 68 32) holds the shard's Fit of "Wonderfully Happy".
        (List<GumpCommand> commands, string[] lines) = Fixture("stable-live-all");
        GumpCommand box = commands.Single(c => c.Name == "croppedtext" && lines[c.Arg(5)].EndsWith("..."));
        Assert.Equal(("Wonderfully H...", 96), (lines[box.Arg(5)], box.Arg(2)));

        // TazUO keeps chars while they fit in 96 - (3 dots of 2 + 3) = 87: "Wonderfully H" is 82, 'a' would make 88.
        Assert.Equal(82, _font.Measure("Wonderfully H"));
        Assert.Equal(88, _font.Measure("Wonderfully Ha"));
        Assert.Equal("Wonderfully H...", _text.CropToWidth(1, "Wonderfully Happy", 96, true));
        UniTextImage cropped = ServerGumpWindow.CroppedImage(_text, _hues, "Wonderfully Happy", 68, 96);
        UniTextImage fit = ServerGumpWindow.CroppedImage(_text, _hues, "Wonderfully H...", 68, 96);
        Assert.Equal((100, 22, 1), (cropped.Width, cropped.Height, cropped.Lines));
        Assert.Equal(fit.Bitmap.Pixels, cropped.Bitmap.Pixels);
    }

    [Theory]
    [InlineData("Stable All Followers", 100)]
    [InlineData("Selecting a pet never claims it.", 120)]
    [InlineData("bank 12,345 · pack 678", 90)]
    [InlineData("What the young may inherit", 60)]
    public void CroppedTextOverItsBoxEndsInDotsWithinIt(string line, int width)
    {
        string crop = _text.CropToWidth(1, line, width, true);
        Assert.EndsWith("...", crop);
        string kept = crop.Substring(0, crop.Length - 3);
        Assert.StartsWith(kept, line);
        Assert.True(_font.Measure(kept) <= width - 9);
        Assert.True(_font.Measure(line.Substring(0, kept.Length + 1)) > width - 9);
        UniTextImage img = ServerGumpWindow.CroppedImage(_text, _hues, line, 1152, width);
        Assert.Equal((width + 4, 22, 1), (img.Width, img.Height, img.Lines));
        Assert.True(DrawnColumns(img.Bitmap).Right <= _font.Measure(crop));
    }

    [Fact]
    public void CroppedTextIgnoresNewlinesAndTakesHtmlColour()
    {
        Assert.Equal(1, ServerGumpWindow.CroppedImage(_text, _hues, "Stable\nAll", 1152, 200).Lines);
        Bitmap32 red = ServerGumpWindow.CroppedImage(_text, _hues, "<BASEFONT COLOR=#FF0000>I", 1152, 200).Bitmap;
        Assert.Equal(0xFFFF0000, red[1, 4]);
        Assert.Equal(UniText.BlackArgb, red[0, 4]);
    }

    // ---- HtmlControl ----

    [Theory]
    // auction-browse's title: SIZE=4 is font 1, <B> solid.
    [InlineData("auction-browse", 300, 1, 0xFFF4F4F4)]
    // stable-live-breeding-claim-first's inheritance block, wrapped in 264.
    [InlineData("stable-live-breeding-claim-first", 264, 9, 0xFFB4B4B4)]
    // stable-live-breeding-claim-first's refusal, italic.
    [InlineData("stable-live-breeding-claim-first", 704, 1, 0xFFFF9090)]
    // pet-lore-care-courting's care note (htmlgump 268 214 348 36): two lines fill its 36 exactly.
    [InlineData("pet-lore-care-courting", 348, 2, 0xFFE0E0E0)]
    public void FixtureHtmlWrapsInEighteenPixelLines(string fixture, int width, int lineCount, uint colour)
    {
        (List<GumpCommand> commands, string[] lines) = Fixture(fixture);
        GumpCommand c = commands.First(c => c.Name == "htmlgump" && c.Arg(2) == width);
        Assert.Equal((0, 0), (c.Arg(5), c.Arg(6)));
        UniTextImage img = ServerGumpWindow.HtmlImage(_text, _hues, lines[c.Arg(4)], width, false, false, 0);
        Assert.Equal((width + 4, lineCount * 18 + 4, lineCount), (img.Width, img.Height, img.Lines));
        Assert.Contains(colour, img.Bitmap.Pixels);
        // No border asked for: only the BASEFONT colour (and the solid/italic fill of it) is drawn.
        Assert.All(img.Bitmap.Pixels, p => Assert.True(p == 0 || p == colour, $"{p:X8}"));
    }

    [Fact]
    public void HtmlWidthsAndColoursFollowHtmlControl()
    {
        Assert.Equal(300, ServerGumpWindow.HtmlTextWidth(300, false, false, 0));
        Assert.Equal(284, ServerGumpWindow.HtmlTextWidth(300, false, true, 0));
        Assert.Equal(283, ServerGumpWindow.HtmlTextWidth(300, true, false, 0));
        Assert.Equal(267, ServerGumpWindow.HtmlTextWidth(300, true, true, 0));
        Assert.Equal(292, ServerGumpWindow.HtmlTextWidth(300, true, false, 0x7C00));

        Assert.Equal((ServerGumpWindow.TextBlackArgb, ServerGumpWindow.TextBlackArgb), ServerGumpWindow.HtmlColors(_hues, true, false, 0));
        Assert.Equal((UniText.UnhuedArgb, ServerGumpWindow.TextBlackArgb), ServerGumpWindow.HtmlColors(_hues, false, false, 0));
        Assert.Equal((UniText.UnhuedArgb, UniText.NoHtmlColor), ServerGumpWindow.HtmlColors(_hues, false, true, 0));
        Assert.Equal(0xFFFF0000u, ServerGumpWindow.HtmlColors(_hues, true, false, 0x7C00).HtmlColor);
        Assert.Equal(0xFEFFFFFFu, ServerGumpWindow.HtmlColors(_hues, true, false, ServerGumpWindow.HtmlColorPart(0x7FFF)).HtmlColor);

        Assert.Equal(4, ServerGumpWindow.HtmlTextOffset(true));
        Assert.Equal(0, ServerGumpWindow.HtmlTextOffset(false));
        Assert.Equal(166 - 100 + 8, ServerGumpWindow.HtmlScrollMax(166, 100, true));
        Assert.Equal(0, ServerGumpWindow.HtmlScrollMax(40, 100, false));
    }

    [Fact]
    public void HtmlLineBreaksAlignmentAndBodyBackground()
    {
        // <BR> and the end of a <P> break the line; the wrap breaks at spaces.
        Assert.Equal(3, ServerGumpWindow.HtmlImage(_text, _hues, "a<BR>b<BR>c", 200, false, false, 0).Lines);
        Assert.Equal(2, ServerGumpWindow.HtmlImage(_text, _hues, "<P>one</P>two", 200, false, false, 0).Lines);
        Assert.Equal(2, ServerGumpWindow.HtmlImage(_text, _hues, "Stable Stable Stable", 100, false, false, 0).Lines);

        // <CENTER>: the line starts (width - 8) / 2 - line / 2 in. "I" (3 wide) in 104 + 4: 50 - 1 = 49, the glyph
        // (no border on HTML) at 50-51. The closing </CENTER> breaks the line.
        UniTextImage centered = ServerGumpWindow.HtmlImage(_text, _hues, "<CENTER>I</CENTER>", 104, false, false, 0);
        Assert.Equal(2, centered.Lines);
        Assert.Equal((50, 51), DrawnColumns(centered.Bitmap));

        // A body bgcolor fills the empty pixels only when there is no background.
        Bitmap32 bg = ServerGumpWindow.HtmlImage(_text, _hues, "<BODY BGCOLOR=#204060>I", 50, false, false, 0).Bitmap;
        Assert.Equal(0xFF204060u, bg[30, 10]);
        Bitmap32 none = ServerGumpWindow.HtmlImage(_text, _hues, "<BODY BGCOLOR=#204060>I", 50, true, false, 0).Bitmap;
        Assert.Equal(0u, none[20, 10]);
    }

    [Theory]
    [InlineData("#F4F4F4", 0xFFF4F4F4)]
    [InlineData("#0xF4F4F4", 0xFFF4F4F4)]
    [InlineData("#FFF", 0xFF000FFF)] // not expanded: TazUO reads the hex as a number
    [InlineData("red", 0xFFFF0000)]
    [InlineData("White", 0xFFFEFEFF)]
    [InlineData("0000FFFF", 0xFFFF0000)] // a bare number is packed RGBA
    [InlineData("nonsense", 0u)]
    public void HtmlColoursReadAsTazUOReadsThem(string value, uint argb)
    {
        Assert.Equal(argb, UniText.ReadColor(value));
    }

    // ---- StbTextBox (textentry / textentrylimited) ----

    /// <summary>The StableGump rename entry: textentrylimited 568 336 136 20, hue 0, EntryId 1, limit 16.</summary>
    private static GumpCommand RenameEntry(out string name)
    {
        (List<GumpCommand> commands, string[] lines) = Fixture("stable-three-pets");
        GumpCommand entry = Assert.Single(commands, c => c.Name == "textentrylimited");
        Assert.Equal((136, 20, 16), (entry.Arg(2), entry.Arg(3), entry.TextLimit));
        name = lines[entry.Arg(6)];
        return entry;
    }

    [Fact]
    public void RenameEntryDrawsItsNameInFontOneWithTheCaretAtTheEnd()
    {
        GumpCommand entry = RenameEntry(out string name);
        Assert.Equal("Shadowmere", name);
        int width = entry.Arg(2);

        // The picture is laid out in the entry's width: 140 wide, one line, in the hue + 1's shade with the border.
        UniTextImage img = ServerGumpWindow.EntryImage(_text, _hues, name, entry.Arg(4), width);
        Assert.Equal((width + 4, 1), (img.Width, img.Lines));
        // The same pixels as a label of it, the rest of the width empty.
        Bitmap32 label = ServerGumpWindow.LabelImage(_text, _hues, name, entry.Arg(4)).Bitmap;
        Assert.Equal(label.Height, img.Height);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                Assert.Equal(x < label.Width ? label[x, y] : 0u, img.Bitmap[x, y]);

        // StbTextBox focuses with the caret after the last char: x is the name's width in font 1.
        Assert.Equal((_font.Measure(name), 0), ServerGumpWindow.EntryCaret(_text, name, name.Length, width));
        Assert.Equal((0, 0), ServerGumpWindow.EntryCaret(_text, name, 0, width));
        Assert.Equal((_font.GetCharWidth('S'), 0), ServerGumpWindow.EntryCaret(_text, name, 1, width));
        Assert.Equal((0, 0), ServerGumpWindow.EntryCaret(_text, "", 0, width));

        // The caret is "_" in the same font and border, unwrapped.
        UniTextImage caret = ServerGumpWindow.EntryCaretImage(_text, _hues, entry.Arg(4));
        Assert.Equal(ServerGumpWindow.LabelImage(_text, _hues, "_", entry.Arg(4)).Width, caret.Width);
    }

    [Fact]
    public void SixteenWideCharsOverflowTheRenameEntryAndWrapUnderIt()
    {
        GumpCommand entry = RenameEntry(out _);
        int width = entry.Arg(2), height = entry.Arg(3);
        int w = _font.GetCharWidth('W');
        string full = new string('W', entry.TextLimit);
        Assert.True(w * full.Length > width); // the limit is in chars, not pixels

        // One word longer than the line breaks where the next char would pass the width.
        int fit = width / w;
        List<(int CharStart, int CharCount, int Width, int MaxHeight)> lines =
            _text.Lines(ServerGumpWindow.GumpFont, full, width, UniTextAlign.Left, ServerGumpWindow.EntryStyle);
        Assert.Equal(2, lines.Count);
        Assert.Equal((0, fit, fit * w), (lines[0].CharStart, lines[0].CharCount, lines[0].Width));
        Assert.Equal((fit, full.Length - fit), (lines[1].CharStart, lines[1].CharCount));
        Assert.Equal(2, ServerGumpWindow.EntryImage(_text, _hues, full, entry.Arg(4), width).Lines);

        // The caret stays at the first line's end until a char of the second, then goes down a line, below the
        // 20-pixel entry's top line; the entry's clip shows only what fits in its height.
        Assert.Equal((fit * w, 0), ServerGumpWindow.EntryCaret(_text, full, fit, width));
        Assert.Equal((w, lines[0].MaxHeight), ServerGumpWindow.EntryCaret(_text, full, fit + 1, width));
        (int endX, int endY) = ServerGumpWindow.EntryCaret(_text, full, full.Length, width);
        Assert.Equal(((full.Length - fit) * w, lines[0].MaxHeight), (endX, endY));
        Assert.True(endY + lines[1].MaxHeight > height);

        // A sixteen-char name of narrower letters stays on the entry's one line.
        string name = "Shadowmere Steed";
        Assert.Equal(entry.TextLimit, name.Length);
        Assert.True(_font.Measure(name) <= width);
        Assert.Equal(1, ServerGumpWindow.EntryImage(_text, _hues, name, entry.Arg(4), width).Lines);
        Assert.Equal((_font.Measure(name), 0), ServerGumpWindow.EntryCaret(_text, name, name.Length, width));
    }

    [Fact]
    public void AClickPutsTheCaretBeforeTheNearerHalfOfAChar()
    {
        GumpCommand entry = RenameEntry(out string name);
        int width = entry.Arg(2);
        for (int k = 0; k < name.Length; k++)
        {
            int x = ServerGumpWindow.EntryCaret(_text, name, k, width).X, cw = _font.GetCharWidth(name[k]);
            Assert.Equal(k, ServerGumpWindow.EntryIndexAt(_text, name, width, x, 5));
            Assert.Equal(k + 1, ServerGumpWindow.EntryIndexAt(_text, name, width, x + cw / 2f, 5));
        }
        Assert.Equal(name.Length, ServerGumpWindow.EntryIndexAt(_text, name, width, width - 1, 5));
        Assert.Equal(0, ServerGumpWindow.EntryIndexAt(_text, name, width, -3, 5));
        Assert.Equal(0, ServerGumpWindow.EntryIndexAt(_text, "", width, 50, 5));

        // On a wrapped entry, a click below the first line's height lands in the second.
        string full = new string('W', entry.TextLimit);
        int fit = width / _font.GetCharWidth('W');
        int lineHeight = _text.Lines(ServerGumpWindow.GumpFont, full, width, UniTextAlign.Left, ServerGumpWindow.EntryStyle)[0].MaxHeight;
        Assert.Equal(fit, ServerGumpWindow.EntryIndexAt(_text, full, width, 0, lineHeight));
        Assert.Equal(full.Length, ServerGumpWindow.EntryIndexAt(_text, full, width, width, lineHeight));
    }

    [Fact]
    public void SelectionCoversItsCharsInFontOneWidths()
    {
        GumpCommand entry = RenameEntry(out string name);
        int width = entry.Arg(2);
        int h = _text.Lines(ServerGumpWindow.GumpFont, name, width, UniTextAlign.Left, ServerGumpWindow.EntryStyle)[0].MaxHeight;
        int s = _font.GetCharWidth('S'), ha = _font.GetCharWidth('h') + _font.GetCharWidth('a');

        Assert.Equal(new[] { (s, 1, ha, h + 1) }, ServerGumpWindow.EntrySelection(_text, name, width, 1, 3));
        Assert.Equal(new[] { (s, 1, ha, h + 1) }, ServerGumpWindow.EntrySelection(_text, name, width, 3, 1));
        // Running to the end of the line takes the rest of it.
        Assert.Equal(new[] { (0, 1, _font.Measure(name), h + 1) }, ServerGumpWindow.EntrySelection(_text, name, width, 0, name.Length));
        Assert.Empty(ServerGumpWindow.EntrySelection(_text, name, width, 4, 4));
    }

    [Fact]
    public void ATypedCharFontOneCannotDrawIsRefused()
    {
        const char none = '\u0001';
        Assert.False(_font.TryGetGlyph(none, out _));
        Assert.Equal(-1, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Shadowm"));
        Assert.Equal(-1, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Shadow "));
        Assert.Equal(6, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Shadow" + none));
        Assert.Equal(2, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Sh" + none + "adow"));
        // A paste goes in whole, and so does a deletion.
        Assert.Equal(-1, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Shadow" + none + none));
        Assert.Equal(-1, ServerGumpWindow.EntryRefusedChar(_text, "Shadow", "Shado"));
    }
}
