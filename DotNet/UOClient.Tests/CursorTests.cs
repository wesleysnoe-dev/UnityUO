using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity.UI;
using Xunit.Abstractions;

namespace UOClient.Tests;

/// <summary>The pointer's art index and hotspot (TazUO GameCursor / Art.CreateCursorSurfacePtr).</summary>
public sealed class CursorTests
{
    // Screen offsets from the player (y down) and the gauntlet TazUO's GetMouseDirection picks.
    [Theory]
    [InlineData(0, -100, 0)]    // up: PointingNorthWest
    [InlineData(100, -100, 1)]  // up-right: PointingNorth
    [InlineData(100, 0, 2)]     // right: PointingNorthEast
    [InlineData(100, 100, 3)]   // down-right: PointingEast
    [InlineData(0, 100, 4)]     // down: PointingSouthEast
    [InlineData(-100, 100, 5)]  // down-left: PointingSouth
    [InlineData(-100, 0, 6)]    // left: PointingSouthWest
    [InlineData(-100, -100, 7)] // up-left: PointingWest
    public void MouseDirectionPicksTheEightGauntlets(int dx, int dy, int index) =>
        Assert.Equal(index, TargetCursor.MouseDirection(500, 400, 500 + dx, 400 + dy, 1));

    [Theory]
    [InlineData(100, -40, 2)]     // 2:5 slope or flatter snaps to the axis (right)
    [InlineData(100, -41, 1)]     // just steeper: the diagonal
    [InlineData(40, -100, 0)]     // 5:2 or steeper snaps to up
    [InlineData(-100, 40, 6)]
    [InlineData(-40, 100, 4)]
    public void MouseDirectionSnapsShallowSlopesToTheAxis(int dx, int dy, int index) =>
        Assert.Equal(index, TargetCursor.MouseDirection(0, 0, dx, dy, 1));

    [Fact]
    public void MouseOnThePlayerKeepsTheGivenFacing() =>
        Assert.Equal(1, TargetCursor.MouseDirection(10, 10, 10, 10, 1));

    [Fact]
    public void WarModeSwapsToTheRedSet()
    {
        Assert.Equal(0x206A, TargetCursor.CursorGraphic(0, war: false));
        Assert.Equal(0x2053, TargetCursor.CursorGraphic(0, war: true));
        Assert.Equal(0x206D, TargetCursor.CursorGraphic(3, war: false));
        Assert.Equal(0x2056, TargetCursor.CursorGraphic(3, war: true));
        // Off the world: the fat west pointer, as before (0x2073 / 0x205C).
        Assert.Equal(0x2073, TargetCursor.CursorGraphic(TargetCursor.PointerIndex(false, 0, 0, 50, 50), war: false));
        Assert.Equal(0x205C, TargetCursor.CursorGraphic(TargetCursor.PointerIndex(false, 0, 0, 50, 50), war: true));
        Assert.Equal(0x2056, TargetCursor.CursorGraphic(TargetCursor.PointerIndex(true, 0, 0, 50, 50), war: true));
    }

    [Fact]
    public void BorderMarkersGiveTheHotspotAndAreCleared()
    {
        const uint green = 0xFF00FF00, red = 0xFFFF0000, black = 0xFF000000;
        var art = new Bitmap32(6, 5);
        art[3, 0] = green;     // hot x
        art[0, 2] = green;     // hot y
        art[1, 0] = red;       // another marker colour on the border: cleared, not a hotspot
        art[5, 4] = red;       // last row/column: cleared
        art[2, 0] = black;     // opaque black is art, kept
        art[2, 2] = red;       // inside: kept
        Bitmap32 img = TargetCursor.CursorImage(art, out int hx, out int hy);
        Assert.Equal((3, 2), (hx, hy));
        Assert.Equal(0u, img[3, 0]);
        Assert.Equal(0u, img[0, 2]);
        Assert.Equal(0u, img[1, 0]);
        Assert.Equal(0u, img[5, 4]);
        Assert.Equal(black, img[2, 0]);
        Assert.Equal(red, img[2, 2]);
        Assert.Equal(green, art[3, 0]); // the source is untouched
    }
}

[Collection("UO install")]
public sealed class CursorArtTests
{
    private readonly UOData _data;
    private readonly ITestOutputHelper _out;

