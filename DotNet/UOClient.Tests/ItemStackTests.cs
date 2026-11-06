using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// One amount rule for every view, as TazUO: coin piles by amount (Item.DisplayedGraphic, Item.cs:53-73), two copies of a
/// non-coin Generic stack (ItemView.cs:154-157, ItemGump.cs:139-151, GameCursor.cs:445-455), one in the grid (GridItem.cs:225).
/// </summary>
public sealed class ItemStackTests
{
    [Theory]
    [InlineData(0x0EED, 0, 0x0EED)]
    [InlineData(0x0EED, 1, 0x0EED)]
    [InlineData(0x0EED, 2, 0x0EEE)]
    [InlineData(0x0EED, 5, 0x0EEE)]
    [InlineData(0x0EED, 6, 0x0EEF)]
    [InlineData(0x0EED, 60000, 0x0EEF)]
    [InlineData(0x0EEA, 3, 0x0EEB)] // copper
    [InlineData(0x0EEA, 9, 0x0EEC)]
    [InlineData(0x0EF0, 2, 0x0EF1)] // silver
    [InlineData(0x0EF0, 100, 0x0EF2)]
    [InlineData(0x0EEE, 100, 0x0EEE)] // the pile arts are not coins themselves (IsCoin is the three single coins)
    [InlineData(0x0F3F, 100, 0x0F3F)] // arrows keep their art
    public void CoinPilesByAmount(int graphic, int amount, int shown) => Assert.Equal(shown, ItemStack.DisplayedGraphic(graphic, amount));

    [Theory]
    [InlineData(0x0F3F, 2, TileFlags.Generic, true)]
    [InlineData(0x0F3F, 1, TileFlags.Generic, false)]
    [InlineData(0x0F3F, 0, TileFlags.Generic, false)]
    [InlineData(0x0E75, 5, TileFlags.Container, false)] // not stackable
    [InlineData(0x0EED, 50, TileFlags.Generic, false)]  // coins never twice
    [InlineData(0x0EEA, 2, TileFlags.Generic, false)]
    public void TwiceForNonCoinStacks(int graphic, int amount, TileFlags flags, bool twice) =>
        Assert.Equal(twice, ItemStack.DrawsTwice(graphic, amount, flags));

    [Theory]
    // ItemHold.Set: one coin off a pile shows one coin; any other lift shows the pile as it lay.
    [InlineData(0x0EED, 1, 100, 0x0EED)]
    [InlineData(0x0EED, 3, 100, 0x0EEF)]
    [InlineData(0x0EED, 3, 3, 0x0EEE)]
    [InlineData(0x0EED, 1, 1, 0x0EED)]
    [InlineData(0x0F3F, 1, 50, 0x0F3F)]
    public void HeldArt(int graphic, int lifted, int total, int shown) => Assert.Equal(shown, ItemStack.HeldGraphic(graphic, lifted, total));

    [Theory]
    [InlineData(0x0F3F, 0x0F3F, 10, true)]
    [InlineData(0x0F3F, 0x0F3F, 1, false)]
    [InlineData(0x0EED, 0x0EEF, 10, false)] // a held pile: its art is not the coin's own
    [InlineData(0x0EED, 0x0EED, 2, true)]   // GameCursor's rule as written: a coin whose art is its own and >1 lifted
    public void HeldTwice(int graphic, int held, int lifted, bool twice) =>
        Assert.Equal(twice, ItemStack.HeldDrawsTwice(graphic, held, lifted, TileFlags.Generic));

    [Fact]
    public void OffsetsMatchTheClassicDraws()
    {
        Assert.Equal(-5, ItemStack.GroundOffset);
        Assert.Equal(5, ItemStack.GumpOffset);
        Assert.Equal(MousePick.StackOffset, ItemStack.GumpOffset); // the window hit test's second copy is the drawn one
    }

