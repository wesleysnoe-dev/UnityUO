using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// The shard's own gumps with no server and no Unity: each layout is built the way ModernUO compiles it (legacy Gump's
/// AppendTo text and interned strings, or DynamicGumpBuilder's), sent as the shard sends it (CustomBots' gumps through
/// UncompressedGump as 0xB0, stock ones as 0xDD), injected into an offline session, worked through GumpControls as
/// ServerGumpWindow does, and the 0xB1 the session sends is checked against GumpSystem.DisplayGumpResponse's rules.
/// </summary>
public sealed class ShardGumpLayoutTests
{
    private const uint Me = 0x00000042;

    // ---- Auction house (CustomBots/Auctions/AuctionGump.cs) ----

    // AuctionGump's Act enum and Id(act, i) = act * 1000 + i.
    private const int Close = 0, TabBrowse = 1000, TabSell = 2000, TabMine = 3000, Category = 4000, Search = 5000,
        Clear = 6000, Sort = 7000, Prev = 8000, Next = 9000, Buy = 10000, TabWanted = 13000, Filters = 17000;

    private static readonly string[] Categories =
        { "All", "Weapons", "Armor", "Jewelry", "Clothing", "Artifacts", "Scrolls", "Reagents", "Resources", "Consumables", "Bulk Orders", "Furniture", "Misc" };

    /// <summary>AuctionGump on the Browse tab: All picked, page 1 of 2, a katana from Ann and 50 black pearl from Bob.</summary>
    internal static ShardGump AuctionBrowse()
    {
        var g = new ShardGump(0x40000101, 0x6A1C3B2E, 60, 40);
        const int W = 860, TextHue = 1153, DimHue = 0x3B2, GoldHue = 0x35, PickHue = 0x44;
        g.AddPage(0);
        g.AddBackground(0, 0, W, 900, 9270);
        g.AddHtml(16, 14, 300, 22, "<BASEFONT COLOR=#F4F4F4 SIZE=4><B>Auction House</B></BASEFONT>");
        g.AddLabel(330, 14, GoldHue, "bank 12,345 · pack 678");
        g.AddButton(W - 36, 12, 0xFB1, 0xFB3, Close);
        (int X, string Text, int Id)[] tabs = { (16, "Browse", TabBrowse), (130, "Wanted", TabWanted), (244, "Sell / Order", TabSell), (400, "My auctions", TabMine) };
        foreach (var t in tabs)
        {
            g.AddButton(t.X, 42, 4005, 4007, t.Id);
            g.AddLabel(t.X + 34, 44, t.Id == TabBrowse ? PickHue : TextHue, t.Text);
        }

        int y = 76;
        for (int i = 0; i < Categories.Length; i++, y += 24)
        {
            g.AddButton(14, y, 0x15E1, 0x15E5, Category + i);
            g.AddLabel(34, y - 3, i == 0 ? PickHue : TextHue, Categories[i]);
        }
        g.AddBackground(140, 72, 230, 26, 3000);
        g.AddTextEntry(146, 75, 220, 20, 0, 1, "");
        g.AddButton(378, 74, 4005, 4007, Search);
        g.AddLabel(412, 76, TextHue, "Search");
        g.AddButton(462, 74, 4005, 4007, Clear);
        g.AddLabel(496, 76, TextHue, "Clear");
        g.AddButton(542, 74, 4005, 4007, Filters);
        g.AddLabel(576, 76, TextHue, "Filters");
        string[] sorts = { "Newest", "Price low", "Price high", "Ending soon" };
        for (int i = 0, sx = 140; i < sorts.Length; i++, sx += 110)
        {
            g.AddButton(sx, 104, 0x15E1, 0x15E5, Sort + i);
            g.AddLabel(sx + 20, 101, i == 0 ? PickHue : DimHue, sorts[i]);
        }

        // Row(lot, y, mine: false, index, own: false), RowsTop 128, RowH 46.
        (int Art, int Hue, uint Serial, string Name, string Seller, string Price, string Each)[] lots =
        {
            (0x13FF, 0, 0x40000A01, "katana", "Ann · 2d 4h left", "1,200 gp", null),
            (0x0F7A, 0, 0x40000A02, "50 black pearl", "Bob · 6h left", "250 gp", "5 ea"),
        };
        y = 128;
        for (int r = 0; r < lots.Length; r++, y += 46)
        {
            g.AddItem(142, y + 4, lots[r].Art, lots[r].Hue);
            g.AddItemProperty(lots[r].Serial);
            g.AddLabelCropped(196, y + 2, 390, 20, TextHue, lots[r].Name);
            g.AddLabelCropped(196, y + 22, 390, 20, DimHue, lots[r].Seller);
            g.AddLabel(610, y + 2, GoldHue, lots[r].Price);
            if (lots[r].Each != null)
                g.AddLabel(610, y + 22, DimHue, lots[r].Each);
            g.AddButton(760, y + 8, 4005, 4007, Buy + r);
            g.AddLabel(794, y + 10, TextHue, "Buy");
        }

        // Pager(2 pages): no Prev on the first page.
        y = 128 + 14 * 46 + 6;
        g.AddLabel(250, y + 2, TextHue, "Page 1/2");
        g.AddButton(340, y, 4005, 4007, Next);
        g.AddLabel(374, y + 2, TextHue, "Next ▸");
        g.AddLabel(470, y + 2, DimHue, "20 listing(s)");
        return g;
    }

    [Fact]
    public void AuctionBrowsePageOpensWithEveryControlAndSearchesThroughAReplyModernUOAccepts()
    {
        ShardGump shard = AuctionBrowse();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, shard.Uncompressed());

