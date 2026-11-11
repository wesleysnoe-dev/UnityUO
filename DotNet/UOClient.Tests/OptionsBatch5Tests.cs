using UOClient.Core;
using UOClient.Net;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Options batch 5: auto-open corpses and doors off by default (they act on the world), and TazUO's grid container
/// (GridContainer, GridItem, GridSlotManager) with GridContainersDefaultToOldStyleView, ContainerOpacity,
/// GridContainersScale and GridContainerScaleItems, plus the options file and draft for the new keys.
/// </summary>
public sealed class OptionsBatch5Tests
{
    private const uint Me = 0x00000042;
    private const int MeX = 1495, MeY = 1629;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(MeX).UInt16(MeY).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        sent.Clear();
        return session;
    }

    private static void GroundItem(GameSession session, uint serial, ushort graphic, int x, int y) =>
        session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(0).UInt32(serial).UInt16(graphic).Byte(0).UInt16(1).UInt16(1)
            .UInt16(x).UInt16(y).Byte(0).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);

    // ---- auto-open defaults: off ----

    [Fact]
    public void AutoOpen_OffByDefault_EverywhereItIsDecided()
    {
        var d = GameOptions.Defaults();
        Assert.False(d.autoOpenCorpses);
        Assert.False(d.autoOpenDoors);
        Assert.True(d.autoOpenDoorsIfHidden); // a preference for when doors are on; sends nothing by itself
        Assert.False(new CorpseAutoOpen().Enabled);
        Assert.False(new DoorAutoOpen().Enabled);
        // A file without the keys (as every file before batch 4) leaves them off.
        GameOptions read = GameOptions.Parse("{\"showNames\": true}");
        Assert.False(read.autoOpenCorpses);
        Assert.False(read.autoOpenDoors);
        // The written file says so.
        string json = d.ToJson();
        Assert.Contains("\"autoOpenCorpses\": false", json);
        Assert.Contains("\"autoOpenDoors\": false", json);
    }

    [Fact]
    public void AutoOpen_WithDefaults_SendsNothing()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent);
        GroundItem(session, 0x40000001, WorldItem.CorpseGraphic, MeX, MeY);     // a corpse underfoot
        GroundItem(session, 0x40000010, 0x0675, MeX, MeY - 1);                 // a closed door to the north
        var corpses = new CorpseAutoOpen();
        var doors = new DoorAutoOpen();
        Func<ushort, bool> isDoor = g => g >= 0x0675 && g <= 0x06F4;

        Assert.Equal(0u, corpses.Tick(session, MeX, MeY));
        doors.Tick(session, MeX, MeY, 0, 4, isDoor);
        Assert.False(doors.Tick(session, MeX, MeY, 0, 0, isDoor)); // facing the door
        Assert.Empty(sent);
        Assert.False(corpses.Opened(0x40000001));
    }

    // ---- grid: which containers ----

    [Fact]
    public void OpensGrid_OnlyWhenOldStyleIsOff_AndNeverForBoardsOrVendorGumps()
    {
        Assert.True(GameOptions.Defaults().gridContainersDefaultToOldStyleView); // this client's default: classic, as before
        Assert.False(GameOptions.OpensGrid(true, 0x003C));   // backpack, old style on
        Assert.True(GameOptions.OpensGrid(false, 0x003C));   // backpack, old style off
        Assert.True(GameOptions.OpensGrid(false, 0x0009));   // a corpse follows it too
        Assert.False(GameOptions.OpensGrid(false, 0x091A));  // chessboard (TazUO's explicit check, and isvendor's 2330)
        Assert.False(GameOptions.OpensGrid(false, 1009));    // TazUO's canuse list
        Assert.False(GameOptions.OpensGrid(false, 1065));
        Assert.False(GameOptions.OpensGrid(false, 10070));   // isvendor list
        Assert.False(GameOptions.OpensGrid(false, 50250));   // "(g >= 50246 && g == 50250)" is 50250 only
        Assert.True(GameOptions.OpensGrid(false, 50247));
        Assert.True(GameOptions.OpensGrid(false, 0x092E));   // backgammon: TazUO opens it as a grid too
    }

    // ---- grid: sizes ----

    [Fact]
    public void GridItemSize_IsRound50TimesScale()
    {
        Assert.Equal(50, GameOptions.GridItemSize(100));
        Assert.Equal(25, GameOptions.GridItemSize(50));
        Assert.Equal(100, GameOptions.GridItemSize(200));
        Assert.Equal(38, GameOptions.GridItemSize(75));  // 37.5 rounds to even: 38
        Assert.Equal(62, GameOptions.GridItemSize(125)); // 62.5 rounds to even: 62
        Assert.Equal(25, GameOptions.GridItemSize(10));  // clamped to 50 %
        Assert.Equal(100, GameOptions.GridItemSize(900));
    }

    [Fact]
    public void GridArtSize_AsCalculateCenteredDimension()
    {
        // Smaller art keeps its size, or times the scale with ScaleItems; centred by halves.
        Assert.Equal((30, 10), GameOptions.GridArtSize(30, 50, false, 100));
        Assert.Equal((30, 10), GameOptions.GridArtSize(30, 50, true, 100));
        Assert.Equal((60, 20), GameOptions.GridArtSize(30, 100, true, 200));
        Assert.Equal((30, 35), GameOptions.GridArtSize(30, 100, false, 200));
        // Larger art: squeezed to the cell, or the cell times the scale.
        Assert.Equal((25, 0), GameOptions.GridArtSize(44, 25, false, 50));
        Assert.Equal((12, 6), GameOptions.GridArtSize(44, 25, true, 50));
        // Equal: the cell.
        Assert.Equal((50, 0), GameOptions.GridArtSize(50, 50, true, 100));
        // Odd sizes: (cell >> 1) - (size >> 1).
        Assert.Equal((31, 10), GameOptions.GridArtSize(31, 51, false, 100));
    }

    [Fact]
    public void Opacity_ClampsAndIsAlpha()
    {
        Assert.Equal(0.5f, GameOptions.GridBackgroundAlpha(50));
        Assert.Equal(0f, GameOptions.GridBackgroundAlpha(-5));
        Assert.Equal(1f, GameOptions.GridBackgroundAlpha(250));
        var c = new GameOptions { containerOpacity = 300, gridContainersScale = 10 }.Clamp();
        Assert.Equal(100, c.containerOpacity);
        Assert.Equal(GameOptions.MinGridContainersScale, c.gridContainersScale);
        c = new GameOptions { containerOpacity = -1, gridContainersScale = 999 }.Clamp();
        Assert.Equal(0, c.containerOpacity);
        Assert.Equal(GameOptions.MaxGridContainersScale, c.gridContainersScale);
        Assert.Equal(0, GameOptions.Parse("{\"containerOpacity\": -40}").containerOpacity);
        Assert.Equal(50, GameOptions.Defaults().containerOpacity);
        Assert.Equal(100, GameOptions.Defaults().gridContainersScale);
        Assert.True(GameOptions.Defaults().gridContainerScaleItems);
    }

    [Fact]
    public void WindowSize_FitsFiveColumnsAndRows_AtEveryScale()
    {
        var origins = new List<(int X, int Y)>();
        foreach (int scale in new[] { 50, 75, 100, 133, 150, 200 })
        {
            int cell = GameOptions.GridItemSize(scale);
            var (w, h) = GridContainerWindow.WindowSize(cell);
            Assert.Equal(8 + 14 + 5 * cell + 6, w);  // GetWidth: borders, bar, cells, a gap before each and after the last
            Assert.Equal(20 + 8 + 5 * (cell + 1), h); // GetHeight less the top bar
            var area = GridContainerWindow.ScrollArea(w, h);
            Assert.Equal((4, 24, w - 8, h - 28), area);
            GridContainerWindow.CellOrigins(25, cell, area.Width, origins);
            Assert.Equal(5, origins.Count(o => o.Y == 0));   // five to a row
            Assert.Equal((1, 0), origins[0]);
            Assert.Equal((1 + 4 * (cell + 1), 0), origins[4]);
            Assert.Equal((1, cell + 1), origins[5]);
            Assert.Equal(0, GridContainerWindow.MaxScroll(origins, cell, area.Height)); // 25 fit without scrolling
            GridContainerWindow.CellOrigins(26, cell, area.Width, origins);
            Assert.Equal(cell, GridContainerWindow.MaxScroll(origins, cell, area.Height)); // a sixth row: to its bottom edge
        }
    }

    // ---- grid: hit tests, scrolling ----

    [Fact]
    public void CellAt_TakesTheWholeCell_ScrolledAndClipped()
    {
        const int cell = 50;
        var (w, h) = GridContainerWindow.WindowSize(cell);
        var area = GridContainerWindow.ScrollArea(w, h);
        var origins = new List<(int X, int Y)>();
        GridContainerWindow.CellOrigins(12, cell, area.Width, origins);

        Assert.Equal(0, GridContainerWindow.CellAt(origins, cell, 1, 0, area.Width, area.Height, 0));      // top-left pixel
        Assert.Equal(0, GridContainerWindow.CellAt(origins, cell, 50, 49, area.Width, area.Height, 0));    // bottom-right pixel
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 0, 10, area.Width, area.Height, 0));    // the gap before it
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 51, 10, area.Width, area.Height, 0));   // the gap after it
        Assert.Equal(1, GridContainerWindow.CellAt(origins, cell, 52, 10, area.Width, area.Height, 0));
        Assert.Equal(5, GridContainerWindow.CellAt(origins, cell, 10, 51, area.Width, area.Height, 0));    // second row
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 10, 50, area.Width, area.Height, 0));   // the gap between rows
        // Scrolled one row: the point that was on cell 0 is now on cell 5.
        Assert.Equal(5, GridContainerWindow.CellAt(origins, cell, 10, 10, area.Width, area.Height, 51));
        // On the bar, outside the area, or past the last item: nothing.
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, area.Width - 5, 10, area.Width, area.Height, 0));
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 10, -1, area.Width, area.Height, 0));
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 10, area.Height, area.Width, area.Height, 51));
        Assert.Equal(-1, GridContainerWindow.CellAt(origins, cell, 200, 120, area.Width, area.Height, 0)); // cell 14 doesn't exist
    }

    [Fact]
    public void Scrolling_ClampsAndTheBarFollows()
    {
        Assert.Equal(0, GridContainerWindow.ClampScroll(-50, 100));
        Assert.Equal(100, GridContainerWindow.ClampScroll(150, 100));
        Assert.Equal(0, GridContainerWindow.ClampScroll(50, 0));
        Assert.Equal((0f, 200f), GridContainerWindow.Thumb(0, 0, 200));   // nothing to scroll: the whole track
        var (top, height) = GridContainerWindow.Thumb(0, 200, 200);
        Assert.Equal((0f, 100f), (top, height));                           // half the content shows: half the track
        Assert.Equal(100f, GridContainerWindow.Thumb(200, 200, 200).Top);  // at the end: at the bottom
        Assert.Equal(0, GridContainerWindow.ScrollFromBar(0, 200, 200));
        Assert.Equal(200, GridContainerWindow.ScrollFromBar(200, 200, 200));
        Assert.Equal(100, GridContainerWindow.ScrollFromBar(100, 200, 200));
        Assert.Equal(0, GridContainerWindow.ScrollFromBar(100, 0, 200));
        Assert.Equal(GridContainerWindow.ScrollbarWidth, GridContainerWindow.Thumb(0, 100000, 200).Height); // never thinner than 14
    }

    // ---- grid: art bounds and the grab point ----

    [Fact]
    public void OpaqueBounds_AndArtPixel_RoundTrip()
    {
        // A 10 x 8 picture opaque only in x 2-5, y 3-6.
        Func<int, int, bool> opaque = (x, y) => x >= 2 && x <= 5 && y >= 3 && y <= 6;
        var b = GridContainerWindow.OpaqueBounds(10, 8, opaque);
        Assert.Equal((2, 3, 4, 4), b);
        Assert.Equal((0, 0, 10, 8), GridContainerWindow.OpaqueBounds(10, 8, (_, _) => false)); // none: the whole art

        // Bounds drawn 4x at (20, 30): a point in the drawn art maps back to its art pixel, kept on the art.
        Assert.Equal((2, 3), GridContainerWindow.ArtPixel(20, 30, 20, 30, 16, 16, b, 10, 8));
        Assert.Equal((5, 6), GridContainerWindow.ArtPixel(35.9f, 45.9f, 20, 30, 16, 16, b, 10, 8));
        Assert.Equal((3, 4), GridContainerWindow.ArtPixel(24, 34, 20, 30, 16, 16, b, 10, 8));
        Assert.Equal((0, 0), GridContainerWindow.ArtPixel(-100, -100, 20, 30, 16, 16, b, 10, 8));
        Assert.Equal((9, 7), GridContainerWindow.ArtPixel(500, 500, 20, 30, 16, 16, b, 10, 8));
    }

    // ---- grid: what is listed, loot, drops, the label ----

    [Fact]
    public void GridListsItem_AsGetItemsInContainer()
    {
        Assert.True(GameOptions.GridListsItem(false, 0, false, 0));
        Assert.True(GameOptions.GridListsItem(true, 0, false, 0));    // loose in a corpse
        Assert.True(GameOptions.GridListsItem(true, 5, true, 5));     // worn on the chest (a BAD_CONTAINER_LAYERS layer)
        Assert.False(GameOptions.GridListsItem(true, 11, true, 11));  // hair
        Assert.False(GameOptions.GridListsItem(true, 21, false, 21)); // the backpack
        Assert.False(GameOptions.GridListsItem(true, 25, false, 0));  // the mount
        Assert.True(GameOptions.GridListsItem(false, 21, false, 21)); // not a corpse: worn layers mean nothing
        Assert.False(GameOptions.GridListsItem(false, 0, true, 16));  // wearable beard anywhere
        Assert.True(GameOptions.GridListsItem(false, 0, false, 16));  // not wearable: listed
        Assert.False(GameOptions.GridListsItem(true, 40, false, 0));  // past the table: left out, as not BAD
    }

    [Fact]
    public void GridLoot_OnlyInACorpse_PlainLootableItems()
    {
        Assert.True(GameOptions.GridLootsOnDoubleClick(true, true, false, false, 0, true));
        Assert.False(GameOptions.GridLootsOnDoubleClick(false, true, false, false, 0, true)); // option off
        Assert.False(GameOptions.GridLootsOnDoubleClick(true, false, false, false, 0, true)); // a chest: TazUO's grid uses it
        Assert.False(GameOptions.GridLootsOnDoubleClick(true, true, true, false, 0, true));   // the own backpack
        Assert.False(GameOptions.GridLootsOnDoubleClick(true, true, false, true, 0, true));   // a bag: opened instead
        Assert.False(GameOptions.GridLootsOnDoubleClick(true, true, false, false, 11, true)); // hair
        Assert.False(GameOptions.GridLootsOnDoubleClick(true, true, false, false, 0, false)); // no backpack to put it in
    }

    [Fact]
    public void Drops_IntoItemBackInPlaceOrIntoContainer()
    {
        Assert.Equal(GridContainerWindow.DropKind.IntoItem, GridContainerWindow.DropKindFor(true, true, true));
        Assert.Equal(GridContainerWindow.DropKind.IntoItem, GridContainerWindow.DropKindFor(true, true, false));
        Assert.Equal(GridContainerWindow.DropKind.BackInPlace, GridContainerWindow.DropKindFor(true, false, true)); // a like-less item
        Assert.Equal(GridContainerWindow.DropKind.BackInPlace, GridContainerWindow.DropKindFor(false, false, true)); // its own cell / space
        Assert.Equal(GridContainerWindow.DropKind.IntoContainer, GridContainerWindow.DropKindFor(false, false, false));
        Assert.Equal(GridContainerWindow.DropKind.IntoContainer, GridContainerWindow.DropKindFor(true, false, false));
        // What "takes it" means is the classic window's rule (GameSession.DropsInto): a container, or a like stack.
        Assert.True(GameSession.DropsInto(0x0EED, IO.Formats.TileFlags.Generic, 0x0EED, IO.Formats.TileFlags.Generic));
        Assert.True(GameSession.DropsInto(0x0F7A, 0, 0x0E75, IO.Formats.TileFlags.Container));
        Assert.False(GameSession.DropsInto(0x0F7A, 0, 0x0EED, IO.Formats.TileFlags.Generic));
    }

    [Fact]
    public void Title_AsGetContainerName()
    {
        Assert.Equal("a container (3)", GridContainerWindow.Title(null, 3));
        Assert.Equal("a backpack (0)", GridContainerWindow.Title("a backpack", 0));
        Assert.Equal("a corpse of an orc... (12)", GridContainerWindow.Title("a corpse of an orcish mage", 12)); // 18 + "..."
        Assert.Equal("exactly twenty-one ch (1)", GridContainerWindow.Title("exactly twenty-one ch", 1));
        Assert.Equal("a pouch", GridContainerWindow.PlainText("<basefont color=#ffffff>a pouch</basefont>"));
    }

    // ---- the options file and draft ----

    [Fact]
    public void Batch5_RoundTrip_UnknownKeys_AndDraftPaths()
    {
        var o = new GameOptions
        {
            gridContainersDefaultToOldStyleView = false, containerOpacity = 80, gridContainersScale = 150, gridContainerScaleItems = false,
        };
        string json = o.ToJson();
        Assert.Contains("\"gridContainersDefaultToOldStyleView\": false", json);
        Assert.Contains("\"containerOpacity\": 80", json);
        Assert.Contains("\"gridContainersScale\": 150", json);
        Assert.Contains("\"gridContainerScaleItems\": false", json);
        GameOptions again = GameOptions.Parse(json, out var unknown);
        Assert.Empty(unknown);
        Assert.True(again.SameAs(o));
        Assert.False(again.SameAs(GameOptions.Defaults()));
        foreach (string key in new[] { "gridContainersDefaultToOldStyleView", "gridContainerScaleItems" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": false}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "containerOpacity", "gridContainersScale" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": 60}").SameAs(GameOptions.Defaults()), key);

        GameOptions read = GameOptions.Parse("{\"containerOpacity\": 20, \"Grid_DefaultColumns\": 7}", out unknown);
        Assert.Equal(20, read.containerOpacity);
        Assert.Equal("Grid_DefaultColumns", Assert.Single(unknown).Key);
        Assert.Contains("\"Grid_DefaultColumns\": 7", read.ToJson(unknown));

        GameOptions live = GameOptions.Defaults();
        int applies = 0, saves = 0;
        var draft = new OptionsDraft(() => live.Clone(), x => { applies++; live = x.Clone(); }, _ => saves++);
        draft.Draft.gridContainersDefaultToOldStyleView = false;
        draft.Draft.containerOpacity = 10;
        draft.Cancel(); // dropped
        Assert.True(draft.Draft.gridContainersDefaultToOldStyleView);
        Assert.Equal(50, draft.Draft.containerOpacity);
        Assert.Equal((0, 0), (applies, saves));

        draft.Draft.gridContainersScale = 400; // clamped on Apply
        draft.Apply();
        Assert.Equal(GameOptions.MaxGridContainersScale, live.gridContainersScale);
        Assert.Equal((1, 0), (applies, saves));

        draft.Defaults(); // the draft only
        Assert.Equal(100, draft.Draft.gridContainersScale);
        Assert.Equal(GameOptions.MaxGridContainersScale, live.gridContainersScale);
        draft.Okay();
        Assert.Equal(100, live.gridContainersScale);
        Assert.Equal((2, 1), (applies, saves));
    }

    [Fact]
    public void Batch5_GreysUpdated()
    {
        // Opacity and grid containers are offered now; what the grid lacks is listed, each with a reason.
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.StartsWith("Container opacity", StringComparison.Ordinal));
        Assert.Contains(OptionsWindow.NotOffered, n => n.Page == OptionsWindow.Page.Containers && n.Text.Contains("Auto loot"));
        Assert.DoesNotContain("Nameplates", OptionsWindow.UnavailablePages);
    }
}
