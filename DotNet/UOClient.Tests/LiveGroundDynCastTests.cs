using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.LiveGroundCompare;

namespace UOClient.Tests;

/// <summary>
/// LiveGroundCompare (TEST only) dynamic casting: mobiles/items as CPU columns in the sprite shadow ceiling
/// (DynamicShadow, ShadowCeilingCore), their movement/removal invalidation, the cross-chunk margin from loaded
/// neighbours, and the sun-only light rule. Unity-free; the Unity upload is SpriteShadowCeiling.
/// </summary>
public sealed class LiveGroundDynCastTests
{
    private const int T = TerrainLandBuilder.Tiles, N = LiveGroundLighting.ShadowSize;

    private static int[,] Flat(int z)
    {
        var c = new int[T + 1, T + 1];
        for (int i = 0; i <= T; i++)
            for (int j = 0; j <= T; j++)
                c[i, j] = z;
        return c;
    }

    private static int[,] Gentle()
    {
        var c = new int[T + 1, T + 1];
        for (int i = 0; i <= T; i++)
            for (int j = 0; j <= T; j++)
                c[i, j] = (i * 3 + j * 5) % 4; // 0..3 Z: real ground is rarely flat
        return c;
    }

    private static float[] Copy(float[] a) => (float[])a.Clone();

    [Theory]
    [InlineData(5, 6, 16)]
    [InlineData(1, 1, 16)]
    [InlineData(9, 12, 20)]
    [InlineData(3, 14, 5)]
    public void Stamp_IsExactlyAStaticColumnInCeilingAt(int i, int j, int height)
    {
        foreach (int[,] cornerZ in new[] { Flat(0), Gentle() })
        {
            const int x0 = 1424, y0 = 1712;
            int z = (int)Math.Round(TerrainLandBuilder.SampleZ(cornerZ, i + 0.5f, j + 0.5f));
            float[] stamped = LiveGroundLighting.ShadowCeiling(cornerZ, null);
            DynamicShadow.Stamp(stamped, x0, y0, x0 + i, y0 + j, z + height, height);
            int[,] occ = StaticOccluders.Empty();
            occ[i, j] = z + height;
            float[] reference = LiveGroundLighting.ShadowCeiling(cornerZ, occ);
            Assert.Equal(reference, stamped);
            Assert.NotEqual(LiveGroundLighting.ShadowCeiling(cornerZ, null), stamped);
        }
    }

    [Fact]
    public void Stamp_NeverShadesItsOwnTile_AndOnlyAwayFromTheSun()
    {
        float[] c = LiveGroundLighting.ShadowCeiling(Flat(0), null), before = Copy(c);
        const int i = 6, j = 6;
        DynamicShadow.Stamp(c, 0, 0, i, j, 16, 16);
        for (int r = 0; r < N; r++)
            for (int col = 0; col < N; col++)
            {
                int tx = col / 4, ty = r / 4;
                if (c[r * N + col] == before[r * N + col])
                    continue;
                Assert.False(tx == i && ty == j, "own tile shaded");
                Assert.True(tx >= i && ty >= j, $"texel tile ({tx},{ty}) is toward the sun of ({i},{j})");
            }
        // The tile right behind it (+x, away from the sun) is shaded at the feet: ceiling above the ground there.
        Assert.True(LiveGroundLighting.SampleCeiling(c, i + 1.5f, j + 0.5f) > 8f);
    }

    [Fact]
    public void FootTile_IsTheTileTheFeetAreDrawnIn()
    {
        foreach ((int x, int y, int z) in new[] { (100, 200, 0), (1430, 1720, 20), (5, 5, -10) })
        {
            (float ax, float ay) = SpriteShadow.TileAnchor(x, y, z, 2);
            Assert.Equal((x, y), DynamicShadow.FootTile(ax, ay, z, 2));
        }
        // Mid-step east (x+1): the anchor halfway between two tiles' centres lands on one of them, never elsewhere.
        (float a0, float b0) = SpriteShadow.TileAnchor(10, 10, 0, 1);
        (float a1, float b1) = SpriteShadow.TileAnchor(11, 10, 0, 1);
        (int mx, int my) = DynamicShadow.FootTile((a0 + a1) / 2f, (b0 + b1) / 2f, 0f, 1);
        Assert.True((mx, my) == (10, 10) || (mx, my) == (11, 10), $"{mx},{my}");
    }

    [Fact]
    public void Footprint_IsBoundedByTheColumnsHeight()
    {
        (int x0, int y0, int x1, int y1) = DynamicShadow.Footprint(10, 20, DynamicShadow.MobileHeight);
        Assert.Equal((10, 20), (x0, y0));
        Assert.InRange(x1 - x0, 2, 4); // a 16 Z column reaches ~2.5 tiles of ground
        Assert.InRange(y1 - y0, 1, 2);
        (_, _, int tx1, _) = DynamicShadow.Footprint(10, 20, 500);
        Assert.True(tx1 - 10 <= 1 + (int)Math.Ceiling(DynamicShadow.MaxReachTiles));
    }

    private static ShadowCeilingCore CoreWith(params (int X0, int Y0, int[,] CornerZ, int[,] Occ)[] chunks)
    {
        var core = new ShadowCeilingCore { MarginsPerFrame = 100 };
        foreach (var c in chunks)
            core.Add(c.X0, c.Y0, LiveGroundLighting.ShadowCeiling(c.CornerZ, c.Occ), c.CornerZ, c.Occ, c);
        core.Frame(new List<ShadowCeilingCore.Chunk>());
        return core;
    }

