using System.Text.RegularExpressions;
using UOClient.LiveGroundCompare;

namespace UOClient.Tests;

/// <summary>
/// LiveGroundCompare (TEST only) reliability: repeated attach / toggle / detach / dispose through the real
/// <see cref="LiveGroundSwitch"/> driving a model of LiveGroundController's Apply (one presenter at most, gone off
/// Terrain), the CPU ceiling's chunk publish/remove/clear cycles (real <see cref="ShadowCeilingCore"/>), fail-closed
/// messages, and source guards that every Unity object the Terrain backend creates is destroyed and every global it
/// changes is put back. Unity-free: the Unity calls themselves are guarded by source, not run.
/// </summary>
public sealed class LiveGroundReliabilityTests
{
    private const int T = TerrainLandBuilder.Tiles, N = LiveGroundLighting.ShadowSize;

    /// <summary>LiveGroundController.Apply / Detach / Dispose, with the presenter and its ceiling as counted fakes.</summary>
    private sealed class ControllerModel
    {
        public int PresentersCreated, PresentersDisposed, CeilingOwners;
        public GroundBackendKind? Emitter; // the world's LandEmitter (null = Original)
        public bool HasPresenter;
        public string FailNextTerrain;
        public bool World = true;
        public readonly List<GroundBackendKind> Applied = new();

        public string Apply(GroundBackendKind kind)
        {
            Applied.Add(kind);
            if (!World)
                return null;
            if (kind == GroundBackendKind.Terrain)
            {
                if (!HasPresenter)
                {
                    if (FailNextTerrain is string why)
                    {
                        FailNextTerrain = null;
                        return why; // TerrainIsoPresenter.TryCreate returned null with this error
                    }
                    HasPresenter = true;
                    PresentersCreated++;
                    CeilingOwners++;
                }
                Emitter = GroundBackendKind.Terrain;
                return null;
            }
            Emitter = kind == GroundBackendKind.Mesh ? GroundBackendKind.Mesh : null;
            DisposeTerrain();
            return null;
        }

        public void DisposeTerrain()
        {
            if (!HasPresenter)
                return;
            HasPresenter = false;
            PresentersDisposed++;
            CeilingOwners--;
        }

        public int Live => PresentersCreated - PresentersDisposed;
    }

    [Fact]
    public void RepeatedToggleCycles_NeverHoldMoreThanOnePresenter_AndReturnToOriginal()
    {
        var m = new ControllerModel();
        var s = new LiveGroundSwitch(true, GroundBackendKind.Original, m.Apply);
        Assert.Equal("Ground: Original", s.Attach("127.0.0.1"));
        var seen = new List<GroundBackendKind>();
        for (int k = 0; k < 300; k++)
        {
            s.Toggle();
            seen.Add(s.Active);
            Assert.True(m.Live is 0 or 1);
            Assert.Equal(s.Active == GroundBackendKind.Terrain, m.HasPresenter);
            Assert.Equal(s.Active == GroundBackendKind.Original ? null : s.Active, m.Emitter);
            Assert.Equal(m.Live, m.CeilingOwners);
        }
        Assert.Equal(new[] { GroundBackendKind.Mesh, GroundBackendKind.Terrain, GroundBackendKind.Original }, seen.Take(3));
        Assert.Equal(100, m.PresentersCreated); // one per Terrain visit, never reused after leaving Terrain
        s.Dispose();
        Assert.Equal(0, m.Live);
        Assert.Null(m.Emitter);
    }

    [Fact]
    public void AttachDetachCycles_OnTerrain_DisposeEveryPresenter()
    {
        var m = new ControllerModel();
        var s = new LiveGroundSwitch(true, GroundBackendKind.Terrain, m.Apply);
        for (int k = 0; k < 50; k++)
        {
            m.World = true;
            s.Attach("localhost");
            Assert.Equal(GroundBackendKind.Terrain, s.Active);
            Assert.Equal(1, m.Live);
            // LiveGroundController.Detach: switch.Detach, then DisposeTerrain (the world is going away).
            s.Detach();
            m.DisposeTerrain();
            m.World = false;
            Assert.Equal(0, m.Live);
            Assert.Equal(GroundBackendKind.Original, s.Active);
            Assert.Null(s.Toggle()); // nothing to switch without a world
        }
        Assert.Equal(50, m.PresentersCreated);
        s.Dispose();
        s.Dispose(); // idempotent
        Assert.Null(s.Attach("localhost"));
        Assert.Equal(0, m.Live);
    }

