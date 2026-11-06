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
}
