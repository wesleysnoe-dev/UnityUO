using System;
using System.Linq;
using UOClient.IO;
using UOClient.WorldLayer;

namespace UOClient.Tests;

/// <summary>World editor step 1 (WORLD_EDITOR.md): regions, the store, brushes, undo, derivation and the wire format.</summary>
public class WorldLayerTests
{
    // A test map: gentle hills, grass west of x = 40 and dirt east of it.
    private static int MapZ(int x, int y) => (int)Math.Round(6 * Math.Sin(x * 0.2) + 4 * Math.Cos(y * 0.15));
    private static byte MapMaterial(int x, int y) => x < 40 ? WorldMaterials.Grass : WorldMaterials.Dirt;

    private static WorldStore Store() => new WorldStore(0, (rx, ry) => rx < 0 || ry < 0 ? null : WorldInit.FromMap(0, rx, ry, MapZ, MapMaterial));

    [Fact]
    public void Init_HeightsPassThroughTheMapsCornersExactly()
    {
        WorldStore store = Store();
        const int s = WorldRegion.SamplesPerTile;
        for (int x = 0; x < 70; x += 7)
            for (int y = 0; y < 70; y += 5)
                Assert.Equal(MapZ(x, y) * WorldRegion.ZScale, store.GetHeight(x * s, y * s));
    }

    [Fact]
    public void Init_MaterialsBlendAtBordersAndArePureFarAway()
    {
        WorldStore store = Store();
        var w = new float[WorldMaterials.Count];
        const int s = WorldRegion.SamplesPerTile;
        store.GetWeights(10 * s + 2, 10 * s + 2, w);
        Assert.Equal(1f, w[WorldMaterials.Grass], 2);
        store.GetWeights(60 * s + 2, 10 * s + 2, w);
        Assert.Equal(1f, w[WorldMaterials.Dirt], 2);
        int mixed = Enumerable.Range(0, 64).Count(sy =>
        {
            store.GetWeights(40 * s, sy, w);
            return w[WorldMaterials.Grass] > 0.05f && w[WorldMaterials.Dirt] > 0.05f;
        });
        Assert.True(mixed > 16, $"only {mixed} blended samples on the border");
    }

    [Fact]
    public void Region_WeightsAlwaysSumTo255AndKeepTheHeaviestFour()
    {
        var r = new WorldRegion(0, 0, 0);
        r.SetWeights(5, new[] { 0.1f, 0.5f, 0.05f, 0.2f, 0.15f, 0f, 0f, 0f, 0f });
        int o = 5 * WorldRegion.Slots;
        Assert.Equal(255, r.MatW.Skip(o).Take(4).Sum(b => b));
        Assert.DoesNotContain((byte)2, r.MatId.Skip(o).Take(4).Where((_, k) => r.MatW[o + k] > 0)); // the lightest (0.05) dropped
        Assert.Equal(1, r.Dominant(5));
    }

