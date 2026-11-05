using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.Net;
using UOClient.Unity.UI;
using static UOClient.Tests.ShardGumpLayoutTests;

namespace UOClient.Tests;

/// <summary>
/// Live's taming and stable windows, from the copies in _cc_logs/references (live UOContent 39039e034, PROVENANCE.txt):
/// CustomBots/Gumps CastleKit.cs, GumpStyle.cs, ItemIconCell.cs, Stables/StableGump.cs (the Breeding tab) and
/// Taming/PetLoreGump.cs. Each layout is the sequence of Add* calls those files make, with CastleKit's stock fallback art
/// (uooffline.castleKitArt off, the default), sent as 0xB0 through UncompressedGump, opened offline and read through
/// ServerGumpWindow's rules.
/// </summary>
public sealed class TamingGumpTests
{
    // CastleKit.cs:67-73 (Fallback), :79-87 (GemIds with Art off), :90 (hues).
    private const int Frame = 2620, Trim = 2620, Sel = 9270, Plate = 2620, Band = 2621;
    private const int ArrL = 5603, ArrLDown = 5607, ArrR = 5601, ArrRDown = 5605, CloseUp = 4017, CloseDown = 4019, GuideUp = 2151, GuideDown = 2152;
    private const int GemUp = 2151, GemDown = 2152, GemSelUp = 9720, GemSelDown = 9721;
    private const int GoldHue = 53, WhiteHue = 1153, BlueHue = 88, GreenHue = 68, DimHue = 946, RedHue = 33;

    // PetLoreGump.cs:50-51.
    private const int BarBack = 0x805, BarBlue = 0x806, BarGreen = 0x808, BarGold = 0x809, BarW = 109, Backdrop = 2524;

    // StableRules.cs:128 (ShrinkTable.DefaultItemID).
    private const int FallbackItemID = 0x1870;

    private enum Gem { Blue, Gold, Red, Grey }

    /// <summary>CastleGump's helpers (CastleKit.cs:105-249) over a legacy gump, as the live server draws them with Art off.</summary>
    private sealed class Castle
    {
        public readonly ShardGump G;
        public const int L = 28;
        public int W, H;

        public Castle(uint serial, uint typeId, int x, int y) => G = new ShardGump(serial, typeId, x, y);

        // GumpStyle.cs:41-55, unifont1 advance widths.
        private static readonly int[] Adv =
        {
            8, 3, 4, 12, 9, 10, 11, 3, 4, 4, 10, 7, 3, 6, 3, 9, 8, 4, 8, 8, 8, 8, 8, 8, 8, 8, 3, 3, 8, 6, 8, 7, 12, 8, 8, 8, 8, 7, 7, 8, 8, 3, 8, 8, 7, 10, 8, 8,
            8, 9, 8, 8, 7, 8, 8, 12, 8, 9, 8, 4, 9, 5, 10, 8, 3, 6, 6, 6, 6, 6, 6, 6, 6, 3, 6, 6, 3, 9, 6, 6, 6, 6, 6, 6, 6, 6, 6, 8, 6, 6, 6, 5, 2, 5, 6,
        };

        public static int TextW(string s) => (s ?? "").Sum(ch => ch >= 32 && ch <= 126 ? Adv[ch - 32] : 8);

        private static int HueOf(Gem c) => c switch { Gem.Gold => GoldHue, Gem.Red => RedHue, Gem.Grey => DimHue, _ => BlueHue };

        // CastleKit.cs:120-145 (no kit icon: Art off).
        public void Frame(int w, int h, string title, string sub)
        {
            W = w;
            H = h;
            G.AddPage(0);
            G.AddBackground(0, 0, w, h, TamingGumpTests.Frame);
            int tw = TextW(title) + (sub != null ? 12 + TextW(sub) : 0);
            int px = w / 2 - tw / 2 - 20;
            G.AddBackground(px, 0, tw + 40, 40, Plate);
            G.AddLabel(px + 20, 11, GoldHue, title);
            if (sub != null)
                G.AddLabel(px + 20 + TextW(title) + 12, 11, WhiteHue, sub);
            G.AddButton(28, 28, GuideUp, GuideDown, 901);
            G.AddLabel(54, 31, DimHue, "Guide");
            G.AddButton(w - 58, 28, CloseUp, CloseDown, 0);
        }