    private static float[] Composite(ShadowCeilingCore core, int x0, int y0) =>
        core.TryGet(x0, y0, out ShadowCeilingCore.Chunk c) ? Copy(c.Composite) : null;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Core_MoveAndRemove_InvalidateExactly_NoStaleShadow(bool mobileLayer)
    {
        int[,] ground = Gentle();
        ShadowCeilingCore core = CoreWith((0, 0, ground, null));
        float[] baseline = Composite(core, 0, 0);
        var mob = new object();
        var changed = new List<ShadowCeilingCore.Chunk>();

        core.Report(mob, 4, 5, 2, DynamicShadow.MobileHeight, mobileLayer);
        core.Frame(changed);
        Assert.Single(changed);
        float[] atA = Composite(core, 0, 0), expectA = Copy(baseline);
        DynamicShadow.Stamp(expectA, 0, 0, 4, 5, 2 + 16, 16);
        Assert.Equal(expectA, atA);

        // Same tile next frame: nothing is redone.
        changed.Clear();
        core.Report(mob, 4, 5, 2, DynamicShadow.MobileHeight, mobileLayer);
        core.Frame(changed);
        Assert.Empty(changed);
        Assert.Equal(0, core.LastDirty);

        // Moved one tile: exactly the new column, none of the old one.
        changed.Clear();
        core.Report(mob, 5, 5, 2, DynamicShadow.MobileHeight, mobileLayer);
        core.Frame(changed);
        float[] expectB = Copy(baseline);
        DynamicShadow.Stamp(expectB, 0, 0, 5, 5, 18, 16);
        Assert.Equal(expectB, Composite(core, 0, 0));

        // Not drawn this frame (logged out, out of range, disposed, hidden): swept, back to the static ceiling.
        changed.Clear();
        core.Frame(changed);
        Assert.Equal(1, core.LastSwept);
        Assert.Equal(0, core.CasterCount);
        Assert.Equal(baseline, Composite(core, 0, 0));
    }

    [Fact]
    public void Core_ACasterNearTheEdgeShadesTheEastNeighbourToo_AndOnlyTheChunksItTouches()
    {
        ShadowCeilingCore core = CoreWith((0, 0, Flat(0), null), (16, 0, Flat(0), null), (0, 16, Flat(0), null), (16, 16, Flat(0), null));
        float[] east0 = Composite(core, 16, 0), south0 = Composite(core, 0, 16);
        var changed = new List<ShadowCeilingCore.Chunk>();
        core.Report(new object(), 15, 3, 0, DynamicShadow.MobileHeight);
        core.Frame(changed);
        Assert.Equal(2, changed.Count); // (0,0) and (16,0); not the southern chunks
        Assert.NotEqual(east0, Composite(core, 16, 0));
        Assert.Equal(south0, Composite(core, 0, 16));
        float[] expect = Copy(east0);
        DynamicShadow.Stamp(expect, 16, 0, 15, 3, 16, 16);
        Assert.Equal(expect, Composite(core, 16, 0));
    }

    [Fact]
    public void Core_ItemAndMobileLayersCombineAsOneMax_AndAStepRestampsNoItem()
    {
        ShadowCeilingCore core = CoreWith((0, 0, Gentle(), null));
        float[] expect = Composite(core, 0, 0);
        var changed = new List<ShadowCeilingCore.Chunk>();
        object chest = new object(), tree = new object(), mob = new object();
        void Items()
        {
            core.Report(chest, 3, 4, 1, 6);
            core.Report(tree, 4, 8, 2, 30);
        }
        Items();
        core.Report(mob, 4, 4, 1, DynamicShadow.MobileHeight, mobile: true);
        core.Frame(changed);
        DynamicShadow.Stamp(expect, 0, 0, 3, 4, 7, 6);
        DynamicShadow.Stamp(expect, 0, 0, 4, 8, 32, 30);
        DynamicShadow.Stamp(expect, 0, 0, 4, 4, 17, 16);
        Assert.Equal(expect, Composite(core, 0, 0));
        Assert.Equal(3, core.CastingCount);

        Items();
        core.Report(mob, 5, 4, 1, DynamicShadow.MobileHeight, mobile: true);
        core.Frame(changed);
        Assert.InRange(core.LastStamped, 1, 200); // the mobile's footprint only, not the tree's ~400 texels
    }

    [Fact]
    public void Core_NonCastingItemsAreTrackedButStampNothing()
    {
        ShadowCeilingCore core = CoreWith((0, 0, Flat(0), null));
        float[] baseline = Composite(core, 0, 0);
        var changed = new List<ShadowCeilingCore.Chunk>();
        core.Report(new object(), 4, 4, 0, 0); // a coin: tiledata height below StaticOccluders.MinHeight
        core.Frame(changed);
        Assert.Equal(1, core.CasterCount);
        Assert.Equal(0, core.CastingCount);
        Assert.Empty(changed);
        Assert.Equal(baseline, Composite(core, 0, 0));
    }

    /// <summary>Brute force over the loaded chunks in world tiles: CeilingAt's ray, no chunk boundary, stops at unloaded ground.</summary>
    private static float WorldCeiling(Dictionary<(int, int), (int[,] Z, int[,] Occ)> loaded, float wx, float wy, int maxZ)
    {
        (float ux, float uy, float rise) = LiveGroundLighting.SunRay;
        int ox = (int)Math.Floor(wx), oy = (int)Math.Floor(wy);
        float ceiling = LiveGroundLighting.NoCeiling;
        for (float s = LiveGroundLighting.ShadowStep; ; s += LiveGroundLighting.ShadowStep)
        {
            float px = wx + ux * s, py = wy + uy * s;
            int cx = (int)Math.Floor(px / T) * T, cy = (int)Math.Floor(py / T) * T;
            if (!loaded.TryGetValue((cx, cy), out var c))
                return ceiling;
            float drop = rise * s;
            if (maxZ - drop <= ceiling)
                return ceiling;
            ceiling = Math.Max(ceiling, TerrainLandBuilder.SampleZ(c.Z, px - cx, py - cy) - drop);
            int i = Math.Min(T - 1, (int)(px - cx)), j = Math.Min(T - 1, (int)(py - cy));
            if (c.Occ != null && ((int)Math.Floor(px) != ox || (int)Math.Floor(py) != oy) && c.Occ[i, j] != StaticOccluders.None)
                ceiling = Math.Max(ceiling, c.Occ[i, j] - drop);
        }
    }

