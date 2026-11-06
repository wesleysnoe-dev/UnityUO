using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// <see cref="ObjectHighlight"/>: TazUO's 0x0035 mouse-over hue, for window items always and in the world by
/// ItemView/MobileView's rules; the pick is the topmost opaque pixel, and nothing under a window is selected.
/// </summary>
public sealed class ObjectHighlightTests
{
    [Fact]
    public void WindowItem_IsHuedWhole_OnlyUnderTheMouse()
    {
        Assert.Equal(0x0035 | ObjectHighlight.WholeArt, ObjectHighlight.WindowItemHue(0x0481, mouseOver: true));
        Assert.True(ObjectHighlight.IsWholeArt(ObjectHighlight.WindowItemHue(0, true)));
        Assert.Equal(0x0481, ObjectHighlight.WindowItemHue(0x0481, mouseOver: false));
        Assert.Equal(0x0481, ObjectHighlight.WindowItemHue(0x0481, true, highlightOnMouseOver: false));
    }

    [Fact]
    public void HighlightGameObjects_DefaultsOn_SoCharactersAndCorpsesHover()
    {
        // Wes: hover feedback on everything interactable; a code default, no profile written.
        Assert.True(ObjectHighlight.HighlightGameObjects);
        Assert.True(ObjectHighlight.HighlightsBody(selected: true, ObjectHighlight.HighlightGameObjects));
        Assert.True(ObjectHighlight.OutlinesInWorld(selected: true, body: true, locked: false, ObjectHighlight.HighlightGameObjects));
        // A fixture (locked furniture, a door) is hued and outlined too.
        Assert.Equal(0x0035 | ObjectHighlight.WholeArt,
            ObjectHighlight.GroundItemHue(0x0021, selected: true, locked: true, multi: false, ObjectHighlight.HighlightGameObjects));
    }

    [Fact]
    public void Outline_InTheWorld_FollowsTheHue()
    {
        Assert.False(ObjectHighlight.OutlinesInWorld(selected: false, body: true, locked: false, highlightGameObjects: true));
        Assert.True(ObjectHighlight.OutlinesInWorld(true, body: false, locked: true, highlightGameObjects: true));
        // Without HighlightGameObjects, as TazUO's hue: movable items only, never bodies or fixtures.
        Assert.True(ObjectHighlight.OutlinesInWorld(true, body: false, locked: false, highlightGameObjects: false));
        Assert.False(ObjectHighlight.OutlinesInWorld(true, body: false, locked: true, highlightGameObjects: false));
        Assert.False(ObjectHighlight.OutlinesInWorld(true, body: true, locked: false, highlightGameObjects: false));
    }

    [Theory]
    [InlineData(ObjectHighlight.PickKind.Mobile, true)]
    [InlineData(ObjectHighlight.PickKind.Corpse, true)]
    [InlineData(ObjectHighlight.PickKind.GroundItem, true)]
    [InlineData(ObjectHighlight.PickKind.LockedItem, true)] // furniture, doors, chests: used and opened
    [InlineData(ObjectHighlight.PickKind.MultiPart, false)] // house and boat parts
    [InlineData(ObjectHighlight.PickKind.MapStatic, false)] // the map's own statics: nothing to use
    [InlineData(ObjectHighlight.PickKind.Land, false)]
    [InlineData(ObjectHighlight.PickKind.NoDraw, false)]
    public void Hovers_IsWhatThePickTakes(ObjectHighlight.PickKind kind, bool hovers) =>
        Assert.Equal(hovers, ObjectHighlight.Hovers(kind));

    private static bool Lit(Bitmap32 b, int x, int y) => b.Pixels[y * b.Width + x] != 0;