        Assert.Equal(new[] { 0x6A1C3B2Eu, 60u, 40u }, new[] { gump.TypeId, (uint)gump.X, (uint)gump.Y });
        Assert.Equal(Count(shard, "button"), Count(gump, "button"));
        Assert.Equal(28, Count(gump, "button"));         // close, 4 tabs, 13 categories, search/clear/filters, 4 sorts, 2 buys, next
        Assert.Equal(2, Count(gump, "tilepic"));
        Assert.Equal(2, Count(gump, "itemproperty"));
        Assert.Equal(4, Count(gump, "croppedtext"));
        Assert.Equal(new[] { 1 }, gump.Commands.Where(c => c.Name == "textentry").Select(c => c.Arg(5)));
        Assert.Equal(shard.ReplyIds.OrderBy(i => i), ReplyButtons(gump).OrderBy(i => i));
        Assert.All(gump.Commands.Where(c => c.Name == "button"), c => Assert.False(ServerGumpWindow.ButtonPress(c).IsPage));

        // Legacy Gump interns its strings: both rows' "Buy" are one line, and every text points at its own words.
        Assert.Equal(shard.Lines.Count, gump.Lines.Length);
        int[] buys = gump.Commands.Where(c => c.Name == "text" && gump.Line(c.Arg(3)) == "Buy").Select(c => c.Arg(3)).ToArray();
        Assert.Equal(2, buys.Length);
        Assert.Equal(buys[0], buys[1]);
        Assert.Equal("Next ▸", gump.Line(gump.Commands.Single(c => c.Name == "text" && c.Arg(0) == 374).Arg(3)));
        Assert.Equal(0x40000A02, gump.Commands.Where(c => c.Name == "itemproperty").Last().Arg(0));

        var controls = new GumpControls(gump);
        Assert.Empty(controls.Switches());
        controls.SetText(Index(gump, c => c.Name == "textentry"), "katana");
        session.ReplyGump(gump, Search, controls.Switches(), controls.Entries());

