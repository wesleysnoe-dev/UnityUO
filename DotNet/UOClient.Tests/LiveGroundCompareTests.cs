using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.LiveGroundCompare;
using UOClient.Unity;

namespace UOClient.Tests;

public class LiveGroundCompareTests
{
    private const int T = LiveGroundProjection.Top, R = LiveGroundProjection.Right, B = LiveGroundProjection.Bottom, L = LiveGroundProjection.Left;

    // ---- projection ----

    [Fact]
    public void Constants_MatchProduction()
    {
        Assert.Equal(22, LiveGroundProjection.TileHalf);
        Assert.Equal(4, LiveGroundProjection.ZStep);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Corners_MatchIsoCompositorPlacementRect(int scale)
    {
        // A stretched tile's PlacementRect is its corners' bounding box (plus 1px padding).
        DrawItem it = LiveGroundProjection.LandItem(1000, 1200, 3, 10, -5, 20, 7, hasTexmap: true);
        Assert.True(it.Stretched);
        (int left, int top, int width, int height) = IsoCompositor.PlacementRect(it, scale, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        int[] z = { 10, -5, 20, 7 };
        for (int c = 0; c < 4; c++)
        {
            (float x, float y) = LiveGroundProjection.Corner(it.X, it.Y, c, z[c], scale);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        Assert.Equal(left + 1, minX);
        Assert.Equal(top + 1, minY);
        Assert.Equal(width - 2, maxX - minX);
        Assert.Equal(height - 2, maxY - minY);
    }

    [Fact]
    public void TopCorner_IsIsoCompositorAnchor()
    {
        DrawItem it = LiveGroundProjection.LandItem(37, 91, 3, 12, 12, 12, 12, hasTexmap: true);
        (int ax, int ay) = IsoCompositor.Anchor(it, 2);
        Assert.Equal((ax, (float)ay), (LiveGroundProjection.Corner(37, 91, T, 12, 2).X, LiveGroundProjection.Corner(37, 91, T, 12, 2).Y));
    }

    [Fact]
    public void FlatSpriteDiamond_MatchesLandSpriteRect()
    {
        // Flat land is a 44x44 sprite at the top-left of its rect; the diamond's corners are its edge midpoints.
        DrawItem it = LiveGroundProjection.LandItem(5, 9, 3, 0, 0, 0, 0, hasTexmap: true);
        Assert.False(it.Stretched);
        (int left, int top, int w, int h) = IsoCompositor.PlacementRect(it, 1, 44, 44);
        for (int c = 0; c < 4; c++)
        {
            (float x, float y) = LiveGroundProjection.Corner(5, 9, c, 0, 1);
            Assert.Equal(left + LiveGroundProjection.SpriteU[c] * w, x);
            Assert.Equal(top + LiveGroundProjection.SpriteV[c] * h, y);
        }
    }

    [Fact]
    public void Triangulation_IsRightLeftDiagonal()
    {
        Assert.Equal(new[] { T, R, L, R, B, L }, LiveGroundProjection.Triangles);
    }

    [Theory]
    [InlineData(0f, 0f, 10f)] // top corner
    [InlineData(1f, 0f, 20f)] // right corner
    [InlineData(1f, 1f, 40f)] // bottom corner
    [InlineData(0f, 1f, 0f)]  // left corner
    public void HeightAt_Corners(float u, float v, float expected)
    {
        Assert.Equal(expected, LiveGroundProjection.HeightAt(10, 20, 40, 0, u, v), 4);
    }

    [Fact]
    public void HeightAt_EdgesAreLinear()
    {
        Assert.Equal(15f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.5f, 0f), 4); // top-right edge
        Assert.Equal(30f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 1f, 0.5f), 4); // right-bottom edge
        Assert.Equal(20f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.5f, 1f), 4); // bottom-left edge
        Assert.Equal(5f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0f, 0.5f), 4);  // left-top edge
    }

    [Fact]
    public void HeightAt_CenterLiesOnRightLeftDiagonal()
    {
        // Split right-left: the center is the right/left midpoint, not the top/bottom one.
        Assert.Equal(10f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.5f, 0.5f), 4);   // (20 + 0) / 2
        Assert.NotEqual(25f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.5f, 0.5f));    // not (10 + 40) / 2
        // Interior points on each side of the diagonal use their own triangle.
        Assert.Equal(10f + 0.25f * 10f + 0.25f * -10f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.25f, 0.25f), 4);
        Assert.Equal(40f + 0.25f * -20f + 0.25f * -40f, LiveGroundProjection.HeightAt(10, 20, 40, 0, 0.75f, 0.75f), 4);
    }

    [Fact]
    public void AverageZ_UsesFlatterDiagonal()
    {
        Assert.Equal(15, LiveGroundProjection.AverageZ(10, 0, 20, 40)); // |10-20| <= |40-0| -> top/bottom
        Assert.Equal(5, LiveGroundProjection.AverageZ(0, 4, 40, 6));    // |0-40| > |6-4| -> left/right
    }

    [Fact]
    public void Shade_FlatIsOne_SlopedDiffersPerFace()
    {
        Assert.Equal(1f, LiveGroundProjection.TriangleShade(new[] { 5, 5, 5, 5 }, 0), 4);
        int[] z = { 0, 20, 20, 0 };
        Assert.NotEqual(1f, LiveGroundProjection.TriangleShade(z, 0));
        float s = LiveGroundProjection.TriangleShade(z, 1);
        Assert.InRange(s, 0.7f, 1.25f);
    }

    // ---- real map patch ----

    private static RealMapPatch Synthetic(int x0, int y0, int w, int h, int mapW = 64, int mapH = 64) =>
        new RealMapPatch(0, x0, y0, w, h, mapW, mapH, (x, y) => new LandCell { Graphic = (ushort)(x * 100 + y), Z = (sbyte)((x * 3 + y * 7) % 50 - 20) });

    private static sbyte Zof(int x, int y) => (sbyte)((x * 3 + y * 7) % 50 - 20);

    [Fact]
    public void Patch_CornersComeFromNeighbors()
    {
        RealMapPatch p = Synthetic(10, 20, 4, 3);
        (sbyte t, sbyte r, sbyte b, sbyte l) = p.Corners(1, 2);
        Assert.Equal(Zof(11, 22), t);
        Assert.Equal(Zof(12, 22), r);
        Assert.Equal(Zof(12, 23), b);
        Assert.Equal(Zof(11, 23), l);
        Assert.Equal((ushort)(11 * 100 + 22), p.Graphic(1, 2));
    }

    [Fact]
    public void Patch_EdgeCornersClampAtMapBorder()
    {
        // Patch touching the map's last row/column: the extra corner row/column repeats the edge (IsoCompositor.GetLandZ).
        RealMapPatch p = Synthetic(60, 61, 4, 3);
        Assert.Equal(Zof(63, 61), p.CornerZ(4, 0));
        Assert.Equal(Zof(63, 63), p.CornerZ(4, 3));
        Assert.Equal(Zof(62, 63), p.CornerZ(2, 3));
    }

    [Fact]
    public void Patch_HeightAtMatchesCornersAndInterior()
    {
        RealMapPatch p = Synthetic(10, 20, 4, 4);
        (sbyte t, sbyte r, sbyte b, sbyte l) = p.Corners(2, 1);
        Assert.Equal((float)t, p.HeightAt(12f, 21f), 4);
        Assert.Equal((float)r, p.HeightAt(13f, 21f), 4);
        Assert.Equal((float)b, p.HeightAt(13f, 22f), 4);
        Assert.Equal((float)l, p.HeightAt(12f, 22f), 4);
        Assert.Equal(LiveGroundProjection.HeightAt(t, r, b, l, 0.3f, 0.6f), p.HeightAt(12.3f, 21.6f), 4);
        Assert.Equal((float)p.CornerZ(4, 4), p.HeightAt(14f, 24f), 4); // far patch corner
    }

    [Theory]
    [InlineData(-1, 0, 4, 4)]
    [InlineData(0, 0, 0, 4)]
    [InlineData(0, 0, RealMapPatch.MaxSide + 1, 4)]
    [InlineData(62, 0, 4, 4)] // past the map's right edge
    public void Patch_RejectsOutOfBounds(int x0, int y0, int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Synthetic(x0, y0, w, h));
    }

    // ---- mesh land ----

    private static Bitmap32 Solid(int size, uint argb)
    {
        var b = new Bitmap32(size, size);
        Array.Fill(b.Pixels, argb);
        return b;
    }

    [Fact]
    public void Mesh_StretchedTileUsesExactCornersAndTriangles()
    {
        DrawItem it = LiveGroundProjection.LandItem(100, 200, 3, 10, -5, 20, 7, hasTexmap: true);
        var mb = new MeshLandBuilder(2);
        mb.AddTile(it, Solid(64, 0xFF336699), texmap: true, depth: -3.5f);
        LandMeshData d = mb.Finish();

        Assert.Equal(6, d.VertexCount);
        int[] z = { 10, -5, 20, 7 };
        for (int v = 0; v < 6; v++)
        {
            int c = LiveGroundProjection.Triangles[v];
            (float x, float y) = LiveGroundProjection.Corner(100, 200, c, z[c], 2);
            Assert.Equal(x, d.X[v]);
            Assert.Equal(y, d.Y[v]);
            Assert.Equal(-3.5f, d.Depth[v]); // the tile's production depth: sorting unchanged
        }
        Assert.Equal(LiveGroundProjection.TriangleShade(z, 0), d.Shade[0]);
        Assert.Equal(LiveGroundProjection.TriangleShade(z, 1), d.Shade[3]);
    }

    [Fact]
    public void Mesh_UnstretchedTileIsFlatAtTopZ()
    {
        // Sloped but without a texmap: the compositor draws the flat sprite at Z, so the mesh does too.
        DrawItem it = LiveGroundProjection.LandItem(4, 4, 3, 10, 0, 30, 0, hasTexmap: false);
        Assert.False(it.Stretched);
        var mb = new MeshLandBuilder(1);
        mb.AddTile(it, Solid(44, 0xFF00FF00), texmap: false, depth: 0f);
        LandMeshData d = mb.Finish();
        for (int v = 0; v < 6; v++)
        {
            int c = LiveGroundProjection.Triangles[v];
            Assert.Equal(LiveGroundProjection.Corner(4, 4, c, 10, 1).Y, d.Y[v]);
            Assert.Equal(1f, d.Shade[v]);
        }
    }

    [Fact]
    public void Mesh_NeighborTilesShareCornerPositions()
    {
        var p = Synthetic(10, 10, 2, 1);
        var mb = new MeshLandBuilder(1);
        var tex = Solid(64, 0xFFFFFFFF);
        for (int tx = 0; tx < 2; tx++)
        {
            (sbyte t, sbyte r, sbyte b, sbyte l) = p.Corners(tx, 0);
            mb.AddTile(LiveGroundProjection.LandItem(10 + tx, 10, 3, t, r, b, l, hasTexmap: true), tex, true, 0f);
        }
        LandMeshData d = mb.Finish();
        // Tile 0's right corner (vertex 1) is tile 1's top corner (vertex 6); tile 0's bottom (4) is tile 1's left (8).
        Assert.Equal((d.X[1], d.Y[1]), (d.X[6], d.Y[6]));
        Assert.Equal((d.X[4], d.Y[4]), (d.X[8], d.Y[8]));
    }

    [Fact]
    public void Mesh_AtlasUvsStayInsideArtAndDedupe()
    {
        var tex = Solid(64, 0xFF112233);
        var mb = new MeshLandBuilder(1);
        mb.AddTile(LiveGroundProjection.LandItem(0, 0, 3, 0, 4, 8, 4, true), tex, true, 0f);
        mb.AddTile(LiveGroundProjection.LandItem(1, 0, 3, 4, 8, 12, 8, true), tex, true, 0f);
        LandMeshData d = mb.Finish();
        Assert.Equal(MeshLandBuilder.AtlasWidth, d.AtlasWidth);
        Assert.Equal(128, d.AtlasHeight); // one 64px texmap + 2px gutter, packed once, rounded up to 64 rows
        foreach (float u in d.U) Assert.InRange(u, 1f / 1024, 65f / 1024);
        foreach (float v in d.V) Assert.InRange(v, 0f, 1f);
        // Top-left texel of the atlas art is the art's pixel (RGBA, rows bottom-up).
        int o = ((d.AtlasHeight - 1 - 1) * d.AtlasWidth + 1) * 4;
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0xFF }, d.AtlasRgba.AsSpan(o, 4).ToArray());
    }

    [Fact]
    public void Mesh_RejectsNonLand()
    {
        var it = new DrawItem { Kind = DrawKind.Static, X = 1, Y = 1 };
        Assert.Throws<ArgumentException>(() => new MeshLandBuilder(1).AddTile(it, Solid(44, 0), false, 0f));
    }

    [Fact]
    public void Mesh_EmptyFinishHasNoGeometry()
    {
        LandMeshData d = new MeshLandBuilder(1).Finish();
        Assert.Equal(0, d.VertexCount);
        Assert.Null(d.AtlasRgba);
    }

    // ---- backend choice, toggle and guard ----

    [Theory]
    [InlineData("original", GroundBackendKind.Original)]
    [InlineData("MESH", GroundBackendKind.Mesh)]
    [InlineData(" terrain ", GroundBackendKind.Terrain)]
    public void Backend_Parses(string text, GroundBackendKind expected)
    {
        Assert.True(GroundBackend.TryParse(text, out GroundBackendKind k));
        Assert.Equal(expected, k);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("meshy")]
    [InlineData("1")]
    [InlineData("-server")]
    public void Backend_MalformedIsOriginal(string? text)
    {
        Assert.False(GroundBackend.TryParse(text, out GroundBackendKind k));
        Assert.Equal(GroundBackendKind.Original, k);
    }

    [Fact]
    public void Toggle_CyclesAvailableBackendsOnly()
    {
        Assert.Equal(GroundBackendKind.Mesh, GroundBackend.Next(GroundBackendKind.Original));
        Assert.Equal(GroundBackendKind.Terrain, GroundBackend.Next(GroundBackendKind.Mesh));
        Assert.Equal(GroundBackendKind.Original, GroundBackend.Next(GroundBackendKind.Terrain));
        foreach (GroundBackendKind k in Enum.GetValues<GroundBackendKind>())
            Assert.True(GroundBackend.IsAvailable(k));
    }

    [Fact]
    public void Toggle_DivertsOnlyUnbakedLand_EntitiesUntouched()
    {
        var land = new DrawItem { Kind = DrawKind.Land, X = 5, Y = 5, Z = 0 };
        var stat = new DrawItem { Kind = DrawKind.Static, X = 5, Y = 5, Z = 0, Graphic = 0x0FA0 };
        var mobile = new DrawItem { Kind = DrawKind.Mobile, X = 5, Y = 6, Z = 0 };
        DrawItem[] before = { stat, mobile };

        foreach (GroundBackendKind k in Enum.GetValues<GroundBackendKind>())
        {
            Assert.False(GroundBackend.Diverts(k, stat, false));
            Assert.False(GroundBackend.Diverts(k, mobile, false));
            Assert.False(GroundBackend.Diverts(k, land, baked: true));
        }
        Assert.False(GroundBackend.Diverts(GroundBackendKind.Original, land, false));
        Assert.True(GroundBackend.Diverts(GroundBackendKind.Mesh, land, false));
        Assert.True(GroundBackend.Diverts(GroundBackendKind.Terrain, land, false));

        // Cycling the backend never touches entity placements (Diverts takes them by readonly reference).
        GroundBackendKind cur = GroundBackendKind.Original;
        for (int i = 0; i < 5; i++)
        {
            cur = GroundBackend.Next(cur);
            GroundBackend.Diverts(cur, stat, false);
            GroundBackend.Diverts(cur, mobile, false);
        }
        Assert.Equal(before, new[] { stat, mobile });
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("uosmallz.duckdns.org", false)]
    [InlineData("uosmallz", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("localhost.uosmallz.example", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Guard_LocalHosts(string? host, bool local)
    {
        Assert.Equal(local, LiveGroundHostGuard.IsLocalHost(host));
    }

    [Fact]
    public void Guard_AgreesWithClientSettingsIsLocal()
    {
        foreach (string h in new[] { "127.0.0.1", "localhost", "::1", "uosmallz.duckdns.org", "10.0.0.1" })
            Assert.Equal(new ClientSettings { host = h }.IsLocal, LiveGroundHostGuard.IsLocalHost(h));
    }

    [Fact]
    public void Guard_FailsClosed()
    {
        Assert.Equal(GroundBackendKind.Mesh, LiveGroundHostGuard.Resolve(true, "127.0.0.1", GroundBackendKind.Mesh, out string ok));
        Assert.Null(ok);

        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(true, "uosmallz.duckdns.org", GroundBackendKind.Mesh, out string live));
        Assert.Contains("non-local", live);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(false, "127.0.0.1", GroundBackendKind.Mesh, out string noFlag));
        Assert.Contains("-liveground", noFlag);
        Assert.Equal(GroundBackendKind.Terrain, LiveGroundHostGuard.Resolve(true, "127.0.0.1", GroundBackendKind.Terrain, out string terrain));
        Assert.Null(terrain);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(true, "uosmallz.duckdns.org", GroundBackendKind.Terrain, out string liveTerrain));
        Assert.Contains("non-local", liveTerrain);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(false, "uosmallz.duckdns.org", GroundBackendKind.Original, out string orig));
        Assert.Null(orig);
    }

    // ---- switch: toggle, host re-check, cleanup ----

    [Fact]
    public void Switch_AppliesGuardedChoiceOnAttachAndToggle()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Mesh, applied.Add);
        Assert.Null(sw.Toggle()); // nothing attached yet
        Assert.Empty(applied);

        Assert.Equal("Ground: Mesh", sw.Attach("127.0.0.1"));
        Assert.Equal(GroundBackendKind.Mesh, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Terrain, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        Assert.Equal(new[] { GroundBackendKind.Mesh, GroundBackendKind.Terrain, GroundBackendKind.Original }, applied);
    }

    [Fact]
    public void Switch_LiveHostAttachRefusesAndTogglesStayOriginal()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Mesh, applied.Add);
        sw.Attach("127.0.0.1");
        string status = sw.Attach("uosmallz.duckdns.org"); // logged in elsewhere from the login screen
        Assert.Contains("non-local", status);
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        Assert.Equal(GroundBackendKind.Original, applied[^1]);
    }

    [Fact]
    public void Switch_DisposeRestoresOriginalOnceAndIgnoresLaterCalls()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Mesh, applied.Add);
        sw.Attach("localhost");
        sw.Dispose();
        sw.Dispose();
        Assert.True(sw.IsDisposed);
        Assert.Equal(new[] { GroundBackendKind.Mesh, GroundBackendKind.Original }, applied);
        Assert.Null(sw.Attach("127.0.0.1"));
        Assert.Null(sw.Toggle());
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void Switch_DetachedDisposeAppliesNothing()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Mesh, applied.Add);
        sw.Attach("127.0.0.1");
        sw.Detach(); // world already torn down: no emitter to remove
        sw.Dispose();
        Assert.Equal(new[] { GroundBackendKind.Mesh }, applied);
    }

    // ---- launch arguments ----

    [Fact]
    public void Launch_DefaultIsOriginal()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "Unity UO.exe", "-server", "uosmallz.duckdns.org:2593" });
        Assert.False(o.LiveGround);
        Assert.Null(o.GroundBackend);
        GroundBackend.TryParse(o.GroundBackend, out GroundBackendKind k);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(o.LiveGround, "uosmallz.duckdns.org", k, out _));
    }

    [Fact]
    public void Launch_OptInWithBackend()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "x.exe", "-server", "127.0.0.1:2593", "-liveground", "-groundbackend", "mesh" });
        Assert.True(o.LiveGround);
        Assert.Equal("mesh", o.GroundBackend);
        Assert.True(o.ServerFromArguments);
    }

    [Theory]
    [InlineData(new[] { "x.exe", "-liveground", "-groundbackend" }, "")]
    [InlineData(new[] { "x.exe", "-groundbackend", "-liveground" }, "")]
    [InlineData(new[] { "x.exe", "-liveground", "-groundbackend", "bogus" }, "bogus")]
    public void Launch_MalformedBackendFallsBackToOriginal(string[] args, string raw)
    {
        LaunchOptions o = LaunchOptions.Parse(args);
        Assert.True(o.LiveGround);
        Assert.Equal(raw, o.GroundBackend);
        Assert.False(GroundBackend.TryParse(o.GroundBackend, out GroundBackendKind k));
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(o.LiveGround, "127.0.0.1", k, out _));
    }

    [Fact]
    public void Launch_BackendWithoutOptInIsRefused()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "x.exe", "-groundbackend", "mesh" });
        Assert.False(o.LiveGround);
        GroundBackend.TryParse(o.GroundBackend, out GroundBackendKind k);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(o.LiveGround, "127.0.0.1", k, out _));
    }

    // ---- 3-way switch, fallback, cleanup ----

    [Fact]
    public void Switch_CyclesThreeWay()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Mesh, applied.Add);
        sw.Attach("127.0.0.1");
        Assert.Equal("Ground: Terrain", sw.Toggle());
        Assert.Equal(GroundBackendKind.Terrain, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        sw.Toggle();
        Assert.Equal(new[] { GroundBackendKind.Mesh, GroundBackendKind.Terrain, GroundBackendKind.Original, GroundBackendKind.Mesh }, applied);
        Assert.Null(sw.Reason);
    }

    [Fact]
    public void Switch_TerrainApplyFailureFallsBackToOriginalVisibly()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Terrain, k =>
        {
            applied.Add(k);
            return k == GroundBackendKind.Terrain ? "shader 'Legacy Shaders/Diffuse' is not in this build" : null;
        });
        string status = sw.Attach("127.0.0.1");

        Assert.Equal(GroundBackendKind.Original, sw.Active);
        Assert.Equal(GroundBackendKind.Terrain, sw.Requested);
        Assert.Equal(new[] { GroundBackendKind.Terrain, GroundBackendKind.Original }, applied);
        Assert.StartsWith("Ground: Original (Terrain failed: shader", status);
        Assert.Contains("using Original", sw.Reason);
        Assert.True(LiveGroundStatus.IsFallback(sw.Requested, sw.Active, sw.Reason));
        string indicator = LiveGroundStatus.Indicator(sw.Requested, sw.Active, sw.Reason, sw.Host, sw.IsAttached, 0);
        Assert.Contains("requested Terrain", indicator);
        Assert.Contains("active Original", indicator);
        Assert.Contains("FALLBACK: Terrain failed", indicator);
        Assert.Contains("host 127.0.0.1", indicator);

        // The next toggle goes on from what is active (Original → Mesh), and a success clears the reason.
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Mesh, sw.Active);
        Assert.Null(sw.Reason);
    }

    [Fact]
    public void Switch_LateFailureFallsBackOnce()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Terrain, applied.Add);
        Assert.Null(sw.Fail("not attached yet"));
        sw.Attach("localhost");
        Assert.Equal(GroundBackendKind.Terrain, sw.Active);

        string status = sw.Fail("terrain chunk 3,4: ArgumentException: bad");
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        Assert.Contains("Terrain failed: terrain chunk 3,4", status);
        Assert.Null(sw.Fail("again")); // already on Original: nothing more to undo
        Assert.Equal(new[] { GroundBackendKind.Terrain, GroundBackendKind.Original }, applied);
    }

    [Fact]
    public void Switch_DisposeFromTerrainRestoresOriginal()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Terrain, applied.Add);
        sw.Attach("127.0.0.1");
        sw.Dispose();
        Assert.Equal(new[] { GroundBackendKind.Terrain, GroundBackendKind.Original }, applied);
        Assert.Null(sw.Fail("after dispose"));
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public void Switch_TerrainOnLiveHostIsRefusedBeforeApplying()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Terrain, applied.Add);
        string status = sw.Attach("uosmallz.duckdns.org");
        Assert.Equal(new[] { GroundBackendKind.Original }, applied);
        Assert.Contains("non-local", status);
        Assert.Contains("FALLBACK", LiveGroundStatus.Indicator(sw.Requested, sw.Active, sw.Reason, sw.Host, true, 0));
    }

    [Fact]
    public void Status_IndicatorStates()
    {
        string ok = LiveGroundStatus.Indicator(GroundBackendKind.Mesh, GroundBackendKind.Mesh, null, "127.0.0.1", true, 24);
        Assert.Equal("LiveGround TEST | requested Mesh | active Mesh | ok | host 127.0.0.1 | mesh chunks 24 | Ctrl+Shift+G cycles", ok);
        Assert.False(LiveGroundStatus.IsFallback(GroundBackendKind.Mesh, GroundBackendKind.Mesh, null));

        string original = LiveGroundStatus.Indicator(GroundBackendKind.Original, GroundBackendKind.Original, null, "127.0.0.1", true, 0);
        Assert.Contains("| ok |", original);
        Assert.DoesNotContain("chunks", original);

        Assert.Contains("no world", LiveGroundStatus.Indicator(GroundBackendKind.Mesh, GroundBackendKind.Original, null, null, false, 0));
        Assert.Contains("host -", LiveGroundStatus.Indicator(GroundBackendKind.Mesh, GroundBackendKind.Original, null, null, false, 0));
        Assert.Equal("Ground: Mesh", LiveGroundStatus.Line(GroundBackendKind.Mesh, null));
        Assert.Equal("Terrain failed: unknown error; using Original", LiveGroundStatus.FailureReason(GroundBackendKind.Terrain, " "));
    }

    [Fact]
    public void Status_ZeroChunksRightAfterASwapSaysStreaming()
    {
        // The streamer rebuilds on later frames, so the toggle-instant line has 0 chunks; it must not read as "broken".
        string swapped = LiveGroundStatus.Indicator(GroundBackendKind.Terrain, GroundBackendKind.Terrain, null, "127.0.0.1", true, 0);
        Assert.Contains("| terrain chunks 0 (streaming) |", swapped);
        Assert.Contains("| ok |", swapped);

        string attached = LiveGroundStatus.Indicator(GroundBackendKind.Terrain, GroundBackendKind.Terrain, null, "127.0.0.1", true, 9);
        Assert.Contains("| terrain chunks 9 |", attached);
        Assert.DoesNotContain("streaming", attached);

        Assert.DoesNotContain("streaming", LiveGroundStatus.Indicator(GroundBackendKind.Original, GroundBackendKind.Original, null, "127.0.0.1", true, 0));
        Assert.DoesNotContain("streaming", LiveGroundStatus.Indicator(GroundBackendKind.Mesh, GroundBackendKind.Mesh, null, null, false, 0));
    }

    // ---- terrain land ----

    private const int Chunk = TerrainLandBuilder.Tiles, S = TerrainLandBuilder.SamplesPerTile;

    /// <summary>A bumpy map height for corner (x, y), within sbyte.</summary>
    private static int MapZ(int x, int y) => (x * 7 - y * 5 + (x * y) % 11) % 60 - 10;

    private static TerrainLandBuilder FilledChunk(int x0, int y0, Func<int, int, bool>? present = null, int texels = 8)
    {
        var tb = new TerrainLandBuilder(x0, y0, texels);
        for (int j = 0; j < Chunk; j++)
            for (int i = 0; i < Chunk; i++)
            {
                if (present != null && !present(i, j))
                    continue;
                int x = x0 + i, y = y0 + j;
                DrawItem it = LiveGroundProjection.LandItem(x, y, 3, MapZ(x, y), MapZ(x + 1, y), MapZ(x + 1, y + 1), MapZ(x, y + 1), hasTexmap: (i + j) % 2 == 0);
                tb.AddTile(it, Solid(it.Stretched ? 64 : 44, 0xFF000000u | (uint)(i * 16 << 16) | (uint)(j * 16 << 8)), it.Stretched);
            }
        return tb;
    }

    [Fact]
    public void Terrain_ResolutionIsUnityValid()
    {
        Assert.Equal(33, TerrainLandBuilder.HeightmapResolution); // 2^n + 1, Unity's minimum
        Assert.Equal(32, TerrainLandBuilder.HolesResolution);
        Assert.Equal(16, TerrainLandBuilder.Tiles); // WorldStreamer.ChunkSize (Unity-side, not compiled here)
    }

    [Fact]
    public void Terrain_HeightmapCornersAreTheMapHeights()
    {
        TerrainLandData d = FilledChunk(1600, 1504).Finish();
        Assert.Equal(256, d.TileCount);
        for (int j = 0; j <= Chunk; j++)
            for (int i = 0; i <= Chunk; i++)
            {
                int z = MapZ(1600 + i, 1504 + j);
                Assert.Equal(z, d.CornerZ[i, j]);
                // Row runs along terrain +Z = tile y descending.
                Assert.Equal(TerrainLandBuilder.Normalize(z), d.Heights[(Chunk - j) * S, i * S], 6);
            }
    }

    [Fact]
    public void Terrain_InteriorSamplesLieOnTheRightLeftSplit()
    {
        TerrainLandData d = FilledChunk(0, 0).Finish();
        for (int row = 0; row < TerrainLandBuilder.HeightmapResolution; row++)
            for (int col = 0; col < TerrainLandBuilder.HeightmapResolution; col++)
            {
                float tx = (float)col / S, ty = Chunk - (float)row / S;
                int i = Math.Min(Chunk - 1, (int)tx), j = Math.Min(Chunk - 1, (int)ty);
                float z = LiveGroundProjection.HeightAt(MapZ(i, j), MapZ(i + 1, j), MapZ(i + 1, j + 1), MapZ(i, j + 1), tx - i, ty - j);
                Assert.Equal(TerrainLandBuilder.Normalize(z), d.Heights[row, col], 5);
            }

        // The tile center sits on the RIGHT–LEFT diagonal: the mean of right and left, not of top and bottom.
        int ci = 3, cj = 5;
        float center = d.Heights[(Chunk - cj) * S - 1, ci * S + 1];
        Assert.Equal(TerrainLandBuilder.Normalize((MapZ(ci + 1, cj) + MapZ(ci, cj + 1)) / 2f), center, 5);
    }

    [Fact]
    public void Terrain_SeamsMatchBetweenNeighborChunks()
    {
        TerrainLandData a = FilledChunk(0, 0).Finish(), right = FilledChunk(Chunk, 0).Finish(), below = FilledChunk(0, Chunk).Finish();
        int last = TerrainLandBuilder.HeightmapResolution - 1;
        for (int k = 0; k <= last; k++)
        {
            Assert.Equal(a.Heights[k, last], right.Heights[k, 0], 6); // tile x seam
            Assert.Equal(a.Heights[0, k], below.Heights[last, k], 6); // tile y seam (row 0 = tile y max)
        }
    }

    [Fact]
    public void Terrain_PositionAndSizePutSamplesAtTheirProjectedCorners()
    {
        TerrainLandData d = FilledChunk(320, 480).Finish();
        (float px, float py, float pz) = d.Position;
        (float sx, float sy, float sz) = d.Size;
        Assert.Equal((16f, 16f), (sx, sz));
        for (int j = 0; j <= Chunk; j += 4)
            for (int i = 0; i <= Chunk; i += 4)
            {
                int row = (Chunk - j) * S, col = i * S;
                // Where Unity puts the sample: position + (col, height, row) in terrain units.
                float wx = px + (float)col / S, wy = py + d.Heights[row, col] * sy, wz = pz + (float)row / S;
                (float x, float y) = TerrainLandBuilder.ToWorldScreen(wx, wy, wz, 2);
                (float ex, float ey) = LiveGroundProjection.Corner(320 + i, 480 + j, LiveGroundProjection.Top, MapZ(320 + i, 480 + j), 2);
                Assert.Equal(ex, x, 2);
                Assert.Equal(ey, y, 1); // 16-bit-free here; Unity's own storage adds ~0.01 Z
            }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Terrain_ProjectionMatchesLandCornersAndSortsByNearness(int scale)
    {
        DrawItem it = LiveGroundProjection.LandItem(1000, 1200, 3, 10, -5, 20, 7, hasTexmap: true);
        int[] z = { 10, -5, 20, 7 };
        (int dx, int dy)[] offset = { (0, 0), (1, 0), (1, 1), (0, 1) };
        for (int c = 0; c < 4; c++)
        {
            (float X, float Y, float Z) p = TerrainLandBuilder.TerrainPoint(it.X + offset[c].dx, it.Y + offset[c].dy, z[c]);
            (float x, float y) = TerrainLandBuilder.ToWorldScreen(p.X, p.Y, p.Z, scale);
            (float ex, float ey) = LiveGroundProjection.Corner(it.X, it.Y, c, z[c], scale);
            Assert.Equal(ex, x, 2);
            Assert.Equal(ey, y, 2);
        }

        // One tile right, one down and 11 Z up is the same pixel (the line of sight) and strictly nearer.
        var a = TerrainLandBuilder.TerrainPoint(50, 60, 0);
        var b = TerrainLandBuilder.TerrainPoint(51, 61, 11);
        Assert.Equal(TerrainLandBuilder.ToWorldScreen(a.X, a.Y, a.Z, scale).X, TerrainLandBuilder.ToWorldScreen(b.X, b.Y, b.Z, scale).X, 3);
        Assert.Equal(TerrainLandBuilder.ToWorldScreen(a.X, a.Y, a.Z, scale).Y, TerrainLandBuilder.ToWorldScreen(b.X, b.Y, b.Z, scale).Y, 2);
        Assert.True(TerrainLandBuilder.Nearness(b.X, b.Y, b.Z) > TerrainLandBuilder.Nearness(a.X, a.Y, a.Z));
    }

    [Fact]
    public void Terrain_MissingTilesAreHoles()
    {
        TerrainLandData d = FilledChunk(0, 0, (i, j) => !(i == 2 && j == 5) && j != 15).Finish();
        Assert.Equal(256 - 16 - 1, d.TileCount);
        for (int row = 0; row < TerrainLandBuilder.HolesResolution; row++)
            for (int col = 0; col < TerrainLandBuilder.HolesResolution; col++)
            {
                int i = col / S, j = Chunk - 1 - row / S;
                bool expected = !(i == 2 && j == 5) && j != 15;
                Assert.Equal(expected, d.Solid[row, col]);
            }
        // Tile y 15 is the first holes row (terrain +Z starts at the chunk's tile-y-max edge).
        Assert.False(d.Solid[0, 0]);
        Assert.True(d.Solid[TerrainLandBuilder.HolesResolution - 1, 0]);
    }

    [Fact]
    public void Terrain_EmptyChunkBuildsNothing()
    {
        TerrainLandData d = new TerrainLandBuilder(0, 0, 8).Finish();
        Assert.Equal(0, d.TileCount);
        Assert.Null(d.Rgba);
    }

    [Fact]
    public void Terrain_RejectsForeignAndNonLandTiles()
    {
        var tb = new TerrainLandBuilder(16, 16, 8);
        DrawItem outside = LiveGroundProjection.LandItem(15, 20, 3, 0, 0, 0, 0, hasTexmap: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => tb.AddTile(outside, Solid(44, 0xFF00FF00), false));
        var stat = new DrawItem { Kind = DrawKind.Static, X = 20, Y = 20 };
        Assert.Throws<ArgumentException>(() => tb.AddTile(stat, Solid(44, 0xFF00FF00), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainLandBuilder(0, 0, 0));
    }

    [Fact]
    public void Terrain_TextureMapsEachTileWhereTerrainUvPutsIt()
    {
        const int texels = 8;
        TerrainLandData d = FilledChunk(0, 0, texels: texels).Finish();
        int size = d.TextureSize;
        Assert.Equal(Chunk * texels, size);
        Assert.Equal(size * size * 4, d.Rgba.Length);
        foreach ((int i, int j) in new[] { (0, 0), (15, 0), (0, 15), (7, 9) })
        {
            // Terrain UV (u, v) = (local x, local z) / 16, local z = 16 - tile y; texture rows are bottom-up.
            float u = (i + 0.5f) / Chunk, v = (Chunk - (j + 0.5f)) / Chunk;
            int px = (int)(u * size), row = (int)(v * size);
            int o = (row * size + px) * 4;
            Assert.Equal((byte)(i * 16), d.Rgba[o]);
            Assert.Equal((byte)(j * 16), d.Rgba[o + 1]);
            Assert.Equal(255, d.Rgba[o + 3]);
        }
    }

    [Fact]
    public void Terrain_SpriteArtIsUnrotatedLikeTheMesh()
    {
        // A 44x44 sprite whose pixel value encodes its position.
        var b = new Bitmap32(44, 44);
        for (int y = 0; y < 44; y++)
            for (int x = 0; x < 44; x++)
                b.Pixels[y * 44 + x] = (uint)(y * 44 + x);
        for (int c = 0; c < 4; c++)
        {
            // Tile corner c (texmap UV) lands on the sprite's diamond corner c (SpriteU/V), as in the mesh.
            float u = Math.Clamp(LiveGroundProjection.TexmapU[c], 0.01f, 0.99f), v = Math.Clamp(LiveGroundProjection.TexmapV[c], 0.01f, 0.99f);
            uint p = TerrainLandBuilder.SampleArt(b, false, u, v);
            int sx = (int)(p % 44), sy = (int)(p / 44);
            Assert.InRange(sx, LiveGroundProjection.SpriteU[c] * 44 - 2, LiveGroundProjection.SpriteU[c] * 44 + 1);
            Assert.InRange(sy, LiveGroundProjection.SpriteV[c] * 44 - 2, LiveGroundProjection.SpriteV[c] * 44 + 1);
        }
        Assert.Equal(b.Pixels[0], TerrainLandBuilder.SampleArt(b, true, 0f, 0f)); // texmaps map directly
        Assert.Equal(b.Pixels[44 * 44 - 1], TerrainLandBuilder.SampleArt(b, true, 0.999f, 0.999f));
    }

    // ---- terrain water ----

    [Theory]
    [InlineData(TileFlags.Wet, "water", true)]
    [InlineData(TileFlags.Wet | TileFlags.Impassable, " Water ", true)]
    [InlineData(TileFlags.Wet | TileFlags.Surface, "acid", false)]           // Ter Mur acid is Wet but not water
    [InlineData(TileFlags.Wet | TileFlags.Surface, "TerrainFallback", false)]
    [InlineData(TileFlags.Impassable, "water", false)]                      // the name alone is not enough
    [InlineData(TileFlags.Wet, null, false)]
    public void Water_NeedsWetFlagAndWaterName(TileFlags flags, string? name, bool water)
    {
        Assert.Equal(water, WaterTiles.IsWater(flags, name!));
    }

    [Fact]
    public void Water_TableLookupIsBoundsSafe()
    {
        var land = new LandTileData[4];
        land[2] = new LandTileData { Flags = TileFlags.Wet, Name = "water" };
        bool[] table = WaterTiles.Table(land);
        Assert.Equal(new[] { false, false, true, false }, table);
        Assert.True(WaterTiles.Lookup(table, 2));
        Assert.False(WaterTiles.Lookup(table, -1));
        Assert.False(WaterTiles.Lookup(table, 4));
        Assert.False(WaterTiles.Lookup(null!, 2));
    }

    [Fact]
    public void Water_TypedDataAccessFailsClosedWithoutData()
    {
        Assert.False(WaterTiles.TryLand(null!, out LandTileData[] land, out string error));
        Assert.Null(land);
        Assert.Contains("WorldStreamer.Data", error);
    }

    private const uint WaterArgb = 0xFF204080u, LandArgb = 0xFF608020u;

    /// <summary>A full chunk where <paramref name="water"/>(i, j) tiles are water (graphic 0xA8) and the rest land.</summary>
    private static WaterSurfaceBuilder WaterChunk(int x0, int y0, Func<int, int, bool> water, Func<int, int, int>? z = null)
    {
        z ??= MapZ;
        var wb = new WaterSurfaceBuilder(x0, y0);
        for (int j = 0; j < Chunk; j++)
            for (int i = 0; i < Chunk; i++)
            {
                int x = x0 + i, y = y0 + j;
                bool w = water(i, j);
                DrawItem it = LiveGroundProjection.LandItem(x, y, (ushort)(w ? 0xA8 : 3),z(x, y), z(x + 1, y), z(x + 1, y + 1), z(x, y + 1), hasTexmap: false);
                wb.AddTile(it, Solid(44, w ? WaterArgb : LandArgb), false, w);
            }
        return wb;
    }

    [Fact]
    public void Water_SurfaceCoversOnlyWaterTilesAtTheMapsCornerHeights()
    {
        const int x0 = 1600, y0 = 1504;
        WaterSurfaceData d = WaterChunk(x0, y0, (i, j) => i < 6).Finish();
        Assert.Equal(6 * Chunk, d.TileCount);
        Assert.Equal(new List<int> { 0xA8 }, d.Graphics);
        Assert.Equal(7 * (Chunk + 1), d.VertexCount);       // corners touching a water tile
        Assert.Equal(6 * Chunk * 6, d.Indices.Length);      // two triangles per water tile

        for (int v = 0; v < d.VertexCount; v++)
        {
            int tx = (int)Math.Round(d.X[v]), ty = (int)Math.Round(-d.Z[v]);
            Assert.InRange(tx - x0, 0, 6);                  // never past the last water column's right edge
            (float _, float py, float _) = TerrainLandBuilder.TerrainPoint(tx, ty, MapZ(tx, ty));
            Assert.Equal(py, d.Y[v], 4);                    // heights are the map's, as the terrain under it
        }
    }

    [Fact]
    public void Water_TrianglesFaceUpWithTheRightLeftSplit()
    {
        WaterSurfaceData d = WaterChunk(0, 0, (_, _) => true, (_, _) => -5).Finish();
        for (int t = 0; t < d.Indices.Length; t += 3)
        {
            int a = d.Indices[t], b = d.Indices[t + 1], c = d.Indices[t + 2];
            float ux = d.X[b] - d.X[a], uz = d.Z[b] - d.Z[a], vx = d.X[c] - d.X[a], vz = d.Z[c] - d.Z[a];
            Assert.True(uz * vx - ux * vz > 0, $"triangle {t / 3} faces down"); // cross(b - a, c - a).y
        }
        // The first tile's triangles share the RIGHT–LEFT diagonal: right (1,0) and left (0,1) are in both.
        var first = new HashSet<int>(d.Indices[..3]);
        first.IntersectWith(d.Indices[3..6]);
        Assert.Equal(2, first.Count);
        foreach (int v in first)
            Assert.Equal(1f, Math.Abs(d.X[v]) + Math.Abs(d.Z[v]), 4);
    }

    [Fact]
    public void Water_OceanStaticsAreFlatWaterOverWhateverLandIsUnder()
    {
        // All land (sand, graphic 3) with sloped corners; an ocean static at Z -5 on columns 8..15.
        WaterSurfaceBuilder wb = WaterChunk(0, 0, (_, _) => false);
        for (int j = 0; j < Chunk; j++)
            for (int i = 8; i < Chunk; i++)
                wb.AddStaticWater(new DrawItem { Kind = DrawKind.Static, Graphic = 0x1797, X = i, Y = j, Z = -5 }, Solid(44, WaterArgb));
        WaterSurfaceData d = wb.Finish();
        Assert.Equal(8 * Chunk, d.TileCount);
        Assert.Contains(0x1797, d.Graphics);
        (float _, float flat, float _) = TerrainLandBuilder.TerrainPoint(0, 0, -5);
        Assert.All(d.Y, y => Assert.Equal(flat, y, 4));                  // flat at the static's Z, not the land's slope
        Assert.Contains(d.Shore, s => s == 0f);                          // the sand beside it is the shoreline
        Assert.Throws<ArgumentException>(() => wb.AddStaticWater(LiveGroundProjection.LandItem(0, 0, 3, 0, 0, 0, 0, hasTexmap: false), Solid(44, WaterArgb)));
    }

    [Fact]
    public void Water_OceanStaticTableTakesOnlyTheFlatOpenWaterStatics()
    {
        var statics = new StaticTileData[0x3500];
        statics[0x1797] = new StaticTileData { Flags = TileFlags.Wet, Name = "water" };
        statics[0x1798] = new StaticTileData { Flags = 0, Name = "water" };          // not Wet
        statics[0x346E] = new StaticTileData { Flags = TileFlags.Wet, Name = "water" }; // a shoreline edge piece
        statics[0x17A0] = new StaticTileData { Flags = TileFlags.Wet, Name = "water trough" };
        bool[] table = WaterTiles.StaticTable(statics);
        Assert.True(WaterTiles.Lookup(table, 0x1797));
        Assert.False(WaterTiles.Lookup(table, 0x1798));
        Assert.False(WaterTiles.Lookup(table, 0x346E));
        Assert.False(WaterTiles.Lookup(table, 0x17A0));
    }

    [Fact]
    public void Water_ShoreDistanceSeesLandInTheNeighbouringChunk()
    {
        // All water in this chunk; the chunk to the west (x < 0) is land. Without the outside the shore is open water.
        WaterSurfaceBuilder wb = WaterChunk(0, 0, (_, _) => true);
        wb.SetOutside((x, _) => x < 0);
        WaterSurfaceData d = wb.Finish();
        for (int v = 0; v < d.VertexCount; v++)
        {
            int ci = (int)Math.Round(d.X[v]);
            Assert.Equal(Math.Min(ci, WaterSurfaceBuilder.MaxShore) / (float)WaterSurfaceBuilder.MaxShore, d.Shore[v], 4);
        }
    }

    [Fact]
    public void Water_ShoreDistanceGrowsFromTheShorelineAndCaps()
    {
        // Land in columns 0..3, water from column 4: corner column 4 touches land.
        WaterSurfaceData d = WaterChunk(0, 0, (i, _) => i >= 4).Finish();
        for (int v = 0; v < d.VertexCount; v++)
        {
            int ci = (int)Math.Round(d.X[v]);
            float expected = Math.Min(ci - 4, WaterSurfaceBuilder.MaxShore) / (float)WaterSurfaceBuilder.MaxShore;
            Assert.Equal(expected, d.Shore[v], 4);
        }
        // Open water with no land in the chunk: everything reads open (1), never shoreline.
        WaterSurfaceData open = WaterChunk(0, 0, (_, _) => true).Finish();
        Assert.All(open.Shore, s => Assert.Equal(1f, s));
    }

    [Fact]
    public void Water_TintIsTheTilesOwnArtColour()
    {
        WaterSurfaceData d = WaterChunk(0, 0, (i, _) => i >= 8).Finish();
        Assert.All(d.Tint, c => Assert.Equal(WaterArgb, c)); // land colour never bleeds into the water tint
        Assert.Equal(WaterArgb, WaterSurfaceBuilder.MeanColor(Solid(44, WaterArgb), false));
    }

    [Fact]
    public void Water_NoWaterTilesGivesNoSurfaceAndBadInputThrows()
    {
        WaterSurfaceData none = WaterChunk(0, 0, (_, _) => false).Finish();
        Assert.Equal(0, none.TileCount);
        Assert.Equal(0, none.VertexCount);

        var wb = new WaterSurfaceBuilder(0, 0);
        DrawItem outside = LiveGroundProjection.LandItem(16, 0, 0xA8, 0, 0, 0, 0, hasTexmap: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => wb.AddTile(outside, Solid(44, WaterArgb), false, true));
        DrawItem stat = outside;
        stat.Kind = DrawKind.Static;
        Assert.Throws<ArgumentException>(() => wb.AddTile(stat, Solid(44, WaterArgb), false, true));
    }

    // ---- Shared light and sun shadow (LiveGroundLighting) ----

    /// <summary>Terrain-space normal of a tile triangle (corner indices into top/right/bottom/left), pointing up.</summary>
    private static (float X, float Y, float Z) TerrainNormal(int[] z, int a, int b, int c)
    {
        (float X, float Y, float Z) P(int k) =>
            TerrainLandBuilder.TerrainPoint(LiveGroundProjection.TexmapU[k], LiveGroundProjection.TexmapV[k], z[k]);
        var (ax, ay, az) = P(a);
        var (bx, by, bz) = P(b);
        var (cx, cy, cz) = P(c);
        float ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        return ny < 0 ? (-nx, -ny, -nz) : (nx, ny, nz);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(10, 0, -10, 0)]
    [InlineData(0, 12, 0, -12)]
    [InlineData(-8, 4, 20, 4)]
    [InlineData(30, -20, 0, 5)]
    public void Light_TerrainShadeIsTheCompositorsShadeForTheSameFace(int zt, int zr, int zb, int zl)
    {
        int[] z = { zt, zr, zb, zl };
        for (int tri = 0; tri < 2; tri++)
        {
            int[] t = LiveGroundProjection.Triangles;
            var (nx, ny, nz) = TerrainNormal(z, t[tri * 3], t[tri * 3 + 1], t[tri * 3 + 2]);
            Assert.Equal(LiveGroundProjection.TriangleShade(z, tri), LiveGroundLighting.Shade(nx, ny, nz, 1f), 4);
        }
    }

    [Fact]
    public void Light_FlatIsOneAndFullShadowIsTheCompositorsFloor()
    {
        Assert.Equal(1f, LiveGroundLighting.Shade(0, 1, 0, 1f), 5);
        Assert.Equal(LiveGroundLighting.MinShade, LiveGroundLighting.Shade(0, 1, 0, 0f), 5);
        Assert.Equal(0.85f, LiveGroundLighting.Shade(0, 1, 0, 0.5f), 5);
        // A face turned away from the sun is already at the floor: shadow cannot take it lower.
        Assert.Equal(LiveGroundLighting.MinShade, LiveGroundLighting.Shade(0.6f, 0.2f, -0.2f, 0f), 5);
        Assert.Equal(LiveGroundLighting.MinShade, LiveGroundLighting.Shade(0.6f, 0.2f, -0.2f, 1f), 5);
        (float x, float y, float zz) = LiveGroundLighting.SunTerrain;
        Assert.Equal((LiveGroundLighting.SunX, LiveGroundLighting.SunUp, -LiveGroundLighting.SunY), (x, y, zz));
    }

    [Fact]
    public void Light_PlayerFalloffIsTheSpriteShadersCurve()
    {
        Assert.Equal(1f, LiveGroundLighting.PointFalloff(0f, 170f), 5);
        Assert.Equal(0.25f, LiveGroundLighting.PointFalloff(85f, 170f), 5);
        Assert.Equal(0f, LiveGroundLighting.PointFalloff(170f, 170f), 5);
        Assert.Equal(0f, LiveGroundLighting.PointFalloff(500f, 170f), 5);
        Assert.Equal(0f, LiveGroundLighting.PointFalloff(2f, 0f), 5); // radius below 1 counts as 1, as the shader
    }

    [Theory]
    [InlineData(1, 0f, 0f)]
    [InlineData(2, -1234.5f, 987f)]
    [InlineData(3, 50f, -60f)]
    public void Light_TerrainToUnityRowsAreToUnityOfTheUoProjection(int scale, float ox, float oy)
    {
        (float[] rx, float[] ry) = LiveGroundLighting.TerrainToUnityRows(scale, ox, oy);
        foreach ((int tx, int ty, int tz) in new[] { (0, 0, 0), (1600, 1504, 7), (1615, 1519, -30), (3, 900, 100) })
        {
            (float px, float py, float pz) = TerrainLandBuilder.TerrainPoint(tx, ty, tz);
            (float sx, float sy) = TerrainLandBuilder.ToWorldScreen(px, py, pz, scale);
            float ux = rx[0] * px + rx[1] * py + rx[2] * pz + rx[3];
            float uy = ry[0] * px + ry[1] * py + ry[2] * pz + ry[3];
            // WorldStreamer.ToUnity: (x - origin.x, -(y - origin.y)); ToUnity(0, 0) = (ox, oy).
            Assert.Equal(sx + ox, ux, 1);
            Assert.Equal(-sy + oy, uy, 1);
            (float cx, float cy) = LiveGroundProjection.Corner(tx, ty, LiveGroundProjection.Top, tz, scale);
            Assert.Equal(cx + ox, ux, 1);
            Assert.Equal(-cy + oy, uy, 1);
        }
    }

    private static int[,] Corners(Func<int, int, int> z)
    {
        var c = new int[Chunk + 1, Chunk + 1];
        for (int i = 0; i <= Chunk; i++)
            for (int j = 0; j <= Chunk; j++)
                c[i, j] = z(i, j);
        return c;
    }

    private static bool MaskLit(byte[] mask, float tx, float ty)
    {
        int col = (int)(tx * LiveGroundLighting.ShadowTexelsPerTile);
        int row = (int)((Chunk - ty) * LiveGroundLighting.ShadowTexelsPerTile);
        return mask[row * LiveGroundLighting.ShadowSize + col] == 255;
    }

    [Fact]
    public void Shadow_FlatAndGentleGroundIsFullyLit()
    {
        byte[] flat = LiveGroundLighting.ShadowMask(Corners((_, _) => 5));
        Assert.Equal(LiveGroundLighting.ShadowSize * LiveGroundLighting.ShadowSize, flat.Length);
        Assert.All(flat, b => Assert.Equal(255, b));
        // A steady slope (4 Z per tile) never rises faster than the sun ray (~9.5 Z per tile): no self-shadow.
        Assert.All(LiveGroundLighting.ShadowMask(Corners((i, j) => (i + j) * 4 - 60)), b => Assert.Equal(255, b));
        Assert.All(LiveGroundLighting.ShadowMask(Corners((i, j) => 60 - (i + j) * 4)), b => Assert.Equal(255, b));
    }

    [Fact]
    public void Shadow_ARidgeShadowsTheSideAwayFromTheSunOnly()
    {
        // A 40 Z wall along corner column 6 (sun comes from -x, slightly -y).
        int[,] c = Corners((i, _) => i == 6 ? 40 : 0);
        byte[] mask = LiveGroundLighting.ShadowMask(c);
        Assert.True(MaskLit(mask, 3.5f, 8.1f), "the sunward side is lit");
        Assert.False(MaskLit(mask, 7.6f, 8.1f), "right behind the ridge is in shadow");
        // Shadow length: height 40 Z over a rise of SunUp/|SunXY| per tile ≈ 4.2 tiles behind the crest.
        float hl = MathF.Sqrt(LiveGroundLighting.SunX * LiveGroundLighting.SunX + LiveGroundLighting.SunY * LiveGroundLighting.SunY);
        float reach = 40f / (LiveGroundLighting.SunUp / hl / TerrainLandBuilder.HeightUnitPerZ) * (-LiveGroundLighting.SunX / hl);
        Assert.False(MaskLit(mask, 6f + reach - 0.6f, 8.1f), "inside the shadow length");
        Assert.True(MaskLit(mask, 6f + reach + 0.9f, 8.1f), "past the shadow length");
        Assert.Equal(0f, LiveGroundLighting.SunVisibility(c, 7.6f, 8.1f, 40));
    }

    [Fact]
    public void Shadow_RaysLeavingTheChunkAreLitAndTheMaskRowsAreBottomUp()
    {
        // The whole west half is a 60 Z plateau: its east foot is shadowed, rows near tile y 0 too (rows bottom-up).
        int[,] c = Corners((i, _) => i <= 4 ? 60 : 0);
        byte[] mask = LiveGroundLighting.ShadowMask(c);
        Assert.False(MaskLit(mask, 5.6f, 0.9f));
        Assert.False(MaskLit(mask, 5.6f, 15.1f));
        int n = LiveGroundLighting.ShadowSize;
        Assert.Equal(0, mask[(n - 1) * n + (int)(5.1f * LiveGroundLighting.ShadowTexelsPerTile)]); // top row = tile y 0
        // The same foot further east at tile y 0.125: its ray leaves the chunk (-y) before reaching the plateau, so it
        // reads lit. The builder cannot see the neighbouring chunk; this is the documented chunk-edge limit.
        Assert.Equal(255, mask[(n - 1) * n + (int)(5.6f * LiveGroundLighting.ShadowTexelsPerTile)]);
        // Nothing in the chunk is higher than the plateau, so its own top is lit up to the chunk edge.
        Assert.True(MaskLit(mask, 0.1f, 0.1f));
        Assert.True(MaskLit(mask, 2f, 8f));
    }

    [Fact]
    public void Shadow_BuilderAttachesTheMaskOnlyWithLand()
    {
        TerrainLandData d = FilledChunk(1600, 1504).Finish();
        Assert.NotNull(d.SunMask);
        Assert.Equal(LiveGroundLighting.ShadowMask(d.CornerZ), d.SunMask);
        Assert.Null(new TerrainLandBuilder(0, 0, 8).Finish().SunMask);
    }

    // ---- static (building) shadow columns ----

    private static StaticTileData Static(int height, TileFlags flags = TileFlags.None) => new StaticTileData { Height = (byte)height, Flags = flags };

    [Fact]
    public void Occluders_OnlyTallDryStaticsCastAndTheHighestTopWins()
    {
        Assert.False(StaticOccluders.Casts(Static(StaticOccluders.MinHeight - 1)));
        Assert.True(StaticOccluders.Casts(Static(StaticOccluders.MinHeight)));
        Assert.False(StaticOccluders.Casts(Static(20, TileFlags.Wet)), "water statics never cast");

        int[,] top = StaticOccluders.Empty();
        StaticOccluders.Add(top, 3, 4, 0, Static(20));
        StaticOccluders.Add(top, 3, 4, 10, Static(5));   // lower top on the same tile
        StaticOccluders.Add(top, 3, 4, 0, Static(1));    // too low to cast
        StaticOccluders.Add(top, -1, 4, 0, Static(20));  // outside the chunk: ignored
        StaticOccluders.Add(top, 16, 4, 0, Static(20));
        Assert.Equal(20, top[3, 4]);
        Assert.Equal(1, StaticOccluders.Count(top));
        Assert.Equal(0, StaticOccluders.Count(null!));
    }

    [Fact]
    public void Occluders_AColumnShadowsFlatGroundAwayFromTheSunOnly()
    {
        int[,] c = Corners((_, _) => 0);
        int[,] top = StaticOccluders.Empty();
        top[5, 8] = 40; // one 40 Z column on tile (5, 8); the sun comes from -x, slightly -y
        byte[] terrainOnly = LiveGroundLighting.ShadowMask(c);
        byte[] withColumn = LiveGroundLighting.ShadowMask(c, top);
        Assert.All(terrainOnly, b => Assert.Equal(255, b));
        Assert.Equal(terrainOnly, LiveGroundLighting.ShadowMask(c, StaticOccluders.Empty()));

        Assert.False(MaskLit(withColumn, 5.5f, 8.5f), "the column's own tile");
        Assert.False(MaskLit(withColumn, 7.1f, 8.9f), "behind it, away from the sun");
        Assert.True(MaskLit(withColumn, 3.5f, 8.5f), "the sunward side");
        Assert.True(MaskLit(withColumn, 5.5f, 11.5f), "beside it");
        // Shadow length as the ridge test: 40 Z over the sun ray's rise ≈ 4.2 tiles along the ray.
        float hl = MathF.Sqrt(LiveGroundLighting.SunX * LiveGroundLighting.SunX + LiveGroundLighting.SunY * LiveGroundLighting.SunY);
        float reach = 40f / (LiveGroundLighting.SunUp / hl / TerrainLandBuilder.HeightUnitPerZ) * (-LiveGroundLighting.SunX / hl);
        Assert.True(MaskLit(withColumn, 6f + reach + 0.9f, 8.9f), "past the shadow length");
        Assert.Equal(0f, LiveGroundLighting.SunVisibility(c, top, 7.1f, 8.9f, 40));
        Assert.Equal(1f, LiveGroundLighting.SunVisibility(c, null!, 7.1f, 8.9f, 40));
    }

    [Fact]
    public void Occluders_ALowColumnUnderAHillsShadowChangesNothing()
    {
        // The ridge already shades behind it; a column lower than the ground it stands on adds no shadow anywhere.
        int[,] c = Corners((i, _) => i == 6 ? 40 : 0);
        int[,] top = StaticOccluders.Empty();
        top[10, 3] = -5;
        Assert.Equal(LiveGroundLighting.ShadowMask(c), LiveGroundLighting.ShadowMask(c, top));
    }

    [Fact]
    public void Occluders_BuilderBakesThemIntoTheSunMaskAndCountsThem()
    {
        TerrainLandBuilder tb = FilledChunk(1600, 1504);
        int[,] top = StaticOccluders.Empty();
        top[2, 2] = 100;
        tb.SetOccluders(top);
        TerrainLandData d = tb.Finish();
        Assert.Equal(1, d.OccluderTiles);
        Assert.Equal(LiveGroundLighting.ShadowMask(d.CornerZ, top), d.SunMask);
        Assert.NotEqual(LiveGroundLighting.ShadowMask(d.CornerZ), d.SunMask);
        Assert.Equal(0, FilledChunk(1600, 1504).Finish().OccluderTiles);
        Assert.Throws<ArgumentException>(() => new TerrainLandBuilder(0, 0, 8).SetOccluders(new int[4, 4]));
    }

    // ---- terrain ↔ sprite occlusion proxy ----

    private static TerrainLandBuilder DepthChunk(int x0, int y0, Func<int, int, bool>? present = null)
    {
        var tb = new TerrainLandBuilder(x0, y0, 8);
        for (int j = 0; j < Chunk; j++)
            for (int i = 0; i < Chunk; i++)
            {
                if (present != null && !present(i, j))
                    continue;
                int x = x0 + i, y = y0 + j;
                DrawItem it = LiveGroundProjection.LandItem(x, y, 3, MapZ(x, y), MapZ(x + 1, y), MapZ(x + 1, y + 1), MapZ(x, y + 1), hasTexmap: true);
                tb.AddTile(it, Solid(64, 0xFF336699u), true, TileDepthOf(i, j));
            }
        return tb;
    }

    /// <summary>A stand-in for DepthKey.Relative: distinct per tile, larger diagonals nearer (more negative).</summary>
    private static float TileDepthOf(int i, int j) => -((i + j) * 30.72f + i * 0.005f);

    [Fact]
    public void Proxy_EveryVertexOfATileCarriesThatTilesDepth()
    {
        TerrainLandData d = DepthChunk(1600, 1504).Finish();
        DepthProxyData p = TerrainDepthProxy.Build(d, 1)!;
        Assert.Equal(256, p.TileCount);
        Assert.Equal(256 * TerrainDepthProxy.VerticesPerTile, p.VertexCount);
        Assert.Equal(256 * TerrainDepthProxy.IndicesPerTile, p.Indices.Length);
        int v = 0;
        for (int j = 0; j < Chunk; j++)
            for (int i = 0; i < Chunk; i++)
                for (int k = 0; k < TerrainDepthProxy.VerticesPerTile; k++)
                    Assert.Equal(TileDepthOf(i, j), p.Depth[v++]);
        // Triangles never mix tiles (the depth is a per-tile step, as the land sprite quads).
        for (int t = 0; t < p.Indices.Length; t += 3)
        {
            int tile = p.Indices[t] / TerrainDepthProxy.VerticesPerTile;
            Assert.Equal(tile, p.Indices[t + 1] / TerrainDepthProxy.VerticesPerTile);
            Assert.Equal(tile, p.Indices[t + 2] / TerrainDepthProxy.VerticesPerTile);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Proxy_TileCornersLandOnTheCompositorsCorners(int scale)
    {
        const int x0 = 320, y0 = 480;
        TerrainLandData d = DepthChunk(x0, y0).Finish();
        DepthProxyData p = TerrainDepthProxy.Build(d, scale)!;
        int g = TerrainDepthProxy.GridPerTile;
        for (int j = 0; j < Chunk; j += 5)
            for (int i = 0; i < Chunk; i += 3)
            {
                int first = (j * Chunk + i) * TerrainDepthProxy.VerticesPerTile;
                int x = x0 + i, y = y0 + j;
                (float tX, float tY) = LiveGroundProjection.Corner(x, y, T, MapZ(x, y), scale);
                (float rX, float rY) = LiveGroundProjection.Corner(x, y, R, MapZ(x + 1, y), scale);
                (float bX, float bY) = LiveGroundProjection.Corner(x, y, B, MapZ(x + 1, y + 1), scale);
                (float lX, float lY) = LiveGroundProjection.Corner(x, y, L, MapZ(x, y + 1), scale);
                AssertNear(tX, tY, p, first);
                AssertNear(rX, rY, p, first + g - 1);
                AssertNear(bX, bY, p, first + g * g - 1);
                AssertNear(lX, lY, p, first + g * (g - 1));
                // The centre sits on the RIGHT–LEFT split: halfway between the right and left corners on screen.
                AssertNear((rX + lX) / 2f, (rY + lY) / 2f, p, first + g + 1);
            }
    }

    private static void AssertNear(float x, float y, DepthProxyData p, int v)
    {
        Assert.Equal(x, p.X[v], 3);
        Assert.Equal(y, p.Y[v], 3);
    }

    [Fact]
    public void Proxy_SkipsHolesAndTilesWithoutDepth()
    {
        TerrainLandData holes = DepthChunk(0, 0, (i, j) => (i + j) % 3 != 0).Finish();
        DepthProxyData p = TerrainDepthProxy.Build(holes, 1)!;
        int present = 0;
        for (int j = 0; j < Chunk; j++)
            for (int i = 0; i < Chunk; i++)
                if ((i + j) % 3 != 0)
                    present++;
        Assert.Equal(present, p.TileCount);
        Assert.All(p.Depth, z => Assert.False(float.IsNaN(z)));

        // Land added without a depth (occlusion off) gives no proxy at all.
        Assert.Null(TerrainDepthProxy.Build(FilledChunk(0, 0).Finish(), 1));
        Assert.Null(TerrainDepthProxy.Build(null!, 1));
        Assert.Null(TerrainDepthProxy.Build(DepthChunk(0, 0).Finish(), 0));
        Assert.All(FilledChunk(0, 0).Finish().TileDepth.Cast<float>(), z => Assert.True(float.IsNaN(z)));
    }

    [Fact]
    public void Proxy_SubQuadsSplitAlongRightLeftSoTheyAreCoplanarOnTheTile()
    {
        // One tile with only its right corner raised: RIGHT–LEFT split → top-right-left is a tilted plane, right-bottom-left
        // another; every proxy triangle must lie in one of them (a top–bottom split would bend through the centre).
        var tb = new TerrainLandBuilder(0, 0, 8);
        DrawItem it = LiveGroundProjection.LandItem(0, 0, 3, 0, 20, 0, 0, hasTexmap: true);
        tb.AddTile(it, Solid(64, 0xFF000000u), true, -1f);
        TerrainLandData d = tb.Finish();
        DepthProxyData p = TerrainDepthProxy.Build(d, 1)!;
        Assert.Equal(1, p.TileCount);
        // Screen Y of a terrain point = (x + y)·22 − 4·z, so z = ((x + y)·22 − Y) / 4 recovers each vertex height.
        int g = TerrainDepthProxy.GridPerTile;
        var vx = new float[g * g];
        var vy = new float[g * g];
        var vz = new float[g * g];
        for (int b = 0; b < g; b++)
            for (int a = 0; a < g; a++)
            {
                int v = b * g + a;
                vx[v] = a / 2f;
                vy[v] = b / 2f;
                vz[v] = ((vx[v] + vy[v]) * LiveGroundProjection.TileHalf - p.Y[v]) / LiveGroundProjection.ZStep;
                Assert.Equal(LiveGroundProjection.HeightAt(0, 20, 0, 0, vx[v], vy[v]), vz[v], 3);
            }
        Assert.Equal(10f, vz[g + 1], 3); // centre = mean(right, left)
        // Each triangle's centroid lies on the surface: it does not cut across the RIGHT–LEFT crease.
        for (int t = 0; t < p.Indices.Length; t += 3)
        {
            int i0 = p.Indices[t], i1 = p.Indices[t + 1], i2 = p.Indices[t + 2];
            float cx = (vx[i0] + vx[i1] + vx[i2]) / 3f, cy = (vy[i0] + vy[i1] + vy[i2]) / 3f, cz = (vz[i0] + vz[i1] + vz[i2]) / 3f;
            Assert.Equal(LiveGroundProjection.HeightAt(0, 20, 0, 0, cx, cy), cz, 3);
        }
    }
}

/// <summary>Water detection against the real tiledata (the shipped client's land rows).</summary>
[Collection("UO install")]
public sealed class LiveGroundWaterDataTests
{
    private readonly UOData _data;

    public LiveGroundWaterDataTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void Water_RealTiledataWaterRowsAreTheClassicWaterLand()
    {
        bool[] table = WaterTiles.Table(_data.TileData.Land);
        var water = new List<int>();
        for (int g = 0; g < table.Length; g++)
            if (table[g])
                water.Add(g);
        // 0xA8–0xAB and 0x136–0x137: the land rows named "water" with Wet (in the 7.0.23.1 data checked).
        Assert.Contains(0xA8, water);
        Assert.Contains(0xAB, water);
        Assert.Contains(0x136, water);
        Assert.All(water, g => Assert.Equal("water", _data.TileData.Land[g].Name.Trim().ToLowerInvariant()));

        int wet = 0;
        foreach (LandTileData row in _data.TileData.Land)
            if ((row.Flags & TileFlags.Wet) != 0)
                wet++;
        Assert.True(wet > water.Count, "Wet alone also covers acid/fallback rows; the name check must stay");
    }

    [Fact]
    public void Occluders_BritainHasCastingStaticColumnsFromTheRealMap()
    {
        // Britain's west bank area (Felucca/Trammel map 0): buildings and walls, all map statics.
        MapFile map = _data.GetMap(0);
        int[,] top = StaticOccluders.FromMap(map, _data.TileData, 1424, 1680);
        Assert.NotNull(top);
        int n = StaticOccluders.Count(top);
        Assert.True(n > 0, "a town chunk has walls/buildings tall enough to cast");
        // Every column is exactly the highest casting static on its tile.
        for (int j = 0; j < TerrainLandBuilder.Tiles; j++)
            for (int i = 0; i < TerrainLandBuilder.Tiles; i++)
            {
                int x = 1424 + i, y = 1680 + j, expected = StaticOccluders.None;
                foreach (StaticCell s in map.ReadStaticBlock(x >> 3, y >> 3))
                    if (s.X == (x & 7) && s.Y == (y & 7) && StaticOccluders.Casts(_data.TileData.GetStatic(s.Graphic)))
                        expected = Math.Max(expected, s.Z + _data.TileData.GetStatic(s.Graphic).Height);
                Assert.Equal(expected, top[i, j]);
            }
        Assert.Null(StaticOccluders.FromMap(null!, _data.TileData, 0, 0));
    }

    [Fact]
    public void Water_TypedDataAccessReadsTheSameLandRows()
    {
        Assert.True(WaterTiles.TryLand(_data, out LandTileData[] land, out string error), error);
        Assert.Null(error);
        Assert.Same(_data.TileData.Land, land);
    }
}

/// <summary>
/// The Phase C evidence fixture: one real coastal chunk of map 0 (water mesh, terrain tile colours, corner heights and
/// the sun mask, all from the production builders) that LiveGroundEvidenceCapture renders in Unity. Rewrite the file
/// with UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1; otherwise a present file must equal what the builders make now.
/// </summary>
[Collection("UO install")]
public sealed class LiveGroundEvidenceFixtureTests
{
    private const int Tiles = TerrainLandBuilder.Tiles;
    private readonly UOData _data;

    public LiveGroundEvidenceFixtureTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static string FixturePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return Path.Combine(dir.FullName, "Assets", "LiveGroundCompare", "Evidence~", "realWaterChunk.json");
        throw new DirectoryNotFoundException("repo root");
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { IncludeFields = true };

    /// <summary>Field names are what the Unity side's JsonUtility reads (LiveGroundEvidenceCapture.Fixture).</summary>
    public sealed class Fixture
    {
        public string source = "";
        public int map, x0, y0, tiles, waterTiles, landTiles, occluderColumns, shadowTexels;
        public float heightUnitPerZ;
        public float[] sun = Array.Empty<float>();
        public int[] waterGraphics = Array.Empty<int>();
        public int[] graphics = Array.Empty<int>();   // [j * tiles + i]
        public int[] tileArgb = Array.Empty<int>();   // [j * tiles + i], mean classic land art colour
        public int[] cornerZ = Array.Empty<int>();    // [j * (tiles + 1) + i]
        public int[] sunMask = Array.Empty<int>();    // ShadowMask bytes, rows bottom-up as the chunk texture
        public float[] waterX = Array.Empty<float>(), waterY = Array.Empty<float>(), waterZ = Array.Empty<float>(), waterShore = Array.Empty<float>();
        public int[] waterTint = Array.Empty<int>();
        public int[] waterIndices = Array.Empty<int>();
    }

    /// <summary>The most mixed water/land chunk (ties: one with casting statics, then the first) in a coastal box around Britain.</summary>
    private (int X0, int Y0) PickCoastalChunk(MapFile map, bool[] water)
    {
        int best = -1, bx = 0, by = 0;
        for (int y0 = 1424; y0 <= 1840; y0 += Tiles)
            for (int x0 = 1280; x0 <= 1712; x0 += Tiles)
            {
                int w = 0;
                for (int j = 0; j < Tiles; j++)
                    for (int i = 0; i < Tiles; i++)
                        if (WaterTiles.Lookup(water, map.ReadLandBlock((x0 + i) >> 3, (y0 + j) >> 3)[(((y0 + j) & 7) << 3) | ((x0 + i) & 7)].Graphic))
                            w++;
                int score = Math.Min(w, Tiles * Tiles - w) * 2;
                if (score > 0 && StaticOccluders.Count(StaticOccluders.FromMap(map, _data.TileData, x0, y0)) > 0)
                    score++;
                if (score > best)
                    (best, bx, by) = (score, x0, y0);
            }
        return (bx, by);
    }

    private Fixture Build()
    {
        MapFile map = _data.GetMap(0);
        bool[] water = WaterTiles.Table(_data.TileData.Land);
        (int x0, int y0) = PickCoastalChunk(map, water);
        RealMapPatch patch = RealMapPatch.FromMap(map, x0, y0, Tiles, Tiles);
        int[,] occluders = StaticOccluders.FromMap(map, _data.TileData, x0, y0);

        var cornerZ = new int[Tiles + 1, Tiles + 1];
        var f = new Fixture
        {
            source = "map0 land/statics + tiledata via UOInstallFixture; tile colour = WaterSurfaceBuilder.MeanColor(classic land art, texmap: false)",
            map = 0, x0 = x0, y0 = y0, tiles = Tiles, shadowTexels = LiveGroundLighting.ShadowTexelsPerTile,
            heightUnitPerZ = TerrainLandBuilder.HeightUnitPerZ,
            sun = new[] { LiveGroundLighting.SunTerrain.X, LiveGroundLighting.SunTerrain.Y, LiveGroundLighting.SunTerrain.Z },
            occluderColumns = StaticOccluders.Count(occluders),
            graphics = new int[Tiles * Tiles], tileArgb = new int[Tiles * Tiles], cornerZ = new int[(Tiles + 1) * (Tiles + 1)],
        };
        for (int j = 0; j <= Tiles; j++)
            for (int i = 0; i <= Tiles; i++)
                f.cornerZ[j * (Tiles + 1) + i] = cornerZ[i, j] = patch.CornerZ(i, j);

        var wb = new WaterSurfaceBuilder(x0, y0);
        for (int j = 0; j < Tiles; j++)
            for (int i = 0; i < Tiles; i++)
            {
                ushort g = patch.Graphic(i, j);
                (sbyte t, sbyte r, sbyte b, sbyte l) = patch.Corners(i, j);
                bool w = WaterTiles.Lookup(water, g);
                Bitmap32 art = _data.GetLand(g);
                wb.AddTile(LiveGroundProjection.LandItem(x0 + i, y0 + j, g, t, r, b, l, hasTexmap: false), art, false, w);
                f.graphics[j * Tiles + i] = g;
                f.tileArgb[j * Tiles + i] = art == null ? unchecked((int)0xFF000000) : unchecked((int)WaterSurfaceBuilder.MeanColor(art, false));
                if (w) f.waterTiles++; else f.landTiles++;
            }
        f.sunMask = Array.ConvertAll(LiveGroundLighting.ShadowMask(cornerZ, occluders), b => (int)b);

        WaterSurfaceData d = wb.Finish();
        f.waterGraphics = d.Graphics.ToArray();
        f.waterX = d.X ?? Array.Empty<float>();
        f.waterY = d.Y ?? Array.Empty<float>();
        f.waterZ = d.Z ?? Array.Empty<float>();
        f.waterShore = d.Shore ?? Array.Empty<float>();
        f.waterTint = Array.ConvertAll(d.Tint ?? Array.Empty<uint>(), c => unchecked((int)c));
        f.waterIndices = d.Indices ?? Array.Empty<int>();
        return f;
    }

    [Fact]
    public void Evidence_RealCoastalChunkHasShoreWaterAndSunShadow()
    {
        Fixture f = Build();
        Assert.True(f.waterTiles > 0 && f.landTiles > 0, $"chunk {f.x0},{f.y0}: {f.waterTiles} water / {f.landTiles} land");
        Assert.All(f.waterGraphics, g => Assert.True(WaterTiles.IsWater(_data.TileData.Land[g]), $"0x{g:X} is not water"));
        Assert.Contains(0f, f.waterShore);                   // a real shoreline (foam band)
        Assert.Contains(255, f.sunMask);
        Assert.Equal(f.waterTiles * 6, f.waterIndices.Length);

        string path = FixturePath();
        string built = System.Text.Json.JsonSerializer.Serialize(f, Json);
        if (Environment.GetEnvironmentVariable("UOCLIENT_WRITE_LIVEGROUND_EVIDENCE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, built + "\n");
        }
        if (File.Exists(path))
            Assert.Equal(built, File.ReadAllText(path).TrimEnd('\n'));
    }
}

/// <summary>Source guards for the TEST seams: typed data access only, and the default sprite shader untouched.</summary>
public sealed class LiveGroundSourceTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void Water_ReadsTheStreamersDataTypedNotByReflection()
    {
        string water = Source("Assets", "LiveGroundCompare", "Scripts", "TerrainWater.cs");
        Assert.DoesNotContain("System.Reflection", water);
        Assert.DoesNotContain("GetField", water);
        Assert.Contains("world?.Data", water);
        Assert.Contains("public UOData Data => _data;", Source("Assets", "UOClient.Unity", "Runtime", "WorldStreamer.cs"));
        foreach (string f in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Scripts"), "*.cs"))
            Assert.DoesNotContain("BindingFlags", File.ReadAllText(f));
    }

    [Fact]
    public void Evidence_CaptureIsExplicitInvokeEditorOnly()
    {
        string dir = Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Editor");
        Assert.Contains("\"Editor\"", File.ReadAllText(Path.Combine(dir, "UOClient.LiveGroundCompare.Editor.asmdef")));
        string capture = File.ReadAllText(Path.Combine(dir, "LiveGroundEvidenceCapture.cs"));
        foreach (string auto in new[] { "InitializeOnLoad", "RuntimeInitializeOnLoadMethod", "DidReloadScripts", "EnterPlaymode", "isPlaying =" })
            Assert.DoesNotContain(auto, capture);
        Assert.Contains("[MenuItem(", capture);
        foreach (string f in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotContain("InitializeOnLoad", File.ReadAllText(f));
    }

    [Fact]
    public void Occlusion_NeedsNoChangeToTheDefaultSpriteShader()
    {
        // The proxy is its own depth-only shader; UOClient/WorldSprite's default variant has no LiveGround global (the
        // sprite shadow's are only under LG_SPRITE_SHADOW: LiveGroundSpriteShadowSourceTests).
        string sprite = LiveGroundSpriteShadowSourceTests.DefaultVariant(
            Source("Assets", "UOClient.Unity", "Runtime", "Resources", "WorldSprite.shader").Replace("\r\n", "\n"));
        Assert.DoesNotContain("_LG", sprite);
        Assert.DoesNotContain("multi_compile", sprite);
        string depth = Source("Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareDepthKey.shader");
        Assert.Contains("ColorMask 0", depth);
        Assert.Contains("ZWrite On", depth);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareDepthKey.shader.meta")));
    }
}

/// <summary>
/// Sprite sun-shadow receive (TEST): the shadow ceiling (<see cref="LiveGroundLighting.ShadowCeiling"/>) and the
/// pixel-to-world mapping (<see cref="SpriteShadow"/>) that UOClient/WorldSprite mirrors under LG_SPRITE_SHADOW.
/// </summary>
public sealed class LiveGroundSpriteShadowTests
{
    private const int N = TerrainLandBuilder.Tiles;

    /// <summary>Deterministic rolling hills with a ridge.</summary>
    private static int[,] Hills()
    {
        var z = new int[N + 1, N + 1];
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
                z[i, j] = (int)Math.Round(12 * Math.Sin(i * 0.7) * Math.Cos(j * 0.45) + (i == 6 ? 25 : 0) + (i + j) % 3);
        return z;
    }

    private static int[,] Flat(int h)
    {
        var z = new int[N + 1, N + 1];
        for (int i = 0; i <= N; i++)
            for (int j = 0; j <= N; j++)
                z[i, j] = h;
        return z;
    }

    private static int MaxZ(int[,] cornerZ, int[,] top)
    {
        int m = int.MinValue;
        foreach (int z in cornerZ) m = Math.Max(m, z);
        if (top != null) foreach (int z in top) m = Math.Max(m, z);
        return m;
    }

    [Fact]
    public void Ceiling_TerrainOnly_AnswersTheSunMaskForEveryTexel()
    {
        int[,] z = Hills();
        byte[] mask = LiveGroundLighting.ShadowMask(z);
        float[] ceiling = LiveGroundLighting.ShadowCeiling(z, null);
        const int n = LiveGroundLighting.ShadowSize;
        int shadowed = 0;
        for (int row = 0; row < n; row++)
            for (int col = 0; col < n; col++)
            {
                float tx = (col + 0.5f) / LiveGroundLighting.ShadowTexelsPerTile, ty = (row + 0.5f) / LiveGroundLighting.ShadowTexelsPerTile;
                float ground = TerrainLandBuilder.SampleZ(z, tx, ty) + LiveGroundLighting.ShadowBiasZ;
                bool inShadow = ground < ceiling[row * n + col];
                // The mask's rows are bottom-up (row 0 = tile y max); the ceiling's are top-down.
                Assert.Equal(mask[(n - 1 - row) * n + col] == 0, inShadow);
                if (inShadow) shadowed++;
            }
        Assert.True(shadowed > 50 && shadowed < n * n / 2, $"{shadowed} shadowed texels");
    }

    [Fact]
    public void Ceiling_WithColumns_AnswersTheMaskOffColumnTiles_AndSkipsTheOwnColumn()
    {
        int[,] z = Hills(), top = StaticOccluders.Empty();
        top[5, 8] = 60;
        top[11, 3] = 35;
        top[12, 3] = 35;
        int maxZ = MaxZ(z, top);
        float[] ceiling = LiveGroundLighting.ShadowCeiling(z, top);
        const int n = LiveGroundLighting.ShadowSize, k = LiveGroundLighting.ShadowTexelsPerTile;
        for (int row = 0; row < n; row++)
            for (int col = 0; col < n; col++)
            {
                if (top[col / k, row / k] != StaticOccluders.None)
                    continue;
                float tx = (col + 0.5f) / k, ty = (row + 0.5f) / k;
                float ground = TerrainLandBuilder.SampleZ(z, tx, ty) + LiveGroundLighting.ShadowBiasZ;
                Assert.Equal(LiveGroundLighting.SunVisibility(z, top, tx, ty, maxZ) == 0f, ground < ceiling[row * n + col]);
            }
        // On the column's own tile the ceiling ignores it (a wall must not shade itself) but not its neighbours.
        int[,] flat = Flat(0), one = StaticOccluders.Empty();
        one[5, 8] = 60;
        Assert.True(LiveGroundLighting.CeilingAt(flat, one, 5.5f, 8.5f, 60) < 0f);
        Assert.True(LiveGroundLighting.CeilingAt(flat, one, 6.5f, 8.5f, 60) > 40f, "one tile away from the sun: deep in the shadow");
        Assert.True(LiveGroundLighting.CeilingAt(flat, one, 4.5f, 8.5f, 60) < 0f, "on the sun side: lit");
    }

    [Fact]
    public void Ceiling_FallsWithDistanceFromTheCaster_AtTheSunsRise()
    {
        int[,] flat = Flat(0), top = StaticOccluders.Empty();
        for (int j = 2; j <= 12; j++)
            top[2, j] = 50; // a wall along tile y, so the slightly slanted rays all meet it
        float near = LiveGroundLighting.CeilingAt(flat, top, 4.5f, 8.5f, 50), far = LiveGroundLighting.CeilingAt(flat, top, 7.5f, 8.5f, 50);
        Assert.True(near > far && far > 0f, $"near {near}, far {far}");
        // The sun climbs SunUp/|sun xy| terrain units per tile along its own direction, ~9.5 Z per tile.
        float hl = (float)Math.Sqrt(LiveGroundLighting.SunX * LiveGroundLighting.SunX + LiveGroundLighting.SunY * LiveGroundLighting.SunY);
        float rise = LiveGroundLighting.SunUp / hl / TerrainLandBuilder.HeightUnitPerZ;
        // 3 tiles along +x is 3 / |ux| tiles along the ray (0.25-tile ray steps: allow a step either way).
        Assert.InRange((near - far) * Math.Abs(LiveGroundLighting.SunX) / hl / 3f, rise * 0.75f, rise * 1.25f);
        Assert.Equal(LiveGroundLighting.NoCeiling, LiveGroundLighting.CeilingAt(flat, null, 0.01f, 8.5f, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PixelPoint_UprightRisesFourPixelsPerZ_AndItsFrontHalfLiesOnTheGround(int scale)
    {
        (float fx, float fy) = SpriteShadow.TileAnchor(100, 200, 10, scale);
        // The foot pixel is the tile centre at its Z.
        (float x, float y, float z) = SpriteShadow.PixelPoint(fx, fy, fx, fy, 10, SpriteShadow.KindUpright, scale);
        Assert.Equal(100.5f, x, 3);
        Assert.Equal(200.5f, y, 3);
        Assert.Equal(10f, z, 3);
        // 40 unscaled pixels straight up: 10 Z higher, same spot.
        (x, y, z) = SpriteShadow.PixelPoint(fx, fy - 40 * scale, fx, fy, 10, SpriteShadow.KindUpright, scale);
        Assert.Equal(20f, z, 3);
        Assert.Equal(100.5f, x, 3);
        Assert.Equal(200.5f, y, 3);
        // Below the foot: the tile's front half on the ground, at the foot's Z, clamped inside the tile.
        (x, y, z) = SpriteShadow.PixelPoint(fx, fy + 22 * scale, fx, fy, 10, SpriteShadow.KindUpright, scale);
        Assert.Equal(10f, z, 3);
        Assert.Equal(101f - SpriteShadow.Inset, x, 3);
        Assert.Equal(201f - SpriteShadow.Inset, y, 3);
        // Sideways stays inside the foot tile (its own column's neighbours never read in).
        (x, y, _) = SpriteShadow.PixelPoint(fx + 30 * scale, fy - 60 * scale, fx, fy, 10, SpriteShadow.KindUpright, scale);
        Assert.InRange(x, 100f + SpriteShadow.Inset - 1e-4f, 101f - SpriteShadow.Inset + 1e-4f);
        Assert.InRange(y, 200f + SpriteShadow.Inset - 1e-4f, 201f - SpriteShadow.Inset + 1e-4f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void PixelPoint_GroundInvertsTheLandProjection(int scale)
    {
        // Every land corner (the compositor's Corner) maps back to its tile corner at the foot's Z.
        foreach (int c in new[] { LiveGroundProjection.Top, LiveGroundProjection.Right, LiveGroundProjection.Bottom, LiveGroundProjection.Left })
        {
            (float wx, float wy) = LiveGroundProjection.Corner(1500, 1700, c, 7, scale);
            (float x, float y, float z) = SpriteShadow.PixelPoint(wx, wy, 0, 0, 7, SpriteShadow.KindGround, scale);
            Assert.Equal(1500 + LiveGroundProjection.TexmapU[c], x, 2);
            Assert.Equal(1700 + LiveGroundProjection.TexmapV[c], y, 2);
            Assert.Equal(7f, z);
        }
    }

    [Fact]
    public void Visibility_IsSoftAroundTheCeiling_AndShadeMatchesTheTerrainFloor()
    {
        Assert.Equal(0f, SpriteShadow.Visibility(0f, 10f));
        Assert.Equal(1f, SpriteShadow.Visibility(20f, 10f));
        Assert.Equal(0.5f, SpriteShadow.Visibility(10f - LiveGroundLighting.ShadowBiasZ, 10f), 4);
        Assert.Equal(1f, SpriteShadow.Visibility(-127f, LiveGroundLighting.NoCeiling));
        Assert.Equal(LiveGroundLighting.MinShade, SpriteShadow.Shade(0f));
        Assert.Equal(1f, SpriteShadow.Shade(1f));
        // A lit sprite gets the terrain's flat lit shade, a shadowed one the terrain's shadow floor.
        Assert.Equal(LiveGroundLighting.Shade(0, 1, 0, 1f), SpriteShadow.Shade(1f), 4);
        Assert.Equal(LiveGroundLighting.Shade(0, 1, 0, 0f), SpriteShadow.Shade(0f), 4);
        Assert.Equal(SpriteShadow.KindGround, SpriteShadow.StaticKind(0));
        Assert.Equal(SpriteShadow.KindUpright, SpriteShadow.StaticKind(20));
    }

    [Fact]
    public void SampleCeiling_IsExactAtTexelCentres_AndBilinearBetween()
    {
        float[] c = LiveGroundLighting.ShadowCeiling(Hills(), null);
        const int n = LiveGroundLighting.ShadowSize;
        Assert.Equal(c[5 * n + 9], LiveGroundLighting.SampleCeiling(c, 9.5f / 4, 5.5f / 4), 4);
        Assert.Equal((c[5 * n + 9] + c[5 * n + 10]) / 2, LiveGroundLighting.SampleCeiling(c, 10f / 4, 5.5f / 4), 3);
    }
}

/// <summary>
/// Sprite shadow receive against the real map and art, and the evidence fixture LiveGroundEvidenceCapture.CaptureShadowRecv
/// renders: one Britain chunk (map statics drawn by IsoCompositor.BuildDrawList, the production placements), its corner
/// heights, static columns, sun mask and shadow ceiling, and a real human body frame (anim body 400) at three tiles.
/// Rewrite with UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1 (also writes the two PNGs); otherwise a present JSON must equal
/// what the builders make now.
/// </summary>
[Collection("UO install")]
public sealed class LiveGroundSpriteShadowFixtureTests
{
    private const int Tiles = TerrainLandBuilder.Tiles, GroundTexels = 16, AtlasWidth = 1024;
    private readonly UOData _data;

    public LiveGroundSpriteShadowFixtureTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static string EvidenceDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "LiveGroundCompare")))
                return Path.Combine(dir.FullName, "Assets", "LiveGroundCompare", "Evidence~");
        throw new DirectoryNotFoundException("repo root");
    }

    /// <summary>Field names are what LiveGroundEvidenceCapture.ShadowFixture reads (JsonUtility: flat arrays only).</summary>
    public sealed class Fixture
    {
        public string source = "";
        public int map, x0, y0, tiles, scale, originX, originY, shadowTexels, wrapTiles, occluderColumns, groundTexels;
        public float heightUnitPerZ, noCeiling, minShade, softZ, biasZ;
        public float[] sun = Array.Empty<float>();
        public int[] cornerZ = Array.Empty<int>();   // [j * (tiles + 1) + i]
        public int[] sunMask = Array.Empty<int>();   // terrain mask, rows bottom-up
        public float[] ceiling = Array.Empty<float>(); // ShadowCeiling, rows top-down in tile y
        public string groundPng = "", atlasPng = "", groundSha = "", atlasSha = "";
        public int atlasWidth, atlasHeight, quadCount, uprightQuads, groundQuads;
        // Per static quad (draw order): world-screen rect, atlas rect (top-down px), foot (wx, wy, z, kind).
        public int[] qRect = Array.Empty<int>(), qAtlas = Array.Empty<int>(), qTile = Array.Empty<int>();
        public float[] qFoot = Array.Empty<float>();
        // The body frame: atlas rect, anchor offsets (left = anchor x - ax, top = anchor y - ay).
        public int[] mobAtlas = Array.Empty<int>();
        public int mobAnchorX, mobAnchorY, mobBody, mobAction, mobDir;
        // Three placements: 0 partial (feet in shadow, head lit), 1 wholly in shadow, 2 wholly lit. Per placement:
        // tile x, y, z; foot probe and head probe world-screen pixel; the CPU shade expected there.
        public int[] mobTiles = Array.Empty<int>(), mobFootProbe = Array.Empty<int>(), mobHeadProbe = Array.Empty<int>();
        public float[] mobFootShade = Array.Empty<float>(), mobHeadShade = Array.Empty<float>();
    }

    private (int X0, int Y0) PickTownChunk(MapFile map)
    {
        int best = -1, bx = 0, by = 0;
        for (int y0 = 1600; y0 <= 1744; y0 += Tiles)
            for (int x0 = 1376; x0 <= 1520; x0 += Tiles)
            {
                int n = StaticOccluders.Count(StaticOccluders.FromMap(map, _data.TileData, x0, y0));
                if (n > best)
                    (best, bx, by) = (n, x0, y0);
            }
        return (bx, by);
    }

    /// <summary>The CPU shade at a world-screen pixel of an upright sprite standing on chunk-local data.</summary>
    private static float ShadeAt(float[] ceiling, int x0, int y0, float wx, float wy, float footX, float footY, float footZ, int kind)
    {
        (float x, float y, float z) = SpriteShadow.PixelPoint(wx, wy, footX, footY, footZ, kind, 1);
        return SpriteShadow.Shade(SpriteShadow.Visibility(z, LiveGroundLighting.SampleCeiling(ceiling, x - x0, y - y0)));
    }

    private sealed class Atlas
    {
        public readonly List<uint> Pixels = new();
        public int Height, X, Y, Shelf;
        private readonly Dictionary<Bitmap32, (int, int)> _placed = new(ReferenceEqualityComparer.Instance);

        public (int U, int V) Add(Bitmap32 b)
        {
            if (_placed.TryGetValue(b, out (int, int) at))
                return at;
            if (X + b.Width + 1 > AtlasWidth) { X = 0; Y += Shelf; Shelf = 0; }
            at = (X, Y);
            X += b.Width + 1;
            Shelf = Math.Max(Shelf, b.Height + 1);
            int need = (Y + Shelf) * AtlasWidth;
            while (Pixels.Count < need) Pixels.Add(0);
            for (int r = 0; r < b.Height; r++)
                for (int c = 0; c < b.Width; c++)
                    Pixels[(at.Item2 + r) * AtlasWidth + at.Item1 + c] = b.Pixels[r * b.Width + c];
            _placed[b] = at;
            Height = Math.Max(Height, Y + Shelf);
            return at;
        }
    }

    private (Fixture F, byte[] GroundRgba, byte[] AtlasRgba) Build()
    {
        MapFile map = _data.GetMap(0);
        (int x0, int y0) = PickTownChunk(map);
        RealMapPatch patch = RealMapPatch.FromMap(map, x0, y0, Tiles, Tiles);
        int[,] occluders = StaticOccluders.FromMap(map, _data.TileData, x0, y0);
        var cornerZ = new int[Tiles + 1, Tiles + 1];
        for (int j = 0; j <= Tiles; j++)
            for (int i = 0; i <= Tiles; i++)
                cornerZ[i, j] = patch.CornerZ(i, j);
        float[] ceiling = LiveGroundLighting.ShadowCeiling(cornerZ, occluders);
        (float ox, float oy) = SpriteShadow.TileAnchor(x0, y0, 0, 1);

        // Ground: the terrain backend's own bake (flat land sprites unrotated into the tile), 16 texels per tile.
        var tb = new TerrainLandBuilder(x0, y0, GroundTexels);
        for (int j = 0; j < Tiles; j++)
            for (int i = 0; i < Tiles; i++)
            {
                (sbyte t, sbyte r, sbyte b, sbyte l) = patch.Corners(i, j);
                ushort g = patch.Graphic(i, j);
                tb.AddTile(LiveGroundProjection.LandItem(x0 + i, y0 + j, g, t, r, b, l, hasTexmap: false), _data.GetLand(g), false);
            }
        tb.SetOccluders(occluders);
        TerrainLandData land = tb.Finish();

        var f = new Fixture
        {
            source = "map0 land/statics + tiledata + anim body 400 via UOInstallFixture; statics = IsoCompositor.BuildDrawList (production placements, scale 1)",
            map = 0, x0 = x0, y0 = y0, tiles = Tiles, scale = 1, originX = (int)ox, originY = (int)oy,
            shadowTexels = LiveGroundLighting.ShadowTexelsPerTile, wrapTiles = SpriteShadow.WrapTiles,
            occluderColumns = StaticOccluders.Count(occluders), groundTexels = GroundTexels,
            heightUnitPerZ = TerrainLandBuilder.HeightUnitPerZ, noCeiling = LiveGroundLighting.NoCeiling, minShade = LiveGroundLighting.MinShade,
            softZ = SpriteShadow.SoftZ, biasZ = LiveGroundLighting.ShadowBiasZ,
            sun = new[] { LiveGroundLighting.SunTerrain.X, LiveGroundLighting.SunTerrain.Y, LiveGroundLighting.SunTerrain.Z },
            cornerZ = new int[(Tiles + 1) * (Tiles + 1)],
            sunMask = Array.ConvertAll(land.SunMask, b => (int)b),
            ceiling = Array.ConvertAll(ceiling, c => (float)Math.Round(c, 3)),
            groundPng = "shadowrecv_ground.png", atlasPng = "shadowrecv_atlas.png",
        };
        for (int j = 0; j <= Tiles; j++)
            for (int i = 0; i <= Tiles; i++)
                f.cornerZ[j * (Tiles + 1) + i] = cornerZ[i, j];

        // Statics, in the compositor's draw order, as the streamer would quad them.
        var atlas = new Atlas();
        var rect = new List<int>(); var at = new List<int>(); var tile = new List<int>(); var foot = new List<float>();
        foreach (IsoCompositor.DrawEntry e in new IsoCompositor(_data).BuildDrawList(0, x0, y0, Tiles, Tiles))
        {
            DrawItem it = e.Item;
            if (e.Sprite == null || it.Kind != DrawKind.Static)
                continue;
            (int u, int v) = atlas.Add(e.Sprite);
            int kind = SpriteShadow.StaticKind(_data.TileData.GetStatic(it.Graphic).Height);
            (float fx, float fy) = SpriteShadow.TileAnchor(it.X, it.Y, it.Z, 1);
            rect.AddRange(new[] { it.Left, it.Top, it.Width, it.Height });
            at.AddRange(new[] { u, v, e.Sprite.Width, e.Sprite.Height });
            tile.AddRange(new[] { it.X, it.Y, it.Z });
            foot.AddRange(new[] { fx, fy, it.Z, kind });
            if (kind == SpriteShadow.KindUpright) f.uprightQuads++; else f.groundQuads++;
        }
        f.qRect = rect.ToArray(); f.qAtlas = at.ToArray(); f.qTile = tile.ToArray(); f.qFoot = foot.ToArray();
        f.quadCount = rect.Count / 4;

        // A real human body frame (stand), anchored as MobileArt does: left = -CenterX, top = -(CenterY + height).
        (f.mobBody, f.mobAction, f.mobDir) = (400, 4, 1);
        AnimationFrame frame = _data.Animations.Read(f.mobBody, f.mobAction, f.mobDir)[0];
        Bitmap32 body = frame.Image;
        (int mu, int mv) = atlas.Add(body);
        f.mobAtlas = new[] { mu, mv, body.Width, body.Height };
        f.mobAnchorX = frame.CenterX;
        f.mobAnchorY = frame.CenterY + body.Height;
        // Probes: an opaque pixel nearest the middle, three rows inside the frame's lowest (feet) and highest (head) opaque row.
        bool Opaque(int c, int r) => c >= 0 && c < body.Width && r >= 0 && r < body.Height && (body.Pixels[r * body.Width + c] >> 24) != 0;
        (int Col, int Row) Probe(bool low)
        {
            int edge = -1;
            for (int k = 0; k < body.Height && edge < 0; k++)
                for (int c = 0; c < body.Width; c++)
                    if (Opaque(c, low ? body.Height - 1 - k : k)) { edge = low ? body.Height - 1 - k : k; break; }
            int row = low ? edge - 3 : edge + 3;
            for (int d = 0; d < body.Width; d++)
                foreach (int c in new[] { body.Width / 2 - d, body.Width / 2 + d })
                    if (Opaque(c, row))
                        return (c, row);
            throw new InvalidOperationException("empty frame");
        }
        (int fc, int fr) = Probe(true);
        (int hc, int hr) = Probe(false);

        int[] found = { -1, -1, -1 };
        var picks = new (int X, int Y, int Z, int Fx, int Fy, int Hx, int Hy, float Fs, float Hs)[3];
        for (int j = 1; j < Tiles - 1; j++)
            for (int i = 1; i < Tiles - 1; i++)
            {
                if (occluders[i, j] != StaticOccluders.None)
                    continue;
                int z = (int)Math.Round(TerrainLandBuilder.SampleZ(cornerZ, i + 0.5f, j + 0.5f));
                (float ax, float ay) = SpriteShadow.TileAnchor(x0 + i, y0 + j, z, 1);
                int left = (int)ax - f.mobAnchorX, top = (int)ay - f.mobAnchorY;
                (int px, int py, int qx, int qy) = (left + fc, top + fr, left + hc, top + hr);
                float fs = ShadeAt(ceiling, x0, y0, px + 0.5f, py + 0.5f, ax, ay, z, SpriteShadow.KindUpright);
                float hs = ShadeAt(ceiling, x0, y0, qx + 0.5f, qy + 0.5f, ax, ay, z, SpriteShadow.KindUpright);
                int which = fs <= LiveGroundLighting.MinShade + 1e-4f && hs >= 1f - 1e-4f ? 0
                    : fs <= LiveGroundLighting.MinShade + 1e-4f && hs <= LiveGroundLighting.MinShade + 1e-4f ? 1
                    : fs >= 1f - 1e-4f && hs >= 1f - 1e-4f ? 2 : -1;
                if (which >= 0 && found[which] < 0)
                {
                    found[which] = 1;
                    picks[which] = (x0 + i, y0 + j, z, px, py, qx, qy, fs, hs);
                }
            }
        Assert.True(found.All(v => v > 0), $"chunk {x0},{y0}: partial/shadow/lit placements found: {string.Join(",", found)}");
        f.mobTiles = picks.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
        f.mobFootProbe = picks.SelectMany(p => new[] { p.Fx, p.Fy }).ToArray();
        f.mobHeadProbe = picks.SelectMany(p => new[] { p.Hx, p.Hy }).ToArray();
        f.mobFootShade = picks.Select(p => p.Fs).ToArray();
        f.mobHeadShade = picks.Select(p => p.Hs).ToArray();

        f.atlasWidth = AtlasWidth;
        f.atlasHeight = (atlas.Height + 63) / 64 * 64;
        var atlasRgba = new byte[AtlasWidth * f.atlasHeight * 4];
        for (int k = 0; k < atlas.Pixels.Count; k++)
        {
            uint p = atlas.Pixels[k];
            (atlasRgba[k * 4], atlasRgba[k * 4 + 1], atlasRgba[k * 4 + 2], atlasRgba[k * 4 + 3]) = ((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
        }
        // The bake is Unity rows bottom-up; the PNG is top-down, so LoadImage gives the bake's layout back.
        int gs = land.TextureSize;
        var groundRgba = new byte[gs * gs * 4];
        for (int r = 0; r < gs; r++)
            Array.Copy(land.Rgba, (gs - 1 - r) * gs * 4, groundRgba, r * gs * 4, gs * 4);
        f.groundSha = Sha(groundRgba);
        f.atlasSha = Sha(atlasRgba);
        return (f, groundRgba, atlasRgba);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public void SpriteShadow_RealTownChunk_ShadesPerPixel_FeetDarkHeadLit()
    {
        (Fixture f, byte[] ground, byte[] atlas) = Build();
        Assert.True(f.occluderColumns > 10, $"{f.occluderColumns} columns");
        Assert.True(f.uprightQuads > 20 && f.groundQuads > 0, $"{f.uprightQuads} upright / {f.groundQuads} ground statics");
        // Partial: the same sprite, same frame, is in the shadow at its feet and in the sun at its head.
        Assert.Equal(LiveGroundLighting.MinShade, f.mobFootShade[0], 3);
        Assert.Equal(1f, f.mobHeadShade[0], 3);
        // Statics receive too: some upright static pixel of this town chunk is in shadow and some is lit.
        float[] ceiling = LiveGroundLighting.ShadowCeiling(ToGrid(f.cornerZ), StaticOccluders.FromMap(_data.GetMap(0), _data.TileData, f.x0, f.y0));
        int dark = 0, lit = 0;
        for (int q = 0; q < f.quadCount; q++)
        {
            if ((int)f.qFoot[q * 4 + 3] != SpriteShadow.KindUpright)
                continue;
            float s = ShadeAt(ceiling, f.x0, f.y0, f.qRect[q * 4] + f.qRect[q * 4 + 2] / 2f, f.qRect[q * 4 + 1] + f.qRect[q * 4 + 3] - 4f,
                f.qFoot[q * 4], f.qFoot[q * 4 + 1], f.qFoot[q * 4 + 2], SpriteShadow.KindUpright);
            if (s < 0.75f) dark++; else if (s > 0.99f) lit++;
        }
        Assert.True(dark > 0 && lit > 0, $"upright statics at their base: {dark} in shadow, {lit} lit");

        string dir = EvidenceDir(), path = Path.Combine(dir, "shadowRecvChunk.json");
        string built = System.Text.Json.JsonSerializer.Serialize(f, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        if (Environment.GetEnvironmentVariable("UOCLIENT_WRITE_LIVEGROUND_EVIDENCE") == "1")
        {
            File.WriteAllText(path, built + "\n");
            File.WriteAllBytes(Path.Combine(dir, f.groundPng), Png.Encode(ground, (int)Math.Sqrt(ground.Length / 4), (int)Math.Sqrt(ground.Length / 4)));
            File.WriteAllBytes(Path.Combine(dir, f.atlasPng), Png.Encode(atlas, f.atlasWidth, f.atlasHeight));
        }
        if (File.Exists(path))
            Assert.Equal(built, File.ReadAllText(path).TrimEnd('\n'));
    }

    [Fact]
    public void SpriteShadow_CeilingCostPerChunk_IsBoundedOnTheWorker()
    {
        MapFile map = _data.GetMap(0);
        (int x0, int y0) = PickTownChunk(map);
        RealMapPatch patch = RealMapPatch.FromMap(map, x0, y0, Tiles, Tiles);
        var cornerZ = new int[Tiles + 1, Tiles + 1];
        for (int j = 0; j <= Tiles; j++)
            for (int i = 0; i <= Tiles; i++)
                cornerZ[i, j] = patch.CornerZ(i, j);
        int[,] occ = StaticOccluders.FromMap(map, _data.TileData, x0, y0);
        LiveGroundLighting.ShadowCeiling(cornerZ, occ); // JIT
        LiveGroundLighting.ShadowMask(cornerZ, occ);
        const int runs = 20;
        var w = System.Diagnostics.Stopwatch.StartNew();
        for (int k = 0; k < runs; k++) LiveGroundLighting.ShadowCeiling(cornerZ, occ);
        double ceilingMs = w.Elapsed.TotalMilliseconds / runs;
        w.Restart();
        for (int k = 0; k < runs; k++) LiveGroundLighting.ShadowMask(cornerZ, occ);
        double maskMs = w.Elapsed.TotalMilliseconds / runs;
        // Worker thread, once per chunk build (the mask already costs the same order); generous bound for CI noise.
        Assert.True(ceilingMs < 25, $"ceiling {ceilingMs:0.00} ms/chunk, mask {maskMs:0.00} ms/chunk (town chunk {x0},{y0})");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "uoclient_lg_ceiling_cost.txt"), $"ceiling {ceilingMs:0.000} ms/chunk, mask {maskMs:0.000} ms/chunk, chunk {x0},{y0}\n");
    }

    private static int[,] ToGrid(int[] flat)
    {
        var g = new int[Tiles + 1, Tiles + 1];
        for (int j = 0; j <= Tiles; j++)
            for (int i = 0; i <= Tiles; i++)
                g[i, j] = flat[j * (Tiles + 1) + i];
        return g;
    }

    /// <summary>Minimal RGBA8 PNG writer (zlib via System.IO.Compression) for the fixture art.</summary>
    private static class Png
    {
        private static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static byte[] Encode(byte[] rgba, int w, int h)
        {
            using var raw = new MemoryStream();
            using (var z = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Optimal, true))
                for (int r = 0; r < h; r++)
                {
                    z.WriteByte(0);
                    z.Write(rgba, r * w * 4, w * 4);
                }
            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            var ihdr = new byte[13];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
            ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", raw.ToArray());
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            s.Write(len);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t);
            s.Write(data);
            uint c = 0xFFFFFFFFu;
            foreach (byte b in t.Concat(data)) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, c ^ 0xFFFFFFFFu);
            s.Write(len);
        }
    }
}

/// <summary>
/// Sprite shadow receive source guards: the default WorldSprite variant (no LG_SPRITE_SHADOW) is the 4aa51a6 shader
/// text exactly, the shader's constants are SpriteShadow's, and every seam is behind MeshBuilder.FootMetadata.
/// </summary>
public sealed class LiveGroundSpriteShadowSourceTests
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

    private static string Shader() => Source("Assets", "UOClient.Unity", "Runtime", "Resources", "WorldSprite.shader");

    /// <summary>
    /// The shader as the compiler sees it without its keywords: every #ifdef LG_SPRITE_SHADOW and #ifdef UO_ITEM_LIGHTS
    /// (item lights, remainingFixes batch G: on only while a light is) block and their pragmas removed.
    /// </summary>
    public static string DefaultVariant(string shader)
    {
        var kept = new List<string>();
        int depth = 0;
        bool lg = false;
        foreach (string line in shader.Split('\n'))
        {
            string t = line.Trim();
            if (depth == 0 && (t == "#ifdef LG_SPRITE_SHADOW" || t == "#ifdef UO_ITEM_LIGHTS")) { depth = 1; lg = t.EndsWith("LG_SPRITE_SHADOW"); continue; }
            if (depth > 0)
            {
                if (t.StartsWith("#if")) depth++;
                else if (t == "#endif") depth--;
                Assert.False(lg && depth > 0 && t.StartsWith("#else"), "no #else in a LG_SPRITE_SHADOW block: the default path must not change");
                continue;
            }
            if (t == "#pragma multi_compile __ LG_SPRITE_SHADOW" || t == "#pragma multi_compile __ UO_ITEM_LIGHTS")
                continue;
            kept.Add(line);
        }
        Assert.Equal(0, depth);
        return string.Join("\n", kept);
    }

    [Fact]
    public void DefaultVariant_IsTheHeadShaderByteForByte()
    {
        string head = Source("Assets", "LiveGroundCompare", "Evidence~", "WorldSprite.4aa51a6.shader.txt");
        string now = Shader();
        Assert.NotEqual(head, now); // the TEST path is there
        // Since 4aa51a6 the normal shader has one more state, the spectral blend (remainingFixes batch D): a property-driven
        // Blend whose defaults, One / Zero, are the no-blend state it had. Without exactly those lines it is the head shader.
        string[] spectral =
        {
            "        // Blend state: One/Zero (no blending, as always) unless a material asks; Avatar's spectral look sets",
            "        // SrcAlpha/OneMinusSrcAlpha for TazUO's SPECTRAL mode (see-through black, MobileLook.SpectralPixel).",
            "        [HideInInspector] _SrcBlend (\"Src blend\", Float) = 1",
            "        [HideInInspector] _DstBlend (\"Dst blend\", Float) = 0",
            "        Blend [_SrcBlend] [_DstBlend]",
        };
        // Since then UO's water art shimmers (WaterArt, the 3D water surface made opt-in): one function and four lines.
        string waterFunction = "            // UO's water art, animated without moving its pixels (that would pull neighbours in from the atlas): slow\n            // rolling light swells crossing each other, a finer ripple, and sharp moving glints on the crests.\n            // world: Unity units (world pixels at the art scale).\n            float3 UOWaterShimmer(float3 rgb, float2 world)\n            {\n                float t = _Time.y;\n                float2 p = world * 0.011;\n                float w1 = sin(p.x * 1.7 + p.y * 0.6 + t * 1.05);\n                float w2 = sin(-p.x * 0.8 + p.y * 1.9 - t * 0.8);\n                float w3 = sin((p.x + p.y) * 3.3 + t * 2.2);\n                float swell = (w1 + w2) * 0.5;\n                float light = 1.0 + 0.08 * swell + 0.035 * w3;\n                // Glints: small sparkles where two fine, fast ripples crest together, brighter on the swells' crests.\n                float2 q = world * 0.07;\n                float g1 = sin(q.x * 1.3 + q.y * 0.9 + t * 3.1);\n                float g2 = sin(-q.x * 0.7 + q.y * 1.6 - t * 2.6);\n                float glint = pow(saturate(g1 * g2), 24.0) * (0.25 + 0.2 * saturate(swell)) ;\n                return rgb * light + glint * float3(0.8, 0.9, 1.0);\n            }\n\n";
        string[] water =
        {
            "                float water : TEXCOORD5;   // 1: UO water art (land or ocean static): animated shimmer",
            "                o.water = v.height.y;",
            "                if (i.water > 0.5)",
            "                    c.rgb = UOWaterShimmer(c.rgb, i.world);",
        };
        string variant = DefaultVariant(now).Replace("\r\n", "\n");
        Assert.Contains(waterFunction, variant);
        variant = variant.Replace(waterFunction, "");
        string[] lines = variant.Split('\n');
        foreach (string s in spectral.Concat(water))
            Assert.Single(lines, l => l.TrimEnd('\r') == s);
        Assert.Equal(head, string.Join("\n", lines.Where(l => !spectral.Contains(l.TrimEnd('\r')) && !water.Contains(l.TrimEnd('\r')))));
        Assert.DoesNotContain("_LG", DefaultVariant(now));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(now, "multi_compile").Count); // LG_SPRITE_SHADOW, UO_ITEM_LIGHTS
    }

    [Fact]
    public void Shader_UsesSpriteShadowsConstants()
    {
        string s = Shader();
        Assert.Contains($"tile + {SpriteShadow.Inset.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}, tile + {(1f - SpriteShadow.Inset).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}", s);
        Assert.Contains($"(z + {LiveGroundLighting.ShadowBiasZ.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} - ceiling) / {SpriteShadow.SoftZ:0.0} + 0.5", s);
        Assert.Contains($"{LiveGroundLighting.MinShade:0.0} + {1f - LiveGroundLighting.MinShade:0.0} * visibility", s);
        Assert.Contains("const float h = 22.0, zs = 4.0;", s);
        Assert.Equal(22, LiveGroundProjection.TileHalf);
        Assert.Equal(4, LiveGroundProjection.ZStep);
        Assert.Contains("foot : TEXCOORD2", s); // UV3, unused by every other sprite mesh
    }

    [Fact]
    public void Seams_AreAllBehindFootMetadata_AndThePresenterRestoresThem()
    {
        string streamer = Source("Assets", "UOClient.Unity", "Runtime", "WorldStreamer.cs");
        Assert.Contains("if (MeshBuilder.FootMetadata)", streamer);
        Assert.Contains("mb.AddQuad(rect, uv, q.Depth, false, q.CutHeight, q.Water);", streamer); // today's call (plus the water flag), kept for the default
        foreach (string f in new[] { Path.Combine("Runtime", "Avatar.cs"), Path.Combine("Runtime", "World", "ItemView.cs") })
        {
            string s = Source("Assets", "UOClient.Unity", f);
            Assert.Contains("MeshBuilder.FootMetadata", s);
        }
        string mb = Source("Assets", "UOClient.Unity", "Runtime", "WorldView.cs");
        Assert.Contains("if (!FootMetadata)", mb);
        Assert.Contains("if (_feet != null)", mb);
        // Only the presenter's ceiling turns the keyword and the flag on, and its Dispose puts them back.
        string ceiling = Source("Assets", "LiveGroundCompare", "Scripts", "SpriteShadowCeiling.cs");
        Assert.Contains("Shader.EnableKeyword(Keyword);", ceiling);
        Assert.Contains("Shader.DisableKeyword(Keyword);", ceiling);
        Assert.Contains("MeshBuilder.FootMetadata = _footWas;", ceiling);
        Assert.DoesNotContain("QualitySettings.", ceiling);
        foreach (string f in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets"), "*.cs", SearchOption.AllDirectories))
        {
            string s = File.ReadAllText(f);
            if (s.Contains("EnableKeyword(") && s.Contains("LG_SPRITE_SHADOW"))
                Assert.True(f.EndsWith("SpriteShadowCeiling.cs") || Path.GetFileName(f).StartsWith("LiveGroundEvidenceCapture."), f); // the harness restores it too
            if (s.Contains("FootMetadata = true"))
                Assert.EndsWith("SpriteShadowCeiling.cs", f);
        }
        Assert.Contains("SpriteCeiling?.Dispose();", Source("Assets", "LiveGroundCompare", "Scripts", "TerrainIsoPresenter.cs"));
        // The receive harness is explicit-invoke only, and puts the keyword back.
        string harness = Source("Assets", "LiveGroundCompare", "Editor", "LiveGroundEvidenceCapture.ShadowRecv.cs");
        foreach (string auto in new[] { "InitializeOnLoad", "RuntimeInitializeOnLoadMethod", "DidReloadScripts", "EnterPlaymode", "isPlaying =" })
            Assert.DoesNotContain(auto, harness);
        Assert.Contains("[MenuItem(", harness);
        Assert.Contains("if (keywordWas) Shader.EnableKeyword(SpriteKeyword); else Shader.DisableKeyword(SpriteKeyword);", harness);
    }
}