    [Fact]
    public void Outline_IsTheRingAroundOpaquePixels_NotTheBox()
    {
        // A 4x4 picture opaque only in its 2x2 middle: its outline is the 4x4 ring around that, inside a 6x6 picture.
        Bitmap32 o = ObjectHighlight.Outline(Ring(), 1, 0xFFFFFFFF);
        Assert.Equal((6, 6), (o.Width, o.Height));
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                bool middle = x is 2 or 3 && y is 2 or 3;          // the art's own opaque pixels (offset by 1)
                bool ring = x is >= 1 and <= 4 && y is >= 1 and <= 4 && !middle;
                Assert.True(ring == Lit(o, x, y), $"({x},{y})");
            }
        Assert.Equal(0xFFFFFFFFu, o.Pixels[1 * 6 + 1]); // diagonal neighbours count, as TazUO's 3x3 test
    }

    [Fact]
    public void Outline_GrowsPastTheArtsEdge_AndTakesTheThickness()
    {
        // Fully opaque art: nothing inside is outlined, the extra border all is.
        Bitmap32 o = ObjectHighlight.Outline(Solid(), 2, 0xFF00FF00);
        Assert.Equal((8, 8), (o.Width, o.Height));
        Assert.True(Lit(o, 0, 0) && Lit(o, 7, 7) && Lit(o, 1, 4));
        Assert.False(Lit(o, 2, 2) || Lit(o, 5, 5));
        Assert.Equal(0xFF00FF00u, o.Pixels[0]);

        // A single pixel at thickness 2: a 5x5 square around it, minus the pixel.
        var dot = new Bitmap32(1, 1);
        dot.Pixels[0] = 0xFF123456;
        Bitmap32 d = ObjectHighlight.Outline(dot, 2, 0xFFFFFFFF);
        Assert.Equal(24, d.Pixels.Count(p => p != 0));
        Assert.False(Lit(d, 2, 2));
    }

    [Fact]
    public void Outline_OfAnEmptyPicture_IsEmpty() =>
        Assert.All(ObjectHighlight.Outline(new Bitmap32(3, 3), 1, 0xFFFFFFFF).Pixels, p => Assert.Equal(0u, p));

    [Fact]
    public void GroundItem_WithoutHighlightGameObjects_OnlyMovableOrLightItemsHighlight()
    {
        // ItemView.cs:141-145: !IsLocked && !IsMulti && isSelected, the item's partial flag kept.
        Assert.Equal(0x0035, ObjectHighlight.GroundItemHue(0x0021, selected: true, locked: false, multi: false, highlightGameObjects: false));
        Assert.Equal(0x0021, ObjectHighlight.GroundItemHue(0x0021, selected: true, locked: true, multi: false, highlightGameObjects: false));
        Assert.Equal(0x0021, ObjectHighlight.GroundItemHue(0x0021, selected: true, locked: false, multi: true, highlightGameObjects: false));
        Assert.Equal(0x0021, ObjectHighlight.GroundItemHue(0x0021, selected: false, locked: false, multi: false, highlightGameObjects: false));
    }

    [Fact]
    public void GroundItem_WithHighlightGameObjects_AnySelectedItemIsHuedWhole()
    {
        Assert.Equal(0x0035 | ObjectHighlight.WholeArt, ObjectHighlight.GroundItemHue(0, true, locked: true, multi: false, highlightGameObjects: true));
        Assert.Equal(0, ObjectHighlight.GroundItemHue(0, false, locked: false, multi: false, highlightGameObjects: true));
    }

    [Theory]
    [InlineData(0x20, 255, false)] // movable: never locked
    [InlineData(0x00, 255, true)]  // a fixture
    [InlineData(0x00, 91, true)]
    [InlineData(0x00, 90, false)]  // not movable but light: TazUO still highlights it
    public void IsLocked_IsTazUOsItemIsLocked(byte flags, byte weight, bool locked) =>
        Assert.Equal(locked, ObjectHighlight.IsLocked(flags, weight));

    [Fact]
    public void Bodies_HighlightOnlyWithHighlightGameObjects()
    {
        Assert.False(ObjectHighlight.HighlightsBody(selected: true, highlightGameObjects: false));
        Assert.True(ObjectHighlight.HighlightsBody(selected: true, highlightGameObjects: true));
        Assert.False(ObjectHighlight.HighlightsBody(selected: false, highlightGameObjects: true));
    }

    [Fact]
    public void World_IsNotSelectedThroughAWindow()
    {
        Assert.Equal(0u, ObjectHighlight.WorldSelection(overUi: true, picked: 0x40001234));
        Assert.Equal(0x40001234u, ObjectHighlight.WorldSelection(overUi: false, picked: 0x40001234));
    }

    // A 4x4 picture opaque only in its 2x2 middle, and a fully opaque one.
    private static Bitmap32 Ring()
    {
        var b = new Bitmap32(4, 4);
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            b.Pixels[y * 4 + x] = 0xFF808080;
        return b;
    }

    private static Bitmap32 Solid()
    {
        var b = new Bitmap32(4, 4);
        Array.Fill(b.Pixels, 0xFF404040u);
        return b;
    }

    private static bool Opaque(Bitmap32 b, int x, int y) => x >= 0 && y >= 0 && x < b.Width && y < b.Height && (b.Pixels[y * b.Width + x] >> 24) != 0;

    [Fact]
    public void Window_TopmostOpaqueItemWins_TransparentPaddingDoesNot()
    {
        // Drawn in order: a solid item at (0,0), then a padded one over it at (0,0).
        var drawn = new[] { (X: 0, Y: 0, Art: Solid()), (X: 0, Y: 0, Art: Ring()) };
        int At(int px, int py) => ObjectHighlight.Topmost(drawn.Length, i => Opaque(drawn[i].Art, px - drawn[i].X, py - drawn[i].Y));
        Assert.Equal(1, At(1, 1));  // the later item's opaque middle
        Assert.Equal(0, At(0, 0));  // the later item's transparent corner: the one under it
        Assert.Equal(-1, At(9, 9)); // nothing
    }

    [Fact]
    public void World_NearestOpaqueSpriteWins_ATransparentBoxInFrontDoesNot()
    {
        // Two sprites over the same spot: the nearer (depth 1) only has pixels in its middle.
        var sprites = new[] { (Depth: 5f, Art: Solid()), (Depth: 1f, Art: Ring()) };
        int At(int x, int y) => ObjectHighlight.Nearest(sprites.Length, i => sprites[i].Depth, i => Opaque(sprites[i].Art, x, y));
        Assert.Equal(1, At(2, 2));
        Assert.Equal(0, At(0, 3));
        Assert.Equal(-1, ObjectHighlight.Nearest(2, i => sprites[i].Depth, _ => false));
    }

    [Fact]
    public void SpritePixel_MapsAWorldPointWithYUp_AndMirrors()
    {
        // A 10x20 sprite drawn over (100, 50)-(110, 70), y growing upwards: the top-left pixel is at (100, 70).
        Assert.Equal((0, 0), ObjectHighlight.SpritePixel(100.2f, 69.8f, 100, 50, 10, 20, 10, 20, false));
        Assert.Equal((9, 19), ObjectHighlight.SpritePixel(109.9f, 50.1f, 100, 50, 10, 20, 10, 20, false));
        Assert.Equal((9, 0), ObjectHighlight.SpritePixel(100.2f, 69.8f, 100, 50, 10, 20, 10, 20, true));
        Assert.Null(ObjectHighlight.SpritePixel(99f, 60f, 100, 50, 10, 20, 10, 20, false));
        Assert.Null(ObjectHighlight.SpritePixel(105f, 70f, 100, 50, 10, 20, 10, 20, false));
        // Drawn at twice the sprite's size.
        Assert.Equal((2, 1), ObjectHighlight.SpritePixel(104.5f, 66.5f, 100, 50, 20, 20, 10, 10, false));
    }
}

