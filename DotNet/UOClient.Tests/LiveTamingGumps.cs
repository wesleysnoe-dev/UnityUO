using System.Globalization;
using static UOClient.Tests.ShardGumpLayoutTests;

namespace UOClient.Tests;

/// <summary>
/// Live's StableGump (with its Breeding tab) and PetLoreGump, replayed from the copies in _cc_logs/references (live
/// UOContent 39039e034, PROVENANCE.txt) for the saved fixtures: CastleKit.cs and GumpStyle.cs (the kit, with
/// uooffline.castleKitArt off, the default: the stock Fallback art), ItemIconCell.cs, Stables/StableGump.cs,
/// Stables/StableBreeding.cs, Taming/PetBreeding.cs, Taming/PetCards.cs, Taming/PetLoreText.cs and Taming/PetLoreGump.cs.
/// Each builder makes the constructor's Add* calls in order for one snapshot; nothing here is drawn that those files do
/// not draw. Where a value comes from a file that is not in the references (PetTiers, PetLoyalty, PetRecord), the
/// snapshot names it; only values the references themselves spell out are used (see each builder).
/// StorageGump (CastleGump's base) is not in the references either: its flags are taken as Gump's defaults, as
/// TamingGumpTests does.
/// </summary>
internal static class LiveTamingGumps
{
    public const string Refs = "live UOContent 39039e034 (_cc_logs/references) CustomBots/";

    // ---- CastleKit.cs with Art off ----

    // CastleKit.cs:67-73 (Fallback, normal and pressed), :79-87 (GemIds), :90 (hues).
    public const int Frame = 2620, Trim = 2620, Sel = 9270, Plate = 2620, Band = 2621;
    public const int ArrL = 5603, ArrLDown = 5607, ArrR = 5601, ArrRDown = 5605, PgL = 5603, PgLDown = 5607, PgR = 5601, PgRDown = 5605;
    public const int CloseUp = 4017, CloseDown = 4019, GuideUp = 2151, GuideDown = 2152;
    public const int GemUp = 2151, GemDown = 2152, GemSelUp = 9720, GemSelDown = 9721;
    public const int GoldHue = 53, WhiteHue = 1153, BlueHue = 88, GreenHue = 68, DimHue = 946, RedHue = 33;
    public const int GuideButtonId = 901;

    // PetLoreGump.cs:50-51.
    public const int BarBack = 0x805, BarBlue = 0x806, BarGreen = 0x808, BarGold = 0x809, BarW = 109, Backdrop = 2524;

    // ItemIconMap.g.cs:9.
    private const int IconSize = 56;

    public enum Gem { Blue, Gold, Red, Grey }

    // GumpStyle.cs:41-45, unifont1 advance widths for ' '..'~'; anything else 8.
    private static readonly int[] Adv =
    {
        8, 3, 4, 12, 9, 10, 11, 3, 4, 4, 10, 7, 3, 6, 3, 9, 8, 4, 8, 8, 8, 8, 8, 8, 8, 8, 3, 3, 8, 6, 8, 7, 12, 8, 8, 8, 8, 7, 7, 8, 8, 3, 8, 8, 7, 10, 8, 8,
        8, 9, 8, 8, 7, 8, 8, 12, 8, 9, 8, 4, 9, 5, 10, 8, 3, 6, 6, 6, 6, 6, 6, 6, 6, 3, 6, 6, 3, 9, 6, 6, 6, 6, 6, 6, 6, 6, 6, 8, 6, 6, 6, 5, 2, 5, 6,
    };

    // GumpStyle.cs:47-55.
    public static int Measure(string s) => (s ?? "").Sum(ch => ch >= 32 && ch <= 126 ? Adv[ch - 32] : 8);