    public CursorArtTests(UOInstallFixture fixture, ITestOutputHelper output)
    {
        _data = fixture.Data;
        _out = output;
    }

    [Fact]
    public void EveryPointerHasArtAndAHotspot()
    {
        for (int war = 0; war < 2; war++)
            for (int i = 0; i < 16; i++)
            {
                int g = TargetCursor.CursorGraphic(i, war == 1);
                Bitmap32 art = _data.GetStatic(g);
                Assert.NotNull(art);
                TargetCursor.CursorImage(art!, out int hx, out int hy);
                _out.WriteLine($"0x{g:X4} {art!.Width}x{art.Height} hot ({hx},{hy})");
                Assert.True(hx > 0 || hy > 0, $"0x{g:X4} has no hotspot marker");
                Assert.InRange(hx, 0, art.Width - 1);
                Assert.InRange(hy, 0, art.Height - 1);
            }
        // The down-right gauntlet's fingertip is at its bottom-right, the up one's at its top.
        TargetCursor.CursorImage(_data.GetStatic(0x206D)!, out int dx, out int dy);
        Assert.Equal((35, 23), (dx, dy));
        TargetCursor.CursorImage(_data.GetStatic(0x206A)!, out int ux, out int uy);
        Assert.Equal((7, 1), (ux, uy));
    }

    // The game runs with the HD pack, which has a 2x copy of every pointer whose upscale blends the pure-green markers
    // (0xFF00FF00 became e.g. 0xFF138370). Read from that copy, every hotspot was (0, 0): the click landed on the
    // picture's top-left corner, not the fingertip. The pointer must come from the stock art.
    [Fact]
    public void HdOverrideWithBlendedMarkersKeepsTheFingertipHotspot()
    {
        const int g = 0x206D; // down-right gauntlet: fingertip at (35, 23), far from the corner
        Bitmap32 stock = _data.GetStockStatic(g)!;
        var hd = new Bitmap32(stock.Width * 2, stock.Height * 2);
        for (int y = 0; y < hd.Height; y++)
            for (int x = 0; x < hd.Width; x++)
            {
                uint p = stock[x / 2, y / 2];
                hd[x, y] = p == 0xFF00FF00 ? 0xFF138370 : p;
            }
        string dir = Path.Combine(Path.GetTempPath(), "uoclient-cursor-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "ExternalImages", "art"));
            Png.Write(Path.Combine(dir, "ExternalImages", "art", $"0x{g:X4}.png"), hd);
            _data.UseOverrides(new ClientOverrides(dir, includeHdPack: false));

            TargetCursor.CursorImage(_data.GetStatic(g)!, out int ox, out int oy);
            Assert.Equal((0, 0), (ox, oy)); // the trap: the override's shape has no marker left

            Bitmap32 img = TargetCursor.PointerImage(_data, g, out int hx, out int hy);
            Assert.Equal((35, 23), (hx, hy));
            Assert.Equal((stock.Width, stock.Height), (img.Width, img.Height));
            Assert.True(img[hx, hy] >> 24 != 0, "the hotspot is on a drawn pixel of the finger");
        }
        finally
        {
            _data.UseOverrides(null);
            Directory.Delete(dir, recursive: true);
        }
    }

    // The fingertip in every pointer the game shows (PointerIndex: the eight directions and the fat west one), peace
    // and war: the hotspot from the stock art is on a drawn pixel or touching one (the fat pointer's marker sits at
    // (1, 1), just off the tip's corner, as TazUO uses it). Indices 8 and 10-15 are other cursors, not pointing hands.
    [Fact]
    public void EveryPointerHotspotIsOnTheDrawnFingertip()
    {
        int[] shown = { 0, 1, 2, 3, 4, 5, 6, 7, TargetCursor.FatPointingWest };
        for (int war = 0; war < 2; war++)
            foreach (int i in shown)
            {
                int g = TargetCursor.CursorGraphic(i, war == 1);
                Bitmap32 img = TargetCursor.PointerImage(_data, g, out int hx, out int hy)!;
                bool touches = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (hx + dx >= 0 && hy + dy >= 0 && hx + dx < img.Width && hy + dy < img.Height && img[hx + dx, hy + dy] >> 24 != 0)
                            touches = true;
                Assert.True(touches, $"0x{g:X4} hotspot ({hx},{hy}) is off the drawn hand");
            }
    }
}