        public void Panel(int x, int y, int w, int h, int kit = Trim) => G.AddBackground(x, y, w, h, kit);

        // CastleKit.cs:150-156.
        public void Section(int x, int y, int w, string t)
        {
            G.AddImageTiled(x, y + 9, w, 4, Band);
            int tw = TextW(t);
            G.AddBackground(x + w / 2 - tw / 2 - 8, y, tw + 16, 22, Trim);
            G.AddLabel(x + w / 2 - tw / 2, y + 2, GoldHue, t);
        }

        // CastleKit.cs:159-176.
        public int GemBtn(int x, int y, int id, string label, Gem c = Gem.Blue, int size = 21, bool sel = false, int hue = -1, bool enabled = true)
        {
            (int up, int down) = sel ? (GemSelUp, GemSelDown) : (GemUp, GemDown);
            if (enabled)
                G.AddButton(x, y, up, down, id);
            else
                G.AddImage(x, y, up);
            if (!string.IsNullOrEmpty(label))
                G.AddLabel(x + size + 5, y + (size - 16) / 2 - 1, hue >= 0 ? hue : enabled ? HueOf(c) : DimHue, label);
            return x + size + 5 + TextW(label ?? "") + 16;
        }

        // CastleKit.cs:179-180.
        public int Tab(int x, int y, int id, string label, bool sel, bool enabled = true) =>
            GemBtn(x, y, id, label, sel ? Gem.Gold : Gem.Grey, 21, sel, sel ? GoldHue : enabled ? WhiteHue : DimHue, enabled);

        // CastleKit.cs:228-238 with GumpStyle.Cropped (the texts here all fit, so Fit leaves them as they are).
        public void Text(int x, int y, string t, int hue = WhiteHue, int w = 0)
        {
            if (w > 0)
                G.AddLabelCropped(x, y, w, 20, hue, t);
            else
                G.AddLabel(x, y, hue, t);
        }
    }

    /// <summary>
    /// PetLoreGump (PetLoreGump.cs:79-119) for a viewer who is not the master: Castle, Tabs (:125-140, Training and Care
    /// grey), Portrait (:153-195, a controlled pet with no record art: StableRules.FallbackItemID, the offline 44 x 44
    /// tile of ItemIconCell.cs:79 centred in the cell), the content panel, and the Stats tab's attribute rows (:354-369).
    /// </summary>
    private static Castle PetLoreStats(uint serial = 0x40000301)
    {
        const int Width = 660, Height = 570, BTab = 10, LX = 28, LW = 214, RX = 252, RW = 380, TY = 56, CY = 100, CH = 410;
        const int X0 = RX + 16;
        var k = new Castle(serial, 0x5E7A1022, 170, 24);
        k.Frame(Width, Height, "Animal Lore", "Shadowmere");

        string[] labels = { "Overview", "Stats", "Skills", "Training", "Abilities", "Care" };
        bool[] open = { true, true, true, false, true, false };
        for (int i = 0; i < labels.Length; i++)
        {
            const int tw = 95, gap = 6;
            bool sel = i == 1;
            int x = Castle.L + i * (tw + gap);
            k.Panel(x, TY, tw, 36, sel ? Sel : Trim);
            int lw = 21 + 5 + Castle.TextW(labels[i]);
            k.Tab(x + (tw - lw) / 2, TY + 8, BTab + i + 1, labels[i], sel, open[i]);
        }

        k.Panel(LX, CY, LW, 176, Sel);
        k.G.AddImageTiled(LX + 8, CY + 8, LW - 16, 160, Backdrop);
        k.G.AddItem(LX + 8 + (LW - 16 - 44) / 2, CY + 8 + (160 - 44) / 2, FallbackItemID, 1109);
        int y = CY + 184;
        k.Text(LX + 4, y, "Shadowmere", GoldHue, LW - 8);
        k.Text(LX + 4, y + 20, "Nightmare", WhiteHue, LW - 8);
        int vy = y + 110;
        (string Label, int Value, int Max, int Fill)[] vitals = { ("HP", 410, 520, BarGreen), ("Stam", 96, 150, BarGold), ("Mana", 0, 0, BarBlue) };
        for (int i = 0; i < vitals.Length; i++)
        {
            (string label, int value, int max, int fill) = vitals[i];
            k.Text(LX + 4, vy + 22 * i, label, DimHue);
            Bar(k, LX + 40, vy + 22 * i + 4, value, max, fill);
            k.Text(LX + 154, vy + 22 * i, $"{value}/{max}", WhiteHue, LW - 154);
        }

        k.Panel(RX, CY, RW, CH);
        k.Section(RX + 8, CY + 8, RW - 16, "Attributes");
        string[] stats = { "Str", "Dex", "Int" };
        for (int i = 0; i < 3; i++)
        {
            int ry = CY + 38 + 24 * i;
            k.Text(X0, ry, stats[i], DimHue);
            k.Text(RX + 60, ry, "400 / 500", WhiteHue, 84);
            Bar(k, RX + 148, ry + 4, 400, 500, BarGreen);
        }
        return k;
    }