    // GumpStyle.cs:57-69.
    public static string Fit(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || Measure(text) <= width)
            return text;
        int n = text.Length;
        while (n > 0 && Measure(text[..n].TrimEnd()) + Measure("...") > width)
            n--;
        return text[..n].TrimEnd() + "...";
    }

    // Server.Utilities.Html.EscapeHtml (Html.cs:150-209).
    public static string EscapeHtml(string s) => string.IsNullOrEmpty(s) ? s ?? "" :
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    // PetLoreGump.cs:241-268, the word-wrapped line count.
    public static int WrapLines(string text, int w)
    {
        int n = 0;
        foreach (string para in (text ?? "").Split('\n'))
        {
            n++;
            int line = 0;
            foreach (string word in para.Split(' '))
            {
                int ww = Measure(word), sp = line == 0 ? 0 : 8;
                if (line > 0 && line + sp + ww > w)
                {
                    n++;
                    line = ww;
                }
                else
                    line += sp + ww;
            }
        }
        return n;
    }

    private static string N0(long v) => v.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>CastleGump (CastleKit.cs:105-260) over a legacy gump.</summary>
    public sealed class Castle
    {
        public readonly ShardGump G;
        public const int L = 28;
        public int W, H;

        public Castle(uint serial, uint typeId, int x, int y) => G = new ShardGump(serial, typeId, x, y);

        private static int HueOf(Gem c) => c switch { Gem.Gold => GoldHue, Gem.Red => RedHue, Gem.Grey => DimHue, _ => BlueHue };

        // CastleKit.cs:120-145 (no kit icon: Art off).
        public void Frame(int w, int h, string title, string sub)
        {
            W = w;
            H = h;
            G.AddPage(0);
            G.AddBackground(0, 0, w, h, LiveTamingGumps.Frame);
            int tw = Measure(title) + (sub != null ? 12 + Measure(sub) : 0);
            int px = w / 2 - tw / 2 - 20;
            G.AddBackground(px, 0, tw + 40, 40, Plate);
            G.AddLabel(px + 20, 11, GoldHue, title);
            if (sub != null)
                G.AddLabel(px + 20 + Measure(title) + 12, 11, WhiteHue, sub);
            G.AddButton(28, 28, GuideUp, GuideDown, GuideButtonId);
            G.AddLabel(54, 31, DimHue, "Guide");
            G.AddButton(w - 58, 28, CloseUp, CloseDown, 0);
        }

        // CastleKit.cs:147.
        public void Panel(int x, int y, int w, int h, int kit = Trim) => G.AddBackground(x, y, w, h, kit);

        // CastleKit.cs:150-156.
        public void Section(int x, int y, int w, string t)
        {
            G.AddImageTiled(x, y + 9, w, 4, Band);
            int tw = Measure(t);
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
            return x + size + 5 + Measure(label ?? "") + 16;
        }

        // CastleKit.cs:179-180.
        public int Tab(int x, int y, int id, string label, bool sel, bool enabled = true) =>
            GemBtn(x, y, id, label, sel ? Gem.Gold : Gem.Grey, 21, sel, sel ? GoldHue : enabled ? WhiteHue : DimHue, enabled);

        // CastleKit.cs:213-226.
        public void Pager(int cx, int y, int page, int pages, int prevId, int nextId)
        {
            pages = Math.Max(1, pages);
            if (page > 0)
                G.AddButton(cx - 70, y + 1, PgL, PgLDown, prevId);
            string t = $"Page {page + 1} / {pages}";
            G.AddLabel(cx - Measure(t) / 2, y, WhiteHue, t);
            if (page < pages - 1)
                G.AddButton(cx + 54, y + 1, PgR, PgRDown, nextId);
        }

        // CastleKit.cs:228-238, with GumpStyle.Cropped (GumpStyle.cs:72-73).
        public void Text(int x, int y, string t, int hue = WhiteHue, int w = 0)
        {
            if (w > 0)
                Cropped(x, y, w, 20, hue, t);
            else
                G.AddLabel(x, y, hue, t);
        }

        // GumpStyle.cs:72-73.
        public void Cropped(int x, int y, int w, int h, int hue, string text) => G.AddLabelCropped(x, y, w, h, hue, Fit(text, w));

        // CastleKit.cs:240.
        public void Centered(int cx, int y, string t, int hue = WhiteHue) => G.AddLabel(cx - Measure(t) / 2, y, hue, t);

        // CastleKit.cs:243-249 through GumpStyle.Message (GumpStyle.cs:81-88, MessageH 40).
        public void Message(string message, bool bad)
        {
            if (string.IsNullOrEmpty(message))
                return;
            int x = L, y = H - 50, w = W - 2 * L;
            G.AddHtml(x, y, w, 40, $"<BASEFONT COLOR={(bad ? "#FF9090" : "#B4E1A8")}><I>{EscapeHtml(message)}</I></BASEFONT>", false, message.Length * 7 > 2 * w);
        }

        // ItemIconCell.Add (ItemIconCell.cs:111-127) as the server measures art without a world: a plain 44 x 44 tile
        // (:79), which fits every cell used here, centred as Centre does (:101-106).
        public void Icon(int cellX, int cellY, int cellW, int cellH, int itemId, int hue) =>
            G.AddItem(cellX + (cellW - 44) / 2, cellY + (cellH - 44) / 2, itemId, hue);
    }

    // ---- StableGump.cs ----

    public enum StableTab { All, Mounts, Combat, Bonded, Breeding }

    /// <summary>StableRow (StableGump.cs:53-56) with the part of its PetCard the windows read here.</summary>
    public sealed record Row(uint Serial, string Name, string Type, int ItemId, int Hue, int Loyalty, int Str, int Dex, int Int,
        int Hits, int HitsMax, int ControlSlots, bool Bonded, bool Dead, bool Mount, string TopSkills, Card Taming = null);

    /// <summary>
    /// What the Breeding tab reads of a PetCard (PetCards.cs:19-38). TierShort is PetCards.TierShort; Traits the names
    /// as the viewer's Lore shows them (null under Lore 30).
    /// </summary>
    public sealed record Card(string TierShort, bool NormalVariant, bool Female, bool Juvenile, string[] Traits);

    /// <summary>BreedPet (StableBreeding.cs:29).</summary>
    public sealed record BreedPet(Row Row, bool Stabled, string Foods, int Breedings, int Level, int NatStr, int NatDex, int NatInt);

    /// <summary>BreedLine (StableBreeding.cs:32).</summary>
    public sealed record BreedLine(string Label, bool? Met, string Note);

    /// <summary>BreedUnderWay (StableBreeding.cs:35); Roman and Fee are PetCards.Roman and PetBreeding.Fee of its Tier.</summary>
    public sealed record UnderWay(BreedPet Sire, BreedPet Dam, DateTime Start, bool Egg, string Roman, int Fee);

    /// <summary>BreedingView (StableBreeding.cs:37-55); Roman and Fee as above for the pair's Tier, null when not told.</summary>
    public sealed record Breeding(IReadOnlyList<BreedPet> Pets, int A, int B, IReadOnlyList<BreedLine> Lines, string Refusal,
        string Roman, int Fee, long Funds, int FoodHave, string Odds, UnderWay UnderWay, DateTime Now)
    {
        public bool Ready => UnderWay == null && Refusal == null;
        public bool Due => UnderWay != null && Now - UnderWay.Start >= Courtship + Gestation;
        public BreedPet PetA => A >= 0 && A < Pets.Count ? Pets[A] : null;
        public BreedPet PetB => B >= 0 && B < Pets.Count ? Pets[B] : null;
    }

    // PetBreeding.cs:112-126, :376.
    public const int LifetimeBreedings = 5, GrowthDaysToMature = 5, FoodsPerParent = 3, StableFees = 60, MinLoyalty = 70, PairLockDays = 14;
    public static readonly TimeSpan Courtship = TimeSpan.FromHours(24), Gestation = TimeSpan.FromHours(48);
    public static readonly int[] FeeByTier = { 0, 2000, 8000, 25000, 100000, 0 };
    public static readonly int[] MinLevelByTier = { 0, 10, 15, 20, 30, 0 };
    private const int StatParentPercent = 60, SurgePercent = 10, SurgeAmount = 5, TraitPassPercent = 35, MutationPercent = 8, HeirloomPercent = 2;

    // PetTraits.MaxTraits is not in the references; PetBreeding.cs:36 gives it: "each parent trait passes at 35% ..., at most 2".
    private const int MaxTraits = 2;

    // StableRules.cs:104-111.
    private static readonly string[] Loyalty =
    {
        "Confused", "Extremely Unhappy", "Rather Unhappy", "Unhappy", "Somewhat Content", "Content",
        "Happy", "Rather Happy", "Very Happy", "Extremely Happy", "Wonderfully Happy",
    };

    public static string LoyaltyText(int loyalty) => Loyalty[Math.Clamp(loyalty / 10, 0, Loyalty.Length - 1)];

    // StableBreeding.cs:124-125.
    public static string Left(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" : t.TotalHours >= 1 ? $"{(int)Math.Ceiling(t.TotalHours)}h" : $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))}m";

    // StableBreeding.cs:128-129.
    private static (int A, int B) FoodSplit(int have) =>
        (Math.Min(have, FoodsPerParent), Math.Clamp(have - FoodsPerParent, 0, FoodsPerParent));

    /// <summary>StableBreeding.Odds (StableBreeding.cs:133-151) for two parents of the Normal variant (VariantOdds: 0 and 0).</summary>
    public static string Odds(BreedPet a, BreedPet b, bool egg)
    {
        if (a == null || b == null)
            return "Pick two pets of a kind to see what their young may inherit.";
        var traits = (a.Row.Taming?.Traits ?? Array.Empty<string>()).Concat(b.Row.Taming?.Traits ?? Array.Empty<string>()).Distinct().ToList();
        if (a.Row.Taming is { NormalVariant: false } || b.Row.Taming is { NormalVariant: false })
            throw new NotSupportedException("only Normal parents are replayed here");
        (int elder, int alpha) = (0, 0);
        return $"Stats: {StatParentPercent}% the parents' average (Str {(a.NatStr + b.NatStr) / 2}, Dex {(a.NatDex + b.NatDex) / 2}, " +
               $"Int {(a.NatInt + b.NatInt) / 2}); {SurgePercent}% +{SurgeAmount} to one.\n" +
               $"Traits: each {TraitPassPercent}% ({(traits.Count == 0 ? "none seen" : string.Join(", ", traits))}), " +
               $"at most {MaxTraits}; {MutationPercent}% a new one.\n" +
               $"Alpha {alpha}%, Elder {elder}%, heirloom hue {HeirloomPercent}%.\n" +
               $"{(egg ? "An egg" : "A newborn")}, young for {GrowthDaysToMature} fed days. Never passed on: level, points, abilities, loyalty, gear.";
    }

    private static bool InTab(Row r, StableTab t) => t switch
    {
        StableTab.Mounts => r.Mount,
        StableTab.Combat => !r.Mount,
        StableTab.Bonded => r.Bonded,
        _ => true,
    };

    // StableGump.cs:69-74.
    public const int BClose = 1, BStablePet = 2, BClaimAll = 3, BClaim = 4, BRename = 5, BRelease = 6, BPrev = 7, BNext = 8, BStableAll = 9,
        BSort = 10, BTab = 20, BBreedPrevA = 30, BBreedNextA = 31, BBreedPrevB = 32, BBreedNextB = 33, BBreedAccept = 34, BBreedCollect = 35, BSelect = 100;

    private const int Width = 760, Height = 700, RowsPerPage = 7;
    private const int LX = 28, LW = 452, RX = 492, RW = 240, GY = 92, GH = 400;
    private const int PH = 96, PW = 344, CLY = GY + PH + 6, CLH = 326, CLW = 404, CRX = LX + CLW + 8, CRW = Width - 28 - CRX;

    /// <summary>
    /// The StableGump constructor (StableGump.cs:150-211). taming: the window's _taming (TamingRedesign.Enabled or a row
    /// with a record); only its Breeding tab is replayed with it on (the table's Taming view reads PetTiers and
    /// PetLoyalty, which are not in the references). sort is the StableSort's name.
    /// </summary>
    public static ShardGump Stable(uint serial, List<Row> rows, int max, int page, uint selected, string notice, int followers,
        int followersMax, StableTab tab, string sort, bool taming, Breeding breeding)
    {
        tab = tab == StableTab.Breeding && breeding == null ? StableTab.All : tab;
        var shown = rows.Where(r => InTab(r, tab)).ToList();
        shown = sort switch
        {
            "Name" => shown.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(), // OrderBy is stable, as :178
            _ => throw new NotSupportedException(sort),
        };
        int pages = Math.Max(1, (shown.Count + RowsPerPage - 1) / RowsPerPage);
        page = Math.Clamp(page, 0, pages - 1);
        selected = shown.Exists(r => r.Serial == selected) ? selected : shown.Count > page * RowsPerPage ? shown[page * RowsPerPage].Serial : 0;

        var k = new Castle(serial, 0x3C51A0F4, 60, 24);
        k.Frame(Width, Height, "Stable", $"{rows.Count} / {max} stabled");
        k.Text(LX, 63, "Followers", DimHue);
        if (followers >= 0)
            k.Text(LX + Measure("Followers") + 8, 63, $"{followers} / {followersMax} slots");
        int x = LX + 190;
        x = k.GemBtn(x, 60, BStablePet, "Stable Pet...") + 6;
        x = k.GemBtn(x, 60, BStableAll, "Stable All Followers") + 6;
        k.GemBtn(x, 60, BClaimAll, "Claim All", enabled: rows.Count > 0);

        if (tab == StableTab.Breeding)
            BreedingTab(k, breeding);
        else if (taming)
            throw new NotSupportedException("the table's Taming view reads PetTiers / PetLoyalty, not in the references");
        else
            TableAndDetails(k, shown, page, pages, selected, followers, followersMax, sort);

        TabStrip(k, rows, tab, breeding);
        bool bad = notice != null && (notice.Contains("stays") || notice.Contains("not") || notice.Contains("full"));
        if (tab == StableTab.Breeding)
        {
            // BreedingMessage (:521-535).
            if (notice != null)
                bad = bad || notice == breeding.Refusal;
            else if (breeding.UnderWay != null)
                bad = false;
            else
                (notice, bad) = breeding.Ready
                    ? ($"All met. Accept pays {N0(breeding.Fee)} gold (+{StableFees} stabling) and {FoodsPerParent * 2} favourite foods; both parents go into your stable to court for a day.", false)
                    : ($"The stable master: \"{breeding.Refusal}\"", true);
        }
        k.Message(notice, bad);
        return k.G;
    }

    // StableGump.cs:214-353 with _taming off.
    private static void TableAndDetails(Castle k, List<Row> shown, int page, int pages, uint selected, int followers, int followersMax, string sort)
    {
        k.Panel(LX, GY, LW, GH);
        int hy = GY + 8;
        k.Text(LX + 80, hy, "Name / type", GoldHue);
        k.Text(LX + 220, hy, "Slots", GoldHue);
        k.Text(LX + 274, hy, "Loyalty", GoldHue);
        k.Text(LX + 392, hy, "HP", GoldHue);
        k.G.AddImageTiled(LX + 8, hy + 20, LW - 16, 4, Band);
        for (int n = 0; n < RowsPerPage && page * RowsPerPage + n < shown.Count; n++)
        {
            Row r = shown[page * RowsPerPage + n];
            int y = GY + 32 + n * 46;
            bool sel = r.Serial == selected;
            if (sel)
                k.Panel(LX + 6, y, LW - 12, 44, Sel);
            (int up, int down) = sel ? (GemSelUp, GemSelDown) : (GemUp, GemDown);
            k.G.AddButton(LX + 10, y + 11, up, down, BSelect + n);
            k.Icon(LX + 33, y, 44, 44, r.ItemId, r.Hue);
            string nm = Fit(r.Name ?? "(unnamed)", 110);
            k.Text(LX + 80, y + 5, nm, sel ? GoldHue : WhiteHue);
            if (r.Bonded)
                k.Text(LX + 80 + Measure(nm) + 6, y + 5, "*", GoldHue);
            k.Cropped(LX + 80, y + 23, 136, 18, r.Dead ? RedHue : DimHue, r.Dead ? r.Type + " (dead)" : r.Type);
            k.Text(LX + 228, y + 13, r.ControlSlots.ToString());
            k.Cropped(LX + 274, y + 13, 114, 18, r.Loyalty < 50 ? RedHue : GreenHue, LoyaltyText(r.Loyalty));
            k.Text(LX + 392, y + 13, r.Dead ? "dead" : $"{r.Hits}/{r.HitsMax}", r.Dead ? RedHue : WhiteHue);
        }
        if (shown.Count == 0)
            k.Text(LX + 16, GY + 40, "No pets in this tab.", DimHue);
        int fy = GY + GH - 32;
        k.G.AddImageTiled(LX + 8, fy - 6, LW - 16, 4, Band);
        k.Text(LX + 12, fy + 4, "Select with the gem.  * = bonded", DimHue);
        k.GemBtn(LX + LW - 128, fy + 1, BSort, $"Sort: {sort}");
        k.Pager(LX + LW / 2, GY + GH + 8, page, pages, BPrev, BNext);

        k.Panel(RX, GY, RW, GH + 28);
        int x0 = RX + 14;
        if (selected == 0)
        {
            k.Text(x0, GY + 16, "No pet selected.", DimHue);
            return;
        }
        Row s = shown.Find(q => q.Serial == selected);
        k.Icon(RX + 8, GY + 8, IconSize, IconSize, s.ItemId, s.Hue);
        k.Cropped(RX + 72, GY + 12, RW - 86, 18, GoldHue, s.Name ?? "(unnamed)");
        k.Cropped(RX + 72, GY + 30, RW - 86, 18, WhiteHue, $"{s.Type} · {s.ControlSlots} slots");
        if (s.Bonded)
            k.Text(RX + 72, GY + 48, "Bonded", GreenHue);
        k.Cropped(RX + 130, GY + 48, RW - 144, 18, s.Loyalty < 50 ? RedHue : GreenHue, LoyaltyText(s.Loyalty));
        k.Section(RX + 8, GY + 72, RW - 16, "Stats");
        (string, string)[] stats = { ("HP", s.Dead ? "dead" : $"{s.Hits}/{s.HitsMax}"), ("Str", s.Str.ToString()), ("Dex", s.Dex.ToString()), ("Int", s.Int.ToString()) };
        for (int n = 0; n < stats.Length; n++)
        {
            k.Text(x0, GY + 100 + n * 20, stats[n].Item1, DimHue);
            k.Text(RX + 90, GY + 100 + n * 20, stats[n].Item2, s.Dead && n == 0 ? RedHue : WhiteHue);
        }
        k.Section(RX + 8, GY + 184, RW - 16, "Top skills");
        string[] sk = (s.TopSkills ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        for (int n = 0; n < sk.Length && n < 4; n++)
        {
            string[] kv = sk[n].Split('=');
            k.Cropped(x0, GY + 212 + n * 20, 130, 18, DimHue, kv[0]);
            k.Text(RX + 150, GY + 212 + n * 20, kv.Length > 1 ? kv[1] : "");
        }
        k.Section(RX + 8, GY + 296, RW - 16, "Actions");
        k.GemBtn(x0, GY + 324, BClaim, "Claim", Gem.Gold, 29);
        if (followers >= 0)
        {
            bool fits = followers + s.ControlSlots <= followersMax;
            k.Cropped(RX + 90, GY + 330, RW - 100, 18, fits ? GreenHue : RedHue,
                $"{(fits ? "fits" : "no room")}: needs {s.ControlSlots}, have {Math.Max(0, followersMax - followers)}");
        }
        k.GemBtn(x0, GY + 358, BRename, "Rename...");
        k.GemBtn(RX + 130, GY + 358, BRelease, "Release...", Gem.Red);
        k.Text(x0, GY + 392, "Selecting a pet never claims it.", DimHue);
    }

    // StableGump.cs:356-382.
    private static void TabStrip(Castle k, List<Row> rows, StableTab tab, Breeding breeding)
    {
        const int TY = 532;
        int n = breeding != null ? 5 : 4;
        k.G.AddImageTiled(LX, TY, Width - 56, 4, Band);
        int step = (Width - 56) / n;
        for (int t = 0; t < n; t++)
        {
            bool sel = (int)tab == t;
            int tx = LX + t * step;
            k.Panel(tx + 6, TY + 12, step - 12, 84, sel ? Sel : Trim);
            string line, lab;
            if (t == (int)StableTab.Breeding)
            {
                line = breeding.UnderWay == null ? "pair up" : breeding.Due ? "ready!" : "under way";
                lab = "Breeding";
            }
            else
            {
                int cnt = rows.Count(q => InTab(q, (StableTab)t));
                line = cnt == 1 ? "1 pet" : $"{cnt} pets";
                lab = $"{(StableTab)t} ({cnt})";
            }
            k.Centered(tx + step / 2, TY + 30, line, sel ? GoldHue : t == 4 && breeding.Due ? GreenHue : DimHue);
            k.Tab(tx + step / 2 - (26 + Measure(lab)) / 2, TY + 66, BTab + t, lab, sel);
        }
    }

    // StableGump.cs:387-395.
    private static void LongBar(Castle k, int x, int y, int w, double value, double max, int fill)
    {
        k.G.AddImageTiled(x, y, w, 11, BarBack);
        int n = max <= 0 ? 0 : (int)Math.Round(w * Math.Clamp(value / max, 0, 1));
        if (n > 0)
            k.G.AddImageTiled(x, y, n, 11, fill);
    }

    // StableGump.cs:398-425.
    private static void Picker(Castle k, int px, BreedPet p, int prevId, int nextId, string role)
    {
        k.Panel(px, GY, PW, PH);
        if (prevId > 0)
        {
            k.G.AddButton(px + 6, GY + 37, ArrL, ArrLDown, prevId);
            k.G.AddButton(px + 128, GY + 37, ArrR, ArrRDown, nextId);
        }
        k.Panel(px + 40, GY + 6, 84, 84, Sel);
        k.G.AddImageTiled(px + 46, GY + 12, 72, 72, Backdrop);
        int tx = px + 164, tw = PW - 172;
        if (p == null)
        {
            k.Text(tx, GY + 23, role ?? "No other pet", DimHue, tw);
            k.Text(tx, GY + 40, role == null ? "Tame or claim one." : "gone", DimHue, tw);
            return;
        }
        Row r = p.Row;
        Card t = r.Taming;
        k.Icon(px + 46, GY + 12, 72, 72, r.ItemId, r.Hue);
        k.Text(tx, GY + 6, Fit((r.Name ?? "(unnamed)") + (r.Bonded ? " *" : ""), tw), GoldHue, tw);
        k.Text(tx, GY + 23, Fit(r.Type, tw), WhiteHue, tw);
        if (t is { NormalVariant: false })
            throw new NotSupportedException("PetCards.VariantHue of a variant parent");
        k.Text(tx, GY + 40, t == null ? "no record" : $"{t.TierShort} · Lv {p.Level}", GoldHue, tw);
        k.Text(tx, GY + 57, t == null ? $"loyalty {r.Loyalty}" : $"{(t.Juvenile ? "Young" : t.Female ? "Female" : "Male")} · loyalty {r.Loyalty}", DimHue, tw);
        string where = p.Stabled ? "In your stable" : "With you";
        k.Text(tx, GY + 74, Fit(role == null ? where : $"{role} · {where.ToLowerInvariant()}", tw), p.Stabled ? DimHue : GreenHue, tw);
    }

    // StableGump.cs:427-518.
    private static void BreedingTab(Castle k, Breeding v)
    {
        UnderWay u = v.UnderWay;
        int canStep = u == null && v.Pets.Count > 1 ? 1 : 0;
        Picker(k, LX, u != null ? u.Sire : v.PetA, canStep * BBreedPrevA, canStep * BBreedNextA, u == null ? null : u.Egg ? "Parent" : "Sire");
        Picker(k, LX + PW + 16, u != null ? u.Dam : v.PetB, canStep * BBreedPrevB, canStep * BBreedNextB, u == null ? null : u.Egg ? "Parent" : "Dam");

        k.Panel(LX, CLY, CLW, CLH);
        k.Panel(CRX, CLY, CRW, CLH);
        int x0 = CRX + 14, vx = CRX + 74, vw = CRW - 88, y = CLY + 34;
        void Row(string label, string value, int hue)
        {
            k.Text(x0, y, label, DimHue, vx - x0 - 4);
            k.Text(vx, y, Fit(value, vw), hue, vw);
            y += 17;
        }

        if (u == null)
        {
            k.Section(LX + 8, CLY + 6, CLW - 16, "Requirements");
            for (int i = 0; i < v.Lines.Count; i++)
            {
                BreedLine l = v.Lines[i];
                int ly = CLY + 34 + i * 17;
                if (l.Met is { } met)
                    k.G.AddImageTiled(LX + 14, ly + 5, 10, 10, met ? BarGreen : BarBack);
                k.Text(LX + 30, ly, Fit(l.Label, 160), l.Met == null ? DimHue : WhiteHue, 160);
                k.Text(LX + 194, ly, Fit(l.Note, 200), l.Met == true ? GreenHue : l.Met == false ? RedHue : DimHue, 200);
            }

            k.Section(CRX + 8, CLY + 6, CRW - 16, "Fee and food");
            long need = v.Fee + StableFees;
            Row("Fee", v.Roman != null ? $"{N0(v.Fee)} + {StableFees} gp, tier {v.Roman}" : "2,000 to 100,000 gp by tier", WhiteHue);
            Row("Gold", $"{N0(v.Funds)} in pack or bank", v.Roman == null ? WhiteHue : v.Funds >= need ? GreenHue : RedHue);
            (int fa, int fb) = FoodSplit(v.FoodHave);
            foreach ((BreedPet p, int have) in new[] { (v.PetA, fa), (v.PetB, fb) })
            {
                Row(Fit(p?.Row.Name ?? "-", vx - x0 - 4), $"{have} / {FoodsPerParent} {(p?.Foods ?? "favourite food").ToLowerInvariant()}",
                    have >= FoodsPerParent ? GreenHue : RedHue);
            }
            k.GemBtn(x0, CLY + 108, BBreedAccept, "Accept", Gem.Gold, 29, enabled: v.Ready);
            int unmet = v.Lines.Count(l => l.Met == false);
            k.Text(x0 + 110, CLY + 114, v.Ready ? "all met" : unmet == 1 ? "1 not met" : $"{Math.Max(1, unmet)} not met",
                v.Ready ? GreenHue : RedHue, CRW - 124);
        }
        else
        {
            DateTime court = u.Start + Courtship, due = court + Gestation, now = v.Now;
            k.Section(LX + 8, CLY + 6, CLW - 16, "Breeding under way");
            int bx = LX + 16, bw = 260, tx = bx + bw + 12, tw = LX + CLW - 12 - tx;
            k.Text(bx, CLY + 34, $"Courtship ({Courtship.TotalHours:0} hours)", GoldHue);
            LongBar(k, bx, CLY + 58, bw, (now - u.Start).TotalMinutes, Courtship.TotalMinutes, BarGreen);
            k.Text(tx, CLY + 53, now >= court ? "done" : $"{Left(court - now)} left", now >= court ? GreenHue : WhiteHue, tw);
            k.Text(bx, CLY + 80, $"Gestation ({Gestation.TotalHours:0} hours)", GoldHue);
            LongBar(k, bx, CLY + 104, bw, (now - court).TotalMinutes, Gestation.TotalMinutes, BarGold);
            k.Text(tx, CLY + 99, now >= due ? "done" : now < court ? "next" : $"{Left(due - now)} left", now >= due ? GreenHue : WhiteHue, tw);
            k.Text(bx, CLY + 130, "Birth", DimHue);
            k.Text(bx + 60, CLY + 130, v.Due ? "due now" : $"due in {Left(due - now)}", v.Due ? GreenHue : WhiteHue, CLW - 92);
            string say = v.Due ? "Collect it here, or say \"breed\" to any stable master."
                : now < court ? "Leave both parents stabled until the courtship ends: claiming one ends it, and the fee is not paid back."
                : "The courtship is over: you may claim the parents. Come back when the birth is due.";
            k.G.AddHtml(bx, CLY + 154, CLW - 32, 40, $"<BASEFONT COLOR=#B4B4B4>{EscapeHtml(say)}</BASEFONT>", false, false);
            k.GemBtn(bx, CLY + 206, BBreedCollect, "Collect", Gem.Gold, 29, enabled: v.Due);
            k.Text(bx + 110, CLY + 212, u.Egg ? "An egg, into your pack (it needs room)" : "A newborn, into your stable (a free slot)",
                v.Due ? WhiteHue : DimHue, CLW - 142);

            k.Section(CRX + 8, CLY + 6, CRW - 16, "Paid");
            Row("Fee", $"{N0(u.Fee)} gp, tier {u.Roman}", WhiteHue);
            Row("Food", $"{FoodsPerParent * 2} favourite foods", WhiteHue);
            Row("Began", $"{Left(now - u.Start)} ago", WhiteHue);
        }

        int sy = u == null ? CLY + 146 : CLY + 96;
        k.Section(CRX + 8, sy, CRW - 16, "What the young may inherit");
        int ow = CRW - 28, oh = CLY + CLH - 8 - (sy + 26);
        string odds = v.Odds ?? "";
        k.G.AddHtml(x0, sy + 26, ow, oh, $"<BASEFONT COLOR=#B4B4B4>{EscapeHtml(odds).Replace("\n", "<BR>")}</BASEFONT>", false,
            WrapLines(odds, ow) * 18 > oh + 4);
    }

    // ---- PetLoreGump.cs ----

    /// <summary>
    /// The window's facts (LoreFacts, PetLoreText.cs:25-54) and the record values the Care tab and the portrait read
    /// (PetRecord is not in the references). TierText is PetCards.TierText(tier, Normal); Band is PetLoyalty.BandFor(Loyalty)
    /// by name and BandHue its PetCards.BandHue.
    /// </summary>
    public sealed record Lore(string Name, string Species, bool Controlled, bool Bonded, bool IsMaster, bool Stabled, string Owner,
        int Hits, int HitsMax, int Stam, int StamMax, int Mana, int ManaMax, int Loyalty, int Art, int Hue,
        string TierText, bool Female, string Band, int BandHue, string Roman, bool TierBreeds, bool Juvenile, int Breedings,
        List<string> Care, string Breeding);

    public enum LorePage { Overview = 1, Stats = 2, Skills = 3, Training = 4, Abilities = 5, Care = 6 }

    private static readonly string[] TabLabels = { "Overview", "Stats", "Skills", "Training", "Abilities", "Care" };

    /// <summary>
    /// The PetLoreGump constructor (PetLoreGump.cs:79-119) on its Care tab (:524-555), a Normal-variant pet. The other
    /// tabs read PetTiers, PetTraining, PetXp, PetTraits and PetAbilities, which are not in the references.
    /// </summary>
    public static ShardGump PetLoreCare(uint serial, Lore f)
    {
        const int W = 660, H = 570, BTabBase = 10, LLX = 28, LLW = 214, LRX = 252, LRW = 380, TY = 56, CY = 100, CH = 410;
        const int X0 = LRX + 16, VX = LRX + 130, RE = LRX + LRW - 16;
        // PetLoreText.PagesFor (PetLoreText.cs:80-90).
        LorePage[] pages = !f.Controlled ? throw new NotSupportedException("a wild creature's overview")
            : f.IsMaster ? Enum.GetValues<LorePage>() : new[] { LorePage.Overview, LorePage.Stats, LorePage.Skills, LorePage.Abilities };

        var k = new Castle(serial, 0x5E7A1022, 170, 24);
        k.Frame(W, H, "Animal Lore", Fit(f.Name ?? f.Species ?? "", 220));

        // Tabs (:125-140).
        for (int i = 0; i < TabLabels.Length; i++)
        {
            const int tw = 95, gap = 6;
            var t = (LorePage)(i + 1);
            bool sel = t == LorePage.Care;
            int x = Castle.L + i * (tw + gap);
            k.Panel(x, TY, tw, 36, sel ? Sel : Trim);
            int lw = 21 + 5 + Measure(TabLabels[i]);
            k.Tab(x + (tw - lw) / 2, TY + 8, BTabBase + (int)t, TabLabels[i], sel, pages.Contains(t));
        }

        // Portrait (:153-195).
        void Bar(int bx, int by, double value, double max, int fill)
        {
            k.G.AddImage(bx, by, BarBack);
            int n = max <= 0 ? 0 : (int)Math.Round(BarW * Math.Clamp(value / max, 0, 1));
            if (n > 0)
                k.G.AddImageTiled(bx, by, n, 11, fill);
        }
        void Vital(int vy, string label, int value, int max, int fill)
        {
            k.Text(LLX + 4, vy, label, DimHue);
            Bar(LLX + 40, vy + 4, value, max, fill);
            string t = $"{value}/{max}";
            k.Text(LLX + 154, vy, Measure(t) > LLW - 154 ? value.ToString() : t, WhiteHue, LLW - 154);
        }
        k.Panel(LLX, CY, LLW, 176, Sel);
        k.G.AddImageTiled(LLX + 8, CY + 8, LLW - 16, 160, Backdrop);
        k.Icon(LLX + 8, CY + 8, LLW - 16, 160, f.Art, f.Hue);
        int y = CY + 184;
        k.Text(LLX + 4, y, f.Name ?? "(unnamed)", GoldHue, LLW - 8);
        k.Text(LLX + 4, y + 20, f.Species ?? "", WhiteHue, LLW - 8);
        k.Text(LLX + 4, y + 40, $"Tier {f.TierText}", GoldHue, LLW - 8);
        k.Text(LLX + 4, y + 60, f.Female ? "Female" : "Male", DimHue, LLW - 8);
        string status = f.Stabled ? "In your stable" : f.IsMaster ? "Your pet" : f.Owner != null ? $"Pet of {f.Owner}" : "Someone's pet";
        k.Text(LLX + 4, y + 80, f.Bonded ? status + " · bonded" : status, GreenHue, LLW - 8);
        int vy0 = y + 110;
        Vital(vy0, "HP", f.Hits, f.HitsMax, BarGreen);
        Vital(vy0 + 22, "Stam", f.Stam, f.StamMax, BarGold);
        Vital(vy0 + 44, "Mana", f.Mana, f.ManaMax, BarBlue);
        k.Text(LLX + 4, vy0 + 70, "Loyalty", DimHue);
        Bar(LLX + 56, vy0 + 74, f.Loyalty, 100, f.Loyalty < 50 ? BarGold : BarGreen);
        k.Text(LLX + 4, vy0 + 92, $"{f.Band}: {LoyaltyText(f.Loyalty)}", f.BandHue, LLW - 8);

        k.Panel(LRX, CY, LRW, CH);

        // CareTab (:524-555).
        void Row(int ry, string label, string value, int hue = WhiteHue)
        {
            k.Text(X0, ry, label, DimHue, VX - X0 - 6);
            k.Text(VX, ry, value, hue, RE - VX);
        }
        k.Section(LRX + 8, CY + 8, LRW - 16, "Loyalty");
        k.Text(X0, CY + 38, f.Band, f.BandHue, 100);
        Bar(LRX + 120, CY + 42, f.Loyalty, 100, f.Loyalty < 50 ? BarGold : BarGreen);
        k.Text(LRX + 240, CY + 38, $"{f.Loyalty} / 100", WhiteHue, RE - LRX - 240);
        k.Text(X0, CY + 58, LoyaltyText(f.Loyalty), DimHue, RE - X0);
        var rest = f.Care.Count > 2 ? f.Care.GetRange(2, f.Care.Count - 2) : new List<string>();
        k.Section(LRX + 8, CY + 86, LRW - 16, "Care and gear");
        string text = string.Join("\n", rest);
        int lines = WrapLines(text, RE - X0);
        int h = Math.Min(Math.Max(lines, 1), 8) * 18;
        k.G.AddHtml(X0, CY + 114, RE - X0, h, $"<BASEFONT COLOR=#E0E0E0>{EscapeHtml(text).Replace("\n", "<BR>")}</BASEFONT>", false, lines > 8);
        y = CY + 120 + h;
        k.Section(LRX + 8, y, LRW - 16, "Breeding");
        y += 30;
        Row(y, "Sex", f.Female ? "Female" : "Male");
        if (f.Juvenile)
            throw new NotSupportedException("a young pet's growth (PetCards.From)");
        Row(y += 22, "Bred", $"{f.Breedings} of {LifetimeBreedings}");
        Row(y += 22, "Can breed", $"tier {f.Roman}: {(f.TierBreeds ? "yes" : "no")}", f.TierBreeds ? GreenHue : DimHue);
        if (f.Breeding != null)
            Row(y += 22, "Now", f.Breeding, GoldHue);
        return k.G;
    }

    // ---- the snapshots ----

    // Nightmare: ModernUO's Nightmare (BaseMount, FavoriteFood Meat), body 0x74, ShrinkTable art 0x259C
    // (Distribution/Data/shrink.json); Horse body 0xE2 -> 0x2599; DireWolf body 0x17 -> 0x25D0.
    private const int NightmareArt = 0x259C, HorseArt = 0x2599, DireWolfArt = 0x25D0;

    // Tier IV: PetCards.cs:65 and :68 spell PetTiers.Info(IV).Name as "IV Dread" (Roman "IV"); PetBreeding.TierBreeds
    // lets Dread breed with pets.breeding.tier4 on (the default, :105, :182).
    private const string TierIV = "IV Dread", RomanIV = "IV";

    /// <summary>
    /// The breeding player: Animal Taming 100 and Animal Lore 45 (no Veterinary) hold the licence (The Long Bond needs
    /// Taming 80, PetQuests.cs:16) and have PetStables.MaxFor = 20 + max(0, StockMax(100, 45, 0) - 5) = 20 stable
    /// slots. Lore 45 counts traits but names none (each name from Lore 50, PetLoreGump.cs:76), so both cards'
    /// traits read "?".
    /// </summary>
    private static Row Shadowmere => new(0x00001001, "Shadowmere", "Nightmare", NightmareArt, 0, 100, 446, 98, 86, 412, 412, 2,
        true, false, true, "Tactics=97.5|Wrestling=94.2|Magery=88.0|Resisting Spells=86.4", new Card(RomanIV, true, false, false, new[] { "?" }));

    private static Row Ember => new(0x00001004, "Ember", "Nightmare", NightmareArt, 0, 90, 431, 104, 92, 398, 398, 2,
        true, false, true, "Wrestling=96.0|Tactics=92.1|Magery=85.3|Resisting Spells=84.0", new Card(RomanIV, true, true, false, new[] { "?" }));

    private static BreedPet ShadowmerePet(bool stabled) => new(Shadowmere, stabled, "Meat", stabled ? 1 : 0, 32, 500, 110, 90);
    private static BreedPet EmberPet => new(Ember, true, "Meat", 2, 31, 480, 104, 96);

    /// <summary>
    /// The checklist for Shadowmere (out with the player, male) and Ember (stabled, female), both bonded tier IV Nightmares
    /// with records: PetBreeding.Assess with all = true (PetBreeding.cs:401-534) tells every row, and only Ember's Here
    /// fails (a stabled pet, :443-448); the notes are StableBreeding.Note's (StableBreeding.cs:284-358).
    /// </summary>
    private static List<BreedLine> ClaimFirstLines() => new()
    {
        new("Breeding licence", true, "The Long Bond done"),
        new("None under way", true, "none"),
        new("Yours, here with you", false, "right: stabled, claim it"),
        new("Both bonded", true, "yes / yes"),
        new("Free to breed", true, "not fighting, unloaded"),
        new("Same species", true, "Nightmare"),
        new("Tier I to IV (V never)", true, TierIV),
        new("Male and female", true, "male / female"),
        new("Not young", true, "grown / grown"),
        new($"Loyalty {MinLoyalty} or more", true, "100 / 90"),
        new($"Level {MinLevelByTier[4]} or more", true, "32 / 31"),
        new($"Under {LifetimeBreedings} breedings", true, $"0/{LifetimeBreedings} / 2/{LifetimeBreedings}"),
        new("Rested", true, "rested"),
        new($"New pair ({PairLockDays} days)", true, "not paired lately"),
        new("Room in the stable", true, "1 + 2 of 20"),
        new("Favourite foods", true, $"6 of {FoodsPerParent * 2}"),
        new("Fee", true, $"{N0(FeeByTier[4] + StableFees)} gp"),
    };

    public const string BreedingClaimFirstState =
        "Breeding tab, nothing under way: Shadowmere (out with the player, male, Lv 32) and Ember (stabled, female, Lv 31), both bonded " +
        "tier IV (\"IV Dread\", PetCards.cs:65) Nightmares; the player holds the licence, Taming 100 / Lore 45 (20 stable slots, traits " +
        "counted but unnamed), 2 / 5 followers, 6 meat in the pack, 150,000 gold; only Ember's \"here\" fails, so Accept is grey and the " +
        "message is the stable master's refusal";

    public static ShardGump BreedingClaimFirst()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        var pets = new List<BreedPet> { ShadowmerePet(false), EmberPet };
        var view = new Breeding(pets, 0, 1, ClaimFirstLines(), "Both must be thine own living pets.", RomanIV, FeeByTier[4], 150_000, 6,
            Odds(pets[0], pets[1], false), null, now);
        return Stable(0x40000401, new List<Row> { Ember }, 20, 0, 0, null, 2, 5, StableTab.Breeding, "Name", true, view);
    }

    /// <summary>The same pair after Accept at 12:00 (PetBreeding.Start: both stabled, one more breeding each), seen at now.</summary>
    public static ShardGump BreedingUnderWay(uint serial, TimeSpan since)
    {
        var start = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        BreedPet sire = ShadowmerePet(true), dam = EmberPet with { Breedings = 3 };
        var pets = new List<BreedPet> { sire, dam };
        var u = new UnderWay(sire, dam, start, false, RomanIV, FeeByTier[4]);
        var view = new Breeding(pets, -1, -1, Array.Empty<BreedLine>(), null, RomanIV, FeeByTier[4], 50_000, 0, Odds(sire, dam, false), u, start + since);
        return Stable(serial, new List<Row> { Ember, Shadowmere }, 20, 0, 0, null, 0, 5, StableTab.Breeding, "Name", true, view);
    }

    public const string BreedingCourtingState =
        "Breeding tab, the same pair 10 hours after Accept (both stabled, 0 / 5 followers): courtship 14h left, gestation next, Collect grey";

    public const string BreedingDueState =
        "Breeding tab, the same pair 80 hours after Accept: courtship and gestation done, the birth due, Collect live";

    public const string StableAllState =
        "redesign off (no records, no Breeding tab): max 20 (stables.maxSlots), three stabled (Shadowmere bonded, Dobbin, a dead " +
        "Fang), All tab sorted by Name, Shadowmere selected, 0 / 5 followers, no notice";

    public static ShardGump StableAll()
    {
        var rows = new List<Row>
        {
            Shadowmere with { Taming = null },
            new(0x00001002, "Dobbin", "Horse", HorseArt, 0, 60, 44, 46, 6, 30, 30, 1, false, false, true, "Wrestling=24.1|Tactics=21.0"),
            new(0x00001003, "Fang", "Dire Wolf", DireWolfArt, 0, 30, 96, 81, 36, 0, 64, 1, false, true, false, "Wrestling=62.3|Tactics=60.0|Resisting Spells=48.5"),
        };
        return Stable(0x40000400, rows, 20, 0, 0x00001001, null, 0, 5, StableTab.All, "Name", false, null);
    }

    public const string LoreCareState =
        "Care tab of Shadowmere looked at from the stable 10 hours into the courtship (StableGump B_Lore, then the Care tab): the master's " +
        "own stabled pet, every tab open; loyalty 100 taken as the Adoring band (PetLoyalty.cs is not in the references; The Long Bond " +
        "the player holds needs an Adoring pet, PetQuests.cs:16, :178); the care not started (PetRecord.CareMaster zero: " +
        "PetLoreText.Care's first lines, PetLoreText.cs:346-357), no gear, no saddlebags line; bred once (PetBreeding.Start)";

    public static ShardGump LoreCare()
    {
        // PetLoreText.Care (PetLoreText.cs:344-357) and GearAndBreeding (:391-420) with LastBred 10 hours ago.
        var care = new List<string>
        {
            $"Loyalty: {LoyaltyText(100)} (100)",
            "Bonded: yes",
            "Feeding, grooming and stable care are not tracked yet.",
            $"Bred 1 of {LifetimeBreedings}; last 10 h ago",
        };
        // PetCards.BreedingNote (PetCards.cs:153-167), 14 hours of the courtship left.
        var f = new Lore("Shadowmere", "Nightmare", true, true, true, true, null, 412, 412, 98, 98, 86, 86, 100, NightmareArt, 0,
            TierIV, false, "Adoring", GreenHue, RomanIV, true, false, 1, care, "Courting, 14 h left (keep stabled)");
        return PetLoreCare(0x40000410, f);
    }
}