    [Fact]
    public void Dispose_WhileOnTerrain_AppliesOriginalOnce_AndIgnoresEverythingAfter()
    {
        var m = new ControllerModel();
        var s = new LiveGroundSwitch(true, GroundBackendKind.Terrain, m.Apply);
        s.Attach("127.0.0.1");
        int before = m.Applied.Count;
        s.Dispose();
        Assert.Equal(new[] { GroundBackendKind.Original }, m.Applied.Skip(before));
        Assert.Equal(0, m.Live);
        Assert.Null(s.Toggle());
        Assert.Null(s.Fail("late"));
        Assert.Equal(before + 1, m.Applied.Count);
    }

    [Theory]
    [InlineData("no world or main camera", "Ground: Original (Terrain failed: no world or main camera; using Original)")]
    [InlineData("", "Ground: Original (Terrain failed: unknown error; using Original)")]
    [InlineData("  ", "Ground: Original (Terrain failed: unknown error; using Original)")]
    public void TerrainThatCannotBeCreated_FailsClosedWithItsReason(string why, string line)
    {
        var m = new ControllerModel { FailNextTerrain = why };
        var s = new LiveGroundSwitch(true, GroundBackendKind.Terrain, m.Apply);
        Assert.Equal(line, s.Attach("127.0.0.1"));
        Assert.Equal(GroundBackendKind.Original, s.Active);
        Assert.Null(m.Emitter);
        Assert.Equal(0, m.Live);
        Assert.True(s.IsAttached && LiveGroundStatus.IsFallback(s.Requested, s.Active, s.Reason));
        string indicator = LiveGroundStatus.Indicator(s.Requested, s.Active, s.Reason, s.Host, s.IsAttached, 0);
        Assert.Contains("requested Terrain | active Original | FALLBACK: Terrain failed: ", indicator);
        // A later try works and clears the reason.
        s.Toggle(); // Original -> Mesh
        s.Toggle(); // Mesh -> Terrain
        Assert.Equal(GroundBackendKind.Terrain, s.Active);
        Assert.Null(s.Reason);
        Assert.Equal(1, m.Live);
    }

    [Fact]
    public void ChunkFailureAfterApply_FallsBackOnce_AndDisposesThePresenter()
    {
        var m = new ControllerModel();
        var s = new LiveGroundSwitch(true, GroundBackendKind.Terrain, m.Apply);
        s.Attach("127.0.0.1");
        Assert.Equal("Ground: Original (Terrain failed: terrain chunk 91,91: IndexOutOfRangeException: x; using Original)",
            s.Fail("terrain chunk 91,91: IndexOutOfRangeException: x"));
        Assert.Equal(0, m.Live);
        Assert.Null(s.Fail("again")); // already Original: nothing changes, the first reason stays
        Assert.StartsWith("Terrain failed: terrain chunk 91,91", s.Reason);
    }

    [Theory]
    [InlineData(false, "127.0.0.1", "experimental ground needs -liveground")]
    [InlineData(true, "play.uosmallz.com", "experimental ground refused for non-local host 'play.uosmallz.com' (TEST 127.0.0.1 only)")]
    [InlineData(true, null, "experimental ground refused for non-local host '' (TEST 127.0.0.1 only)")]
    public void GuardRefusals_NeverCreateAPresenter(bool optIn, string? host, string reason)
    {
        var m = new ControllerModel();
        var s = new LiveGroundSwitch(optIn, GroundBackendKind.Terrain, m.Apply);
        s.Attach(host!);
        for (int k = 0; k < 6; k++)
            s.Toggle();
        Assert.Equal(0, m.PresentersCreated);
        Assert.Null(m.Emitter);
        Assert.Equal(GroundBackendKind.Original, s.Active);
        Assert.Equal(reason, s.Reason);
    }

    // ---- the CPU ceiling (real ShadowCeilingCore, as SpriteShadowCeiling drives it) ----

    private static int[,] Gentle(int seed)
    {
        var c = new int[T + 1, T + 1];
        for (int i = 0; i <= T; i++)
            for (int j = 0; j <= T; j++)
                c[i, j] = (i * 3 + j * 5 + seed) % 7;
        return c;
    }

    private static List<(int X0, int Y0)> Block3(int cx0, int cy0)
    {
        var l = new List<(int, int)>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                l.Add((cx0 + dx * T, cy0 + dy * T));
        return l;
    }

