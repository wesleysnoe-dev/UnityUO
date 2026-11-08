using System;
using UOClient.LiveGroundCompare;

namespace UOClient.Tests;

/// <summary>Grass pilot: blades grow where the natural ground is grass, as thick as the paint, on the terrain's heights.</summary>
public class GrassBuilderTests
{
    private const int Tiles = TerrainLandBuilder.Tiles;

    /// <summary>A flat chunk at one height (0..1 of the Z range) with every splat sample set to one material weight.</summary>
    private static TerrainLandData Chunk(int material, byte weight, float height = 0.5f, int art = 0)
    {
        int n = Tiles * TerrainLandBuilder.SmoothSamplesPerTile + 1, s = SplatBuilder.Size;
        var heights = new float[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                heights[r, c] = height;
        var splat = new byte[3][];
        for (int k = 0; k < 3; k++)
            splat[k] = new byte[s * s * 4];
        for (int i = 0; i < s * s; i++)
        {
            splat[material / 4][i * 4 + material % 4] = weight;
            splat[2][i * 4] = (byte)art;
        }
        return new TerrainLandData { ChunkX0 = 160, ChunkY0 = 320, HeightmapResolution = n, Heights = heights, Splat = splat, TileCount = Tiles * Tiles };
    }

    [Fact]
    public void FullGrass_GrowsAboutTheCandidateCount()
    {
        GrassData g = GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 255));
        Assert.NotNull(g);
        int candidates = Tiles * Tiles * GrassBuilder.BladesPerTile;
        Assert.InRange(g.Count, (int)(candidates * 0.97), candidates);
        Assert.Equal(g.Count * 3, g.VertexCount);
        Assert.Equal(g.VertexCount * 4, g.Colors.Count);
    }

    [Fact]
    public void HalfPaint_GrowsAboutHalf()
    {
        int full = GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 255)).Count;
        int half = GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 128)).Count;
        Assert.InRange(half, full * 0.4, full * 0.6);
    }

    [Theory]
    [InlineData(TerrainMaterials.Sand)]
    [InlineData(TerrainMaterials.Rock)]
    [InlineData(TerrainMaterials.Snow)]
    [InlineData(TerrainMaterials.Cobble)]
    public void BareGround_GrowsNone(int material) => Assert.Null(GrassBuilder.Build(Chunk(material, 255)));

    [Fact]
    public void ClassicArt_GrowsNone() => Assert.Null(GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 255, art: 255)));

    [Fact]
    public void Blades_StandOnTheTerrainInsideTheChunk()
    {
        TerrainLandData d = Chunk(TerrainMaterials.Grass, 255, height: 0.75f);
        GrassData g = GrassBuilder.Build(d);
        var (_, posY, _) = d.Position;
        var (_, sizeY, _) = d.Size;
        float ground = posY + 0.75f * sizeY;
        for (int v = 0; v < g.VertexCount; v += 3) // each blade's first root vertex
        {
            float x = g.Positions[v * 3], y = g.Positions[v * 3 + 1], z = g.Positions[v * 3 + 2];
            Assert.InRange(y, ground - 0.05f, ground + 0.001f);
            Assert.InRange(x, d.ChunkX0 - 0.1f, d.ChunkX0 + Tiles + 0.1f);
            Assert.InRange(-z, d.ChunkY0 - 0.1f, d.ChunkY0 + Tiles + 0.1f);
            Assert.True(g.Positions[(v + 2) * 3 + 1] > y + 0.2f); // the tip is well above the root
        }
    }

    [Fact]
    public void Project_EachBladeSortsJustNearerThanItsOwnTilesLand()
    {
        TerrainLandData d = Chunk(TerrainMaterials.Grass, 255);
        GrassData g = GrassBuilder.Build(d);
        var tileDepth = new float[Tiles, Tiles];
        for (int i = 0; i < Tiles; i++)
            for (int j = 0; j < Tiles; j++)
                tileDepth[i, j] = i == 3 && j == 5 ? float.NaN : -(i + j) * 30.72f; // one diagonal per DepthKey.PerDiagonal * Step
        GrassBuilder.Project(g, tileDepth, d.ChunkX0, d.ChunkY0, 1);
        Assert.Equal(g.VertexCount, g.Depth.Length);
        for (int v = 0; v < g.VertexCount; v += 3)
        {
            float x = (g.Positions[v * 3] + g.Positions[(v + 1) * 3]) * 0.5f, z = (g.Positions[v * 3 + 2] + g.Positions[(v + 1) * 3 + 2]) * 0.5f;
            int i = (int)Math.Floor(x) - d.ChunkX0, j = (int)Math.Floor(-z) - d.ChunkY0;
            float nearHalf = (x - (float)Math.Floor(x)) + (-z - (float)Math.Floor(-z)) > 1f ? GrassBuilder.DiagonalDepth : 0f;
            if (i == 3 && j == 5)
                Assert.True(float.IsNaN(g.Depth[v]));
            else
                Assert.Equal(tileDepth[i, j] - nearHalf - GrassBuilder.NearerSteps * GrassBuilder.DepthStep, g.Depth[v], 4);
            Assert.Equal(g.Depth[v], g.Depth[v + 2]); // the whole blade sorts as one
            Assert.True(g.ScreenY[v + 2] < g.ScreenY[v]); // its tip is above its root on screen
        }
    }

    [Fact]
    public void SameChunk_SameBlades()
    {
        GrassData a = GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 200));
        GrassData b = GrassBuilder.Build(Chunk(TerrainMaterials.Grass, 200));
        Assert.Equal(a.Positions, b.Positions);
    }
}