    [Fact]
    public void Region_SerializeRoundTrips()
    {
        WorldRegion a = WorldInit.FromMap(0, 1, 2, MapZ, MapMaterial);
        a.Version = 7;
        a.Edited[123] = true;
        WorldRegion b = WorldRegion.Deserialize(a.Serialize());
        Assert.Equal((a.Map, a.Rx, a.Ry, a.Version), (b.Map, b.Rx, b.Ry, b.Version));
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.MatId, b.MatId);
        Assert.Equal(a.MatW, b.MatW);
        Assert.Equal(a.Edited, b.Edited);
        Assert.True(a.Serialize().Length < 300_000, $"{a.Serialize().Length} bytes compressed");
    }

    [Fact]
    public void Store_EdgeSamplesStayTheSameInBothRegions()
    {
        WorldStore store = Store();
        int edge = WorldRegion.SamplesPerRegion; // sample 256: last of region 0, first of region 1
        store.SetHeight(edge, 100, 555);
        Assert.Equal(555, store.Find(0, 0)!.Height[WorldRegion.Index(WorldRegion.N - 1, 100)]);
        Assert.Equal(555, store.Find(1, 0)!.Height[WorldRegion.Index(0, 100)]);
        store.SetHeight(edge, edge, 321); // a corner shared by four regions
        foreach ((int rx, int ry, int lx, int ly) in new[] { (0, 0, 256, 256), (1, 0, 0, 256), (0, 1, 256, 0), (1, 1, 0, 0) })
            Assert.Equal(321, store.Find(rx, ry)!.Height[WorldRegion.Index(lx, ly)]);
    }

    [Fact]
    public void Brush_RaiseFallsOffToTheRadiusAndMarksTiles()
    {
        WorldStore store = Store();
        const int s = WorldRegion.SamplesPerTile;
        short centre = store.GetHeight(20 * s, 20 * s), outside = store.GetHeight(26 * s, 20 * s);
        EditStep step = WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Raise, X = 20, Y = 20, Radius = 4, Softness = 0.5f, Strength = 1 })!;
        Assert.Equal(centre + WorldBrush.RaisePerStroke * WorldRegion.ZScale, store.GetHeight(20 * s, 20 * s));
        Assert.Equal(outside, store.GetHeight(26 * s, 20 * s));
        short half = store.GetHeight(23 * s, 20 * s); // inside the soft band: raised, but less
        Assert.InRange(half - (short)WorldInit.FromMap(0, 0, 0, MapZ, MapMaterial).Height[WorldRegion.Index(23 * s, 20 * s)], 1, 15);
        Assert.Contains((20, 20), step.Tiles);
        Assert.True(store.Find(0, 0)!.Edited[20 * WorldRegion.Tiles + 20]);
        Assert.False(store.Find(0, 0)!.Edited[40 * WorldRegion.Tiles + 40]);
    }

    [Fact]
    public void Brush_FlattenSmoothAndPaint()
    {
        WorldStore store = Store();
        const int s = WorldRegion.SamplesPerTile;
        for (int k = 0; k < 20; k++)
            WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Flatten, X = 30, Y = 30, Radius = 3, Softness = 0, Strength = 0.5f, TargetZ = 10 });
        Assert.InRange(store.GetHeight(30 * s, 30 * s), 10 * WorldRegion.ZScale - 1, 10 * WorldRegion.ZScale + 1);

        store.SetHeight(50 * s, 50 * s, 400); // a spike
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Smooth, X = 50, Y = 50, Radius = 1, Softness = 0, Strength = 1 });
        Assert.True(store.GetHeight(50 * s, 50 * s) < 200);

        var w = new float[WorldMaterials.Count];
        for (int k = 0; k < 6; k++)
            WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 10, Y = 10, Radius = 2, Softness = 0.3f, Strength = 0.6f, Material = WorldMaterials.Sand });
        store.GetWeights(10 * s, 10 * s, w);
        Assert.True(w[WorldMaterials.Sand] > 0.95f);
        store.GetWeights(10 * s + 7, 10 * s, w); // 1.75 tiles out: blended
        Assert.InRange(w[WorldMaterials.Sand], 0.05f, 0.95f);
    }

    [Fact]
    public void Undo_PutsEverySampleBackAndRedoReapplies()
    {
        WorldStore store = Store();
        const int s = WorldRegion.SamplesPerTile;
        short[] before = Enumerable.Range(0, 40).Select(i => store.GetHeight(15 * s + i, 15 * s)).ToArray();
        EditStep step = WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Raise, X = 18, Y = 15, Radius = 5, Softness = 0.4f, Strength = 1 })!;
        short[] after = Enumerable.Range(0, 40).Select(i => store.GetHeight(15 * s + i, 15 * s)).ToArray();
        Assert.NotEqual(before, after);
        WorldBrush.Restore(store, step, after: false);
        Assert.Equal(before, Enumerable.Range(0, 40).Select(i => store.GetHeight(15 * s + i, 15 * s)).ToArray());
        WorldBrush.Restore(store, step, after: true);
        Assert.Equal(after, Enumerable.Range(0, 40).Select(i => store.GetHeight(15 * s + i, 15 * s)).ToArray());
    }

    [Fact]
    public void Brush_IsDeterministic()
    {
        WorldStore a = Store(), b = Store();
        var stroke = new BrushStroke { Tool = BrushTool.Raise, X = 63.7f, Y = 12.2f, Radius = 3.3f, Softness = 0.7f, Strength = 0.8f };
        WorldBrush.Apply(a, stroke);
        WorldBrush.Apply(b, WorldProtocol.StrokeFrom(WorldProtocol.StrokeBytes(stroke), 0)); // through the wire format
        Assert.Equal(a.Find(0, 0)!.Height, b.Find(0, 0)!.Height);
        Assert.Equal(a.Find(1, 0)!.Height, b.Find(1, 0)!.Height);
    }

    [Fact]
    public void Derive_HeightFromTheTopCornerAndGroundFromThePaint()
    {
        WorldStore store = Store();
        int[][] plain = new int[WorldMaterials.Count][];
        for (int m = 0; m < plain.Length; m++)
            plain[m] = new[] { 100 + m * 10, 101 + m * 10 };
        // Untouched: the map's own Z, and the original graphic for its own material.
        (int g, int z) = WorldDerive.Tile(store, 12, 12, 0x0003, WorldMaterials.Grass, plain);
        Assert.Equal((0x0003, MapZ(12, 12)), (g, z));
        // Painted sand and raised: a plain sand graphic, the raised Z.
        for (int k = 0; k < 8; k++)
        {
            WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 12.5f, Y = 12.5f, Radius = 2, Softness = 0, Strength = 1, Material = WorldMaterials.Sand });
            WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Raise, X = 12, Y = 12, Radius = 2, Softness = 0, Strength = 1 });
        }
        (g, z) = WorldDerive.Tile(store, 12, 12, 0x0003, WorldMaterials.Grass, plain);
        Assert.Contains(g, plain[WorldMaterials.Sand]);
        Assert.Equal(MapZ(12, 12) + 8 * (int)WorldBrush.RaisePerStroke, z);
    }

    [Fact]
    public void Stroke_ClampsWhatTheServerAccepts()
    {
        BrushStroke c = new BrushStroke { Tool = (BrushTool)99, Radius = 500, Softness = 3, Strength = -1, Material = 200, TargetZ = 900 }.Clamped();
        Assert.Equal(BrushTool.Raise, c.Tool);
        Assert.Equal(BrushStroke.MaxRadius, c.Radius);
        Assert.Equal(1f, c.Softness);
        Assert.Equal(0f, c.Strength);
        Assert.Equal(WorldMaterials.Classic, c.Material);
        Assert.Equal(127f, c.TargetZ);
        Assert.Equal(WorldProtocol.StrokeLength, WorldProtocol.StrokeBytes(c).Length);
    }
}

