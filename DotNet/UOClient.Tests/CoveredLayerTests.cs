using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>Worn pieces another piece hides (TazUO's MobileView.IsCovered): hair under a hood, the torso under a robe.</summary>
public sealed class CoveredLayerTests
{
    private const byte Shoes = 3, Pants = 4, Helmet = 6, Hair = 11, Torso = 13, Tunic = 17, Arms = 19, Robe = 22, Skirt = 23, Legs = 24;

    private static Dictionary<byte, (int Graphic, int Hue)> Worn(params (byte Layer, int Graphic)[] items) =>
        items.ToDictionary(i => i.Layer, i => (i.Graphic, 0));

    [Theory]
    [InlineData(0x2683, true)]   // hooded shroud
    [InlineData(0x2687, true)]
    [InlineData(0x204E, true)]   // death shroud
    [InlineData(0x2FB9, true)]
    [InlineData(0x3173, true)]
    [InlineData(0x4B9D, true)]
    [InlineData(0x7816, true)]
    [InlineData(0x1F03, false)]  // a plain robe
    [InlineData(0x2682, false)]
    [InlineData(0x2688, false)]
    public void Hair_AndHelmet_HideUnderHoodedRobes(int robe, bool hidden)
    {
        var worn = Worn((Robe, robe), (Hair, 0x203B), (Helmet, 0x1412));
        Assert.Equal(hidden, MobileLook.IsCovered(worn, Hair));
        Assert.Equal(hidden, MobileLook.IsCovered(worn, Helmet));
    }

    [Fact]
    public void Hair_ShowsWithNoRobe() => Assert.False(MobileLook.IsCovered(Worn((Hair, 0x203B)), Hair));

    [Fact]
    public void Robe_HidesTorsoAndArms_ButNotOpenRobes()
    {
        Assert.True(MobileLook.IsCovered(Worn((Robe, 0x1F03), (Torso, 0x1415), (Arms, 0x1410)), Torso));
        Assert.True(MobileLook.IsCovered(Worn((Robe, 0x1F03), (Arms, 0x1410)), Arms));
        Assert.False(MobileLook.IsCovered(Worn((Robe, 0x9985), (Torso, 0x1415)), Torso));
        Assert.False(MobileLook.IsCovered(Worn((Robe, 0xA2CA), (Arms, 0x1410)), Arms));
        Assert.False(MobileLook.IsCovered(Worn((Torso, 0x1415)), Torso));
    }

    [Fact]
    public void LegArmour_HidesShoesAndPants()
    {
        var worn = Worn((Legs, 0x1411), (Shoes, 0x170B), (Pants, 0x152E));
        Assert.True(MobileLook.IsCovered(worn, Shoes));
        Assert.True(MobileLook.IsCovered(worn, Pants));
        Assert.False(MobileLook.IsCovered(Worn((Shoes, 0x170B), (Pants, 0x152E)), Shoes));
    }

    [Fact]
    public void Tunic0x238_HidesUnderAClosedRobe()
    {
        Assert.True(MobileLook.IsCovered(Worn((Tunic, 0x0238), (Robe, 0x1F03)), Tunic));
        Assert.False(MobileLook.IsCovered(Worn((Tunic, 0x0238), (Robe, 0x9986)), Tunic));
        Assert.False(MobileLook.IsCovered(Worn((Tunic, 0x1FA1), (Robe, 0x1F03)), Tunic));
    }

    [Fact]
    public void SkirtOverCertainPants_HidesThePants()
    {
        Assert.True(MobileLook.IsCovered(Worn((Pants, 0x01EB), (Skirt, 0x1516)), Pants));
        Assert.False(MobileLook.IsCovered(Worn((Pants, 0x01EB), (Skirt, 0x01C7)), Pants));
    }
}
