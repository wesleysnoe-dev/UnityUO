using UOClient.ArtPipeline;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class BakerTests
{
    // The timber-framed house tested with ComfyUI: roofs, interior, sloped land.
    private const int Map = 0, X = 1460, Y = 1664, W = 14, H = 14;

    private readonly UOData _data;

    public BakerTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void PlacementKeysMatchAcrossRoofPasses()
    {
        Composite roofs = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        Composite noRoofs = new IsoCompositor(_data) { HideRoofs = true }.Compose(Map, X, Y, W, H);

        var keys = roofs.Items.Select(i => i.Key).ToHashSet();
        Assert.Equal(roofs.Items.Count, keys.Count); // keys are unique
        Assert.True(noRoofs.Items.Count < roofs.Items.Count);
        Assert.All(noRoofs.Items, i => Assert.Contains(i.Key, keys));
    }

    /// <summary>
    /// A restyle with position-dependent noise (like real diffusion output) must come back exactly
    /// from the baked placements wherever it is visible: no repetition, no seams.
    /// </summary>
    [Fact]
    public void BakedPlacementsReproduceAPositionDependentRestyle()
    {
        Composite roofs = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        Composite noRoofs = new IsoCompositor(_data) { HideRoofs = true }.Compose(Map, X, Y, W, H);
        Bitmap32 restyledRoofs = NoisyRestyle(roofs);

        var baker = new Baker(_data, 2);
        baker.Add(roofs, restyledRoofs);
        baker.Add(noRoofs, NoisyRestyle(noRoofs));
        List<BakedPlacement> baked = baker.Build();
        Assert.Equal(roofs.Items.Count, baked.Count);

        var byKey = baked.ToDictionary(b => b.Key, b => b.Image);
        Composite rebuilt = new IsoCompositor(_data)
        {
            Scale = 2,
            PlacementOverride = it => byKey.TryGetValue(it.Key, out Bitmap32? b) ? b : null
        }.Compose(Map, X, Y, W, H);

        // Offsets agree with where the renderer put each placement.
        foreach (BakedPlacement b in baked)
        {
            DrawItem it = rebuilt.Items.Single(i => i.Key.Equals(b.Key));
            (int ax, int ay) = IsoCompositor.Anchor(it, 2);
            Assert.Equal(ax + b.OffsetX - rebuilt.OriginX, it.Left);
            Assert.Equal(ay + b.OffsetY - rebuilt.OriginY, it.Top);
        }

        // Compare in world coordinates: the 2x render's frame can sit a pixel off the 2x-upscaled 1x frame.
        int dx = rebuilt.OriginX - roofs.OriginX * 2, dy = rebuilt.OriginY - roofs.OriginY * 2;
        int exact = 0, drawn = 0;
        for (int y = 0; y < rebuilt.Image.Height; y++)
        {
            for (int x = 0; x < rebuilt.Image.Width; x++)
            {
                int sx = x + dx, sy = y + dy;
                if (rebuilt.Owner[y * rebuilt.Image.Width + x] < 0 || (uint)sx >= (uint)restyledRoofs.Width || (uint)sy >= (uint)restyledRoofs.Height)
                    continue;
                drawn++;
                if (rebuilt.Image[x, y] == (restyledRoofs[sx, sy] | 0xFF000000))
                    exact++;
            }
        }
        double fraction = (double)exact / drawn;
        // The only misses are where 2x sloped-land rasterization covers slightly different edge pixels than 1x.
        Assert.True(fraction > 0.99, $"only {fraction:P2} of {drawn} pixels reproduced exactly");
    }

    [Fact]
    public void HiddenPixelsAreFilledInsideTheSilhouette()
    {
        Composite roofs = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        var baker = new Baker(_data, 2);
        baker.Add(roofs, NoisyRestyle(roofs)); // roofs only: interiors are hidden, so fills must run
        List<BakedPlacement> baked = baker.Build();

        Assert.Contains(baked, b => b.FromGraphic + b.FromOriginal > 0);
        var compositor = new IsoCompositor(_data) { Scale = 2 };
        foreach (BakedPlacement b in baked)
        {
            DrawItem it = roofs.Items.Single(i => i.Key.Equals(b.Key));
            Bitmap32 silhouette = compositor.RenderPlacementAlone(it);
            for (int i = 0; i < silhouette.Pixels.Length; i++)
            {
                bool inside = silhouette.Pixels[i] >> 24 != 0;
                Assert.Equal(inside, b.Image.Pixels[i] >> 24 != 0);
                Assert.Equal(inside, b.Source[i] != BakedSource.Transparent);
            }
            Assert.Equal(b.Opaque, b.FromOwn + b.FromOtherPass + b.FromGraphic + b.FromOriginal);
        }
    }

    [Fact]
    public void BakedRegionRoundTripsThroughDisk()
    {
        Composite roofs = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        var baker = new Baker(_data, 2);
        baker.Add(roofs, NoisyRestyle(roofs));
        List<BakedPlacement> baked = baker.Build();

        string dir = Path.Combine(Path.GetTempPath(), "uoclient-bake-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            BakedRegion.Save(dir, Map, 2, baked, pageSize: 512); // small pages force several
            BakedRegion region = BakedRegion.Open(dir);
            Assert.Equal(baked.Count, region.Count);
            Assert.True(Directory.GetFiles(dir, "page_*.png").Length > 1);

            foreach (BakedPlacement b in baked)
            {
                DrawItem it = roofs.Items.Single(i => i.Key.Equals(b.Key));
                Bitmap32 back = region.Get(it)!;
                Assert.Equal(b.Image.Pixels, back.Pixels);
                Assert.True(region.TryGetOffset(b.Key, out int ox, out int oy));
                Assert.Equal((b.OffsetX, b.OffsetY), (ox, oy));
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>2x upscale plus noise that depends on image position, so every placement of a graphic differs.</summary>
    private static Bitmap32 NoisyRestyle(Composite c)
    {
        Bitmap32 up = Resample.Nearest(c.Image, 2);
        for (int y = 0; y < up.Height; y++)
        {
            for (int x = 0; x < up.Width; x++)
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663);
                up.Pixels[y * up.Width + x] ^= (h & 0x001F1F1F);
            }
        }
        return up;
    }
}