/// <summary>The paint graphics against the real tiledata.</summary>
[Collection("UO install")]
public sealed class WorldLayerRealDataTests
{
    private readonly UOData _data;

    public WorldLayerRealDataTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void Plain_EveryMaterialHasGraphicsOfItsOwnName()
    {
        var land = _data.TileData.Land;
        int[][] plain = WorldMaterials.Plain(g => land[g].Name, land.Length);
        for (int m = 0; m < WorldMaterials.Count; m++)
        {
            if (m == WorldMaterials.Classic)
            {
                Assert.Empty(plain[m]);
                continue;
            }
            Assert.NotEmpty(plain[m]);
            Assert.All(plain[m], g => Assert.Equal(m, WorldMaterials.Classify(land[g].Name)));
        }
    }
}

/// <summary>The SURFACE palette: UO ground painted per tile.</summary>
public class WorldLayerGroundTests
{
    private static int MapZ(int x, int y) => 0;
    private static byte MapMaterial(int x, int y) => WorldMaterials.Grass;
    private static WorldStore Store() => new WorldStore(0, (rx, ry) => rx < 0 || ry < 0 ? null : WorldInit.FromMap(0, rx, ry, MapZ, MapMaterial));
    private static readonly int[][] Plain = Enumerable.Range(0, WorldMaterials.Count).Select(m => new[] { 100 + m }).ToArray();

