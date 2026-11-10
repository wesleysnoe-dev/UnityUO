using UOClient.Core;

namespace UOClient.Tests;

/// <summary>A facet change clears the old facet's things (TazUO World.InternalMapChangeClear).</summary>
public class MapChangeTests
{
    [Fact]
    public void ChangingFacet_DropsTheOldFacetsItemsAndMobiles_KeepsThePlayer()
    {
        var s = new GameSession(new LoginSettings());
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x08, 0x00 }, true); // Felucca
        typeof(World).GetProperty(nameof(World.PlayerSerial))!.SetValue(s.World, 1u);
        s.World.Mobiles[1] = new Mobile { Serial = 1, X = 326, Y = 433 };
        s.World.Mobiles[2] = new Mobile { Serial = 2, X = 330, Y = 433 };
        s.World.Items[0x40000001] = new WorldItem { Serial = 0x40000001, Graphic = 0x1DBC, X = 326, Y = 433 };
        var removed = new System.Collections.Generic.List<uint>();
        s.World.EntityRemoved += removed.Add;

        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x08, 0x03 }, true); // Malas

        Assert.Equal(3, s.World.Map);
        Assert.Empty(s.World.Items);
        Assert.Single(s.World.Mobiles);
        Assert.True(s.World.Mobiles.ContainsKey(1));
        Assert.Contains(2u, removed);
        Assert.Contains(0x40000001u, removed);
    }

    [Fact]
    public void TheSameFacetAgain_ClearsNothing()
    {
        var s = new GameSession(new LoginSettings());
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x08, 0x03 }, true);
        s.World.Items[0x40000001] = new WorldItem { Serial = 0x40000001, X = 1, Y = 1 };
        s.Inject(new byte[] { 0xBF, 0x00, 0x06, 0x00, 0x08, 0x03 }, true);
        Assert.Single(s.World.Items);
    }
}
