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
}
