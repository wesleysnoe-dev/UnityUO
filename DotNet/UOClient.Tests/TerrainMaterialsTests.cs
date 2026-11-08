using System;
using UOClient.LiveGroundCompare;

namespace UOClient.Tests;

/// <summary>Natural terrain pilot: land tiles to materials, and the blended splat weights.</summary>
public class TerrainMaterialsTests
{
    [Theory]
    [InlineData("grass", TerrainMaterials.Grass)]
    [InlineData("decimated_grass", TerrainMaterials.Grass)]
    [InlineData("embank", TerrainMaterials.Dirt)]
    [InlineData("tree", TerrainMaterials.Forest)]
    [InlineData("sand stone", TerrainMaterials.Sand)]
    [InlineData("snow grass path", TerrainMaterials.Snow)]
    [InlineData("cobblestones", TerrainMaterials.Cobble)]
    [InlineData("wooden floor", TerrainMaterials.Art)]
    [InlineData("noname", TerrainMaterials.Art)]
    [InlineData(null, TerrainMaterials.Art)]
    public void Classify(string name, int expected) => Assert.Equal(expected, TerrainMaterials.Classify(name));

    private static int Weight(byte[][] images, int sx, int sy, int material)
    {
        int s = SplatBuilder.Size;
        return images[material / 4][((s - 1 - sy) * s + sx) * 4 + material % 4];
    }

    [Fact]
    public void Splat_OneMaterialEverywhereIsFullWeight()
    {
        byte[][] img = SplatBuilder.Build(1600, 1600, (_, _) => TerrainMaterials.Sand);
        for (int sy = 0; sy < SplatBuilder.Size; sy += 7)
            for (int sx = 0; sx < SplatBuilder.Size; sx += 7)
            {
                Assert.Equal(255, Weight(img, sx, sy, TerrainMaterials.Sand));
                Assert.Equal(0, Weight(img, sx, sy, TerrainMaterials.Grass));
            }
    }

    [Fact]
    public void Splat_BordersBlendAndWanderButFarSamplesArePure()
    {
        // Grass west of x = 1608, dirt east of it.
        byte[][] img = SplatBuilder.Build(1600, 1600, (x, _) => x < 1608 ? TerrainMaterials.Grass : TerrainMaterials.Dirt);
        int r = SplatBuilder.SamplesPerTile;
        Assert.Equal(255, Weight(img, 1 * r, 5 * r, TerrainMaterials.Grass)); // 7 tiles from the border
        Assert.Equal(255, Weight(img, 14 * r, 5 * r, TerrainMaterials.Dirt));
        int mixed = 0, borderX = 8 * r;
        var crossings = new System.Collections.Generic.HashSet<int>();
        for (int sy = 0; sy < SplatBuilder.Size; sy++)
        {
            int g = Weight(img, borderX, sy, TerrainMaterials.Grass);
            if (g > 20 && g < 235)
                mixed++;
            // Where along the row the grass weight drops below half: should vary with y (the border wanders).
            for (int sx = 0; sx < SplatBuilder.Size; sx++)
                if (Weight(img, sx, sy, TerrainMaterials.Grass) < 128)
                {
                    crossings.Add(sx);
                    break;
                }
        }
        Assert.True(mixed > SplatBuilder.Size / 4, $"only {mixed} blended samples on the border");
        Assert.True(crossings.Count >= 3, $"border is straight: {string.Join(",", crossings)}");
    }

    [Fact]
    public void Splat_RoadsKeepAFirmEdge()
    {
        byte[][] img = SplatBuilder.Build(1600, 1600, (x, _) => x < 1608 ? TerrainMaterials.Grass : TerrainMaterials.Cobble);
        int r = SplatBuilder.SamplesPerTile;
        // Half a tile into the road there is no grass at all (cobble's softness is 0.22 tiles).
        for (int sy = 0; sy < SplatBuilder.Size; sy++)
            Assert.Equal(0, Weight(img, 8 * r + r / 2, sy, TerrainMaterials.Grass));
    }

    [Fact]
    public void Splat_IsTheSameOnBothSidesOfAChunkBorder()
    {
        Func<int, int, int> map = (x, y) => (x / 3 + y / 5) % 2 == 0 ? TerrainMaterials.Grass : TerrainMaterials.Forest;
        // The noise is world-positioned: the same world sample gives the same weights whichever chunk asks.
        Assert.Equal(SplatBuilder.Noise(1615.3f, 1601.7f, 11), SplatBuilder.Noise(1615.3f, 1601.7f, 11));
        byte[][] a = SplatBuilder.Build(1600, 1600, map), b = SplatBuilder.Build(1616, 1600, map);
        int last = SplatBuilder.Size - 1;
        int diff = 0;
        for (int sy = 0; sy < SplatBuilder.Size; sy++)
            diff += Math.Abs(Weight(a, last, sy, TerrainMaterials.Grass) - Weight(b, 0, sy, TerrainMaterials.Grass));
        Assert.True(diff / SplatBuilder.Size < 60, $"mean jump {diff / SplatBuilder.Size} across the border"); // neighbouring samples, a quarter tile apart
    }
}