    // PetLoreGump.cs:216-225: the red back as it is, the fill tiled to value / max of 109.
    private static void Bar(Castle k, int x, int y, double value, double max, int fill)
    {
        k.G.AddImage(x, y, BarBack);
        int n = max <= 0 ? 0 : (int)Math.Round(BarW * Math.Clamp(value / max, 0, 1));
        if (n > 0)
            k.G.AddImageTiled(x, y, n, 11, fill);
    }

    /// <summary>
    /// StableGump's Breeding tab, nothing under way (StableGump.cs:427-481): two Pickers (:398-425) with their arrows, the
    /// Requirements checklist with its 10 x 10 met / unmet squares (:448-459, rows and notes from StableBreeding.cs:60-79
    /// and :304-319), and Accept greyed while a requirement is unmet (:473).
    /// </summary>
    private static Castle StableBreedingTab()
    {
        const int Width = 760, Height = 700, LX = 28, GY = 92, PH = 96, PW = 344, CLY = GY + PH + 6, CLH = 326, CLW = 404, CRX = LX + CLW + 8, CRW = Width - 28 - CRX;
        const int BBreedPrevA = 30, BBreedNextA = 31, BBreedPrevB = 32, BBreedNextB = 33, BBreedAccept = 34, BStablePet = 2, BStableAll = 9, BClaimAll = 3;
        var k = new Castle(0x40000302, 0x3C51A0F4, 60, 24);
        k.Frame(Width, Height, "Stable", "2 / 5 stabled");
        k.Text(LX, 63, "Followers", DimHue);
        k.Text(LX + Castle.TextW("Followers") + 8, 63, "1 / 5 slots");
        int x = LX + 190;
        x = k.GemBtn(x, 60, BStablePet, "Stable Pet...") + 6;
        x = k.GemBtn(x, 60, BStableAll, "Stable All Followers") + 6;
        k.GemBtn(x, 60, BClaimAll, "Claim All");

        void Picker(int px, string name, string type, string where, int whereHue, int prevId, int nextId)
        {
            k.Panel(px, GY, PW, PH);
            k.G.AddButton(px + 6, GY + 37, ArrL, ArrLDown, prevId);
            k.G.AddButton(px + 128, GY + 37, ArrR, ArrRDown, nextId);
            k.Panel(px + 40, GY + 6, 84, 84, Sel);
            k.G.AddImageTiled(px + 46, GY + 12, 72, 72, Backdrop);
            k.G.AddItem(px + 46 + (72 - 44) / 2, GY + 12 + (72 - 44) / 2, FallbackItemID, 0);
            int tx = px + 164, tw = PW - 172;
            k.Text(tx, GY + 6, name, GoldHue, tw);
            k.Text(tx, GY + 23, type, WhiteHue, tw);
            k.Text(tx, GY + 74, where, whereHue, tw);
        }
        Picker(LX, "Shadowmere *", "Nightmare", "With you", GreenHue, BBreedPrevA, BBreedNextA);
        Picker(LX + PW + 16, "Ember", "Nightmare", "In your stable", DimHue, BBreedPrevB, BBreedNextB);

        k.Panel(LX, CLY, CLW, CLH);
        k.Panel(CRX, CLY, CRW, CLH);
        k.Section(LX + 8, CLY + 6, CLW - 16, "Requirements");
        (string Label, bool? Met, string Note)[] lines =
        {
            ("Breeding licence", true, "The Long Bond done"),
            ("None under way", true, "none"),
            ("Yours, here with you", false, "right: stabled, claim it"),
            ("Both bonded", true, "yes / yes"),
            ("Free to breed", null, ""),
        };
        for (int i = 0; i < lines.Length; i++)
        {
            (string label, bool? met, string note) = lines[i];
            int ly = CLY + 34 + i * 17;
            if (met is { } m)
                k.G.AddImageTiled(LX + 14, ly + 5, 10, 10, m ? BarGreen : BarBack);
            k.Text(LX + 30, ly, label, met == null ? DimHue : WhiteHue, 160);
            k.Text(LX + 194, ly, note, met == true ? GreenHue : met == false ? RedHue : DimHue, 200);
        }
        k.Section(CRX + 8, CLY + 6, CRW - 16, "Fee and food");
        k.GemBtn(CRX + 14, CLY + 108, BBreedAccept, "Accept", Gem.Gold, 29, enabled: false);
        k.Text(CRX + 14 + 110, CLY + 114, "1 not met", RedHue, CRW - 124);
        return k;
    }

