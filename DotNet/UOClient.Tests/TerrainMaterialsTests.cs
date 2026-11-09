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
    [InlineData("cobblestones", TerrainMaterials.Art)] // town paving keeps UO's tile art
    [InlineData("flagstone", TerrainMaterials.Art)]
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

/// <summary>Natural terrain: smooth heights, the water bed and the flat water surface with its skirt.</summary>
public class NaturalHeightsTests
{
    [Fact]
    public void SmoothZ_PassesThroughEveryCornerAndNeverOvershootsACliff()
    {
        Func<int, int, float> cliff = (x, _) => x < 10 ? 0f : 40f; // a 40-Z cliff between corners 9 and 10
        for (int x = 7; x < 13; x++)
            Assert.Equal(cliff(x, 0), TerrainLandBuilder.SmoothZ(cliff, x, 5), 4);
        for (float x = 7; x < 13; x += 0.25f)
        {
            float z = TerrainLandBuilder.SmoothZ(cliff, x, 5.5f);
            Assert.InRange(z, 0f, 40f);
            if (x < 9) Assert.Equal(0f, z, 4);   // flat ground next to the cliff stays flat (clamped)
            if (x >= 10) Assert.Equal(40f, z, 4);
        }
        // A gentle slope is curved, not the flat tile triangles: midway it differs from the straight average.
        Func<int, int, float> bowl = (x, y) => (x - 10) * (x - 10) + (y - 10) * (y - 10);
        float mid = TerrainLandBuilder.SmoothZ(bowl, 12.5f, 10f);
        Assert.InRange(mid, 6.2f, 6.3f); // the parabola's own value, 6.25 (linear would give 6.5)
    }

    [Fact]
    public void SmoothHeights_ChunksMeetExactlyAndUseFinerSamples()
    {
        Func<int, int, float> hills = (x, y) => (float)(10 * Math.Sin(x * 0.4) + 8 * Math.Cos(y * 0.3));
        TerrainLandData Chunk(int x0, int y0)
        {
            var b = new TerrainLandBuilder(x0, y0, 16);
            for (int i = 0; i < 16; i++)
                for (int j = 0; j < 16; j++)
                {
                    int x = x0 + i, y = y0 + j;
                    var it = LiveGroundProjection.LandItem(x, y, 3, (int)hills(x, y), (int)hills(x + 1, y), (int)hills(x + 1, y + 1), (int)hills(x, y + 1), hasTexmap: false);
                    b.AddTile(it, new UOClient.IO.Formats.Bitmap32(44, 44), false);
                }
            b.UseSmoothHeights(hills);
            return b.Finish();
        }
        TerrainLandData a = Chunk(1600, 1600), right = Chunk(1616, 1600);
        Assert.Equal(65, a.HeightmapResolution);
        Assert.Equal(64, a.Solid.GetLength(0));
        for (int k = 0; k < 65; k++)
            Assert.Equal(a.Heights[k, 64], right.Heights[k, 0], 6);
    }

    private static (bool, int) Sea(int x, int y) => x >= 20 ? (true, -5) : (false, 0); // sea east of x = 20

    [Fact]
    public void WaterBed_OpenWaterSinksBanksDipAndDryLandStays()
    {
        Assert.Equal(-5f - WaterBed.Depth, WaterBed.Corner(-5, Sea, 25, 5));        // all four tiles sea
        Assert.Equal(-5f - WaterBed.BankDip, WaterBed.Corner(-5, Sea, 20, 5));      // corner on the shoreline
        Assert.Equal(-5f - WaterBed.BankDip, WaterBed.Corner(3, Sea, 20, 5));       // a higher bank corner dips too
        Assert.Equal(3f, WaterBed.Corner(3, Sea, 18, 5));                           // inland: the map's height
        Assert.Equal(-20f, WaterBed.Corner(-20, Sea, 25, 5));                       // never raised
    }

    [Fact]
    public void FlatWater_IsLevelAndReachesOneTileOverTheBank()
    {
        var wb = new WaterSurfaceBuilder(16, 0);
        for (int j = 0; j < 16; j++)
            for (int i = 0; i < 16; i++)
            {
                int x = 16 + i;
                bool w = x >= 20;
                var it = LiveGroundProjection.LandItem(x, j, (ushort)(w ? 0xA8 : 3), w ? -5 : 2, w ? -5 : 2, w ? -5 : 2, w ? -5 : 2, hasTexmap: false);
                var art = new UOClient.IO.Formats.Bitmap32(44, 44);
                for (int p = 0; p < art.Pixels.Length; p++) art.Pixels[p] = 0xFF204080u;
                wb.AddTile(it, art, false, w);
            }
        wb.UseFlatLevel(Sea);
        WaterSurfaceData d = wb.Finish();
        (float _, float level, float _) = TerrainLandBuilder.TerrainPoint(0, 0, -5f - WaterBed.SurfaceDrop);
        Assert.All(d.Y, y => Assert.Equal(level, y, 4));            // one flat level, not the corner heights
        int minX = int.MaxValue;
        for (int v = 0; v < d.VertexCount; v++)
            minX = Math.Min(minX, (int)Math.Round(d.X[v]));
        Assert.Equal(19, minX);                                    // the skirt reaches one tile onto the bank (x 19..20)
        Assert.Equal(12 * 16 * 6 + 16 * 6, d.Indices.Length);      // 12 water columns + 1 skirt column
    }
}

public class WaterByDepthTests
{
    private static (bool, int) Sea(int x, int y) => x >= 20 ? (true, -5) : (false, 0);

    [Fact]
    public void ByDepth_ShoreValueIsTheWaterDepthAndDryCellsAreLeftOut()
    {
        // Ground: a smooth beach rising from the bed (-13) at x = 24 to +3 at x = 16.
        Func<float, float, float> ground = (x, _) => Math.Max(-13f, Math.Min(3f, 3f - (x - 16f) * 2f));
        var wb = new WaterSurfaceBuilder(16, 0);
        var art = new UOClient.IO.Formats.Bitmap32(44, 44);
        for (int p = 0; p < art.Pixels.Length; p++) art.Pixels[p] = 0xFF204080u;
        for (int j = 0; j < 16; j++)
            for (int i = 0; i < 16; i++)
            {
                int x = 16 + i;
                bool w = x >= 20;
                wb.AddTile(LiveGroundProjection.LandItem(x, j, (ushort)(w ? 0xA8 : 3), -5, -5, -5, -5, hasTexmap: false), art, false, w);
            }
        wb.UseFlatLevel(Sea);
        wb.UseGround(ground);
        WaterSurfaceData d = wb.Finish();
        float level = -5f - WaterBed.SurfaceDrop;
        for (int v = 0; v < d.VertexCount; v++)
        {
            float depth = level - ground(d.X[v], -d.Z[v]);
            Assert.Equal(Math.Clamp(depth / WaterSurfaceBuilder.OpenDepth, 0f, 1f), d.Shore[v], 4);
        }
        // The shoreline (depth 0) falls inside a tile, at x = 20.2: the mesh's vertices step by a quarter tile there.
        Assert.Contains(d.X, px => Math.Abs(px - 20.25f) < 1e-4);
        Assert.True(d.Indices.Length > 0);
        // No cell lies wholly on dry ground (x < 20.2).
        for (int t = 0; t < d.Indices.Length; t += 6)
            Assert.True(Math.Max(d.X[d.Indices[t + 1]], d.X[d.Indices[t]]) > 20.2f - 0.25f);
    }
}