    [Fact]
    public void Margin_WestAndNorthNeighboursCastAcrossTheSeam_AsOneWorldWould()
    {
        // A tall building column in the west neighbour right at the seam, a hill in the north-west one.
        int[,] occW = StaticOccluders.Empty();
        occW[15, 6] = 40;
        occW[14, 9] = 30;
        int[,] zNW = Flat(0);
        for (int i = 10; i <= T; i++)
            for (int j = 10; j <= T; j++)
                zNW[i, j] = 35;
        var loaded = new Dictionary<(int, int), (int[,] Z, int[,] Occ)>
        {
            [(0, 0)] = (Flat(0), null), [(-16, 0)] = (Flat(0), occW), [(-16, -16)] = (zNW, null), [(0, -16)] = (Flat(0), null)
        };
        ShadowCeilingCore core = CoreWith(loaded.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value.Z, kv.Value.Occ)).ToArray());
        Assert.Equal(0, core.PendingMargins);
        Assert.True(core.TryGet(0, 0, out ShadowCeilingCore.Chunk c) && c.HasMargin);
        float[] own = LiveGroundLighting.ShadowCeiling(Flat(0), null);
        int differs = 0, mismatch = 0;
        for (int r = 0; r < N; r++)
            for (int col = 0; col < N; col++)
            {
                float expect = WorldCeiling(loaded, (col + 0.5f) / 4f, (r + 0.5f) / 4f, 40);
                if (Math.Abs(c.Base[r * N + col] - expect) > 1e-3f)
                    mismatch++;
                if (c.Base[r * N + col] > own[r * N + col] + 0.5f)
                    differs++;
            }
        Assert.Equal(0, mismatch);
        Assert.True(differs > 20, $"{differs} texels shaded across the seam");
    }

    [Fact]
    public void Margin_MissingNeighbourIsLit_ThenFillsIn_ThenGoesAwayWithIt_NoStaleSeam()
    {
        int[,] occW = StaticOccluders.Empty();
        occW[15, 6] = 40;
        var core = new ShadowCeilingCore { MarginsPerFrame = 100 };
        var changed = new List<ShadowCeilingCore.Chunk>();
        object me = new object(), west = new object();
        float[] own = LiveGroundLighting.ShadowCeiling(Flat(0), null);
        core.Add(0, 0, own, Flat(0), null, me);
        core.Frame(changed);
        Assert.True(core.TryGet(0, 0, out ShadowCeilingCore.Chunk c));
        Assert.False(c.HasMargin);
        Assert.Equal(own, c.Composite); // west not loaded: its rays are lit (fail-safe), nothing guessed

        core.Add(-16, 0, LiveGroundLighting.ShadowCeiling(Flat(0), occW), Flat(0), occW, west);
        core.Frame(changed);
        Assert.True(c.HasMargin);
        Assert.NotEqual(own, c.Composite);
        float seam = LiveGroundLighting.SampleCeiling(c.Composite, 0.5f, 6.5f);
        Assert.True(seam > 20f, $"seam ceiling {seam}");

        Assert.True(core.Remove(-16, 0, west));
        core.Frame(changed);
        Assert.False(c.HasMargin);
        Assert.Equal(own, c.Composite); // its shadow leaves with it
        Assert.False(core.Remove(0, 0, west)); // only the owner removes
    }

    [Fact]
    public void Margin_WorkIsBudgetedPerFrame_AndCounted()
    {
        var core = new ShadowCeilingCore { MarginsPerFrame = 2 };
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                core.Add(x * T, y * T, LiveGroundLighting.ShadowCeiling(Gentle(), null), Gentle(), null, (x, y));
        int pending = core.PendingMargins;
        Assert.Equal(9, pending);
        var changed = new List<ShadowCeilingCore.Chunk>();
        core.Frame(changed);
        Assert.Equal(2, core.LastMargins);
        Assert.Equal(pending - 2, core.PendingMargins);
        for (int k = 0; k < 10 && core.PendingMargins > 0; k++)
            core.Frame(changed);
        Assert.Equal(0, core.PendingMargins);
    }

    [Fact]
    public void Light_SunShadowDimsOnlyTheAmbient_NotThePlayersLight()
    {
        float shade = LiveGroundLighting.MinShade;
        // Day, no player light: as before (shade x ambient).
        Assert.Equal(1f * shade, LiveGroundLighting.Lit(1f, 0f, shade), 5);
        // Night: the player's light near the receiver is not dimmed by a sun that is not there.
        float night = 0.3f, lamp = 0.8f;
        Assert.Equal(night * shade + lamp, LiveGroundLighting.Lit(night, lamp, shade), 5);
        Assert.True(LiveGroundLighting.Lit(night, lamp, shade) > (night + lamp) * shade);
        // Lit: unchanged by the rule.
        Assert.Equal(night + lamp, LiveGroundLighting.Lit(night, lamp, 1f), 5);
    }
}

