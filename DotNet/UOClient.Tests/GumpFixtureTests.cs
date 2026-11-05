using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;
using static UOClient.Tests.ShardGumpLayoutTests;

namespace UOClient.Tests;

/// <summary>
/// Saved shard gump layouts (Fixtures/Gumps/*.json) for working offline: each is a gump's compiled form as ModernUO
/// sends it (the layout's commands in order, its lines, and the button ids, switch and text entry counts
/// DisplayGumpResponse checks), built by replaying the source's Add* calls for one stated snapshot. Only gumps whose
/// source is on this machine are here: StableGump and StableReleaseConfirmGump from uo-offline-dev @ 0d14143, and
/// AuctionGump, GroupFinderGump and the stock AnimalLoreGump from ModernUO. Live's CustomBots/Taming redesign (the
/// Breeding tab, the pet-picture Animal Lore) is not in either tree and has no fixture.
/// Serials and type ids are stand-ins (ModernUO numbers gumps by a counter and hashes the type's full name).
/// Set UOCLIENT_WRITE_GUMP_FIXTURES=1 to rewrite the files from the builders.
/// </summary>
public sealed class GumpFixtureTests
{
    private const string OfflineDev = "uo-offline-dev @ 0d14143 playerbots/source/CustomBots/";
    private const string ModernUO = "ModernUO @ b170e7eb3 Projects/UOContent/";

    // ---- the saved form ----

    internal sealed record GumpFixture(
        string Gump, string Source, string State, string Packet, uint Serial, uint TypeId, int X, int Y, bool Dynamic,
        string[] Layout, string[] Lines, int[] ButtonIds, int[] ReplyButtons, int Switches, int TextEntries)
    {
        public ShardGump ToShardGump() => ShardGump.FromSaved(Serial, TypeId, X, Y, Dynamic, string.Concat(Layout), Lines,
            ButtonIds, ReplyButtons, Switches, TextEntries);

        public byte[] ToPacket() => Packet == "0xB0" ? ToShardGump().Uncompressed() : ToShardGump().Compressed();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep <BASEFONT>, ' and · readable in the files
    };

    private static GumpFixture Save(string gump, string source, string state, ShardGump g, bool compressed) => new(
        gump, source, state, compressed ? "0xDD" : "0xB0", g.Serial, g.TypeId, g.X, g.Y, g.Dynamic,
        Regex.Matches(g.Layout, @"\{[^}]*\}").Select(m => m.Value).ToArray(), g.Lines.ToArray(),
        g.ButtonIds.Order().ToArray(), g.ReplyIds.Order().ToArray(), g.Switches, g.TextEntries);

    /// <summary>Every fixture: its file name and how it is built from the source.</summary>
    private static readonly Dictionary<string, Func<GumpFixture>> Catalog = new()
    {
        ["stable-three-pets"] = () => Save("StableGump", OfflineDev + "Stables/StableGump.cs",
            "max 20, three stabled (Shadowmere bonded, Dobbin, a dead Fang), page 1, first pet selected, no notice",
            Stable(ThreePets(), 20, 0, 0, null), false),
        ["stable-page2-notice"] = () => Save("StableGump", OfflineDev + "Stables/StableGump.cs",
            "max 25, 22 horses, page 2 of 2 (two pets, three empty slots), StableActions.Rename's jail refusal as the notice",
            Stable(Horses(22), 25, 1, 0, "You can't rename pets from jail."), false),
        ["stable-empty"] = () => Save("StableGump", OfflineDev + "Stables/StableGump.cs",
            "max 20, nothing stabled: no selected pet, no rename entry", Stable(new List<Pet>(), 20, 0, 0, null), false),
        ["stable-release-confirm"] = () => Save("StableReleaseConfirmGump", OfflineDev + "Stables/StableReleaseConfirmGump.cs",
            "first confirm for Shadowmere (Nightmare)", StableReleaseConfirm("Shadowmere", "Nightmare", false), false),
        ["stable-release-bonded"] = () => Save("StableReleaseConfirmGump", OfflineDev + "Stables/StableReleaseConfirmGump.cs",
            "second (bonded) confirm for Shadowmere", StableReleaseConfirm("Shadowmere", "Nightmare", true), false),
        ["auction-browse"] = () => Save("AuctionGump", ModernUO + "CustomBots/Auctions/AuctionGump.cs (860 x 900 Browse; " +
                "uo-offline-dev @ 0d14143 has a newer 920 x 700 Browse with sub-filters, not saved here)",
            "Browse tab, All, Newest, page 1 of 2, two lots", AuctionBrowse(), false),
        ["auction-sell"] = () => Save("AuctionSellGump", ModernUO + "CustomBots/Auctions/AuctionGump.cs",
            "selling a katana at 1500, 3 days", AuctionSell(), false),
        ["group-finder-dungeons"] = () => Save("GroupFinderGump", ModernUO + "CustomBots/Chat/GroupFinder.cs (same file in uo-offline-dev @ 0d14143)",
            "Dungeons tab, page 1 of 2: Covetous with Ann's forming party, Deceit with none", GroupFinderDungeons(), false),
        ["animal-lore-dragon"] = () => Save("AnimalLoreGump", ModernUO + "Skills/AnimalLore.cs (stock DynamicGump)",
            "a tamed dragon under AOS, SE and ML: five pages", AnimalLore(), true),
    };

    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Gumps");