    /// <summary>The hue table the client reads from hues.mul, with every shade of every hue a different colour.</summary>
    private static HueEntry[] DistinctHues(int count = 3000)
    {
        var hues = new HueEntry[count];
        for (int h = 0; h < count; h++)
        {
            var ramp = new ushort[32];
            for (int c = 0; c < 32; c++)
                ramp[c] = (ushort)((((h >> 5) & 0x1F) << 10) | ((h & 0x1F) << 5) | c);
            hues[h] = new HueEntry { Colors = ramp };
        }
        return hues;
    }

    private static IEnumerable<int> LabelHues(ServerGump g) => g.Commands
        .Where(c => c.Name is "text" or "croppedtext")
        .Select(c => c.Name == "text" ? c.Arg(2) : c.Arg(4));

    [Fact]
    public void TamingGumpsUseOnlyCommandsTheRendererDraws()
    {
        // ServerGumpWindow.DrawCommand draws each of these; nothing in the live helpers sends anything else.
        var drawn = new HashSet<string> { "page", "resizepic", "gumppic", "gumppictiled", "tilepic", "tilepichue", "text", "croppedtext", "htmlgump", "button" };
        foreach (Castle k in new[] { PetLoreStats(), StableBreedingTab() })
        {
            var sent = new List<byte[]>();
            ServerGump g = Open(InWorld(sent), k.G.Uncompressed());
            Assert.All(g.Commands, c => Assert.Contains(c.Name, drawn));
            Assert.Equal(ServerGump.ParseLayout(k.G.Layout).Count, g.Commands.Count);
        }
    }