    private static void Settle(ShadowCeilingCore core, List<ShadowCeilingCore.Chunk> changed)
    {
        for (int k = 0; k < 64 && core.PendingMargins > 0; k++)
            core.Frame(changed);
        core.Frame(changed);
    }

    [Fact]
    public void ChunkPublishRemoveCycles_LeaveNoOwners_AndRebuildIdentically()
    {
        var core = new ShadowCeilingCore();
        var changed = new List<ShadowCeilingCore.Chunk>();
        var chunks = Block3(1424, 1712);
        var mobile = new object();
        float[]? reference = null;
        for (int cycle = 0; cycle < 40; cycle++)
        {
            var owners = new Dictionary<(int, int), object>();
            foreach ((int x0, int y0) in chunks)
            {
                int[,] z = Gentle(x0 / T + y0 / T);
                object owner = new object(); // a fresh TerrainLandChunk each stream-in
                owners[(x0, y0)] = owner;
                core.Add(x0, y0, LiveGroundLighting.ShadowCeiling(z, null), z, null, owner);
            }
            core.Report(mobile, 1424 + 3, 1712 + 4, 0, DynamicShadow.MobileHeight, mobile: true);
            Settle(core, changed);
            core.Report(mobile, 1424 + 3, 1712 + 4, 0, DynamicShadow.MobileHeight, mobile: true);
            changed.Clear();
            core.Frame(changed);
            Assert.True(core.TryGet(1424, 1712, out ShadowCeilingCore.Chunk centre));
            reference ??= (float[])centre.Composite.Clone();
            Assert.Equal(reference, centre.Composite); // every cycle rebuilds the same ceiling

            // A stale owner (an older chunk object at the same place) never removes the live one.
            Assert.False(core.Remove(1424, 1712, new object()));
            Assert.Equal(chunks.Count, core.ChunkCount);
            foreach ((int x0, int y0) in chunks)
                Assert.True(core.Remove(x0, y0, owners[(x0, y0)]));
            Assert.Equal(0, core.ChunkCount);
            changed.Clear();
            core.Frame(changed); // queued margins of removed chunks: nothing to recompose; the undrawn mobile is swept
            Assert.Empty(changed);
            Assert.Equal(0, core.PendingMargins);
            Assert.Equal(0, core.CasterCount);
        }
    }

    [Fact]
    public void Clear_DropsEveryChunkCasterAndQueuedMargin_AndAFreshStartMatches()
    {
        var core = new ShadowCeilingCore();
        var changed = new List<ShadowCeilingCore.Chunk>();
        int[,] z = Gentle(1);
        float[] own = LiveGroundLighting.ShadowCeiling(z, null);
        for (int k = 0; k < 10; k++)
        {
            foreach ((int x0, int y0) in Block3(1424, 1712))
                core.Add(x0, y0, (float[])own.Clone(), z, null, (x0, y0));
            core.Report(new object(), 1430, 1718, 0, 20);
            Assert.True(core.PendingMargins > 0);
            core.Clear(); // SpriteShadowCeiling.Dispose
            Assert.Equal(0, core.ChunkCount);
            Assert.Equal(0, core.CasterCount);
            Assert.Equal(0, core.PendingMargins);
            Assert.Equal(0, core.CastingCount);
            changed.Clear();
            core.Frame(changed);
            Assert.Empty(changed);
        }
        core.Add(1424, 1712, (float[])own.Clone(), z, null, "a");
        core.Frame(changed);
        var fresh = new ShadowCeilingCore();
        fresh.Add(1424, 1712, (float[])own.Clone(), z, null, "a");
        fresh.Frame(new List<ShadowCeilingCore.Chunk>());
        Assert.True(core.TryGet(1424, 1712, out var a));
        Assert.True(fresh.TryGet(1424, 1712, out var b));
        Assert.Equal(b.Composite, a.Composite);
        Assert.Equal(N * N, a.Composite.Length);
    }

