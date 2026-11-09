using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>Which art lights, how, and when: TazUO GameScene.AddLight / LightColors / IsometricLight (see LightSources).</summary>
public sealed class ItemLightTests
{
    [Theory]
    [InlineData(0x0A0F, 5, 5, 30)]   // candle: its own id, orange (0x09FB-0x0A14)
    [InlineData(0x0A15, 3, 3, 0)]    // lantern: white (0x0A15-0x0A29)
    [InlineData(0x0DE3, 2, 2, 31)]   // campfire: orange small
    [InlineData(0x0E31, 1, 1, 40)]   // brazier: red
    [InlineData(0x0F6C, 1, 1, 2)]    // moongate: light blue
    [InlineData(0x398C, 1, 1, 31)]   // fire field
    [InlineData(0x3946, 1, 1, 6)]    // energy field
    [InlineData(0x3915, 9, 2, 1)]    // poison field: shape 2 whatever the id, green
    [InlineData(0x3E05, 9, 2, 1)]
    [InlineData(0x1234, 210, 1, 10)] // over 200: shape 1 in colour id - 200
    [InlineData(0x1234, 7, 7, 0)]    // not in the table: white
    public void Resolve(int graphic, int id, int shape, int color) => Assert.Equal((shape, color), LightSources.Resolve(graphic, id));

    [Fact]
    public void ShapesPastTheLoaderCountDrawNothing() => Assert.Null(LightSources.Resolve(0x1234, 150));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(26, 0, 26)]   // no personal light: as sent
    [InlineData(26, 20, 12)]  // Night Sight: max(20, 32 - 26) = 20 -> level 12
    [InlineData(26, 4, 26)]   // a weak personal light changes nothing
    [InlineData(10, 31, 1)]   // never full-bright from it alone
    public void EffectiveLevel(int overall, int personal, int level) => Assert.Equal(level, LightSources.EffectiveLevel(overall, personal));

    [Theory]
    [InlineData(0, 0, false)]  // day: no item lights
    [InlineData(26, 0, true)]
    [InlineData(26, 26, false)]
    [InlineData(26, 30, false)]
    public void LightsOnlyWhileTheLevelIsAboveThePersonalLight(int overall, int personal, bool on) =>
        Assert.Equal(on, LightSources.LightsOn(overall, personal));

    [Fact]
    public void TheNearestFewWithinRange()
    {
        var c = Enumerable.Range(0, 40).Select(i => (X: 100 + i, Y: 100)).ToList();
        var into = new List<(int X, int Y)>();
        LightSources.Nearest(c, p => p, 110, 100, 18, 16, into);
        Assert.Equal(16, into.Count);
        Assert.Equal((110, 100), into[0]);
        Assert.All(into, p => Assert.InRange(Math.Abs(p.X - 110), 0, 8));
        LightSources.Nearest(c, p => p, 500, 500, 18, 16, into);
        Assert.Empty(into); // nothing in range: no lights, the shaders' old path
    }

    [Fact]
    public void ColoursAreTheLoadersBaseColours()
    {
        Assert.Equal((1f, 127 / 255f, 0f), LightSources.Rgb(30));
        Assert.Equal((1f, 0f, 0f), LightSources.Rgb(40));
        Assert.Equal((1f, 1f, 1f), LightSources.Rgb(0));
    }
}

[Collection("UO install")]
public sealed class ItemLightInstallTests
{
    private readonly UOData _data;

    public ItemLightInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Theory]
    [InlineData(0x0A0F)] // candle
    [InlineData(0x0A12)] // torch
    [InlineData(0x09FB)] // wall sconce
    [InlineData(0x0DE3)] // campfire
    [InlineData(0x0F6C)] // moongate
    [InlineData(0x0A07)] // wall torch (Britain map statics)
    public void RealLightSourcesResolveToRealShapes(int graphic)
    {
        StaticTileData td = _data.TileData.GetStatic(graphic);
        Assert.True(LightSources.IsLight(td.Flags), $"0x{graphic:X4} LightSource");
        (int shape, int _)? r = LightSources.Resolve(graphic, td.Layer);
        Assert.NotNull(r);
        LightShape s = _data.LightShapes.Get(r.Value.shape);
        Assert.True(s.Exists, $"0x{graphic:X4} layer {td.Layer} -> shape {r.Value.shape}");
        Assert.InRange(s.Peak, 1, 31);
        Assert.InRange(s.Radius, 4f, 400f);
    }

    [Fact]
    public void TheShapesAreCentredRadialFalloffs()
    {
        // Each used shape's texels fall off from the centre; (1 - d/r)^2 at the peak is the radial fit the shaders use.
        // Measured: the mean absolute error of that fit per texel, in 0-31 units, over the shapes the audited lights use.
        LightShapes shapes = _data.LightShapes;
        Assert.True(shapes.Count >= 30);
        foreach (int id in new[] { 1, 2, 3, 5, 10 })
        {
            LightShape s = shapes.Get(id);
            if (!s.Exists)
                continue;
            double err = 0;
            int n = 0;
            float cx = (s.Width - 1) / 2f, cy = (s.Height - 1) / 2f;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / s.Radius;
                    double f = Math.Max(0, 1 - d);
                    err += Math.Abs(shapes.Texel(id, x, y) - s.Peak * f * f);
                    n++;
                }
            Assert.True(err / n < 6, $"shape {id}: mean |error| {err / n:F2} of 31");
            Assert.True(shapes.Texel(id, (int)cx, (int)cy) >= shapes.Texel(id, 0, (int)cy), $"shape {id} brightest at its centre");
        }
    }
}

/// <summary>
/// Every shader that lights through the shared item-light sum compiles its UO_ITEM_LIGHTS variant: without it the keyword
/// ItemLights enables finds no variant and that surface stays dark (the TEST water did, GPU acceptance 2026-11-06).
/// </summary>
public sealed class ItemLightShaderTests
{
    private static string Source(params string[] path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return File.ReadAllText(Path.Combine(new[] { dir.FullName }.Concat(path).ToArray())).Replace("\r\n", "\n");
        throw new DirectoryNotFoundException("repo root");
    }

    private const string Pragma = "#pragma multi_compile __ UO_ITEM_LIGHTS";

    [Theory]
    [InlineData("UOClient.Unity", "Runtime", "Resources", "WorldSprite.shader")]
    [InlineData("LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareTerrain.shader")]
    [InlineData("LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareWater.shader")]
    public void ItemLitShadersCompileTheVariant(params string[] path)
    {
        string shader = Source(new[] { "Assets" }.Concat(path).ToArray());
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(shader, System.Text.RegularExpressions.Regex.Escape(Pragma)));
    }

    [Fact]
    public void TheWaterTakesThemThroughTheSharedLocalTerm()
    {
        string water = Source("Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareWater.shader");
        string include = Source("Assets", "LiveGroundCompare", "Shaders", "LiveGroundLight.cginc");
        Assert.Contains("#include \"../LiveGroundLight.cginc\"", water);
        Assert.Contains("* LGLitShadowed(n, visibility, i.world);", water);                  // ambient x shade + LGLocalLight x shade
        // Under UO_ITEM_LIGHTS, at the terrain's own tile so walls stop the lights (ItemLights.Trace).
        Assert.Contains("return player + UOItemLightAddAt(unityXY, float2(terrainWorld.x, -terrainWorld.z), _UOAmbient.rgb + player);", include);
        Assert.DoesNotContain("UOItemLightAdd", water);                                         // no second, water-only light term
    }
}
