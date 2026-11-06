using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// The order worn layers stack over the body per facing: TazUO's <c>LayerOrder.UsedLayers</c> (LayerOrder.cs:7-64), as
/// MobileView draws them (MobileView.cs:335-441). Facing the viewer the cloak is under everything; every other way over.
/// </summary>
public sealed class LayerOrderTests
{
    private const byte Cloak = 20, Robe = 22, Hair = 11, Helmet = 6, TwoHanded = 2, OneHanded = 1, Shirt = 5;

    private static MobileLook Dressed(bool mounted)
    {
        var look = new MobileLook { Body = 400, Mounted = mounted };
        if (mounted)
            look.Layers.Add(new MobileLook.Layer { Body = 0xC8, IsMount = true });
        look.Layers.Add(new MobileLook.Layer { Body = 400 });
        foreach (byte slot in new[] { Shirt, Robe, Hair, Helmet, OneHanded, TwoHanded, Cloak })
            look.Layers.Add(new MobileLook.Layer { Body = 1000 + slot, Slot = slot });
        return look;
    }

    private static int IndexOf(List<MobileLook.Layer> order, byte slot) => order.FindIndex(l => l.Slot == slot);

    [Fact]
    public void TheTablesAreTazUOs()
    {
        // LayerOrder.cs, by Layer value (Layers.cs): 23 layers per facing, each once, no backpack/mount/shop layers.
        Assert.Equal(8, MobileLook.UsedLayers.Length);
        foreach (byte[] order in MobileLook.UsedLayers)
        {
            Assert.Equal(23, order.Length);
            Assert.Equal(23, order.Distinct().Count());
            Assert.All(order, s => Assert.InRange(s, (byte)1, (byte)24));
            Assert.DoesNotContain((byte)21, order); // backpack
        }
        Assert.Equal(Cloak, MobileLook.UsedLayers[0][22]);             // north: last of all
        Assert.Equal(Cloak, MobileLook.UsedLayers[3][0]);              // facing the viewer: first
        Assert.Equal(new byte[] { OneHanded, Cloak, Helmet, TwoHanded }, MobileLook.UsedLayers[1][19..]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void TheCloakIsUnderTheClothesOnlyFacingTheViewer(int direction, bool under)
    {
        foreach (bool mounted in new[] { false, true })
        {
            List<MobileLook.Layer> order = Dressed(mounted).DrawOrder(direction);
            int body = order.FindIndex(l => l.Slot == 0 && !l.IsMount), cloak = IndexOf(order, Cloak);
            // The body (and the mount under it) always come first: the cloak is drawn over them either way.
            Assert.True(cloak > body);
            if (mounted)
                Assert.True(order[0].IsMount);
            foreach (byte other in new[] { Shirt, Robe, Hair })
                Assert.Equal(under, cloak < IndexOf(order, other));
        }
    }

    [Fact]
    public void NorthPutsTheCloakOverHelmetAndWeaponWestDoesNot()
    {
        // Facings 0 and 6 share a file direction (3) but not an order, so their frames are cached apart.
        List<MobileLook.Layer> north = Dressed(false).DrawOrder(0), west = Dressed(false).DrawOrder(6);
        Assert.True(IndexOf(north, Cloak) > IndexOf(north, Helmet));
        Assert.True(IndexOf(north, Cloak) > IndexOf(north, TwoHanded));
        Assert.True(IndexOf(west, Cloak) < IndexOf(west, Helmet));
        Assert.Equal(AnimationFile.FileDirection(0, out _), AnimationFile.FileDirection(6, out _));
        Assert.NotEqual(MobileLook.OrderClass(0), MobileLook.OrderClass(6));
    }

    [Fact]
    public void FacingsWithTheSameTableShareFrames()
    {
        Assert.Equal(new[] { 0, 1, 1, 3, 1, 1, 1, 1 }, Enumerable.Range(0, 8).Select(MobileLook.OrderClass).ToArray());
    }

    [Fact]
    public void TheHighlightKeepsTheLayersOrdered()
    {
        MobileLook lit = Dressed(true).Rehued(0x35);
        Assert.Equal(Dressed(true).DrawOrder(2).Select(l => l.Slot), lit.DrawOrder(2).Select(l => l.Slot));
        Assert.Equal(2, IndexOf(lit.DrawOrder(3), Cloak)); // mount, body, cloak
    }
}

/// <summary>A worn cloak from the install's tiledata, through <see cref="MobileLook.From"/>.</summary>
[Collection("UO install")]
public sealed class LayerOrderInstallTests
{
    private readonly UOData _data;

    public LayerOrderInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    /// <summary>The first wearable static on <paramref name="layer"/> with an animation.</summary>
    private int Wearable(byte layer)
    {
        StaticTileData[] statics = _data.TileData.Statics;
        for (int g = 0; g < statics.Length; g++)
            if (statics[g].Layer == layer && statics[g].AnimId != 0 && (statics[g].Flags & TileFlags.Wearable) != 0)
                return g;
        return -1;
    }

    [Fact]
    public void AWornCloakStacksByFacingOnFootAndMounted()
    {
        int cloak = Wearable(20), robe = Wearable(22);
        Assert.True(cloak > 0 && robe > 0);
        foreach (bool mounted in new[] { false, true })
        {
            var worn = new List<(byte, int, int)> { (20, cloak, 0x8021), (22, robe, 0) };
            if (mounted)
                worn.Add((25, 0x3EA2, 0));
            MobileLook look = MobileLook.From(_data, 400, 0x83EA, worn);
            Assert.Equal(mounted, look.Mounted);
            MobileLook.Layer worn20 = look.Layers.Single(l => l.Slot == 20);
            Assert.Equal(_data.TileData.GetStatic(cloak).AnimId, worn20.Body);
            Assert.Equal(0x0021, worn20.Hue);
            Assert.True(worn20.Partial); // 0x8000 in the hue
            Assert.Contains("@20;", look.Key);

            List<byte> front = look.DrawOrder(3).Select(l => l.Slot).ToList(), back = look.DrawOrder(7).Select(l => l.Slot).ToList();
            Assert.True(front.IndexOf(20) < front.IndexOf(22));
            Assert.True(back.IndexOf(20) > back.IndexOf(22));
            Assert.Equal(mounted, look.DrawOrder(3)[0].IsMount);
        }
    }
}