/// <summary>Source guards: the seams stay behind FootMetadata/ShadowCasters, cleanup restores everything, shaders mirror the CPU.</summary>
public sealed class LiveGroundDynCastSourceTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(path).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void CasterReports_AreOnlyInsideTheFootMetadataBranches()
    {
        string avatar = Source("Assets", "UOClient.Unity", "Runtime", "Avatar.cs");
        int foot = avatar.IndexOf("if (MeshBuilder.FootMetadata)", StringComparison.Ordinal);
        int report = avatar.IndexOf("MeshBuilder.ShadowCasters?.Mobile(this, anchor, footZ);", StringComparison.Ordinal);
        int elseAt = avatar.IndexOf("else\n                MeshBuilder.SetQuad(_mesh, r, _art.Atlas.Uv(f.Slot), LastDepth, mirror, LastCut);", StringComparison.Ordinal);
        Assert.True(foot > 0 && report > foot && report < elseAt, "Avatar reports inside the FootMetadata branch");
        string item = Source("Assets", "UOClient.Unity", "Runtime", "World", "ItemView.cs");
        int guard = item.IndexOf("if (!MeshBuilder.FootMetadata)", StringComparison.Ordinal);
        int ret = item.IndexOf("return;", guard, StringComparison.Ordinal);
        int itemReport = item.IndexOf("MeshBuilder.ShadowCasters?.Item(this, _item.X, _item.Y, _item.Z, _graphic);", StringComparison.Ordinal);
        Assert.True(guard > 0 && itemReport > ret, "ItemView reports after the default path returned");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(avatar + item, "ShadowCasters").Count);
    }

    [Fact]
    public void OnlyTheCeilingSetsTheSink_AndDisposeRestoresIt()
    {
        foreach (string f in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets"), "*.cs", SearchOption.AllDirectories))
        {
            string s = File.ReadAllText(f);
            if (s.Contains("MeshBuilder.ShadowCasters = "))
                Assert.EndsWith("SpriteShadowCeiling.cs", f);
        }
        string ceiling = Source("Assets", "LiveGroundCompare", "Scripts", "SpriteShadowCeiling.cs");
        Assert.Contains("MeshBuilder.ShadowCasters = this;", ceiling);
        Assert.Contains("if (ReferenceEquals(MeshBuilder.ShadowCasters, this))\n                MeshBuilder.ShadowCasters = _castersWas;", ceiling);
        Assert.Contains("Shader.SetGlobalFloat(CeilOnId, 0f);", ceiling);
        Assert.Contains("_core.Clear();", ceiling);
        Assert.DoesNotContain("System.Reflection", ceiling);
        Assert.DoesNotContain("GetField(", ceiling);
        Assert.DoesNotContain("QualitySettings.", ceiling);
        // Detach / logout / backend swap all dispose the presenter, which disposes the ceiling.
        string controller = Source("Assets", "LiveGroundCompare", "Scripts", "LiveGroundController.cs");
        Assert.Contains("DisposeTerrain(); // the world (and its terrain chunks) is going away", controller);
        Assert.Contains("SpriteCeiling?.Dispose();", Source("Assets", "LiveGroundCompare", "Scripts", "TerrainIsoPresenter.cs"));
    }

    [Fact]
    public void TerrainShader_MirrorsTheCpuLightAndCeiling()
    {
        string inc = Source("Assets", "LiveGroundCompare", "Shaders", "LiveGroundLight.cginc");
        var hz = System.Text.RegularExpressions.Regex.Match(inc, @"#define LG_HEIGHT_UNIT_PER_Z ([0-9.]+)");
        Assert.True(hz.Success);
        Assert.Equal(TerrainLandBuilder.HeightUnitPerZ, float.Parse(hz.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 6);
        Assert.Contains("return _UOAmbient.rgb * LGShade(n, visibility) + LGLocalLight(terrainWorld) * LGShade(n, 1.0);", inc);
        Assert.Contains("return saturate((z + 0.25 - ceiling) / 2.0 + 0.5);", inc);
        Assert.Contains("if (_LGCeilOn < 0.5)\n        return 1.0;", inc);
        string terrain = Source("Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareTerrain.shader");
        Assert.Contains("min(tex2D(_LGShadowTex, i.uv).r, LGCeilVisibility(i.world))", terrain);
        string sprite = Source("Assets", "UOClient.Unity", "Runtime", "Resources", "WorldSprite.shader");
        Assert.Contains("c.rgb *= (_UOAmbient.rgb * LGSpriteSunShade(i.world, i.foot) + _UOLightColor.rgb * falloff) / max(lgLit, 1e-4);", sprite);
    }
}

/// <summary>
/// Real data: the dynamic cast and margins on a 3x3 block of Britain chunks (map statics as their own columns, real
/// static graphics standing in for server items, mobiles walking), measured after warmup: CPU ms per frame, casters,
/// chunks redone, allocations. Writes Evidence~/dynCastChunk.json (stills fixture) with UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1.
/// </summary>
[Collection("UO install")]
public sealed class LiveGroundDynCastFixtureTests
{
    private const int T = TerrainLandBuilder.Tiles, N = LiveGroundLighting.ShadowSize;
    private readonly UOData _data;

    public LiveGroundDynCastFixtureTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static string EvidenceDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return Path.Combine(dir.FullName, "Assets", "LiveGroundCompare", "Evidence~");
        throw new DirectoryNotFoundException("repo root");
    }

    private (int[,] Z, int[,] Occ, float[] Own) Chunk(MapFile map, int x0, int y0)
    {
        RealMapPatch patch = RealMapPatch.FromMap(map, x0, y0, T, T);
        var z = new int[T + 1, T + 1];
        for (int j = 0; j <= T; j++)
            for (int i = 0; i <= T; i++)
                z[i, j] = patch.CornerZ(i, j);
        int[,] occ = StaticOccluders.FromMap(map, _data.TileData, x0, y0);
        return (z, occ, LiveGroundLighting.ShadowCeiling(z, occ));
    }

    /// <summary>The same town chunk LiveGroundSpriteShadowFixtureTests picks (most static columns).</summary>
    private (int X0, int Y0) PickTownChunk(MapFile map)
    {
        int best = -1, bx = 0, by = 0;
        for (int y0 = 1600; y0 <= 1744; y0 += T)
            for (int x0 = 1376; x0 <= 1520; x0 += T)
            {
                int n = StaticOccluders.Count(StaticOccluders.FromMap(map, _data.TileData, x0, y0));
                if (n > best)
                    (best, bx, by) = (n, x0, y0);
            }
        return (bx, by);
    }

    [Fact]
    public void Perf_ThreeByThreeTownBlock_WalkingMobilesAndItems_AfterWarmup()
    {
        MapFile map = _data.GetMap(0);
        (int cx0, int cy0) = PickTownChunk(map);
        var core = new ShadowCeilingCore(); // the shipped budget (1 margin a frame)
        var chunks = new List<(int X0, int Y0)>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x0 = cx0 + dx * T, y0 = cy0 + dy * T;
                var c = Chunk(map, x0, y0);
                core.Add(x0, y0, c.Own, c.Z, c.Occ, (x0, y0));
                chunks.Add((x0, y0));
            }
        var changed = new List<ShadowCeilingCore.Chunk>();
        int warm = 0;
        while (core.PendingMargins > 0) { core.Frame(changed); warm++; }

        // Items: real casting static graphics from the block (as server items would be: house parts, furniture).
        var items = new List<(object Key, int X, int Y, int Z, int H)>();
        foreach ((int x0, int y0) in chunks)
            for (int by = y0 / 8; by < (y0 + T) / 8; by++)
                for (int bx = x0 / 8; bx < (x0 + T) / 8; bx++)
                    foreach (StaticCell s in map.ReadStaticBlock(bx, by) ?? Array.Empty<StaticCell>())
                    {
                        StaticTileData td = _data.TileData.GetStatic(s.Graphic);
                        items.Add((new object(), bx * 8 + s.X, by * 8 + s.Y, s.Z, StaticOccluders.Casts(td) ? td.Height : 0));
                    }
        // Mobiles: 40 walkers, one tile every 12 frames each (a walk step at 60 fps is 24; this is busier), staggered.
        const int mobiles = 40, frames = 600, stepEvery = 12;
        var rng = new Random(1234);
        var mob = Enumerable.Range(0, mobiles).Select(_ => (Key: new object(), X: cx0 - T + rng.Next(3 * T), Y: cy0 - T + rng.Next(3 * T), Phase: rng.Next(stepEvery))).ToArray();
        void Tick(int f)
        {
            foreach (var it in items)
                core.Report(it.Key, it.X, it.Y, it.Z, it.H);
            for (int m = 0; m < mobiles; m++)
            {
                var k = mob[m];
                if ((f + k.Phase) % stepEvery == 0)
                {
                    int nx = k.X + rng.Next(3) - 1, ny = k.Y + rng.Next(3) - 1;
                    k.X = Math.Clamp(nx, cx0 - T, cx0 + 2 * T - 1);
                    k.Y = Math.Clamp(ny, cy0 - T, cy0 + 2 * T - 1);
                    mob[m] = k;
                }
                core.Report(k.Key, k.X, k.Y, 0, DynamicShadow.MobileHeight, mobile: true);
            }
            changed.Clear();
            core.Frame(changed);
        }
        for (int f = 0; f < 120; f++) Tick(f); // warmup: JIT, dictionaries at size, every item's first stamp

        var times = new double[frames];
        int dirtyTotal = 0, dirtyMax = 0, stampedTotal = 0;
        var w = new System.Diagnostics.Stopwatch();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < frames; f++)
        {
            w.Restart();
            Tick(120 + f);
            w.Stop();
            times[f] = w.Elapsed.TotalMilliseconds;
            dirtyTotal += core.LastDirty;
            dirtyMax = Math.Max(dirtyMax, core.LastDirty);
            stampedTotal += core.LastStamped;
        }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Array.Sort(times);
        double mean = times.Average(), p50 = times[frames / 2], p95 = times[frames * 95 / 100], max = times[^1];

        // Margin cost alone (main thread, per chunk, the shipped budget runs at most 2 a frame).
        var one = new ShadowCeilingCore { MarginsPerFrame = 1 };
        foreach ((int x0, int y0) in chunks)
        {
            var c = Chunk(map, x0, y0);
            one.Add(x0, y0, c.Own, c.Z, c.Occ, (x0, y0));
        }
        while (one.PendingMargins > 0) one.Frame(changed); // warm
        var centre = Chunk(map, cx0, cy0);
        one.Add(cx0, cy0, centre.Own, centre.Z, centre.Occ, "again"); // reattached: its own margin is first in the queue
        w.Restart();
        one.Frame(changed);
        w.Stop();
        double marginMs = w.Elapsed.TotalMilliseconds;
        Assert.Equal(1, one.LastMargins);

        string report =
            $"block 3x3 chunks around town chunk ({cx0},{cy0}) map 0; {items.Count} items ({items.Count(i => i.H > 0)} casting) + {mobiles} walking mobiles; " +
            $"casters {core.CasterCount} ({core.CastingCount} casting); margins warmup {warm} frames; measured {frames} frames after 120 warmup: " +
            $"Frame() mean {mean:0.000} ms, p50 {p50:0.000}, p95 {p95:0.000}, max {max:0.000}; chunks redone/frame mean {dirtyTotal / (double)frames:0.00} max {dirtyMax}; " +
            $"texels stamped/frame mean {stampedTotal / (double)frames:0}; allocated {alloc} bytes over {frames} frames; one margin recompute {marginMs:0.000} ms (main thread, budget 1/frame). " +
            "CPU only (no texture upload; see dynacast.md for the Unity upload). .NET " + Environment.Version + ", " + Environment.ProcessorCount + " logical CPUs.";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "uoclient_lg_dyncast_perf.txt"), report + "\n");
        Assert.True(core.CastingCount > mobiles, report);
        Assert.True(dirtyMax <= 9, report);
        Assert.True(alloc < 64 * 1024, report); // steady state: no per-frame garbage
        Assert.True(p95 < 8, report); // generous bound for CI noise; the measured figure is in the report
    }

    /// <summary>JsonUtility-friendly fixture for LiveGroundEvidenceCapture.DynCast (flat arrays).</summary>
    public sealed class DynFixture
    {
        public string source = "", baseFixture = "shadowRecvChunk.json";
        public int map, x0, y0, tiles, mobileHeight;
        public int[] casterA = Array.Empty<int>(), casterB = Array.Empty<int>(), receiver = Array.Empty<int>();
        public int marginNeighbours, marginTexels, castATexels, castBTexels;
        public float[] ceilStatic = Array.Empty<float>(), ceilCastA = Array.Empty<float>(), ceilCastB = Array.Empty<float>(),
            ceilRemoved = Array.Empty<float>(), ceilMargin = Array.Empty<float>(), ceilMarginCastA = Array.Empty<float>();
    }

    private static float[] R(float[] a) => Array.ConvertAll(a, c => (float)Math.Round(c, 3));

    [Fact]
    public void Fixture_RealTownChunk_CastMoveRemoveAndMargin()
    {
        MapFile map = _data.GetMap(0);
        (int x0, int y0) = PickTownChunk(map);
        var me = Chunk(map, x0, y0);
        var changed = new List<ShadowCeilingCore.Chunk>();

        // The receiver: a tile in sun in this dense town chunk (235 of 256 tiles carry a static column); caster A right in
        // front of it toward the sun (west, so the receiver is inside A's footprint), preferably open ground; B one tile
        // south of A (+y), whose shadow misses the receiver's tile.
        float[] own = me.Own;
        (int X, int Y, int Z) a = default;
        int bestScore = -1;
        for (int j = 0; j < T - 1; j++)
            for (int i = 0; i < T - 2; i++)
            {
                int z = (int)Math.Round(TerrainLandBuilder.SampleZ(me.Z, i + 0.5f, j + 0.5f));
                bool Open(int di, int dj) => me.Occ[i + di, j + dj] == StaticOccluders.None;
                bool Lit(int di, int dj) => LiveGroundLighting.SampleCeiling(own, i + di + 0.5f, j + dj + 0.5f) < z - 1;
                if (!Lit(1, 0))
                    continue; // the receiver must stand in sun, or the caster shows nothing on it
                int score = (Open(0, 0) ? 8 : 0) + (Open(1, 0) ? 4 : 0) + (Open(0, 1) ? 2 : 0) + (Lit(0, 0) ? 1 : 0) + (Lit(2, 0) ? 1 : 0) + (Lit(0, 1) ? 1 : 0);
                if (score > bestScore)
                    (bestScore, a) = (score, (x0 + i, y0 + j, z));
            }
        Assert.True(bestScore >= 0, $"a lit tile with a tile west of it (best score {bestScore})");
        (int X, int Y, int Z) b = (a.X, a.Y + 1, a.Z), rcv = (a.X + 1, a.Y, a.Z);
        var core = new ShadowCeilingCore { MarginsPerFrame = 0 }; // own only first
        core.Add(x0, y0, me.Own, me.Z, me.Occ, "me");
        core.Frame(changed);
        float[] ceilStatic = Composite(core, x0, y0);
        var mob = new object();
        core.Report(mob, a.X, a.Y, a.Z, DynamicShadow.MobileHeight, mobile: true);
        core.Frame(changed);
        float[] ceilA = Composite(core, x0, y0);
        core.Report(mob, b.X, b.Y, b.Z, DynamicShadow.MobileHeight, mobile: true);
        core.Frame(changed);
        float[] ceilB = Composite(core, x0, y0);
        core.Frame(changed); // not drawn: swept
        float[] ceilRemoved = Composite(core, x0, y0);
        Assert.Equal(ceilStatic, ceilRemoved);
        Assert.NotEqual(ceilStatic, ceilA);
        Assert.NotEqual(ceilA, ceilB);

        // Margin: the real west, north and north-west neighbours loaded.
        int neighbours = 0;
        foreach ((int dx, int dy) in new[] { (-T, 0), (0, -T), (-T, -T) })
        {
            var n = Chunk(map, x0 + dx, y0 + dy);
            core.Add(x0 + dx, y0 + dy, n.Own, n.Z, n.Occ, (dx, dy));
            neighbours++;
        }
        core.MarginsPerFrame = 100;
        core.Frame(changed);
        float[] ceilMargin = Composite(core, x0, y0);
        core.Report(mob, a.X, a.Y, a.Z, DynamicShadow.MobileHeight, mobile: true);
        core.Frame(changed);
        float[] ceilMarginA = Composite(core, x0, y0);
        int marginTexels = Enumerable.Range(0, N * N).Count(k => ceilMargin[k] > ceilStatic[k] + 0.5f);
        Assert.True(marginTexels > 0, "the real neighbours shade across the seam");

        var f = new DynFixture
        {
            source = "map0 corner heights + map static columns via UOInstallFixture; ceilings from ShadowCeilingCore (shipped code)",
            map = 0, x0 = x0, y0 = y0, tiles = T, mobileHeight = DynamicShadow.MobileHeight,
            casterA = new[] { a.X, a.Y, a.Z }, casterB = new[] { b.X, b.Y, b.Z }, receiver = new[] { rcv.X, rcv.Y, rcv.Z },
            marginNeighbours = neighbours, marginTexels = marginTexels,
            castATexels = Enumerable.Range(0, N * N).Count(k => ceilA[k] > ceilStatic[k]),
            castBTexels = Enumerable.Range(0, N * N).Count(k => ceilB[k] > ceilStatic[k]),
            ceilStatic = R(ceilStatic), ceilCastA = R(ceilA), ceilCastB = R(ceilB), ceilRemoved = R(ceilRemoved),
            ceilMargin = R(ceilMargin), ceilMarginCastA = R(ceilMarginA)
        };
        string dir = EvidenceDir(), path = Path.Combine(dir, "dynCastChunk.json");
        string built = System.Text.Json.JsonSerializer.Serialize(f, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        if (Environment.GetEnvironmentVariable("UOCLIENT_WRITE_LIVEGROUND_EVIDENCE") == "1")
            File.WriteAllText(path, built + "\n");
        if (File.Exists(path))
            Assert.Equal(built, File.ReadAllText(path).TrimEnd('\n'));
    }

    private static float[] Composite(ShadowCeilingCore core, int x0, int y0) =>
        core.TryGet(x0, y0, out ShadowCeilingCore.Chunk c) ? (float[])c.Composite.Clone() : null;
}