    [Fact]
    public void CastleKitLabelsTakeTazUOsUnicodeTextShade()
    {
        HueEntry[] hues = DistinctHues();
        var sent = new List<byte[]>();
        ServerGump lore = Open(InWorld(sent), PetLoreStats().G.Uncompressed());
        ServerGump stable = Open(InWorld(sent), StableBreedingTab().G.Uncompressed());
        var used = LabelHues(lore).Concat(LabelHues(stable)).Distinct().OrderBy(h => h).ToArray();
        Assert.Equal(new[] { RedHue, GoldHue, GreenHue, BlueHue, DimHue, WhiteHue }, used);

        foreach (int serverHue in used)
        {
            int hue = ServerGumpWindow.TextHue(serverHue);
            uint argb = ServerGumpWindow.TextArgb(hues, hue);
            // Label/CroppedText → RenderedText cell 30 → GetPolygoneColor(30, hue): entry hue-1, shade 30.
            Assert.Equal(Color16.ToArgb32(hues[hue - 1].Colors[30]), argb);
            Assert.NotEqual(Color16.ToArgb32(hues[hue - 1].Colors[24]), argb);
            Assert.True(ServerGumpWindow.HasBlackBorder(argb));
        }
    }

    [Fact]
    public void TextShadeEdgesFollowGetPolygoneColor()
    {
        HueEntry[] hues = DistinctHues(16);
        Assert.Equal(ServerGumpWindow.TextBlackArgb, ServerGumpWindow.TextArgb(hues, 0));
        Assert.Equal(ServerGumpWindow.TextBlackArgb, ServerGumpWindow.TextArgb(hues, 16)); // color < HuesCount
        Assert.Equal(Color16.ToArgb32(hues[14].Colors[30]), ServerGumpWindow.TextArgb(hues, 15));
        Assert.Equal(ServerGumpWindow.TextBlackArgb, ServerGumpWindow.TextArgb(null, 1153));
        // A server hue of 0xFFFF wraps to client hue 0 (TextHue's ushort): near-black, as Label's (ushort)(hue + 1).
        Assert.Equal(ServerGumpWindow.TextBlackArgb, ServerGumpWindow.TextArgb(hues, ServerGumpWindow.TextHue(0xFFFF)));
    }

    [Fact]
    public void BlackBorderIsEightNeighboursAndSkipsBlackText()
    {
        Assert.Equal(8, ServerGumpWindow.BlackBorderOffsets.Distinct().Count());
        Assert.All(ServerGumpWindow.BlackBorderOffsets, o => Assert.True(Math.Abs(o.Dx) <= 1 && Math.Abs(o.Dy) <= 1 && (o.Dx, o.Dy) != (0, 0)));
        Assert.False(ServerGumpWindow.HasBlackBorder(ServerGumpWindow.TextBlackArgb));
        Assert.False(ServerGumpWindow.HasBlackBorder(0xFF080808));
        Assert.True(ServerGumpWindow.HasBlackBorder(0xFF090808));
        Assert.True(ServerGumpWindow.HasBlackBorder(0xFF000009));
    }

    [Fact]
    public void BreedingTabRepliesOnlyWithItsLiveButtons()
    {
        Castle k = StableBreedingTab();
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump g = Open(session, k.G.Uncompressed());

        // Accept is unmet: a grey gem picture (GemBtn enabled: false), no reply button 34.
        int[] replies = g.Commands.Where(c => c.Name == "button").Select(ServerGumpWindow.ButtonPress)
            .Where(b => !b.IsPage).Select(b => b.Value).ToArray();
        Assert.Equal(new[] { 901, 0, 2, 9, 3, 30, 31, 32, 33 }, replies);
        Assert.Contains(g.Commands, c => c.Name == "gumppic" && c.Arg(2) == GemUp && c.Arg(0) == 28 + 404 + 8 + 14);

        // The checklist's squares are 10 x 10 cuts of the 109 x 11 status bars; unknown rows have none.
        var squares = g.Commands.Where(c => c.Name == "gumppictiled" && c.Arg(2) == 10 && c.Arg(3) == 10).Select(c => c.Arg(4)).ToArray();
        Assert.Equal(new[] { BarGreen, BarGreen, BarBack, BarGreen }, squares);

        // The left picker's next arrow, answered as DisplayGumpResponse takes it.
        session.ReplyGump(g, 31, new GumpControls(g).Switches(), new GumpControls(g).Entries());
        Assert.Equal(31, k.G.Accept(Assert.Single(sent)).Button);
    }

