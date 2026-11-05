using System.Text.Json;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

/// <summary>
/// Unicode font 1 from the install's unifont1.mul, measured as TazUO's FontsLoader does (UniFont.cs cites the lines).
/// The widths are the real file's; the shard's GumpStyle table (GumpStyle.cs:41-45, copied in LiveTamingGumps) was
/// made from the same file, so it checks every ASCII advance independently.
/// </summary>
[Collection("UO install")]
public sealed class UniFontTests
{
    private readonly UOData _data;
    private readonly UniFont _font;

    public UniFontTests(UOInstallFixture fixture)
    {
        _data = fixture.Data;
        _font = fixture.Data.GetUniFont(1);
    }

    [Fact]
    public void LoadsUnifont1FromTheDataFolder()
    {
        Assert.True(File.Exists(Path.Combine(_data.Directory, "unifont1.mul")));
        Assert.NotNull(_font);
        Assert.Same(_font, _data.GetUniFont(1));
        Assert.Null(_data.GetUniFont(20));
        Assert.Null(_data.GetUniFont(-1));
    }

    [Theory]
    // char, OffsetX, OffsetY, Width, Height, advance
    [InlineData('A', 0, 4, 7, 10, 8)]
    [InlineData('W', 0, 4, 11, 10, 12)]
    [InlineData('I', 0, 4, 2, 10, 3)]
    [InlineData('a', 0, 8, 5, 6, 6)]
    [InlineData('g', 0, 8, 5, 8, 6)]
    [InlineData('m', 0, 8, 8, 6, 9)]
    [InlineData('f', 0, 3, 5, 11, 6)]
    [InlineData('.', 0, 12, 2, 2, 3)]
    [InlineData('|', 0, 3, 1, 13, 2)]
    [InlineData('_', 0, 15, 7, 1, 8)]
    [InlineData('(', 0, 2, 3, 14, 4)]
    [InlineData('0', 0, 4, 7, 10, 8)]
    public void GlyphHeadersAndAdvances(char c, int ox, int oy, int w, int h, int advance)
    {
        Assert.True(_font.TryGetGlyph(c, out UniGlyph g));
        Assert.Equal((ox, oy, w, h), (g.OffsetX, g.OffsetY, g.Width, g.Height));
        Assert.Equal(advance, g.Advance);
        Assert.Equal(advance, _font.GetCharWidth(c));
        Assert.Equal(((w - 1) / 8 + 1) * h, g.Data.Length);
    }

    [Fact]
    public void AsciiAdvancesMatchTheShardsTable()
    {
        for (char c = ' '; c <= '~'; c++)
            Assert.True(LiveTamingGumps.Measure(c.ToString()) == _font.GetCharWidth(c), $"'{c}': table {LiveTamingGumps.Measure(c.ToString())}, font {_font.GetCharWidth(c)}");
    }

    [Fact]
    public void SpaceHasNoGlyphAndAdvancesEight()
    {
        Assert.False(_font.TryGetGlyph(' ', out _));
        Assert.Equal(UniFont.SpaceWidth, _font.GetCharWidth(' '));
        Assert.Equal(16, _font.Measure("  "));
    }

    [Fact]
    public void EmptyAndNullMeasureNothing()
    {
        Assert.Equal(0, _font.Measure(""));
        Assert.Equal(0, _font.Measure(null));
        Assert.Equal(0, _font.Height(""));
        Assert.Equal(0, _font.Height(null));
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\t')]
    [InlineData('\r')]
    [InlineData('')]
    [InlineData('￿')]
    public void MissingGlyphsTakeNoRoom(char c)
    {
        Assert.False(_font.TryGetGlyph(c, out _));
        Assert.Equal(0, _font.GetCharWidth(c));
        Assert.Equal(12, _font.Measure($"a{c}b"));
        Assert.Equal(14, _font.Height($"a{c}b"));
    }

    [Fact]
    public void NonAsciiGlyphsUseTheirOwnWidths()
    {
        // The shard's table counts every non-ASCII char as 8; the font does not.
        Assert.Equal(3, _font.GetCharWidth('·')); // ·
        Assert.Equal(6, _font.GetCharWidth('é')); // é
        Assert.Equal(15, _font.GetCharWidth('…')); // …
    }

    [Fact]
    public void MultilineMeasuresTheWidestLineAndSumsLineHeights()
    {
        Assert.Equal(18, _font.Measure("a\nbbb"));
        Assert.Equal(18, _font.Measure("bbb\na"));
        Assert.Equal(28, _font.Height("a\nbbb", out int lines));
        Assert.Equal(2, lines);
        // A blank line and a trailing '\n' are 14 tall (FontsLoader.cs:1428, :1601).
        Assert.Equal(42, _font.Height("a\n\nb", out lines));
        Assert.Equal(3, lines);
        Assert.Equal(28, _font.Height("a\n"));
        Assert.Equal(28, _font.Height("\n"));
        Assert.Equal(14, _font.Height("\r"));
    }

    [Fact]
    public void LineHeightIsTheLowestGlyphBottom()
    {
        Assert.Equal(14, _font.Height("Stable"));  // capitals and 'a': OffsetY + Height = 14
        Assert.Equal(16, _font.Height("Ag"));      // 'g' reaches 16
        Assert.Equal(16, _font.Height("Breeding"));
        Assert.Equal(5, _font.Height(" "));        // only spaces (FontsLoader.cs:1570-1573)
        Assert.Equal(24, _font.MaxCharHeight);
    }

    [Theory]
    // StableGump (live refs), saved in stable-live-*.json.
    [InlineData("stable-live-breeding-claim-first", "Stable", 35)]
    [InlineData("stable-live-breeding-claim-first", "Followers", 51)]
    [InlineData("stable-live-breeding-claim-first", "Stable Pet...", 72)]
    [InlineData("stable-live-breeding-claim-first", "Stable All Followers", 116)]
    [InlineData("stable-live-breeding-claim-first", "Claim All", 51)]
    [InlineData("stable-live-breeding-claim-first", "Requirements", 74)]
    [InlineData("stable-live-breeding-claim-first", "Fee and food", 77)]
    [InlineData("stable-live-breeding-claim-first", "What the young may inherit", 167)]
    [InlineData("stable-live-breeding-claim-first", "Breeding", 47)]
    [InlineData("stable-live-all", "Shadowmere", 67)]
    [InlineData("stable-live-all", "Page 1 / 1", 67)]
    // PetLoreGump (live refs).
    [InlineData("pet-lore-care-courting", "Animal Lore", 68)]
    [InlineData("pet-lore-care-courting", "Overview", 49)]
    [InlineData("pet-lore-care-courting", "Care and gear", 84)]
    // AuctionGump.
    [InlineData("auction-browse", "bank 12,345 · pack 678", 146)]
    [InlineData("auction-browse", "Sell / Order", 77)]
    [InlineData("auction-browse", "My auctions", 69)]
    [InlineData("auction-browse", "Consumables", 68)]
    [InlineData("auction-browse", "Bulk Orders", 69)]
    public void FixtureLabelWidths(string fixture, string text, int width)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Gumps", fixture + ".json")));
        Assert.Contains(text, doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(width, _font.Measure(text));
        if (text.All(c => c >= ' ' && c <= '~'))
            Assert.Equal(LiveTamingGumps.Measure(text), width);
    }
}