/// <summary>
/// Water receive (TEST only): the real coastal chunk of realWaterChunk.json, its own CPU ceiling (terrain; the chunk has
/// no casting statics) and the same with a mobile-height column standing in open water, so the world ceiling the water
/// shader reads shades water texels. Writes Evidence~/waterRecvChunk.json (LiveGroundEvidenceCapture.WaterRecv) with
/// UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1; otherwise checks it is current.
/// </summary>
[Collection("UO install")]
public sealed class LiveGroundWaterRecvFixtureTests
{
    private const int T = TerrainLandBuilder.Tiles, K = LiveGroundLighting.ShadowTexelsPerTile;
    private readonly UOData _data;

    public LiveGroundWaterRecvFixtureTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static string EvidenceDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return Path.Combine(dir.FullName, "Assets", "LiveGroundCompare", "Evidence~");
        throw new DirectoryNotFoundException("repo root");
    }

    /// <summary>JsonUtility-friendly (LiveGroundEvidenceCapture.WaterRecvFixture, same field names).</summary>
    public sealed class WaterRecvFixture
    {
        public string source = "", baseFixture = "realWaterChunk.json";
        public int map, x0, y0, tiles, shadowTexels, wrapTiles, casterHeight, waterTexels, shadowedStatic, shadowedCast;
        public float heightUnitPerZ, noCeiling, softZ, biasZ;
        public int[] caster = Array.Empty<int>();    // tile x, y, Z
        public float[] probe = Array.Empty<float>(); // world tile x, y and ground Z of a water point the column shades
        public float[] ceilStatic = Array.Empty<float>(), ceilCast = Array.Empty<float>();
    }

    private static float[] R(float[] a) => Array.ConvertAll(a, c => (float)Math.Round(c, 3));

    [Fact]
    public void Fixture_RealCoastalChunk_CeilingShadesWater()
    {
        string dir = EvidenceDir();
        using var baseJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "realWaterChunk.json")));
        int x0 = baseJson.RootElement.GetProperty("x0").GetInt32(), y0 = baseJson.RootElement.GetProperty("y0").GetInt32();
        MapFile map = _data.GetMap(0);
        bool[] waterTable = WaterTiles.Table(_data.TileData.Land);
        RealMapPatch patch = RealMapPatch.FromMap(map, x0, y0, T, T);
        var z = new int[T + 1, T + 1];
        for (int j = 0; j <= T; j++)
            for (int i = 0; i <= T; i++)
                z[i, j] = patch.CornerZ(i, j);
        var water = new bool[T, T];
        for (int j = 0; j < T; j++)
            for (int i = 0; i < T; i++)
                water[i, j] = WaterTiles.Lookup(waterTable, patch.Graphic(i, j));
        int[,] occ = StaticOccluders.FromMap(map, _data.TileData, x0, y0);
        float[] ceilStatic = LiveGroundLighting.ShadowCeiling(z, occ);

        int Shadowed(float[] c)
        {
            int n = 0;
            for (int r = 0; r < T * K; r++)
                for (int col = 0; col < T * K; col++)
                {
                    float tx = (col + 0.5f) / K, ty = (r + 0.5f) / K;
                    if (water[(int)tx, (int)ty] && SpriteShadow.Visibility(TerrainLandBuilder.SampleZ(z, tx, ty), c[r * T * K + col]) < 0.5f)
                        n++;
                }
            return n;
        }

        // The column: the open-water tile whose shadow (away from the sun, +x/+y) falls on the most lit water texels.
        int best = 0;
        (int I, int J, int Z) caster = default;
        float[] ceilCast = null;
        for (int j = 0; j < T - 2; j++)
            for (int i = 0; i < T - 2; i++)
            {
                if (!water[i, j])
                    continue;
                int cz = (int)Math.Round(TerrainLandBuilder.SampleZ(z, i + 0.5f, j + 0.5f));
                float[] c = (float[])ceilStatic.Clone();
                DynamicShadow.Stamp(c, x0, y0, x0 + i, y0 + j, cz + DynamicShadow.MobileHeight, DynamicShadow.MobileHeight);
                int gain = Shadowed(c) - Shadowed(ceilStatic);
                if (gain > best)
                    (best, caster, ceilCast) = (gain, (i, j, cz), c);
            }
        Assert.True(best > 0 && ceilCast != null, "a water tile whose column shades water");

        // Probe: the water texel just behind the column (away from the sun) that it shades and the terrain alone does not.
        float[] probe = null;
        for (int d = 1; d <= 3 && probe == null; d++)
            foreach ((int di, int dj) in new[] { (d, 0), (d, d), (0, d) })
            {
                int i = caster.I + di, j = caster.J + dj;
                if (i >= T || j >= T || !water[i, j])
                    continue;
                float tx = i + 0.5f, ty = j + 0.5f, gz = TerrainLandBuilder.SampleZ(z, tx, ty);
                int k = (j * K + K / 2) * T * K + i * K + K / 2;
                if (SpriteShadow.Visibility(gz, ceilCast[k]) < 0.25f && SpriteShadow.Visibility(gz, ceilStatic[k]) > 0.99f)
                {
                    probe = new[] { x0 + tx, y0 + ty, gz };
                    break;
                }
            }
        Assert.NotNull(probe);

        int waterTexels = 0;
        foreach (bool w in water)
            if (w)
                waterTexels += K * K;
        var f = new WaterRecvFixture
        {
            source = "map0 corner heights + map static columns via UOInstallFixture; ceilings LiveGroundLighting.ShadowCeiling + DynamicShadow.Stamp (shipped code)",
            map = 0, x0 = x0, y0 = y0, tiles = T, shadowTexels = K, wrapTiles = SpriteShadow.WrapTiles, casterHeight = DynamicShadow.MobileHeight,
            heightUnitPerZ = TerrainLandBuilder.HeightUnitPerZ, noCeiling = LiveGroundLighting.NoCeiling, softZ = SpriteShadow.SoftZ, biasZ = LiveGroundLighting.ShadowBiasZ,
            caster = new[] { x0 + caster.I, y0 + caster.J, caster.Z }, probe = probe,
            waterTexels = waterTexels, shadowedStatic = Shadowed(ceilStatic), shadowedCast = Shadowed(ceilCast),
            ceilStatic = R(ceilStatic), ceilCast = R(ceilCast)
        };
        string path = Path.Combine(dir, "waterRecvChunk.json");
        string built = System.Text.Json.JsonSerializer.Serialize(f, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        if (Environment.GetEnvironmentVariable("UOCLIENT_WRITE_LIVEGROUND_EVIDENCE") == "1")
            File.WriteAllText(path, built + "\n");
        if (File.Exists(path))
            Assert.Equal(built, File.ReadAllText(path).TrimEnd('\n'));
    }
}