        Relay relay = shard.Accept(Assert.Single(sent));
        Assert.Equal(Search, relay.Button);
        Assert.Equal("katana", relay.GetTextEntry(1));
        Assert.Empty(relay.Switches);
        Assert.Empty(session.World.Gumps);
    }

    [Fact]
    public void AuctionBrowseRefusesAButtonThePageDoesNotHave()
    {
        // DisplayGumpResponse disconnects on a button the legacy gump never added: Prev is not on page 1, nor is a
        // third row's Buy. Close (0) always exists; every button laid out is accepted.
        ShardGump shard = AuctionBrowse();
        Assert.Equal("Button 8000 doesn't exist", shard.Refusal(Reply(shard, Prev)));
        Assert.Equal("Button 10002 doesn't exist", shard.Refusal(Reply(shard, Buy + 2)));
        Assert.Null(shard.Refusal(Reply(shard, Close, texts: new[] { (1, "") })));
        foreach (int id in shard.ReplyIds)
            Assert.Null(shard.Refusal(Reply(shard, id, texts: new[] { (1, "x") })));

        // One text entry declared: two are a bad text entry count; no switches declared: one is a bad switch count.
        Assert.Equal("Bad text entry count 2", shard.Refusal(Reply(shard, Search, texts: new[] { (1, "a"), (1, "b") })));
        Assert.Equal("Bad switch count 1", shard.Refusal(Reply(shard, Search, switches: new[] { 100 })));
    }

    /// <summary>AuctionSellGump for a katana: price entry 1, AddGroup(0) and the duration radios 100-102 (3 days on).</summary>
    internal static ShardGump AuctionSell(string message = null)
    {
        var g = new ShardGump(0x40000102, 0x2F7D11A4, 120, 80);
        const int W = 460, H = 330;
        g.AddPage(0);
        g.AddBackground(0, 0, W, H, 9270);
        g.AddHtml(16, 14, W - 32, 22, "<BASEFONT COLOR=#F4F4F4 SIZE=4><B>Sell at auction</B></BASEFONT>");
        g.AddItem(24, 54, 0x13FF, 0);
        g.AddItemProperty(0x40000A01);
        g.AddLabelCropped(84, 52, W - 100, 20, 1153, "katana");
        g.AddLabel(84, 74, 0x3B2, "Market estimate: about 1,500 gp (at or under this sells within hours)");
        g.AddLabel(24, 112, 1153, "Price (gold):");
        g.AddBackground(130, 108, 160, 26, 3000);
        g.AddTextEntry(136, 111, 150, 20, 0, 1, "1500");
        g.AddLabel(24, 148, 1153, "Duration:");
        string[] names = { "1 day", "3 days", "7 days" };
        g.AddGroup(0);
        for (int i = 0; i < names.Length; i++)
        {
            g.AddRadio(110 + i * 100, 146, 0xD2, 0xD3, i == 1, 100 + i);
            g.AddLabel(134 + i * 100, 148, 1153, names[i]);
        }
        g.AddHtml(24, 184, W - 48, 60, "<BASEFONT COLOR=#C8C0A8>Listing fee 2% now (at least 1 gp); 5% of the price is kept when it sells.</BASEFONT>");
        if (message != null)
            g.AddHtml(24, 238, W - 48, 22, $"<BASEFONT COLOR=#FF9080>{message}</BASEFONT>");
        g.AddButton(24, H - 46, 4005, 4007, 1);
        g.AddLabel(58, H - 44, 0x44, "List it");
        g.AddButton(W - 130, H - 46, 4017, 4019, 0);
        g.AddLabel(W - 96, H - 44, 1153, "Back");
        return g;
    }

    [Fact]
    public void AuctionSellReplyIsTheExactBytesModernUOReadsThePriceAndDurationFrom()
    {
        ShardGump shard = AuctionSell();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, shard.Uncompressed());

        Assert.Equal(new[] { 100, 101, 102 }, gump.Commands.Where(c => c.Name == "radio").Select(c => c.Arg(5)));
        Assert.Equal(new[] { 1 }, gump.Commands.Where(c => c.Name == "textentry").Select(c => c.Arg(5)));
        Assert.Equal(new[] { 1, 0 }, ReplyButtons(gump));
        Assert.Equal(1, Count(gump, "group"));
        Assert.Equal(1, Count(gump, "itemproperty"));

        var controls = new GumpControls(gump);
        Assert.Equal(new[] { 101 }, controls.Switches());           // "3 days" starts on
        Assert.Equal(new List<(int, string)> { (1, "1500") }, controls.Entries());

        controls.SetText(Index(gump, c => c.Name == "textentry"), "2,500");
        controls.Click(Index(gump, c => c.Name == "radio" && c.Arg(5) == 102));
        session.ReplyGump(gump, 1, controls.Switches(), controls.Entries());

        byte[] p = Assert.Single(sent);
        Assert.Equal(new byte[]
        {
            0xB1, 0x00, 0x29,           // length 41
            0x40, 0x00, 0x01, 0x02,     // serial
            0x2F, 0x7D, 0x11, 0xA4,     // type id
            0x00, 0x00, 0x00, 0x01,     // "List it"
            0x00, 0x00, 0x00, 0x01,     // one switch
            0x00, 0x00, 0x00, 0x66,     //   102: 7 days
            0x00, 0x00, 0x00, 0x01,     // one text entry
            0x00, 0x01, 0x00, 0x05,     //   PriceEntry, 5 characters
            0x00, (byte)'2', 0x00, (byte)',', 0x00, (byte)'5', 0x00, (byte)'0', 0x00, (byte)'0',
        }, p);

        // OnResponse: GetTextEntry(PriceEntry) with "," stripped parses; IsSwitched(Duration0 + 2) picks 7 days.
        Relay relay = shard.Accept(p);
        Assert.Equal("2500", relay.GetTextEntry(1).Replace(",", ""));
        Assert.True(relay.IsSwitched(102));
        Assert.False(relay.IsSwitched(101));
    }

    [Fact]
    public void AuctionSellResentWithAMessageStillAcceptsTheReply()
    {
        // TryList failing re-sends the gump with the error line; the reply rules do not change with the extra htmlgump.
        ShardGump shard = AuctionSell("Enter a price in whole gold pieces.");
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, shard.Uncompressed());
        Assert.Equal(3, Count(gump, "htmlgump"));        // title, fee, the error
        Assert.Equal("<BASEFONT COLOR=#FF9080>Enter a price in whole gold pieces.</BASEFONT>",
            gump.Line(gump.Commands.Where(c => c.Name == "htmlgump").Last().Arg(4)));

        var controls = new GumpControls(gump);
        controls.SetText(Index(gump, c => c.Name == "textentry"), new string('9', 400)); // a paste past ModernUO's cap
        session.ReplyGump(gump, 1, controls.Switches(), controls.Entries());
        Relay relay = shard.Accept(Assert.Single(sent));
        Assert.Equal(239, relay.GetTextEntry(1).Length);
    }

    [Fact]
    public void AuctionOrderBackStillCarriesBothEntriesAndTheDuration()
    {
        // AuctionOrderGump: AmountEntry 1 and PriceEntry 2, then the radios. TazUO's OnButtonClick sends every
        // StbTextBox and checked radio on any button, Back (0) included, and DisplayGumpResponse takes it.
        var g = new ShardGump(0x40000103, 0x51E0C0DE, 120, 80);
        g.AddPage(0);
        g.AddBackground(0, 0, 460, 330, 9270);
        g.AddHtml(16, 14, 428, 22, "<BASEFONT COLOR=#F4F4F4 SIZE=4><B>Post a buy order</B></BASEFONT>");
        g.AddItem(24, 54, 0x1BF2, 0);
        g.AddLabelCropped(84, 52, 360, 20, 1153, "Wanted: iron ingot");
        g.AddLabel(84, 74, 0x3B2, "Market estimate: about 5 gp each");
        g.AddLabel(24, 112, 1153, "How many:");
        g.AddBackground(130, 108, 100, 26, 3000);
        g.AddTextEntry(136, 111, 90, 20, 0, 1, "100");
        g.AddLabel(24, 146, 1153, "Pay in all:");
        g.AddBackground(130, 142, 160, 26, 3000);
        g.AddTextEntry(136, 145, 150, 20, 0, 2, "500");
        g.AddLabel(24, 182, 1153, "Duration:");
        g.AddGroup(0);
        string[] names = { "1 day", "3 days", "7 days" };
        for (int i = 0; i < names.Length; i++)
        {
            g.AddRadio(110 + i * 100, 180, 0xD2, 0xD3, i == 1, 100 + i);
            g.AddLabel(134 + i * 100, 182, 1153, names[i]);
        }
        g.AddLabel(24, 212, 0x3B2, "The gold is held now, plus a 2% fee.");
        g.AddButton(24, 284, 4005, 4007, 1);
        g.AddLabel(58, 286, 0x44, "Post it");
        g.AddButton(330, 284, 4017, 4019, 0);
        g.AddLabel(364, 286, 1153, "Back");

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, g.Uncompressed());
        Assert.Equal(new[] { 1, 2 }, gump.Commands.Where(c => c.Name == "textentry").Select(c => c.Arg(5)));
        Assert.Equal((g.TextEntries, g.Switches), (2, 3));

        var controls = new GumpControls(gump);
        controls.SetText(Index(gump, c => c.Name == "textentry" && c.Arg(5) == 1), "5");
        session.ReplyGump(gump, 0, controls.Switches(), controls.Entries());

        Relay relay = g.Accept(Assert.Single(sent));
        Assert.Equal(0, relay.Button);
        Assert.Equal(new[] { 101 }, relay.Switches);
        Assert.Equal(new List<(int, string)> { (1, "5"), (2, "500") }, relay.Texts);
    }

    // ---- Group Finder (CustomBots/Chat/GroupFinder.cs) ----

    /// <summary>
    /// GroupFinderGump(m, Dungeons, page 0, onlyActive false): a header with Start, a joinable party, a header with
    /// no groups; 16 rows a page and 20 rows, so a Next and no Prev. The "only places with groups" toggle is a
    /// button (0xD2/0xD3 art), not a checkbox: the gump declares no switches and no text entries.
    /// </summary>
    internal static ShardGump GroupFinderDungeons()
    {
        const int W = 680, ListTop = 92, RowH = 22;
        var g = new ShardGump(0x40000104, 0x7C4F0A55, 80, 60);
        int h = ListTop + 16 * RowH + 74;
        g.AddPage(0);
        g.AddBackground(0, 0, W, h, 9270);
        g.AddAlphaRegion(12, 12, W - 24, h - 24);
        g.AddHtml(24, 20, 300, 22, "<BASEFONT COLOR=#F4F4F4 SIZE=5><B>Group Finder</B></BASEFONT>");
        g.AddButton(W - 128, 20, 4011, 4013, 12);
        g.AddLabel(W - 94, 21, 1153, "Refresh");
        string[] tabs = { "Dungeons", "Champion Spawns", "Faction" };
        for (int i = 0, tx = 24; i < tabs.Length; i++)
        {
            g.AddButton(tx, 52, i == 0 ? 4006 : 4005, 4007, 1 + i);
            g.AddLabel(tx + 34, 53, i == 0 ? 0x35 : 1153, tabs[i]);
            tx += i == 1 ? 170 : 130;
        }
        g.AddButton(W - 220, 52, 0xD2, 0xD3, 13);
        g.AddLabel(W - 198, 52, 1153, "Only places with groups");
        g.AddImageTiled(24, 78, W - 48, 2, 9357);

        (bool Header, string Text, string Right, int Hue, string Act)[] rows =
        {
            (true, "Covetous", "1 group(s), 1 forming", 0x35, "Start a group"),
            (false, "Ann's party (3/6), forming", "120 tiles NE", 1153, "Join"),
            (true, "Deceit", "no groups", 1153, "Start a group"),
        };
        for (int k = 0; k < rows.Length; k++)
        {
            int y = ListTop + k * RowH;
            if (rows[k].Header)
            {
                g.AddImageTiled(24, y + RowH - 3, W - 48, 1, 9357);
                g.AddLabel(24, y + 2, rows[k].Hue, rows[k].Text);
            }
            else
                g.AddLabelCropped(110, y + 2, W - 300, 20, rows[k].Hue, rows[k].Text);
            g.AddLabelCropped(W - 180, y + 2, 156, 20, 0x3B2, rows[k].Right);
            int bx = rows[k].Header ? W - 300 : 34;
            g.AddButton(bx, y + 1, 4005, 4007, 100 + k);
            g.AddLabel(bx + 34, y + 2, 0x35, rows[k].Act);
        }
        int fy = h - 52;
        g.AddButton(W - 80, fy + 20, 4005, 4007, 11);
        g.AddLabel(W - 46, fy + 21, 1153, "Next");
        g.AddLabel(W / 2 - 40, fy + 21, 0x3B2, "Page 1 of 2");
        return g;
    }

    [Fact]
    public void GroupFinderDungeonsTabRepliesARowButtonWithNothingElse()
    {
        ShardGump g = GroupFinderDungeons();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, g.Uncompressed());

        Assert.Equal(new[] { 12, 1, 2, 3, 13, 100, 101, 102, 11 }, ReplyButtons(gump));
        Assert.Equal(1, Count(gump, "checkertrans"));
        Assert.Equal(3, Count(gump, "gumppictiled"));
        Assert.Equal(0, Count(gump, "checkbox") + Count(gump, "radio") + Count(gump, "textentry"));
        // Both "Start a group" labels share one interned line.
        Assert.Equal(1, gump.Lines.Count(l => l == "Start a group"));

        var controls = new GumpControls(gump);
        session.ReplyGump(gump, 101, controls.Switches(), controls.Entries());
        byte[] p = Assert.Single(sent);
        Assert.Equal(new byte[]
        {
            0xB1, 0x00, 0x17,
            0x40, 0x00, 0x01, 0x04,
            0x7C, 0x4F, 0x0A, 0x55,
            0x00, 0x00, 0x00, 0x65,     // row 1: Join
            0x00, 0x00, 0x00, 0x00,     // no switches
            0x00, 0x00, 0x00, 0x00,     // no text entries
        }, p);
        Assert.Equal(101, g.Accept(p).Button);
        Assert.Equal("Button 10 doesn't exist", g.Refusal(Reply(g, 10))); // Prev, not on the first page
    }

    [Fact]
    public void GroupDockIsUnclosableAndRepliesOne()
    {
        // GroupDockGump: Closable = Disposable = false, so Compile writes { noclose }{ nodispose } ahead of the entries.
        var g = new ShardGump(0x40000105, 0x0BADD0C5, 900, 20) { Closable = false, Disposable = false };
        g.AddPage(0);
        g.AddBackground(0, 0, 112, 27, 9200);
        g.AddButton(5, 4, 4005, 4007, 1);
        g.AddLabel(40, 4, 0x35, "Groups (2)");
        Assert.StartsWith("{ noclose }{ nodispose }{ page 0 }", g.Layout);

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, g.Uncompressed());
        Assert.True(gump.NoClose);
        Assert.True(gump.NoDispose);
        Assert.False(gump.NoMove);
        Assert.Equal(new[] { 1 }, ReplyButtons(gump));

        session.ReplyGump(gump, 1, new GumpControls(gump).Switches(), new GumpControls(gump).Entries());
        Assert.Equal(1, g.Accept(Assert.Single(sent)).Button);
    }

    // ---- Animal Lore (UOContent/Skills/AnimalLore.cs, a DynamicGump sent as 0xDD) ----

    private const int LoreLabel = 0x24E5;

    /// <summary>AnimalLoreGump.BuildLayout under AOS, SE and ML: five pages for a tamed dragon.</summary>
    internal static ShardGump AnimalLore()
    {
        var b = new ShardGump(0x40000106, 0x3D7E2A19, 250, 50, dynamic: true);
        b.AddPage(0);
        b.AddImage(100, 100, 2080);
        b.AddImage(118, 137, 2081);
        b.AddImage(118, 207, 2081);
        b.AddImage(118, 277, 2081);
        b.AddImage(118, 347, 2083);
        b.AddHtml(147, 108, 210, 18, "<center><i>a dragon</i></center>");
        b.AddButton(240, 77, 2093, 2093, 2);
        b.AddImage(140, 138, 2091);
        b.AddImage(140, 335, 2091);
        const int pages = 5;
        int page = 0;

        // Attributes.
        b.AddPage(++page);
        b.AddImage(128, 152, 2086);
        b.AddHtmlLocalized(147, 150, 160, 18, 1049593, 200);
        (int Number, int X, int W, string Value)[] attributes =
        {
            (1049578, 280, 75, "<div align=right>700/750</div>"), (1049579, 280, 75, "<div align=right>89/89</div>"),
            (1049580, 280, 75, "<div align=right>436/436</div>"), (1028335, 320, 35, "<div align=right>820</div>"),
            (3000113, 320, 35, "<div align=right>89</div>"), (3000112, 320, 35, "<div align=right>436</div>"),
            (1070793, 320, 35, "<div align=right>121.1</div>"),
        };
        for (int i = 0; i < attributes.Length; i++)
        {
            b.AddHtmlLocalized(153, 168 + i * 18, 160, 18, attributes[i].Number, LoreLabel);
            b.AddHtml(attributes[i].X, 168 + i * 18, attributes[i].W, 18, attributes[i].Value);
        }
        b.AddImage(128, 296, 2086);
        b.AddHtmlLocalized(147, 294, 160, 18, 1049594, 200);
        b.AddHtmlLocalized(153, 312, 160, 18, 1049595 + 10, LoreLabel);
        b.AddButton(340, 358, 5601, 5605, 0, type: 0, param: page + 1);
        b.AddButton(317, 358, 5603, 5607, 0, type: 0, param: pages);

        // Resistances, then damage.
        foreach ((int Title, string[] Values) section in new[]
                 {
                     (1061645, new[] { "55%", "75%", "45%", "45%", "45%" }),
                     (1017319, new[] { "100%", "0%", "0%", "0%", "0%" }),
                 })
        {
            b.AddPage(++page);
            b.AddImage(128, 152, 2086);
            b.AddHtmlLocalized(147, 150, 160, 18, section.Title, 200);
            string[] colors = { null, "#FF0000", "#000080", "#008000", "#BF80FF" };
            for (int i = 0; i < 5; i++)
            {
                b.AddHtmlLocalized(153, 168 + i * 18, 160, 18, 1061646 + i, LoreLabel);
                string v = colors[i] == null ? section.Values[i] : $"<BASEFONT COLOR={colors[i]}>{section.Values[i]}</BASEFONT>";
                b.AddHtml(320, 168 + i * 18, 35, 18, $"<div align=right>{v}</div>");
            }
            if (section.Title == 1017319)
            {
                b.AddHtmlLocalized(153, 258, 160, 18, 1076750, LoreLabel);
                b.AddHtml(300, 258, 55, 18, "<div align=right>16-22</div>");
            }
            b.AddButton(340, 358, 5601, 5605, 0, type: 0, param: page + 1);
            b.AddButton(317, 358, 5603, 5607, 0, type: 0, param: page - 1);
        }

        // Combat ratings and lore & knowledge.
        b.AddPage(++page);
        b.AddImage(128, 152, 2086);
        b.AddHtmlLocalized(147, 150, 160, 18, 3001030, 200);
        int[] combat = { 1044103, 1044087, 1044086, 1044061, 1044090 };
        for (int i = 0; i < combat.Length; i++)
        {
            b.AddHtmlLocalized(153, 168 + i * 18, 160, 18, combat[i], LoreLabel);
            b.AddHtml(320, 168 + i * 18, 35, 18, i == 4 ? "<div align=right>---</div>" : "<div align=right>90.0</div>");
        }
        b.AddImage(128, 260, 2086);
        b.AddHtmlLocalized(147, 258, 160, 18, 3001032, 200);
        int[] lore = { 1044085, 1044076, 1044106 };
        for (int i = 0; i < lore.Length; i++)
        {
            b.AddHtmlLocalized(153, 276 + i * 18, 160, 18, lore[i], LoreLabel);
            b.AddHtml(320, 276 + i * 18, 35, 18, "<div align=right>90.0</div>");
        }
        b.AddButton(340, 358, 5601, 5605, 0, type: 0, param: page + 1);
        b.AddButton(317, 358, 5603, 5607, 0, type: 0, param: page - 1);

        // Preferred foods and pack instincts.
        b.AddPage(++page);
        b.AddImage(128, 152, 2086);
        b.AddHtmlLocalized(147, 150, 160, 18, 1049563, 200);
        b.AddHtmlLocalized(153, 168, 160, 18, 1049564, LoreLabel);   // Meat
        b.AddImage(128, 188, 2086);
        b.AddHtmlLocalized(147, 186, 160, 18, 1049569, 200);
        b.AddHtmlLocalized(153, 204, 160, 18, 1049574, LoreLabel);   // Daemon
        b.AddButton(340, 358, 5601, 5605, 0, type: 0, param: 1);
        b.AddButton(317, 358, 5603, 5607, 0, type: 0, param: page - 1);
        return b;
    }

    [Fact]
    public void AnimalLorePagesTurnLocallyAndOnlyTheCloseButtonReplies()
    {
        ShardGump shard = AnimalLore();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, shard.Compressed());

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, gump.Commands.Where(c => c.Name == "page").Select(c => c.Arg(0)));
        Assert.Equal(11, Count(gump, "button"));
        Assert.Equal(new[] { 2 }, ReplyButtons(gump));
        Assert.Equal(0, Count(gump, "textentry") + Count(gump, "radio") + Count(gump, "checkbox"));

        // Page buttons (type 0) turn pages in the client: next, last, and page 5's next back round to 1.
        int[] turns = gump.Commands.Where(c => c.Name == "button" && c.Arg(4) == 0).Select(c => ServerGumpWindow.ButtonPress(c).Value).ToArray();
        Assert.Equal(new[] { 2, 5, 3, 1, 4, 2, 5, 3, 1, 4 }, turns);

        // xmfhtmlgumpcolor x y w h number bg scroll color; the 0x24E5 labels and the 200 headings.
        GumpCommand hits = gump.Commands.First(c => c.Name == "xmfhtmlgumpcolor" && c.Arg(4) == 1049578);
        Assert.Equal(new[] { 153, 168, 160, 18, 1049578, 0, 0, LoreLabel }, hits.Args);
        Assert.Equal(8, gump.Commands.Count(c => c.Name == "xmfhtmlgumpcolor" && c.Arg(7) == 200));
        Assert.Equal(29, gump.Commands.Count(c => c.Name == "xmfhtmlgumpcolor" && c.Arg(7) == LoreLabel));
        // DynamicGumpBuilder does not intern: each htmlgump has its own line, repeats and all.
        Assert.Equal(Count(gump, "htmlgump"), gump.Lines.Length);
        Assert.Equal(7, gump.Lines.Count(l => l == "<div align=right>90.0</div>"));
        Assert.Equal("<center><i>a dragon</i></center>", gump.Line(gump.Commands.First(c => c.Name == "htmlgump").Arg(4)));

        session.ReplyGump(gump, 2, new GumpControls(gump).Switches(), new GumpControls(gump).Entries());
        Relay relay = shard.Accept(Assert.Single(sent));
        Assert.Equal(2, relay.Button);

        // A DynamicGump is not a legacy Gump: DisplayGumpResponse checks no button ids for it, only the counts.
        Assert.Null(shard.Refusal(Reply(shard, 7)));
        Assert.Equal("Bad switch count 1", shard.Refusal(Reply(shard, 2, switches: new[] { 1 })));
    }

    // ---- Faction horse breeder (UOContent/Engines/Factions/Gumps/HorseBreederGump.cs, legacy, 0xDD) ----

    [Fact]
    public void HorseBreederContinueRepliesOne()
    {
        var g = new ShardGump(0x40000107, 0x1C0FFEE5, 20, 30);
        g.AddPage(0);
        g.AddBackground(0, 0, 320, 280, 5054);
        g.AddBackground(10, 10, 300, 260, 3000);
        g.AddHtmlLocalized(20, 30, 300, 25, 1011535);    // FactionGump.AddHtmlText: a TextDefinition number
        g.AddHtmlLocalized(20, 60, 300, 25, 1018306);
        g.AddItem(70, 120, 0x3FFE, 0);
        g.AddItem(150, 120, 0xEF2, 0);
        g.AddLabel(190, 122, 0x3E3, "500");
        g.AddItem(150, 150, 0xEEF, 0);
        g.AddLabel(190, 152, 0x3E3, "3,000");
        g.AddHtmlLocalized(55, 210, 200, 25, 1011011);
        g.AddButton(20, 210, 4005, 4007, 1);
        g.AddHtmlLocalized(55, 240, 200, 25, 1011012);
        g.AddButton(20, 240, 4005, 4007, 0);

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, g.Compressed());
        Assert.Equal(new[] { 1011535, 1018306, 1011011, 1011012 }, gump.Commands.Where(c => c.Name == "xmfhtmlgump").Select(c => c.Arg(4)));
        Assert.Equal(3, Count(gump, "tilepic"));
        Assert.Equal(new[] { 1, 0 }, ReplyButtons(gump));

        session.ReplyGump(gump, 1, new GumpControls(gump).Switches(), new GumpControls(gump).Entries());
        Assert.Equal(1, g.Accept(Assert.Single(sent)).Button);
    }

    [Theory]
    [InlineData(true, true, true, true)]    // plain gump: right-click closes, CanCloseWithEsc stays set
    [InlineData(false, true, false, true)]  // noclose only: right-click blocked (GumpHelpers.cs:427-430)
    [InlineData(true, false, true, false)]  // nodispose only: right-click still closes, CanCloseWithEsc cleared (:431-434)
    [InlineData(false, false, false, false)] // GroupDockGump: both
    public void NoCloseBlocksRightClickAndNoDisposeOnlyClearsEscFlag(bool closable, bool disposable, bool rightClick, bool escFlag)
    {
        var g = new ShardGump(0x40000105, 0x0BADD0C5, 50, 50) { Closable = closable, Disposable = disposable };
        g.AddPage(0);
        g.AddBackground(0, 0, 112, 27, 9200);
        g.AddButton(5, 4, 4005, 4007, 1);

        var sent = new List<byte[]>();
        ServerGump gump = Open(InWorld(sent), g.Uncompressed());
        Assert.Equal(!closable, gump.NoClose);
        Assert.Equal(!disposable, gump.NoDispose);
        Assert.Equal(rightClick, ServerGumpWindow.ClosesOnRightClick(gump));
        Assert.Equal(escFlag, ServerGumpWindow.CanCloseWithEsc(gump));
        // TazUO never reads CanCloseWithEsc and its Esc closes no gump (GameSceneInputHandler.cs:1370-1419).
        Assert.False(ServerGumpWindow.EscCloses(gump));
    }

    [Fact]
    public void RightClickCloseAnswersButtonZero()
    {
        // Gump.cs:311-314: a server gump closed by right-click sends OnButtonClick(0); DisplayGumpResponse takes 0 as "closed".
        var g = new ShardGump(0x40000105, 0x0BADD0C5, 50, 50) { Disposable = false };
        g.AddPage(0);
        g.AddBackground(0, 0, 112, 27, 9200);
        g.AddButton(5, 4, 4005, 4007, 1);

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, g.Uncompressed());
        Assert.True(ServerGumpWindow.ClosesOnRightClick(gump));
        session.ReplyGump(gump, ServerGumpWindow.RightClickReplyButton, new GumpControls(gump).Switches(), new GumpControls(gump).Entries());
        Assert.Equal(0, g.Accept(Assert.Single(sent)).Button);
    }

    // ---- harness ----

    internal static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    internal static ServerGump Open(GameSession session, byte[] packet)
    {
        ServerGump opened = null;
        session.World.GumpOpened += g => opened = g;
        session.Inject(packet, true);
        Assert.NotNull(opened);
        return opened;
    }

    private static int Count(ServerGump g, string name) => g.Commands.Count(c => c.Name == name);

    private static int Count(ShardGump g, string name) => ServerGump.ParseLayout(g.Layout).Count(c => c.Name == name);

    private static int Index(ServerGump g, Func<GumpCommand, bool> match) => g.Commands.FindIndex(c => match(c));

    /// <summary>The ids a press of each reply button (type 1) sends, in layout order.</summary>
    private static int[] ReplyButtons(ServerGump g) => g.Commands
        .Where(c => c.Name is "button" or "buttontileart")
        .Select(ServerGumpWindow.ButtonPress).Where(b => !b.IsPage).Select(b => b.Value).ToArray();

    private static byte[] Reply(ShardGump g, int button, int[] switches = null, (int, string)[] texts = null) =>
        GameSession.GumpReply(g.Serial, g.TypeId, button, switches ?? Array.Empty<int>(), texts?.ToList() ?? new List<(int, string)>());

    /// <summary>What OnResponse gets in its RelayInfo.</summary>
    internal sealed record Relay(int Button, int[] Switches, List<(int Id, string Text)> Texts)
    {
        // RelayInfo.GetTextEntry: the first entry with that id.
        public string GetTextEntry(int id) => Texts.Where(t => t.Id == id).Select(t => t.Text).FirstOrDefault();

        public bool IsSwitched(int id) => Switches.Contains(id);
    }

    /// <summary>
    /// A gump as ModernUO builds it. Legacy (Gump): the entries' AppendTo text after Compile's flag commands, and its
    /// OrderedSet of strings, one line per distinct text. Dynamic (DynamicGumpBuilder): the same command text, but a
    /// new line for every string. Counts switches and text entries as Compile does, and keeps the button ids
    /// DisplayGumpResponse looks for.
    /// </summary>
    internal sealed class ShardGump
    {
        public readonly uint Serial, TypeId;
        public readonly int X, Y;
        public readonly List<string> Lines = new();
        public readonly HashSet<int> ReplyIds = new();
        public int Switches, TextEntries;
        public bool Closable = true, Disposable = true, Draggable = true, Resizable = true;

        private readonly bool _dynamic;
        private readonly HashSet<int> _buttonIds = new();
        private readonly StringBuilder _entries = new();

        public ShardGump(uint serial, uint typeId, int x, int y, bool dynamic = false)
        {
            (Serial, TypeId, X, Y, _dynamic) = (serial, typeId, x, y, dynamic);
        }

        public bool Dynamic => _dynamic;

        /// <summary>Every button id the gump added, page buttons too: what DisplayGumpResponse checks a reply against.</summary>
        public IReadOnlyCollection<int> ButtonIds => _buttonIds;

        /// <summary>A gump read back from a saved fixture: its layout as sent (flag commands included) and its counts.</summary>
        public static ShardGump FromSaved(uint serial, uint typeId, int x, int y, bool dynamic, string layout,
            IEnumerable<string> lines, IEnumerable<int> buttonIds, IEnumerable<int> replyIds, int switches, int textEntries)
        {
            var g = new ShardGump(serial, typeId, x, y, dynamic) { Switches = switches, TextEntries = textEntries };
            g._entries.Append(layout);
            g.Lines.AddRange(lines);
            g._buttonIds.UnionWith(buttonIds);
            g.ReplyIds.UnionWith(replyIds);
            return g;
        }

        public string Layout =>
            (Draggable ? "" : "{ nomove }") + (Closable ? "" : "{ noclose }") + (Disposable ? "" : "{ nodispose }") +
            (Resizable ? "" : "{ noresize }") + _entries;

        private int Text(string s)
        {
            s ??= "";
            int i = _dynamic ? -1 : Lines.IndexOf(s);
            if (i >= 0)
                return i;
            Lines.Add(s);
            return Lines.Count - 1;
        }

        private void Add(string entry) => _entries.Append("{ ").Append(entry).Append(" }");

        public void AddPage(int page) => Add($"page {page}");
        public void AddBackground(int x, int y, int w, int h, int id) => Add($"resizepic {x} {y} {id} {w} {h}");
        public void AddAlphaRegion(int x, int y, int w, int h) => Add($"checkertrans {x} {y} {w} {h}");
        public void AddImage(int x, int y, int id) => Add($"gumppic {x} {y} {id}");
        public void AddImage(int x, int y, int id, int hue) => Add(hue == 0 ? $"gumppic {x} {y} {id}" : $"gumppic {x} {y} {id} hue={hue}");
        public void AddImageTiled(int x, int y, int w, int h, int id) => Add($"gumppictiled {x} {y} {w} {h} {id}");
        public void AddItem(int x, int y, int id, int hue) => Add(hue == 0 ? $"tilepic {x} {y} {id}" : $"tilepichue {x} {y} {id} {hue}");
        public void AddItemProperty(uint serial) => Add($"itemproperty {serial}");
        public void AddGroup(int group) => Add($"group {group}");
        public void AddLabel(int x, int y, int hue, string text) => Add($"text {x} {y} {hue} {Text(text)}");
        public void AddLabelCropped(int x, int y, int w, int h, int hue, string text) => Add($"croppedtext {x} {y} {w} {h} {hue} {Text(text)}");
        public void AddHtml(int x, int y, int w, int h, string text) => Add($"htmlgump {x} {y} {w} {h} {Text(text)} 0 0");
        public void AddHtml(int x, int y, int w, int h, string text, bool background, bool scrollbar) =>
            Add($"htmlgump {x} {y} {w} {h} {Text(text)} {(background ? 1 : 0)} {(scrollbar ? 1 : 0)}");
        public void AddHtmlLocalized(int x, int y, int w, int h, int number) => Add($"xmfhtmlgump {x} {y} {w} {h} {number} 0 0");
        public void AddHtmlLocalized(int x, int y, int w, int h, int number, int color) => Add($"xmfhtmlgumpcolor {x} {y} {w} {h} {number} 0 0 {color}");

        public void AddButton(int x, int y, int up, int down, int id, int type = 1, int param = 0)
        {
            Add($"button {x} {y} {up} {down} {type} {param} {id}");
            _buttonIds.Add(id);
            if (type != 0)
                ReplyIds.Add(id);
        }

        public void AddRadio(int x, int y, int off, int on, bool initial, int id)
        {
            Add($"radio {x} {y} {off} {on} {(initial ? 1 : 0)} {id}");
            Switches++;
        }

        public void AddTextEntry(int x, int y, int w, int h, int hue, int id, string text)
        {
            Add($"textentry {x} {y} {w} {h} {hue} {id} {Text(text)}");
            TextEntries++;
        }

        /// <summary>Gump.AddTextEntry with a size: GumpTextEntryLimited.</summary>
        public void AddTextEntry(int x, int y, int w, int h, int hue, int id, string text, int size)
        {
            Add($"textentrylimited {x} {y} {w} {h} {hue} {id} {Text(text)} {size}");
            TextEntries++;
        }

        private byte[] LineBytes()
        {
            var text = new List<byte>();
            foreach (string line in Lines)
            {
                text.Add((byte)(line.Length >> 8));
                text.Add((byte)line.Length);
                text.AddRange(Encoding.BigEndianUnicode.GetBytes(line));
            }
            return text.ToArray();
        }

        /// <summary>0xDD as Gump.Compile / DynamicGump.SendTo write it (layout Latin-1, 0-terminated).</summary>
        public byte[] Compressed()
        {
            var w = new PacketWriter(0xDD).UInt32(Serial).UInt32(TypeId).Int32(X).Int32(Y);
            Packed(w, Encoding.Latin1.GetBytes(Layout + "\0"));
            w.Int32(Lines.Count);
            Packed(w, LineBytes());
            return w.ToArray();
        }

        /// <summary>UncompressedGump.ToUncompressed: the inflated 0xDD sections as 0xB0.</summary>
        public byte[] Uncompressed()
        {
            byte[] layout = Encoding.Latin1.GetBytes(Layout + "\0");
            var w = new PacketWriter(0xB0).UInt32(Serial).UInt32(TypeId).Int32(X).Int32(Y).UInt16(layout.Length);
            foreach (byte b in layout)
                w.Byte(b);
            w.UInt16(Lines.Count);
            foreach (byte b in LineBytes())
                w.Byte(b);
            return w.ToArray();
        }

        private static void Packed(PacketWriter w, byte[] data)
        {
            var ms = new MemoryStream();
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(data);
            byte[] packed = ms.ToArray();
            w.Int32(4 + packed.Length).Int32(data.Length);
            foreach (byte b in packed)
                w.Byte(b);
        }

        /// <summary>
        /// Why GumpSystem.DisplayGumpResponse would drop this 0xB1 (its InvalidGumpResponseException text), or null
        /// when it hands it to OnResponse. A reply for another serial or type id finds no gump and is ignored.
        /// </summary>
        public string Refusal(byte[] p)
        {
            if (p[0] != 0xB1 || BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(1)) != p.Length)
                return "Bad packet";
            int pos = 3;
            int Int() { int v = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
            if ((uint)Int() != Serial || (uint)Int() != TypeId)
                return "No such gump";
            int button = Int();
            if (!_dynamic && button != 0 && !_buttonIds.Contains(button))
                return $"Button {button} doesn't exist";
            int switches = Int();
            if (switches < 0 || switches > Switches)
                return $"Bad switch count {switches}";
            pos += switches * 4;
            int texts = Int();
            if (texts < 0 || texts > TextEntries)
                return $"Bad text entry count {texts}";
            for (int i = 0; i < texts; i++)
            {
                int length = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(pos + 2));
                if (length > 239)
                    return $"Text entry {i} is too long ({length})";
                pos += 4 + length * 2;
            }
            return pos == p.Length ? null : "Trailing bytes";
        }

        /// <summary>Asserts ModernUO accepts the reply and returns what OnResponse reads from it.</summary>
        public Relay Accept(byte[] p)
        {
            Assert.Null(Refusal(p));
            int pos = 11;
            int Int() { int v = BinaryPrimitives.ReadInt32BigEndian(p.AsSpan(pos)); pos += 4; return v; }
            int button = Int();
            int[] switches = new int[Int()];
            for (int i = 0; i < switches.Length; i++)
                switches[i] = Int();
            var texts = new List<(int, string)>();
            for (int n = Int(), i = 0; i < n; i++)
            {
                int id = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(pos)), length = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(pos + 2));
                texts.Add((id, Encoding.BigEndianUnicode.GetString(p, pos + 4, length * 2)));
                pos += 4 + length * 2;
            }
            return new Relay(button, switches, texts);
        }
    }
}