/// <summary>ShopWindow's pictures as TazUO's ShopItem.Draw makes them: creatures on sale as their body, items as static art.</summary>
public sealed class ShopIconTests
{
    private const uint HorseSerial = 0x00001234, ItemSerial = 0x40001234;

    [Fact]
    public void BuyLine_MobileSerial_DrawsTheBody_ItemSerial_DrawsStaticArt()
    {
        Assert.True(ShopWindow.DrawsBody(selling: false, HorseSerial));
        Assert.False(ShopWindow.DrawsBody(selling: false, ItemSerial));
        Assert.False(ShopWindow.DrawsBody(selling: true, HorseSerial)); // InBuyGump only
        Assert.False(ShopWindow.DrawsBody(selling: false, 0));
    }

    [Fact]
    public void LineHeight_IsAtLeast50_AndAnItemsTileHeight()
    {
        Assert.Equal(50, ShopWindow.LineHeight(HorseSerial, 80)); // a body number's tiledata means nothing
        Assert.Equal(50, ShopWindow.LineHeight(ItemSerial, 10));
        Assert.Equal(60, ShopWindow.LineHeight(ItemSerial, 60));
    }

    [Fact]
    public void IconRect_BodyIsSqueezedTo45_ItemFitsTheColumn()
    {
        Assert.Equal((2, 2, 45, 45), ShopWindow.IconRect(body: true, 80, 66, 50));
        Assert.Equal((10, 10, 30, 30), ShopWindow.IconRect(body: true, 30, 30, 50));
        Assert.Equal((15, 15, 20, 20), ShopWindow.IconRect(body: false, 20, 20, 50));
        Assert.Equal((0, 0, 50, 50), ShopWindow.IconRect(body: false, 70, 90, 50));
    }