    [Fact]
    public void Ground_LaysTheGraphicOnCoveredTilesAndDerivesIt()
    {
        WorldStore store = Store();
        EditStep step = WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 20.5f, Y = 20.5f, Radius = 2, Softness = 0.2f, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x0500 })!;
        Assert.Equal(0x0500, store.GetGround(20, 20));
        Assert.Equal(0x0500, store.GetGround(21, 21));
        Assert.Equal(0, store.GetGround(24, 20));            // outside the brush
        Assert.Contains(step.Ground, g => g.X == 20 && g.Y == 20 && g.After == 0x0500);
        Assert.Equal((0x0500, 0), WorldDerive.Tile(store, 20, 20, 0x0003, WorldMaterials.Grass, Plain));
        Assert.True(store.Find(0, 0)!.Edited[20 * WorldRegion.Tiles + 20]);
    }

    [Fact]
    public void Ground_NaturalPaintClearsItAndUndoBringsItBack()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 10.5f, Y = 10.5f, Radius = 1, Softness = 0, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x0501 });
        EditStep clear = WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 10.5f, Y = 10.5f, Radius = 1, Softness = 0, Strength = 1, Material = WorldMaterials.Sand })!;
        Assert.Equal(0, store.GetGround(10, 10));
        WorldBrush.Restore(store, clear, after: false);
        Assert.Equal(0x0501, store.GetGround(10, 10));
        WorldBrush.Restore(store, clear, after: true);
        Assert.Equal(0, store.GetGround(10, 10));
    }

    [Fact]
    public void Ground_TravelsInTheStrokeAndOnlyWithClassic()
    {
        var s = new BrushStroke { Tool = BrushTool.Paint, X = 1, Y = 2, Radius = 3, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x1234 };
        Assert.Equal(0x1234, WorldProtocol.StrokeFrom(WorldProtocol.StrokeBytes(s), 0).Ground);
        s.Material = WorldMaterials.Rock;
        Assert.Equal(0, s.Clamped().Ground);
    }

    [Fact]
    public void Region_GroundRoundTripsAndVersion1FilesStillLoad()
    {
        WorldRegion a = WorldInit.FromMap(0, 0, 0, MapZ, MapMaterial);
        a.Ground[77] = 0x0444;
        Assert.Equal(0x0444, WorldRegion.Deserialize(a.Serialize()).Ground[77]);

        // A version 1 file: the same bytes up to Edited, with the version field set to 1 and no Ground.
        byte[] v2;
        using (var z = new System.IO.Compression.DeflateStream(new System.IO.MemoryStream(a.Serialize()), System.IO.Compression.CompressionMode.Decompress))
        using (var raw = new System.IO.MemoryStream())
        {
            z.CopyTo(raw);
            v2 = raw.ToArray();
        }
        byte[] v1 = v2.Take(v2.Length - a.Ground.Length * 2).ToArray();
        BitConverter.GetBytes(1).CopyTo(v1, 4);
        using var packed = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, true))
            z.Write(v1, 0, v1.Length);
        WorldRegion old = WorldRegion.Deserialize(packed.ToArray());
        Assert.Equal(a.Height, old.Height);
        Assert.All(old.Ground, g => Assert.Equal(0, g));
    }
}

/// <summary>Brush shapes (circle, square, whole tiles) and the eraser.</summary>
public class WorldLayerShapeTests
{
    private static int MapZ(int x, int y) => (int)Math.Round(3 * Math.Sin(x * 0.3) + 2 * Math.Cos(y * 0.2));
    private static byte MapMaterial(int x, int y) => WorldMaterials.Grass;
    private static WorldStore Store() => new WorldStore(0, (rx, ry) => rx < 0 || ry < 0 ? null : WorldInit.FromMap(0, rx, ry, MapZ, MapMaterial));
    private const int S = WorldRegion.SamplesPerTile;

    [Fact]
    public void Square_ReachesTheCornersACircleMisses()
    {
        // (2.8, 2.8) from the centre: 3.96 away (outside a radius-3 circle), 2.8 along each axis (inside the square).
        WorldStore circle = Store(), square = Store();
        var stroke = new BrushStroke { Tool = BrushTool.Raise, X = 20, Y = 20, Radius = 3, Softness = 0, Strength = 1 };
        WorldBrush.Apply(circle, stroke);
        stroke.Shape = BrushShape.Square;
        WorldBrush.Apply(square, stroke);
        int sx = (int)Math.Round(22.8f * S), sy = (int)Math.Round(22.8f * S);
        Assert.Equal(Store().GetHeight(sx, sy), circle.GetHeight(sx, sy));
        Assert.NotEqual(Store().GetHeight(sx, sy), square.GetHeight(sx, sy));
    }