/// <summary>
/// The water shader's receive, Unity-free: the shipped source wires the world ceiling like TerrainLit, and the light it
/// computes equals the 83c0a85 water (Evidence~/LiveGroundCompareWater.83c0a85.shader.txt) whenever visibility is 1
/// (ceiling off, missing, or empty). The GPU render of both is compared in waterrecv.md.
/// </summary>
public sealed class LiveGroundWaterRecvTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(path).ToArray())).Replace("\r\n", "\n");

    private static string Water() => Source("Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareWater.shader");

    private static string Frag(string shader) => shader.Substring(shader.IndexOf("fixed4 frag(", StringComparison.Ordinal));

    [Fact]
    public void WaterShader_ReadsTheCeilingLikeTerrain_AmbientAndGlintOnly()
    {
        string frag = Frag(Water());
        Assert.Contains("float visibility = LGCeilVisibility(i.world);", frag);
        Assert.Contains("* LGLitShadowed(n, visibility, i.world);", frag);
        Assert.Contains("* _Gloss * visibility;", frag);
        Assert.Contains("lit += spec * _UOAmbient.rgb;", frag); // the glint is sun light: never the player's
        Assert.DoesNotContain("LGSpriteLight", frag);         // unshadowed light path gone (ambient would escape the shadow)
        Assert.DoesNotContain("_LGShadowCeil", frag);         // only through the shared include (off when _LGCeilOn is 0)
        // Motion, foam and sorting untouched: one _Time.y read (the evidence time patch relies on it), same blend state.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(frag, @"_Time\.y"));
        string water = Water(), old = Source("Assets", "LiveGroundCompare", "Evidence~", "LiveGroundCompareWater.83c0a85.shader.txt");
        foreach (string keep in new[]
        {
            "Tags { \"Queue\" = \"Transparent\" \"RenderType\" = \"Transparent\" \"IgnoreProjector\" = \"True\" }",
            "Blend SrcAlpha OneMinusSrcAlpha", "ZWrite Off", "ZTest LEqual", "Offset -1, -1",
            "float band = sin((open * 4.0 + t * 0.6) * 6.2831853) * 0.5 + 0.5;",
            "float alpha = saturate(lerp(_ShoreAlpha, _OpenAlpha, open) + foam * 0.5 + spec * 0.3);",
            "return fixed4(LGGhost(lit), alpha);"
        })
        {
            Assert.Contains(keep, water);
            Assert.Contains(keep, old);
        }
        Assert.Contains("float3 light = LGSpriteLight(i.world);", old); // the reference really is the pre-receive shader
    }

    [Fact]
    public void WaterShader_OnlyItsLightingLinesChangedSince83c0a85()
    {
        string[] now = Frag(Water()).Split('\n'), old = Frag(Source("Assets", "LiveGroundCompare", "Evidence~", "LiveGroundCompareWater.83c0a85.shader.txt")).Split('\n');
        var added = now.Except(old).Select(l => l.Trim()).ToArray();
        var removed = old.Except(now).Select(l => l.Trim()).ToArray();
        Assert.Equal(3, added.Length);
        Assert.Equal(3, removed.Length);
        Assert.All(removed, l => Assert.True(l.Contains("light") || l.Contains("spec ="), l));
    }

    // CPU mirrors of the cginc (LGShade / LGLocalLight already mirrored by LiveGroundLighting.Shade / PointFalloff).
    private static float Shade(float ndotl, float sunY, float vis)
    {
        float s = Math.Clamp(ndotl / sunY, 0.7f, 1.25f);
        return 0.7f + (s - 0.7f) * Math.Clamp(vis, 0f, 1f);
    }

    private static float CeilVisibility(bool on, float z, float ceiling) =>
        !on ? 1f : Math.Clamp((z + 0.25f - ceiling) / 2f + 0.5f, 0f, 1f);

    [Fact]
    public void WaterLight_EqualsThePriorWaterWheneverTheCeilingIsOffOrEmpty_AndDimsOnlyAmbientInShadow()
    {
        var rng = new Random(83);
        for (int k = 0; k < 20000; k++)
        {
            float ndotl = 0.4f + (float)rng.NextDouble() * 0.6f, sunY = 0.77f;
            float amb = (float)rng.NextDouble(), local = (float)rng.NextDouble() * 1.5f, spec = (float)rng.NextDouble() * 1.2f;
            float z = (float)rng.Next(-10, 60), ceiling = z + (float)rng.NextDouble() * 40f - 20f;
            float prior = Shade(ndotl, sunY, 1f) * (amb + local) + spec * amb; // 83c0a85: LGShade(n, 1) * LGSpriteLight + spec * ambient

            foreach ((bool on, float c) in new[] { (false, ceiling), (true, LiveGroundLighting.NoCeiling) })
            {
                float vis = CeilVisibility(on, z, c);
                Assert.Equal(1f, vis);
                float now = amb * Shade(ndotl, sunY, vis) + local * Shade(ndotl, sunY, 1f) + spec * vis * amb;
                Assert.True(Math.Abs(prior - now) < 1e-5f, $"{prior} vs {now}"); // float re-association only, far below 1/255
            }

            float v = CeilVisibility(true, z, ceiling);
            float shadowed = amb * Shade(ndotl, sunY, v) + local * Shade(ndotl, sunY, 1f) + spec * v * amb;
            Assert.True(shadowed <= prior + 1e-5f);
            // The player's light alone (night, no ambient) is never dimmed by the sun shadow.
            Assert.Equal(local * Shade(ndotl, sunY, 1f), 0f * Shade(ndotl, sunY, v) + local * Shade(ndotl, sunY, 1f) + spec * v * 0f, 6);
        }
    }
}