    private static GumpFixture Load(string name) =>
        JsonSerializer.Deserialize<GumpFixture>(File.ReadAllText(Path.Combine(Dir, name + ".json")), Json);

    public static IEnumerable<object[]> Names => Catalog.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureIsWhatTheSourceBuilds(string name)
    {
        GumpFixture built = Catalog[name]();
        if (Environment.GetEnvironmentVariable("UOCLIENT_WRITE_GUMP_FIXTURES") == "1")
            File.WriteAllText(Path.Combine(SourceDir(), name + ".json"), JsonSerializer.Serialize(built, Json) + "\n");

        GumpFixture saved = Load(name);
        Assert.Equal(JsonSerializer.Serialize(built, Json), JsonSerializer.Serialize(saved, Json));
    }

    [Fact]
    public void EveryFixtureFileIsInTheCatalog()
    {
        Assert.Equal(Catalog.Keys.Order(), Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).Order());
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void FixtureOpensOfflineWithTheSourcesControlsAndRepliesEveryButton(string name)
    {
        GumpFixture f = Load(name);
        ShardGump shard = f.ToShardGump();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, f.ToPacket());

        Assert.Equal((f.Serial, f.TypeId, f.X, f.Y), (gump.Serial, gump.TypeId, gump.X, gump.Y));
        Assert.Equal(f.Lines, gump.Lines);
        Assert.Equal(f.Layout.Length, gump.Commands.Count);
        Assert.Equal(f.Layout.Select(c => c.Split(' ')[1]), gump.Commands.Select(c => c.Name));
        Assert.Equal(f.ReplyButtons, ReplyIds(gump).Distinct().Order());
        Assert.Equal(f.TextEntries, gump.Commands.Count(c => c.Name is "textentry" or "textentrylimited"));
        Assert.Equal(f.Switches, gump.Commands.Count(c => c.Name is "checkbox" or "radio"));
        // Every text points at a line the gump sent.
        foreach (GumpCommand c in gump.Commands)
        {
            int line = c.Name switch { "text" => 3, "htmlgump" => 4, "croppedtext" => 5, "textentry" or "textentrylimited" => 6, _ => -1 };
            if (line >= 0)
                Assert.InRange(c.Arg(line), 0, f.Lines.Length - 1);
        }

