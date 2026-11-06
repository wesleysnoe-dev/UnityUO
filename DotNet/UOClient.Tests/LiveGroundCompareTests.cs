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
    public void Occlusion_NeedsNoChangeToTheDefaultSpriteShader()
    {
        // The proxy is its own depth-only shader; UOClient/WorldSprite has no LiveGround keyword or global.
        string sprite = Source("Assets", "UOClient.Unity", "Runtime", "Resources", "WorldSprite.shader");
        Assert.DoesNotContain("_LG", sprite);
        Assert.DoesNotContain("multi_compile", sprite);
        string depth = Source("Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareDepthKey.shader");
        Assert.Contains("ColorMask 0", depth);
        Assert.Contains("ZWrite On", depth);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Assets", "LiveGroundCompare", "Shaders", "Resources", "LiveGroundCompareDepthKey.shader.meta")));
    }
}
