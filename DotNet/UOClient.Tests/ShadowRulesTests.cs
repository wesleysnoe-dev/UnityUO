using UOClient.IO.Formats;

namespace UOClient.Tests;

/// <summary>Which objects cast their own outline as a sun shadow (free-standing things) and which keep the ground's sun mask.</summary>
public class ShadowRulesTests
{
    private static StaticTileData Td(TileFlags flags, byte height) => new StaticTileData { Flags = flags, Height = height };

    [Theory]
    [InlineData(TileFlags.Impassable | TileFlags.Foliage, 20)] // a tree
    [InlineData(TileFlags.Impassable, 6)]                      // a rock, a post
    [InlineData(TileFlags.None, 12)]                           // a sign, a lamp
    [InlineData(TileFlags.Foliage, 4)]                         // a bush
    [InlineData(TileFlags.Background | TileFlags.Impassable, 20)] // UO's tree trunks are Background
    [InlineData(TileFlags.Impassable | TileFlags.Foliage, 2)]  // a tree's leaves: low in tiledata, tall art
    public void FreeStanding_CastsOutline(TileFlags flags, int height) => Assert.True(ShadowRules.CastsOutline(Td(flags, (byte)height)));

    [Theory]
    [InlineData(0, 40, true)]  // a signpost: height 0 in tiledata, 40 px of art
    [InlineData(2, 26, true)]  // a rock
    [InlineData(0, 12, false)] // a paper or a coin: flat in both
    [InlineData(1, 0, false)]  // art unknown: tiledata alone
    public void LowTiledata_CastsByArtHeight(int height, int art, bool casts) =>
        Assert.Equal(casts, ShadowRules.CastsOutline(Td(TileFlags.Transparent, (byte)height), art));

    [Theory]
    [InlineData(TileFlags.Wall | TileFlags.Impassable, 20)]
    [InlineData(TileFlags.Roof, 3)]
    [InlineData(TileFlags.Surface, 5)]
    [InlineData(TileFlags.Door, 20)]
    [InlineData(TileFlags.Window | TileFlags.Wall, 20)]
    [InlineData(TileFlags.Bridge, 5)]
    [InlineData(TileFlags.Wet, 10)]
    [InlineData(TileFlags.None, 2)] // flat: papers, rugs
    public void BuildingPartsWaterAndFlatThings_DoNot(TileFlags flags, int height) => Assert.False(ShadowRules.CastsOutline(Td(flags, (byte)height)));

    [Theory]
    [InlineData("stone wall")]
    [InlineData("wooden wall")]
    [InlineData("Wall Trim")]
    public void WallPiecesWithoutTheWallFlag_DoNot(string name) =>
        Assert.False(ShadowRules.CastsOutline(new StaticTileData { Flags = TileFlags.Impassable, Height = 20, Name = name }));

    [Fact]
    public void WalnutTrees_StillCast() =>
        Assert.True(ShadowRules.CastsOutline(new StaticTileData { Flags = TileFlags.Impassable | TileFlags.Background, Height = 20, Name = "walnut tree" }));

    [Fact]
    public void OutlineCasters_MakeNoColumn_OnlyWhileOn()
    {
        var tree = Td(TileFlags.Impassable | TileFlags.Foliage, 20);
        var wall = Td(TileFlags.Wall | TileFlags.Impassable, 20);
        Assert.True(UOClient.LiveGroundCompare.StaticOccluders.Casts(tree));
        try
        {
            UOClient.LiveGroundCompare.StaticOccluders.OutlineCasters = true;
            Assert.False(UOClient.LiveGroundCompare.StaticOccluders.Casts(tree));
            Assert.True(UOClient.LiveGroundCompare.StaticOccluders.Casts(wall));
        }
        finally
        {
            UOClient.LiveGroundCompare.StaticOccluders.OutlineCasters = false;
        }
    }
    [Theory]
    [InlineData(0, 1, 0)]   // the object reaches the art's bottom edge: as before
    [InlineData(18, 1, 18)] // a rock that stops 18 rows short: from its own lowest row
    [InlineData(60, 1, 22)] // a tree's leaves, high in the art: from the tile's centre, never higher
    [InlineData(60, 2, 44)] // the same at 2x art
    [InlineData(30, 2, 30)]
    public void BaseLift_ObjectsLowestRow_AtMostHalfATile(int empty, int scale, int lift) =>
        Assert.Equal(lift, ShadowRules.BaseLift(empty, scale));

    [Fact]
    public void EmptyRowsBelow_CountsRowsUnderTheLowestOpaquePixel()
    {
        var art = new uint[4 * 5];                 // top-down, 4 wide, 5 tall
        art[1 * 4 + 2] = 0xFF112233;               // opaque on row 1
        art[3 * 4 + 0] = 0x20FFFFFF;               // too faint to count (under the shadow shader's cutoff) on row 3
        Assert.Equal(3, ShadowRules.EmptyRowsBelow(art, 4, 5));
        art[4 * 4 + 3] = 0x80FFFFFF;
        Assert.Equal(0, ShadowRules.EmptyRowsBelow(art, 4, 5));
        Assert.Equal(5, ShadowRules.EmptyRowsBelow(new uint[20], 4, 5));
    }
}