    [Fact]
    public void ChunkCycles_AllocateNothingPerFrameOnceWarm()
    {
        var core = new ShadowCeilingCore();
        var changed = new List<ShadowCeilingCore.Chunk>(16);
        int[,] z = Gentle(2);
        float[] own = LiveGroundLighting.ShadowCeiling(z, null);
        var keys = Enumerable.Range(0, 8).Select(_ => new object()).ToArray();
        foreach ((int x0, int y0) in Block3(1424, 1712))
            core.Add(x0, y0, own, z, null, (x0, y0));
        void Tick(int f)
        {
            for (int m = 0; m < keys.Length; m++)
                core.Report(keys[m], 1420 + (f + m) % 24, 1708 + m * 3, 0, DynamicShadow.MobileHeight, mobile: true);
            changed.Clear();
            core.Frame(changed);
        }
        for (int f = 0; f < 200; f++) Tick(f);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 200; f < 400; f++) Tick(f);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - a0 < 16 * 1024);
    }

    // ---- source guards: Unity objects destroyed, globals restored, shaders shipped by Resources ----

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Scripts", name)).Replace("\r\n", "\n");

    /// <summary>Every Unity object the runtime scripts create, by file and the field it lands in (destroyed in that file).</summary>
    private static readonly (string File, string Field, string Destroy)[] Owned =
    {
        ("TerrainLandChunk.cs", "_terrainData", "UnityEngine.Object.Destroy(_terrainData);"),
        ("TerrainLandChunk.cs", "_texture", "UnityEngine.Object.Destroy(_texture);"),
        ("TerrainLandChunk.cs", "_sunMask", "UnityEngine.Object.Destroy(_sunMask);"),
        ("TerrainLandChunk.cs", "_material", "UnityEngine.Object.Destroy(_material);"),
        ("TerrainLandChunk.cs", "_proxyMesh", "UnityEngine.Object.Destroy(_proxyMesh);"),
        ("TerrainLandChunk.cs", "_proxyMaterial", "UnityEngine.Object.Destroy(_proxyMaterial);"),
        ("TerrainLandChunk.cs", "_proxyGo", "UnityEngine.Object.Destroy(_proxyGo);"),
        ("TerrainWater.cs", "material", "UnityEngine.Object.Destroy(Material);"),
        ("TerrainWater.cs", "s._mesh", "UnityEngine.Object.Destroy(_mesh);"),
        ("TerrainWater.cs", "s._go", "UnityEngine.Object.Destroy(_go);"),
        ("TerrainGrass.cs", "g._mesh", "UnityEngine.Object.Destroy(_mesh);"),
        ("TerrainGrass.cs", "g._material", "UnityEngine.Object.Destroy(_material);"),
        ("TerrainGrass.cs", "g._go", "UnityEngine.Object.Destroy(_go);"),
        ("SpriteShadowCeiling.cs", "_texture", "UnityEngine.Object.Destroy(_texture);"),
        ("GrassCover.cs", "_texture", "UnityEngine.Object.Destroy(_texture);"),
        ("SpriteShadowCeiling.cs", "s = _staging[k]", "UnityEngine.Object.Destroy(_staging[k]);"),
        ("LiveGroundController.cs", "_texture", "Object.Destroy(_texture);"),
        ("LiveGroundController.cs", "_mesh", "Object.Destroy(_mesh);"),
        ("LiveGroundController.cs", "_material", "Object.Destroy(_material);"),
        ("LiveGroundController.cs", "_go", "Object.Destroy(_go);"),
        ("TerrainIsoPresenter.cs", "_root", "UnityEngine.Object.Destroy(_root);"),
    };

    [Fact]
    public void EveryUnityObjectCreated_IsListed_AndDestroyedInItsDispose()
    {
        var creates = new Regex(@"([\w.]+(?:\s*=\s*_staging\[k\])?)\s*\??=\s*new (Texture2D|Material|Mesh|TerrainData|RenderTexture|GameObject)\b(?!\[)");
        var listed = Owned.GroupBy(o => o.File).ToDictionary(g => g.Key, g => g.Select(o => o.Field).ToHashSet());
        foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Scripts"), "*.cs"))
        {
            string file = Path.GetFileName(path), src = Script(file);
            foreach (Match m in creates.Matches(src))
            {
                string target = m.Groups[1].Value.Replace("var ", "");
                if (file == "TerrainIsoPresenter.cs" && target is "camGo" or "_light" && m.Groups[2].Value == "GameObject")
                    continue; // children of _root
                if (file == "LiveGroundController.cs" && target == "go")
                    continue; // the indicator object: destroyed via _overlay.gameObject
                Assert.True(listed.TryGetValue(file, out var fields) && fields.Contains(target),
                    $"{file}: '{m.Value}' creates a Unity object not in the Owned list (add it and its Destroy)");
            }
        }
        foreach ((string file, string _, string destroy) in Owned)
            Assert.Contains(destroy, Script(file));
        Assert.Contains("UnityEngine.Object.Destroy(_overlay.gameObject);", Script("LiveGroundController.cs"));
        // Terrain.CreateTerrainGameObject is the chunk's _go: destroyed with it.
        string chunk = Script("TerrainLandChunk.cs");
        Assert.Contains("_go = Terrain.CreateTerrainGameObject(_terrainData);", chunk);
        Assert.Contains("if (_go != null) UnityEngine.Object.Destroy(_go);", chunk);
    }

    [Fact]
    public void ChunkDispose_UndoesEveryCounterAndCeilingOwner_ItAdded()
    {
        string chunk = Script("TerrainLandChunk.cs");
        string dispose = chunk.Substring(chunk.IndexOf("public void Dispose()", StringComparison.Ordinal));
        foreach ((string add, string undo) in new[]
        {
            ("_presenter.ChunkCount++;", "_presenter.ChunkCount--;"),
            ("_presenter.ProxyChunkCount++;", "_presenter.ProxyChunkCount--;"),
            ("_presenter.WaterChunkCount++;", "_presenter.WaterChunkCount--;"),
            ("_presenter.WaterTileCount += _waterTiles;", "_presenter.WaterTileCount -= _waterTiles;"),
            ("_presenter.OccluderTileCount += _occluderTiles;", "_presenter.OccluderTileCount -= _occluderTiles;"),
            ("_presenter.SpriteCeiling.Publish(d.ChunkX0, d.ChunkY0, d.SunCeiling, d.CornerZ, d.OccluderTop, this);",
             "_presenter.SpriteCeiling?.Remove(_ceiling.Value.X0, _ceiling.Value.Y0, this);"),
        })
        {
            Assert.Contains(add, chunk);
            Assert.True(chunk.Contains(undo), $"missing undo for {add}");
        }
        Assert.Contains("DisposeProxy();", dispose);
        // A failed attach disposes what it made before reporting (fail closed, nothing half-attached).
        Assert.Contains("Dispose();\n                _presenter.ReportFailure($\"terrain chunk {name}: {e.GetType().Name}: {e.Message}\");", chunk);
    }

    [Fact]
    public void PresenterAndCeilingDispose_RestoreCameraKeywordGlobalsAndSeams()
    {
        string presenter = Script("TerrainIsoPresenter.cs");
        string pd = presenter.Substring(presenter.IndexOf("public void Dispose()", StringComparison.Ordinal));
        foreach (string s in new[]
        {
            "if (_root == null)\n                return;", "Camera.onPreCull -= OnPreCull;", "_main.cullingMask = _savedMask;",
            "_main.clearFlags = _savedClear;", "Water?.Dispose();", "SpriteCeiling?.Dispose();", "_root = null;"
        })
            Assert.Contains(s, pd);
        Assert.Single(Regex.Matches(presenter, @"Camera\.onPreCull \+= OnPreCull;"));

        string ceiling = Script("SpriteShadowCeiling.cs");
        string cd = ceiling.Substring(ceiling.IndexOf("public void Dispose()", StringComparison.Ordinal));
        foreach (string s in new[]
        {
            "if (_texture == null)\n                return;",
            "if (!_keywordWas)\n                Shader.DisableKeyword(Keyword);",
            "MeshBuilder.FootMetadata = _footWas;",
            "if (ReferenceEquals(MeshBuilder.ShadowCasters, this))\n                MeshBuilder.ShadowCasters = _castersWas;",
            "Shader.SetGlobalTexture(CeilId, Texture2D.whiteTexture);",
            "Shader.SetGlobalFloat(CeilOnId, 0f);",
            "_owners.Clear();", "_core.Clear();", "_texture = null;", "_staging[k] = null;"
        })
            Assert.Contains(s, cd);
        // The saved state is captured before it is changed.
        int saveKw = ceiling.IndexOf("_keywordWas = Shader.IsKeywordEnabled(Keyword);", StringComparison.Ordinal);
        int setKw = ceiling.IndexOf("Shader.EnableKeyword(Keyword);", StringComparison.Ordinal);
        int saveFoot = ceiling.IndexOf("_footWas = MeshBuilder.FootMetadata;", StringComparison.Ordinal);
        int setFoot = ceiling.IndexOf("MeshBuilder.FootMetadata = true;", StringComparison.Ordinal);
        Assert.True(saveKw > 0 && saveKw < setKw && saveFoot > 0 && saveFoot < setFoot);
        // Only the ceiling turns the water/terrain receive on.
        foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets"), "*.cs", SearchOption.AllDirectories))
            if (File.ReadAllText(path).Contains("CeilOnId, 1f"))
                Assert.EndsWith("SpriteShadowCeiling.cs", path);
    }

    [Fact]
    public void Controller_SwapsTheEmitterBeforeDisposingTerrain_AndDisposesOnDetachAndDispose()
    {
        string c = Script("LiveGroundController.cs");
        int swap = c.IndexOf("_world.LandEmitter = kind == GroundBackendKind.Mesh ? _mesh : null;", StringComparison.Ordinal);
        int dispose = c.IndexOf("DisposeTerrain();", swap, StringComparison.Ordinal);
        Assert.True(swap > 0 && dispose > swap, "the streamer drops the terrain chunks before the presenter goes");
        Assert.Contains("_switch.Detach();\n            DisposeTerrain();", c);
        Assert.Contains("_switch.Dispose(); // puts the original land back on a still-attached world\n            DisposeTerrain();", c);
        Assert.Contains("if (_terrain == null || _terrain.IsDisposed)", c);
        Assert.Contains("_terrain?.Dispose();\n            _terrain = null;", c);
    }

    [Fact]
    public void FailClosedPaths_SayWhy()
    {
        string presenter = Script("TerrainIsoPresenter.cs"), water = Script("TerrainWater.cs"), ceiling = Script("SpriteShadowCeiling.cs");
        foreach (string s in new[]
        {
            "error = \"no world or main camera\";",
            "error = $\"shader '{ShaderName}' is not in this build or not supported\";",
            "error = $\"presenter: {e.GetType().Name}: {e.Message}\";",
            "OcclusionError = $\"shader '{DepthShaderName}' is not in this build or not supported\";",
            "string spriteWhy = \"light isolated\";",
        })
            Assert.Contains(s, presenter);
        Assert.Contains("error = $\"shader '{ShaderName}' is not in this build or not supported\";", water);
        Assert.Contains("error = \"RHalf textures not supported\";", ceiling);
        Assert.Contains("error = $\"{e.GetType().Name}: {e.Message}\";", ceiling);
        string controller = Script("LiveGroundController.cs");
        Assert.Contains("return $\"water off: {t.WaterError}\";", controller);
        Assert.Contains("return $\"occlusion off: {t.OcclusionError}\";", controller);
        Assert.Contains("return $\"sprites unshadowed: {t.SpriteShadowError}\";", controller);
        Assert.Contains(": $\"light ISOLATED: {_terrain.LightIsolatedWhy}\");", controller);
    }

    [Fact]
    public void Shaders_ShipThroughResources_WithStableNamesAndNoSettingsEdits()
    {
        string res = Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Shaders", "Resources");
        foreach ((string file, string name, string script, string constant) in new[]
        {
            ("LiveGroundCompareTerrain.shader", "LiveGroundCompare/TerrainLit", "TerrainIsoPresenter.cs", "LitShaderResource = \"LiveGroundCompareTerrain\""),
            ("LiveGroundCompareWater.shader", "LiveGroundCompare/Water", "TerrainWater.cs", "ShaderResource = \"LiveGroundCompareWater\""),
            ("LiveGroundCompareDepthKey.shader", "LiveGroundCompare/DepthKey", "TerrainIsoPresenter.cs", "DepthShaderResource = \"LiveGroundCompareDepthKey\""),
        })
        {
            string path = Path.Combine(res, file);
            Assert.True(File.Exists(path) && File.Exists(path + ".meta"), file);
            Assert.Contains($"Shader \"{name}\"", File.ReadAllText(path));
            Assert.Contains(constant, Script(script));
        }
        // The include sits next to Resources (not a shader asset itself) and every Resources shader finds it.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Shaders", "LiveGroundLight.cginc")));
        foreach (string s in Directory.GetFiles(res, "*.shader"))
            if (!s.EndsWith("DepthKey.shader"))
                Assert.Contains("#include \"../LiveGroundLight.cginc\"", File.ReadAllText(s));
        foreach (string path in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare"), "*.cs", SearchOption.AllDirectories))
        {
            string s = File.ReadAllText(path);
            Assert.DoesNotContain("QualitySettings.", s.Replace("QualitySettings.activeColorSpace", ""));
            Assert.DoesNotContain("GraphicsSettings.", s);
            Assert.DoesNotContain("InitializeOnLoad", s);
        }
    }
}
