using UOClient.ArtPipeline;
using UOClient.IO.Formats;

namespace UOClient.Tests;

/// <summary>Which statics lean in the wind and take the season (PlantKind), by tiledata as UO's forests have it.</summary>
public class PlantKindTests
{
    private static StaticTileData Td(string name, TileFlags flags, byte height) => new StaticTileData { Name = name, Flags = flags, Height = height };

    [Theory]
    [InlineData("leaves", TileFlags.Impassable | TileFlags.Foliage, 2, PlantKind.Leafy + PlantKind.CanopyWeight)]
    [InlineData("cedar needles", TileFlags.Impassable | TileFlags.Foliage, 2, PlantKind.Evergreen + PlantKind.CanopyWeight)]
    [InlineData("walnut leaves", TileFlags.Impassable | TileFlags.Foliage, 2, PlantKind.Leafy + PlantKind.CanopyWeight)]
    [InlineData("oak tree", TileFlags.Background | TileFlags.Impassable, 20, PlantKind.Woody + PlantKind.TrunkWeight)]
    [InlineData("o'hii tree", TileFlags.Impassable, 20, PlantKind.Leafy + PlantKind.PalmWeight)]
    [InlineData("grasses", TileFlags.Background, 0, PlantKind.Leafy + PlantKind.SmallWeight)]
    [InlineData("flowers", TileFlags.Background, 0, PlantKind.Leafy + PlantKind.SmallWeight)]
    [InlineData("sapling", TileFlags.Background, 6, PlantKind.Leafy + PlantKind.SmallWeight)]
    [InlineData("brambles", TileFlags.Damaging, 5, PlantKind.Leafy + PlantKind.SmallWeight)]
    public void Plants(string name, TileFlags flags, byte height, float expected) =>
        Assert.Equal(expected, PlantKind.Of(Td(name, flags, height)), 4);

    [Theory]
    [InlineData("fallen log", TileFlags.None, 1)]
    [InlineData("rock", TileFlags.None, 2)]
    [InlineData("wooden wall", TileFlags.Wall | TileFlags.Impassable, 20)]
    [InlineData("potted plant", TileFlags.None, 10)]       // indoors, no wind
    [InlineData("fence corner", TileFlags.None, 10)]       // not a plant (no "corn")
    [InlineData("flowers", TileFlags.Surface, 0)]          // a flowered floor tile is built
    [InlineData("tree", TileFlags.None, 3)]                // a tree's low stump part
    public void NotPlants(string name, TileFlags flags, byte height) =>
        Assert.Equal(PlantKind.None, PlantKind.Of(Td(name, flags, height)));

    [Fact]
    public void Foot_IsTheKindAlone()
    {
        Assert.Equal(PlantKind.Leafy, PlantKind.AtFoot(PlantKind.Leafy + PlantKind.CanopyWeight));
        Assert.Equal(PlantKind.Woody, PlantKind.AtFoot(PlantKind.Woody + PlantKind.TrunkWeight));
        Assert.Equal(0f, PlantKind.AtFoot(PlantKind.None));
    }
}