    [Fact]
    public void LoreTabsGreyWhatTheViewerMayNotSee()
    {
        var sent = new List<byte[]>();
        ServerGump g = Open(InWorld(sent), PetLoreStats().G.Uncompressed());
        // PetLoreGump.Navigate takes B_Tab + LorePage (11-16); Training (14) and Care (16) are pictures for a non-master.
        int[] tabs = g.Commands.Where(c => c.Name == "button").Select(c => c.Arg(6)).Where(id => id is > 10 and <= 16).ToArray();
        Assert.Equal(new[] { 11, 12, 13, 15 }, tabs);
        Assert.Equal(1, g.Commands.Count(c => c.Name == "button" && c.Arg(6) == 12 && c.Arg(2) == GemSelUp));

        // Vitals: the 109-wide back drawn whole, the fill tiled to value / max; an empty pool (Mana 0/0) has no fill.
        var fills = g.Commands.Where(c => c.Name == "gumppictiled" && c.Arg(3) == 11).Select(c => (c.Arg(2), c.Arg(4))).ToArray();
        Assert.Equal(new[] { (86, BarGreen), (70, BarGold), (87, BarGreen), (87, BarGreen), (87, BarGreen) }, fills);
        Assert.Equal(6, g.Commands.Count(c => c.Name == "gumppic" && c.Arg(2) == BarBack));
        Assert.Contains(g.Commands, c => c.Name == "tilepichue" && c.Arg(2) == FallbackItemID && c.Arg(3) == 1109);
    }

    [Fact]
    public void LoreTabClickReopensWhereThePlayerLeftIt()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        var positions = new GumpPositions();
        Castle first = PetLoreStats();
        ServerGump g = Open(session, first.G.Uncompressed());
        Assert.True(ServerGumpWindow.CanMove(g));
        Assert.Equal((170, 24), positions.Open(g.TypeId, g.X, g.Y));

        // Dragged to (400, 90), then the Skills tab (B_Tab + LorePage.Skills = 13): a reply DisplayGumpResponse takes.
        session.ReplyGump(g, 13, new GumpControls(g).Switches(), new GumpControls(g).Entries());
        positions.Closed(g.TypeId, ServerGumpWindow.CanMove(g), 400, 90);
        Assert.Equal(13, first.G.Accept(Assert.Single(sent)).Button);
        Assert.Empty(session.World.Gumps);

        // OnResponse: GumpSystem already dropped the old gump, so CloseGump<PetLoreGump> sends nothing; SendGump's new
        // PetLoreGump (a new serial, the same type and x, y) opens where the old one was left (GumpHelpers.cs:76-80).
        ServerGump next = Open(session, PetLoreStats(0x40000311).G.Uncompressed());
        Assert.Equal((170, 24), (next.X, next.Y));
        Assert.Equal((400, 90), positions.Open(next.TypeId, next.X, next.Y));