    [Fact]
    public void DoubledPutsTheSecondCopyDownRightOverTheFirst()
    {
        var src = new Bitmap32(3, 2);
        src[0, 0] = 0xFF112233;
        src[2, 1] = 0xFF445566;
        Bitmap32 d = ItemStack.Doubled(src);
        Assert.Equal(8, d.Width);
        Assert.Equal(7, d.Height);
        Assert.Equal(0xFF112233u, d[0, 0]);
        Assert.Equal(0xFF445566u, d[2, 1]);
        Assert.Equal(0xFF112233u, d[5, 5]);
        Assert.Equal(0xFF445566u, d[7, 6]);
        Assert.Equal(0u, d[1, 0]);
        Assert.Null(ItemStack.Doubled(null));
    }

    [Fact]
    public void TheWindowHitTestTakesEitherCopy()
    {
        // A 3x3 art with only its top-left pixel opaque: the second copy's is 5 px down-right.
        var art = new Bitmap32(3, 3);
        art[0, 0] = 0xFF000000;
        bool Opaque(int k, int x, int y) => x >= 0 && y >= 0 && x < 3 && y < 3 && art[x, y] >> 24 != 0;
        Assert.Equal(0, MousePick.WindowItem(1, 10, 10, 1, _ => (10, 10), _ => true, Opaque).Index);
        Assert.Equal(0, MousePick.WindowItem(1, 15, 15, 1, _ => (10, 10), _ => true, Opaque).Index);
        Assert.Equal(-1, MousePick.WindowItem(1, 15, 15, 1, _ => (10, 10), _ => false, Opaque).Index);
    }
}

/// <summary>The real 7.0.23.1 art and tiledata behind the rule.</summary>
[Collection("UO install")]
public sealed class ItemStackInstallTests
{
    private readonly UOData _data;

    public ItemStackInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Theory]
    [InlineData(0x0EED)]
    [InlineData(0x0EEA)]
    [InlineData(0x0EF0)]
    public void EachCoinHasThreeDistinctPileArts(int coin)
    {
        Bitmap32 one = _data.GetStatic(coin), few = _data.GetStatic(coin + 1), many = _data.GetStatic(coin + 2);
        Assert.NotNull(one);
        Assert.NotNull(few);
        Assert.NotNull(many);
        Assert.NotEqual(Opaque(one), Opaque(few));
        Assert.NotEqual(Opaque(few), Opaque(many));
        Assert.True(Opaque(many) > Opaque(one)); // the big pile covers more
        Assert.True((_data.TileData.GetStatic(coin).Flags & TileFlags.Generic) != 0);
        Assert.False(ItemStack.DrawsTwice(coin, 100, _data.TileData.GetStatic(coin).Flags));
    }

    [Theory]
    [InlineData(0x0F3F)] // arrow
    [InlineData(0x0E21)] // bandage
    [InlineData(0x1BDD)] // log
    [InlineData(0x1BF2)] // iron ingot
    [InlineData(0x0F7A)] // black pearl
    public void StackablesDrawTwice(int graphic)
    {
        TileFlags flags = _data.TileData.GetStatic(graphic).Flags;
        Assert.True(ItemStack.DrawsTwice(graphic, 2, flags));
        Assert.False(ItemStack.DrawsTwice(graphic, 1, flags));
        Bitmap32 art = _data.GetStatic(graphic);
        Bitmap32 two = ItemStack.Doubled(art);
        // Every opaque pixel of the art is opaque in both copies of the composite.
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                if (art[x, y] >> 24 != 0)
                {
                    Assert.NotEqual(0u, two[x, y] >> 24);
                    Assert.Equal(art[x, y], two[x + 5, y + 5]); // the copy on top is the art itself
                }
    }

    [Theory]
    [InlineData(0x0E75)] // backpack
    [InlineData(0x0E40)] // metal chest
    public void ContainersAreNotStacks(int graphic) =>
        Assert.False(ItemStack.DrawsTwice(graphic, 5, _data.TileData.GetStatic(graphic).Flags));

    private static int Opaque(Bitmap32 b)
    {
        int n = 0;
        foreach (uint p in b.Pixels)
            if (p >> 24 != 0)
                n++;
        return n;
    }
}