    [Theory]
    [InlineData(0.5f, 1)]
    [InlineData(1f, 2)]
    [InlineData(1.5f, 3)]
    public void Tile_FlattensExactlyTheFootprintsTiles(float radius, int n)
    {
        WorldStore store = Store();
        var stroke = new BrushStroke { Tool = BrushTool.Flatten, Shape = BrushShape.Tile, X = 30.4f, Y = 30.6f, Radius = radius, Softness = 0.9f, Strength = 1, TargetZ = 9 };
        (int x0, int y0, int fn) = stroke.TileFootprint();
        Assert.Equal(n, fn);
        WorldBrush.Apply(store, stroke);
        WorldStore pristine = Store();
        for (int sx = (x0 - 1) * S; sx <= (x0 + n + 1) * S; sx++)
            for (int sy = (y0 - 1) * S; sy <= (y0 + n + 1) * S; sy++)
            {
                bool inside = sx >= x0 * S && sy >= y0 * S && sx <= (x0 + n) * S && sy <= (y0 + n) * S;
                if (inside)
                    Assert.Equal(9 * WorldRegion.ZScale, store.GetHeight(sx, sy)); // hard edge: softness does not apply
                else
                    Assert.Equal(pristine.GetHeight(sx, sy), store.GetHeight(sx, sy));
            }
    }

    [Fact]
    public void Tile_PaintsGroundOnWholeTilesOnly()
    {
        WorldStore store = Store();
        var stroke = new BrushStroke { Tool = BrushTool.Paint, Shape = BrushShape.Tile, X = 10.9f, Y = 10.1f, Radius = 1, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x0600 };
        (int x0, int y0, int n) = stroke.TileFootprint();
        WorldBrush.Apply(store, stroke);
        for (int tx = x0 - 1; tx <= x0 + n; tx++)
            for (int ty = y0 - 1; ty <= y0 + n; ty++)
                Assert.Equal(tx >= x0 && ty >= y0 && tx < x0 + n && ty < y0 + n ? 0x0600 : 0, store.GetGround(tx, ty));
    }

    [Fact]
    public void Erase_PutsTheOriginalMapBack()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Raise, X = 15, Y = 15, Radius = 3, Strength = 1 });
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 15, Y = 15, Radius = 3, Strength = 1, Material = WorldMaterials.Snow });
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 15.5f, Y = 15.5f, Radius = 1, Softness = 0, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x0700 });
        Assert.Equal(0x0700, store.GetGround(15, 15));

        EditStep erase = WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Erase, X = 15, Y = 15, Radius = 4, Softness = 0, Strength = 1 })!;
        WorldStore pristine = Store();
        var w = new float[WorldMaterials.Count];
        var p = new float[WorldMaterials.Count];
        for (int sx = 12 * S; sx <= 18 * S; sx++)
            for (int sy = 12 * S; sy <= 18 * S; sy++)
            {
                Assert.Equal(pristine.GetHeight(sx, sy), store.GetHeight(sx, sy));
                store.GetWeights(sx, sy, w);
                pristine.GetWeights(sx, sy, p);
                for (int m = 0; m < w.Length; m++)
                    Assert.Equal(p[m], w[m], 2);
            }
        Assert.Equal(0, store.GetGround(15, 15));

        // Undo brings the painting back.
        WorldBrush.Restore(store, erase, after: false);
        Assert.Equal(0x0700, store.GetGround(15, 15));
        Assert.NotEqual(pristine.GetHeight(15 * S, 15 * S), store.GetHeight(15 * S, 15 * S));
    }

    [Fact]
    public void Erase_AtHalfStrengthMovesHalfwayAndKeepsPaintedGround()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Flatten, X = 15, Y = 15, Radius = 3, Softness = 0, Strength = 1, TargetZ = 20 });
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Paint, X = 15.5f, Y = 15.5f, Radius = 1, Softness = 0, Strength = 1, Material = WorldMaterials.Classic, Ground = 0x0701 });
        WorldBrush.Apply(store, new BrushStroke { Tool = BrushTool.Erase, X = 15, Y = 15, Radius = 3, Softness = 0, Strength = 0.4f });
        int original = Store().GetHeight(15 * S, 15 * S);
        Assert.Equal(20 * WorldRegion.ZScale + 0.4f * (original - 20 * WorldRegion.ZScale), store.GetHeight(15 * S, 15 * S), 0);
        Assert.Equal(0x0701, store.GetGround(15, 15)); // a light erase leaves painted UO ground alone
    }

    [Fact]
    public void ShapeAndEraser_TravelInTheStroke()
    {
        var s = new BrushStroke { Tool = BrushTool.Erase, Shape = BrushShape.Tile, X = 3, Y = 4, Radius = 1, Strength = 1 };
        BrushStroke back = WorldProtocol.StrokeFrom(WorldProtocol.StrokeBytes(s), 0);
        Assert.Equal(BrushTool.Erase, back.Tool);
        Assert.Equal(BrushShape.Tile, back.Shape);
        Assert.Equal(WorldProtocol.StrokeLength, WorldProtocol.StrokeBytes(s).Length);
        Assert.Equal(BrushShape.Circle, new BrushStroke { Shape = (BrushShape)9 }.Clamped().Shape);
    }
}