        // Every reply button, answered with the controls as they open, is a 0xB1 ModernUO hands to OnResponse.
        var controls = new GumpControls(gump);
        foreach (int id in f.ReplyButtons.Prepend(0))
        {
            Assert.Null(shard.Refusal(GameSession.GumpReply(f.Serial, f.TypeId, id, controls.Switches(), controls.Entries())));
        }
        session.ReplyGump(gump, f.ReplyButtons.Prepend(0).Max(), controls.Switches(), controls.Entries());
        Assert.Equal(f.ReplyButtons.Prepend(0).Max(), shard.Accept(Assert.Single(sent)).Button);
    }

    // ---- what each fixture shows ----

    [Fact]
    public void StableThreePetsShowsTheGridThePanelAndTheRenameEntry()
    {
        GumpFixture f = Load("stable-three-pets");
        ServerGump gump = Open(InWorld(new List<byte[]>()), f.ToPacket());

        // B_Close 1, B_StablePet 2, B_ClaimAll 3, B_Claim 4, B_Rename 5, B_Release 6, B_Select 100 + cell; one page,
        // so no Prev (7) or Next (8).
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 100, 101, 102 }, f.ReplyButtons);
        Assert.Equal(f.ButtonIds, f.ReplyButtons);
        Assert.Equal(new[] { 0x259C, 0x2599, 0x25D0, 0x259C }, gump.Commands.Where(c => c.Name is "tilepic" or "tilepichue").Select(c => c.Arg(2)));
        // 17 empty slots outlined with four 1-pixel tiles each, the panel divider, three stat bars and their fills,
        // and the rename box.
        Assert.Equal(17 * 4 + 1 + 3 * 2 + 1, gump.Commands.Count(c => c.Name == "gumppictiled"));

        GumpCommand entry = Assert.Single(gump.Commands, c => c.Name == "textentrylimited");
        Assert.Equal(new[] { 568, 336, 136, 20, 0, 1 }, entry.Args.Take(6));
        Assert.Equal(16, entry.TextLimit);
        Assert.Equal("Shadowmere", gump.Line(entry.Arg(6)));
        Assert.Equal("Loyalty: Wonderfully Happy", gump.Lines.Single(l => l.StartsWith("Loyalty")));
        Assert.Contains("2 slots, bonded", gump.Lines);
        Assert.Contains("(dead) Dire Wolf", gump.Lines);
        // StatBar caps the fill at 200: Shadowmere's 446 Str fills all 120 pixels.
        Assert.Contains(gump.Commands, c => c.Name == "gumppictiled" && c.Args.SequenceEqual(new[] { 600, 203, 120, 9, 2627 }));
    }

    [Fact]
    public void StableRenameRepliesTheTypedNameUnderEntryOne()
    {
        GumpFixture f = Load("stable-three-pets");
        ShardGump shard = f.ToShardGump();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, f.ToPacket());

        var controls = new GumpControls(gump);
        Assert.Equal(new List<(int, string)> { (1, "Shadowmere") }, controls.Entries());
        controls.SetText(gump.Commands.FindIndex(c => c.Name == "textentrylimited"), "Midnight");
        session.ReplyGump(gump, 5, controls.Switches(), controls.Entries());

        Relay relay = shard.Accept(Assert.Single(sent));
        Assert.Equal(5, relay.Button);
        Assert.Equal("Midnight", relay.GetTextEntry(1));
        Assert.Empty(relay.Switches);
    }

    [Fact]
    public void StableSecondPageHasPrevNoNextAndTheEscapedNotice()
    {
        GumpFixture f = Load("stable-page2-notice");
        ShardGump shard = f.ToShardGump();
        ServerGump gump = Open(InWorld(new List<byte[]>()), f.ToPacket());

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 100, 101 }, f.ReplyButtons);
        Assert.Equal("Button 8 doesn't exist", shard.Refusal(GameSession.GumpReply(f.Serial, f.TypeId, 8, Array.Empty<int>(), new List<(int, string)>())));
        Assert.Equal("Button 102 doesn't exist", shard.Refusal(GameSession.GumpReply(f.Serial, f.TypeId, 102, Array.Empty<int>(), new List<(int, string)>())));
        Assert.Contains("Page 2 / 2", gump.Lines);
        Assert.Contains("Stable - 22 / 25 slots", gump.Lines.Single(l => l.Contains("slots")));
        Assert.Equal("<BASEFONT COLOR=#FFE080>You can&#39;t rename pets from jail.</BASEFONT>",
            gump.Line(gump.Commands.Where(c => c.Name == "htmlgump").Last().Arg(4)));
        Assert.Equal(3 * 4 + 1 + 3 * 2 + 1, gump.Commands.Count(c => c.Name == "gumppictiled"));
        Assert.Equal("Horse 21", gump.Line(Assert.Single(gump.Commands, c => c.Name == "textentrylimited").Arg(6)));
    }

    [Fact]
    public void StableEmptyHasNoEntryAndOnlyTheBottomRow()
    {
        GumpFixture f = Load("stable-empty");
        ServerGump gump = Open(InWorld(new List<byte[]>()), f.ToPacket());
        Assert.Equal(new[] { 1, 2, 3 }, f.ReplyButtons);
        Assert.Equal(0, f.TextEntries);
        Assert.Empty(new GumpControls(gump).Entries());
        Assert.Equal(20 * 4 + 1, gump.Commands.Count(c => c.Name == "gumppictiled"));
        Assert.Contains("<BASEFONT COLOR=#A0A0A0>No pet selected. Stable one with Stable Pet.</BASEFONT>", gump.Lines);
    }

    [Theory]
    [InlineData("stable-release-confirm", "<BASEFONT COLOR=#FF9090 SIZE=4><B>Release Shadowmere?</B></BASEFONT>", "Yes, release")]
    [InlineData("stable-release-bonded", "<BASEFONT COLOR=#FF5050 SIZE=4><B>Bonded pet!</B></BASEFONT>", "Yes, release it")]
    public void StableReleaseConfirmRepliesCancelOrConfirm(string name, string title, string yes)
    {
        GumpFixture f = Load(name);
        ShardGump shard = f.ToShardGump();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump gump = Open(session, f.ToPacket());

        Assert.Equal((250, 200), (gump.X, gump.Y));
        Assert.Equal(new[] { 1, 2 }, f.ReplyButtons);
        Assert.Equal(title, gump.Line(gump.Commands.First(c => c.Name == "htmlgump").Arg(4)));
        Assert.Equal(yes, gump.Line(gump.Commands.Last(c => c.Name == "text").Arg(3)));
        session.ReplyGump(gump, 2, new GumpControls(gump).Switches(), new GumpControls(gump).Entries());
        Assert.Equal(2, shard.Accept(Assert.Single(sent)).Button);
    }

    [Fact]
    public void AnimalLoreFixtureTurnsPagesLocallyAndAnswersOnlyClose()
    {
        GumpFixture f = Load("animal-lore-dragon");
        Assert.True(f.Dynamic);
        Assert.Equal("0xDD", f.Packet);
        ServerGump gump = Open(InWorld(new List<byte[]>()), f.ToPacket());
        Assert.Equal(new[] { 2 }, f.ReplyButtons);
        Assert.Equal(new[] { 0, 2 }, f.ButtonIds);   // ten page buttons, all id 0, and the close
        Assert.Equal(10, gump.Commands.Count(c => c.Name == "button" && ServerGumpWindow.ButtonPress(c).IsPage));
    }

    [Fact]
    public void GroupFinderAndAuctionFixturesDeclareWhatTheirRepliesNeed()
    {
        GumpFixture group = Load("group-finder-dungeons");
        Assert.Equal((0, 0), (group.Switches, group.TextEntries));
        Assert.Equal(new[] { 1, 2, 3, 11, 12, 13, 100, 101, 102 }, group.ReplyButtons);

        GumpFixture browse = Load("auction-browse");
        Assert.Equal((0, 1), (browse.Switches, browse.TextEntries));
        Assert.Equal(28, browse.ReplyButtons.Length);
        Assert.DoesNotContain(8000, browse.ButtonIds); // Prev is not on page 1

        GumpFixture sell = Load("auction-sell");
        Assert.Equal((3, 1), (sell.Switches, sell.TextEntries));
        ServerGump gump = Open(InWorld(new List<byte[]>()), sell.ToPacket());
        Assert.Equal(new[] { 101 }, new GumpControls(gump).Switches());
    }

    // ---- StableGump and StableReleaseConfirmGump (uo-offline-dev @ 0d14143 CustomBots/Stables) ----

    /// <summary>StableRow: what the window is drawn from.</summary>
    internal readonly record struct Pet(uint Serial, string Name, string Type, int ItemId, int Hue, int Loyalty,
        int Str, int Dex, int Int, int Hits, int HitsMax, int ControlSlots, bool Bonded, bool Dead);

    // Item ids are ShrinkTable's (Distribution/Data/shrink.json) for the stock bodies: Nightmare 0x74, Horse 0xE2,
    // DireWolf 23.
    private static List<Pet> ThreePets() => new()
    {
        new Pet(0x00001001, "Shadowmere", "Nightmare", 0x259C, 0, 100, 446, 98, 86, 412, 412, 2, true, false),
        new Pet(0x00001002, "Dobbin", "Horse", 0x2599, 0, 60, 44, 46, 6, 30, 30, 1, false, false),
        new Pet(0x00001003, "Fang", "Dire Wolf", 0x25D0, 0, 30, 96, 81, 36, 0, 64, 1, false, true),
    };

    private static List<Pet> Horses(int n) => Enumerable.Range(1, n)
        .Select(i => new Pet(0x00002000 + (uint)i, $"Horse {i}", "Horse", 0x2599, 0, 50, 44, 46, 6, 30, 30, 1, false, false))
        .ToList();

    private const int Cols = 5, PerPage = 20;

    private static readonly string[] Loyalty =
    {
        "Confused", "Extremely Unhappy", "Rather Unhappy", "Unhappy", "Somewhat Content", "Content",
        "Happy", "Rather Happy", "Very Happy", "Extremely Happy", "Wonderfully Happy",
    };

    // Server.Utilities.Html.EscapeHtml.
    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    /// <summary>The StableGump constructor's Add* calls, in order (StableRules' paging inlined).</summary>
    internal static ShardGump Stable(List<Pet> rows, int max, int page, uint selected, string notice)
    {
        const int Width = 790, Height = 540, CellW = 100, CellH = 100, Gap = 6, GridX = 20, GridY = 56, PanelX = 566;
        const int TextHue = 1153, DimHue = 902, SelHue = 53, BadHue = 0x22, GoodHue = 0x44;
        int cells = Math.Max(rows.Count, max), pages = Math.Max(1, (cells + PerPage - 1) / PerPage);
        page = Math.Clamp(page, 0, pages - 1);
        selected = rows.Exists(r => r.Serial == selected) ? selected
            : rows.Count > page * PerPage ? rows[page * PerPage].Serial : 0;

        var g = new ShardGump(0x40000201, 0x5AB1E000, 40, 40);
        g.AddPage(0);
        g.AddBackground(0, 0, Width, Height, 9270);
        g.AddHtml(20, 16, PanelX - 30, 24, $"<CENTER><BASEFONT COLOR=#F0D890 SIZE=5><B>Stable - {rows.Count} / {max} slots</B></BASEFONT></CENTER>");

        for (int i = 0; i < PerPage; i++)
        {
            if (page * PerPage + i >= cells)
                continue;
            int x = GridX + i % Cols * (CellW + Gap), y = GridY + i / Cols * (CellH + Gap), idx = page * PerPage + i;
            if (idx >= rows.Count)
            {
                g.AddImageTiled(x, y, CellW, 1, 2624);
                g.AddImageTiled(x, y + CellH - 1, CellW, 1, 2624);
                g.AddImageTiled(x, y, 1, CellH, 2624);
                g.AddImageTiled(x + CellW - 1, y, 1, CellH, 2624);
                continue;
            }
            Pet r = rows[idx];
            bool sel = r.Serial == selected;
            g.AddBackground(x, y, CellW, CellH, sel ? 9300 : 2620);
            g.AddButton(x + 4, y + 4, 1209, 1210, 100 + i);
            g.AddItem(x + 30, y + 14, r.ItemId, r.Hue);
            g.AddLabelCropped(x + 6, y + 60, CellW - 12, 18, sel ? SelHue : TextHue, r.Name ?? "(unnamed)");
            g.AddLabelCropped(x + 6, y + 78, CellW - 12, 18, r.Dead ? BadHue : DimHue, r.Dead ? "(dead) " + r.Type : r.Type);
        }

        g.AddImageTiled(PanelX - 10, GridY, 1, 4 * (CellH + Gap) - Gap, 2624);
        g.AddHtml(PanelX, GridY, 200, 20, "<BASEFONT COLOR=#F0D890><B>Selected Pet</B></BASEFONT>");
        if (selected != 0)
        {
            Pet r = rows.Find(p => p.Serial == selected);
            g.AddBackground(PanelX, GridY + 26, 80, 80, 2620);
            g.AddItem(PanelX + 18, GridY + 40, r.ItemId, r.Hue);
            g.AddLabelCropped(PanelX + 90, GridY + 30, 115, 20, SelHue, r.Name ?? "(unnamed)");
            g.AddLabelCropped(PanelX + 90, GridY + 50, 115, 20, TextHue, r.Type);
            g.AddLabelCropped(PanelX + 90, GridY + 70, 115, 20, DimHue,
                $"{r.ControlSlots} slot{(r.ControlSlots == 1 ? "" : "s")}{(r.Bonded ? ", bonded" : "")}");
            g.AddLabel(PanelX, GridY + 116, TextHue, $"Loyalty: {Loyalty[Math.Clamp(r.Loyalty / 10, 0, Loyalty.Length - 1)]}");
            (string Label, int Value, int Hue)[] bars = { ("Str", r.Str, 0x44), ("Dex", r.Dex, 0x59), ("Int", r.Int, 0x5A) };
            for (int b = 0; b < bars.Length; b++)
            {
                int y = GridY + 142 + b * 22;
                g.AddLabel(PanelX, y, TextHue, bars[b].Label);
                g.AddImageTiled(PanelX + 34, y + 5, 120, 9, 2624);
                int fill = Math.Clamp(bars[b].Value, 0, 200) * 120 / 200;
                if (fill > 0)
                    g.AddImageTiled(PanelX + 34, y + 5, fill, 9, 2627);
                g.AddLabel(PanelX + 160, y, bars[b].Hue, bars[b].Value.ToString());
            }
            g.AddLabel(PanelX, GridY + 210, r.Dead ? BadHue : TextHue, r.Dead ? "HP: dead" : $"HP: {r.Hits} / {r.HitsMax}");

            g.AddButton(PanelX, GridY + 244, 4005, 4007, 4);
            g.AddLabel(PanelX + 34, GridY + 246, GoodHue, "Claim");
            g.AddButton(PanelX + 110, GridY + 244, 4017, 4019, 6);
            g.AddLabel(PanelX + 144, GridY + 246, BadHue, "Release");
            g.AddImageTiled(PanelX, GridY + 280, 140, 20, 0xBBC);
            g.AddTextEntry(PanelX + 2, GridY + 280, 136, 20, 0, 1, r.Name ?? "", 16);
            g.AddButton(PanelX + 150, GridY + 280, 4011, 4013, 5);
            g.AddLabel(PanelX + 184, GridY + 282, TextHue, "Rename");
        }
        else
            g.AddHtml(PanelX, GridY + 30, 200, 60, "<BASEFONT COLOR=#A0A0A0>No pet selected. Stable one with Stable Pet.</BASEFONT>");

        if (!string.IsNullOrEmpty(notice))
            g.AddHtml(20, Height - 74, Width - 40, 20, $"<BASEFONT COLOR=#FFE080>{EscapeHtml(notice)}</BASEFONT>");

        int by = Height - 44;
        g.AddButton(20, by, 4005, 4007, 2);
        g.AddLabel(54, by + 2, TextHue, "Stable Pet");
        g.AddButton(160, by, 4005, 4007, 3);
        g.AddLabel(194, by + 2, TextHue, "Claim All");
        if (pages > 1)
        {
            if (page > 0)
                g.AddButton(310, by, 4014, 4016, 7);
            g.AddLabel(346, by + 2, TextHue, $"Page {page + 1} / {pages}");
            if (page < pages - 1)
                g.AddButton(430, by, 4005, 4007, 8);
        }
        g.AddButton(Width - 110, by, 4017, 4019, 1);
        g.AddLabel(Width - 76, by + 2, TextHue, "Close");
        return g;
    }

    /// <summary>StableReleaseConfirmGump's constructor: the first confirm, or the bonded pet's second.</summary>
    internal static ShardGump StableReleaseConfirm(string name, string type, bool second)
    {
        const int Width = 380, Height = 180;
        string n = EscapeHtml(name ?? "this pet");
        var g = new ShardGump(0x40000202, 0x5AB1E001, 250, 200);
        g.AddPage(0);
        g.AddBackground(0, 0, Width, Height, 9270);
        if (!second)
        {
            g.AddHtml(16, 14, Width - 32, 22, $"<BASEFONT COLOR=#FF9090 SIZE=4><B>Release {n}?</B></BASEFONT>");
            g.AddHtml(16, 42, Width - 32, 80,
                $"<BASEFONT COLOR=#E0E0E0>{n} ({EscapeHtml(type ?? "")}) leaves the stables and goes wild next to the stable master. This can't be undone.</BASEFONT>");
        }
        else
        {
            g.AddHtml(16, 14, Width - 32, 22, "<BASEFONT COLOR=#FF5050 SIZE=4><B>Bonded pet!</B></BASEFONT>");
            g.AddHtml(16, 42, Width - 32, 80,
                $"<BASEFONT COLOR=#FFD0D0>This pet is bonded to you. Releasing it is permanent. Really release {n}?</BASEFONT>");
        }
        g.AddButton(20, Height - 40, 4017, 4019, 1);
        g.AddLabel(54, Height - 38, 1153, "Cancel");
        g.AddButton(Width - 160, Height - 40, 4005, 4007, 2);
        g.AddLabel(Width - 126, Height - 38, 0x22, second ? "Yes, release it" : "Yes, release");
        return g;
    }

    // ---- harness ----

    private static IEnumerable<int> ReplyIds(ServerGump g) => g.Commands
        .Where(c => c.Name is "button" or "buttontileart")
        .Select(ServerGumpWindow.ButtonPress).Where(b => !b.IsPage).Select(b => b.Value);

    // The test project's own Fixtures/Gumps, for rewriting the files.
    private static string SourceDir([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "Gumps");
}