    [Fact]
    public void IconPixel_MapsTheMouseOntoTheDrawnPart_OnlyInsideIt()
    {
        // The part (3, 2, 4, 4) of a picture drawn stretched to 8 x 8 at (10, 20).
        var dest = (10f, 20f, 8f, 8f);
        var src = (3, 2, 4, 4);
        Assert.Equal((3, 2), ShopWindow.IconPixel(10.5f, 20.5f, dest, src));
        Assert.Equal((6, 5), ShopWindow.IconPixel(17.9f, 27.9f, dest, src));
        Assert.Equal((4, 3), ShopWindow.IconPixel(12f, 22f, dest, src));
        Assert.Null(ShopWindow.IconPixel(9.9f, 22f, dest, src));
        Assert.Null(ShopWindow.IconPixel(12f, 28f, dest, src));
    }

    [Fact]
    public void ShopIcon_HoversOnlyOnOpaquePixels()
    {
        // A 4x4 picture opaque only in its middle, drawn at 1:1: its corner is not on it, its middle is.
        var b = new Bitmap32(4, 4);
        b.Pixels[1 * 4 + 1] = b.Pixels[2 * 4 + 2] = 0xFF808080;
        bool Hover(float x, float y) => ShopWindow.IconPixel(x, y, (0f, 0f, 4f, 4f), (0, 0, 4, 4)) is { } p
                                        && (b.Pixels[p.Y * 4 + p.X] >> 24) != 0;
        Assert.True(Hover(1.5f, 1.5f));
        Assert.False(Hover(0.5f, 0.5f));
        Assert.False(Hover(5f, 1f));
    }

    [Fact]
    public void OutlineRect_IsThePartGrownByOnePixel_AtTheDrawnScale()
    {
        // Part (3, 2, 4, 4) drawn at twice its size: one outline pixel is 2 drawn pixels.
        var (dest, src) = ShopWindow.OutlineRect((10f, 20f, 8f, 8f), (3, 2, 4, 4));
        Assert.Equal((8f, 18f, 12f, 12f), dest);
        Assert.Equal((3, 2, 6, 6), src); // in the outline's coordinates, which start one pixel up and left
    }

    [Fact]
    public void OpaqueBounds_IsTheArtsRealBounds()
    {
        var b = new Bitmap32(10, 8);
        b.Pixels[2 * 10 + 3] = 0xFF000000;
        b.Pixels[5 * 10 + 6] = 0xFF000000;
        Assert.Equal((3, 2, 4, 4), ShopWindow.OpaqueBounds(b));
        Assert.Equal((0, 0, 10, 8), ShopWindow.OpaqueBounds(new Bitmap32(10, 8)));
    }
}

/// <summary>Against the install: a horse on sale (body 0xCC, as ModernUO's AnimalBuyInfo lists it) has a standing frame.</summary>
[Collection("UO install")]
public sealed class ShopIconInstallTests
{
    private readonly UOData _data;

    public ShopIconInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Theory]
    [InlineData(0xCC)]  // horse
    [InlineData(0x123)] // pack horse (291)
    public void ABodyOnSale_HasAStandingFrameFacingFileDirection1(int body)
    {
        AnimationLibrary anims = _data.AnimationLibrary;
        AnimationFrame[] frames = anims.Read(body, anims.Actions(body).Stand, 1);
        Assert.NotEmpty(frames);
        Assert.True(frames[0].Image.Width > 0 && frames[0].Image.Height > 0);
    }
}