        // Another type keeps its own spot.
        Assert.Equal((60, 24), positions.Open(0x3C51A0F4, 60, 24));
    }

    [Fact]
    public void NomoveGumpStaysPutAndForgetsItsSpot()
    {
        var shard = new ShardGump(0x40000320, 0x0BADF00D, 200, 150) { Draggable = false };
        shard.AddPage(0);
        shard.AddBackground(0, 0, 200, 100, Frame);
        shard.AddButton(10, 10, GemUp, GemDown, 1);
        ServerGump g = Open(InWorld(new List<byte[]>()), shard.Uncompressed());
        Assert.False(ServerGumpWindow.CanMove(g));

        var positions = new GumpPositions();
        positions.Closed(g.TypeId, true, 30, 40); // an earlier movable copy left at (30, 40)
        Assert.Equal((30, 40), positions.Open(g.TypeId, g.X, g.Y));
        positions.Closed(g.TypeId, ServerGumpWindow.CanMove(g), 30, 40); // RemovePosition (Gump.cs:297)
        Assert.Equal((200, 150), positions.Open(g.TypeId, g.X, g.Y));
    }

    [Fact]
    public void LoreFromTheStableClosesOnlyTheOldLoreWindow()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        ServerGump lore = Open(session, PetLoreStats().G.Uncompressed());
        Castle stable = StableBreedingTab();
        ServerGump stableGump = Open(session, stable.G.Uncompressed());

        // B_Lore (StableGump.cs:754-764): Show re-sends the stable, then PetLoreGump.DisplayTo closes the open Lore
        // window (NetStateGumps.Close: 0xBF 0x04 with its type id, button 0) and sends the new one.
        session.Inject(new PacketWriter(0xBF).UInt16(0x04).UInt32(lore.TypeId).UInt32(0).ToArray(), true);
        Assert.Equal(new[] { stableGump }, session.World.Gumps);
        Assert.Empty(sent); // no reply: TazUO's close with button 0 only disposes (ExtendedCommand.cs:59-69)
    }

    /// <summary>
    /// PlayerPromptGump (GumpStyle.cs:150-171) as StableGump's Rename sends it (StableGump.cs:783): title, hint, the
    /// one textentrylimited (EntryId 1, size 16) over its 0xBBC strip, Cancel 1 and Ok 2.
    /// </summary>
    private static ShardGump RenamePrompt(string petName)
    {
        const int Width = 360, Height = 150;
        var g = new ShardGump(0x40000330, 0x2F0C11A7, 200, 160);
        g.AddPage(0);
        g.AddBackground(0, 0, Width, Height, 9270);
        g.AddHtml(16, 14, Width - 32, 22, $"<BASEFONT COLOR=#F4F4F4 SIZE=4><B>Rename {petName}</B></BASEFONT>");
        g.AddLabelCropped(16, 40, Width - 32, 20, 0x3B2, "Up to 16 letters.");
        g.AddImageTiled(16, 70, Width - 32, 20, 0xBBC);
        g.AddTextEntry(18, 70, Width - 36, 20, 0x480, 1, petName, 16);
        g.AddButton(14, 110, 4005, 4007, 1);
        g.AddLabel(48, 112, 1153, "Cancel");
        g.AddButton(230, 110, 4005, 4007, 2);
        g.AddLabel(264, 112, 0x35, "Rename");
        return g;
    }

    [Fact]
    public void RenamePromptAnswersWithItsEntryWhateverTheButton()
    {
        ShardGump shard = RenamePrompt("Shadowmere");
        foreach (int button in new[] { 2, 1, ServerGumpWindow.RightClickReplyButton })
        {
            var sent = new List<byte[]>();
            GameSession session = InWorld(sent);
            ServerGump g = Open(session, shard.Uncompressed());
            int entry = ServerGumpWindow.FirstTextEntry(g.Commands);
            Assert.Equal("textentrylimited", g.Commands[entry].Name);
            Assert.Equal(16, g.Commands[entry].TextLimit);

            var controls = new GumpControls(g);
            Assert.Equal("Shadowmere", controls.Text(entry));
            controls.SetText(entry, "  Ember  ");
            Assert.Empty(controls.Switches());

            // Gump.OnButtonClick sends every text box with any button; OnResponse trims (GumpStyle.cs:182).
            session.ReplyGump(g, button, controls.Switches(), controls.Entries());
            Relay relay = shard.Accept(Assert.Single(sent));
            Assert.Equal(button, relay.Button);
            Assert.Equal("Ember", relay.GetTextEntry(1)?.Trim());
        }
    }
}