/// <summary>The slope (ramp) brush.</summary>
public class WorldLayerSlopeTests
{
    private static int MapZ(int x, int y) => 0;
    private static byte MapMaterial(int x, int y) => WorldMaterials.Grass;
    private static WorldStore Store() => new WorldStore(0, (rx, ry) => rx < 0 || ry < 0 ? null : WorldInit.FromMap(0, rx, ry, MapZ, MapMaterial));
    private const int S = WorldRegion.SamplesPerTile;

    private static BrushStroke Ramp(float radius = 2f, float softness = 0f) => new BrushStroke
    {
        Tool = BrushTool.Slope, X = 10, Y = 20, X2 = 30, Y2 = 20, Z1 = 0, Z2 = 20, Radius = radius, Softness = softness, Strength = 1
    };

    [Fact]
    public void Slope_RisesEvenlyFromOneEndToTheOther()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, Ramp());
        for (int x = 10; x <= 30; x += 5)
        {
            float expected = (x - 10) / 20f * 20f;
            Assert.Equal(expected, store.GetHeight(x * S, 20 * S) / (float)WorldRegion.ZScale, 1);
            Assert.Equal(expected, store.GetHeight(x * S, 21 * S) / (float)WorldRegion.ZScale, 1); // across its width, the same
        }
    }

    [Fact]
    public void Slope_LeavesTheGroundBeyondItsWidthAlone()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, Ramp(radius: 2f));
        Assert.Equal(0, store.GetHeight(20 * S, 23 * S)); // 3 tiles off the centre line
        Assert.Equal(0, store.GetHeight(34 * S, 20 * S)); // 4 tiles past the far end
        Assert.NotEqual(0, store.GetHeight(20 * S, 21 * S));
    }

    [Fact]
    public void Slope_SoftEdgesBlendIntoTheGroundBeside()
    {
        WorldStore store = Store();
        WorldBrush.Apply(store, Ramp(radius: 4f, softness: 1f));
        float centre = store.GetHeight(25 * S, 20 * S), side = store.GetHeight(25 * S, 23 * S);
        Assert.True(side > 0 && side < centre);
    }

    [Fact]
    public void Slope_TravelsInTheStrokeAndIsCapped()
    {
        var s = Ramp();
        BrushStroke back = WorldProtocol.StrokeFrom(WorldProtocol.StrokeBytes(s), 0);
        Assert.Equal(BrushTool.Slope, back.Tool);
        Assert.Equal((30f, 20f, 0f, 20f), (back.X2, back.Y2, back.Z1, back.Z2));
        Assert.Equal(WorldProtocol.StrokeLength, WorldProtocol.StrokeBytes(s).Length);
        s.X2 = 500;
        Assert.Equal(10 + BrushStroke.MaxSlopeLength, s.Clamped().X2, 3);
    }
}
