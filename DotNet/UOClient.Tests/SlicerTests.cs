using UOClient.ArtPipeline;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class SlicerTests
{
    // Britain bank: buildings, roofs and sloped ground, so occlusion and stretched land both occur.
    private const int Map = 0, X = 1424, Y = 1688, W = 16, H = 16;

    private readonly UOData _data;

    public SlicerTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void PngRoundTrips()
    {
        var bmp = new Bitmap32(7, 3);
        for (int i = 0; i < bmp.Pixels.Length; i++)
            bmp.Pixels[i] = (uint)(i * 0x01030507) | (i % 2 == 0 ? 0xFF000000 : 0x40000000);

        using var ms = new MemoryStream();
        Png.Write(ms, bmp);
        Bitmap32 back = Png.Read(ms.ToArray());

        Assert.Equal(bmp.Width, back.Width);
        Assert.Equal(bmp.Height, back.Height);
        Assert.Equal(bmp.Pixels, back.Pixels);
    }

    [Fact]
    public void ScaledComposeAlignsWithUpscaledCompose()
    {
        Composite c1 = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        Composite c2 = new IsoCompositor(_data) { Scale = 2 }.Compose(Map, X, Y, W, H);
        Assert.Equal(c1.Items.Count, c2.Items.Count);

        int k = c1.Items.FindIndex(i => !i.Stretched);
        int dx = c2.Items[k].Left - 2 * c1.Items[k].Left, dy = c2.Items[k].Top - 2 * c1.Items[k].Top;

        int compared = 0;
        for (int i = 0; i < c1.Items.Count; i++)
        {
            if (c1.Items[i].Stretched)
                continue;
            Assert.Equal(2 * c1.Items[i].Left + dx, c2.Items[i].Left);
            Assert.Equal(2 * c1.Items[i].Top + dy, c2.Items[i].Top);
        }

        for (int y = 0; y < c2.Image.Height; y++)
        {
            for (int x = 0; x < c2.Image.Width; x++)
            {
                int o2 = c2.Owner[y * c2.Image.Width + x];
                int x1 = (x - dx) / 2, y1 = (y - dy) / 2;
                if (o2 < 0 || c2.Items[o2].Stretched || x - dx < 0 || y - dy < 0 || x1 >= c1.Image.Width || y1 >= c1.Image.Height)
                    continue;
                if (c1.Owner[y1 * c1.Image.Width + x1] != o2)
                    continue; // stretched neighbors rasterize slightly differently at 2x
                Assert.Equal(c1.Image[x1, y1], c2.Image[x, y]);
                compared++;
            }
        }
        Assert.True(compared > 100_000, $"only {compared} pixels compared");
    }

    [Fact]
    public void IdentityRestyleSlicesBackToOriginalArt()
    {
        Composite c = new IsoCompositor(_data).Compose(Map, X, Y, W, H);
        var slicer = new Slicer(_data, 2);
        slicer.Add(c, Resample.Nearest(c.Image, 2));
        List<SlicedSprite> sprites = slicer.Build();

        Assert.NotEmpty(sprites);
        Assert.Contains(sprites, s => s.Fallback > 0); // the region has occluded art, so the fallback path runs

        foreach (SlicedSprite s in sprites)
        {
            Bitmap32 original = Resample.Nearest(s.Kind == DrawKind.Land ? _data.GetLand(s.Graphic)! : _data.GetStatic(s.Graphic)!, 2);
            Assert.Equal(original.Width, s.Image.Width);
            Assert.Equal(original.Height, s.Image.Height);
            for (int i = 0; i < original.Pixels.Length; i++)
            {
                uint o = original.Pixels[i], p = s.Image.Pixels[i];
                Assert.Equal(o >> 24 == 0, p >> 24 == 0); // silhouette re-applied exactly
                if (o >> 24 != 0)
                    Assert.True(MaxChannelDiff(o, p) <= 2, $"{s.Kind} 0x{s.Graphic:X4} pixel {i}: {o:X8} vs {p:X8}");
            }
        }

        // Reassembling from the sliced sprites reproduces the 2x render.
        var byId = sprites.ToDictionary(s => (s.Kind, s.Graphic), s => s.Image);
        Composite expected = new IsoCompositor(_data) { Scale = 2 }.Compose(Map, X, Y, W, H);
        Composite rebuilt = new IsoCompositor(_data)
        {
            Scale = 2,
            SpriteOverride = (kind, g) => byId.TryGetValue((kind, g), out Bitmap32? b) ? b : null
        }.Compose(Map, X, Y, W, H);

        Assert.Equal(expected.Image.Width, rebuilt.Image.Width);
        Assert.Equal(expected.Image.Height, rebuilt.Image.Height);
        int worst = 0;
        for (int i = 0; i < expected.Image.Pixels.Length; i++)
            worst = Math.Max(worst, MaxChannelDiff(expected.Image.Pixels[i], rebuilt.Image.Pixels[i]));
        Assert.True(worst <= 2, $"reassembled differs by up to {worst} per channel");
    }

    [Fact]
    public void FallbackPixelsFollowTheRestyleColors()
    {
        Composite c = new IsoCompositor(_data).Compose(Map, X, Y, W, H);

        // A "restyle" that halves red everywhere it is visible.
        Bitmap32 restyled = Resample.Nearest(c.Image, 2);
        for (int i = 0; i < restyled.Pixels.Length; i++)
        {
            uint p = restyled.Pixels[i];
            uint r = ((p >> 16) & 0xFF) / 2;
            restyled.Pixels[i] = (p & 0xFF00FFFF) | (r << 16);
        }

        var slicer = new Slicer(_data, 2);
        slicer.Add(c, restyled);

        // Pixels hidden in every placement must still come out red-halved, not with the original colors.
        double origRed = 0, slicedRed = 0, origGreen = 0, slicedGreen = 0;
        int fallbackPixels = 0;
        foreach (SlicedSprite s in slicer.Build().Where(s => s.Fallback > 0 && s.Coverage > 0.05f))
        {
            Bitmap32 original = Resample.Nearest(s.Kind == DrawKind.Land ? _data.GetLand(s.Graphic)! : _data.GetStatic(s.Graphic)!, 2);
            for (int i = 0; i < original.Pixels.Length; i++)
            {
                if (s.Source[i] != PixelSource.Fallback)
                    continue;
                origRed += (original.Pixels[i] >> 16) & 0xFF;
                slicedRed += (s.Image.Pixels[i] >> 16) & 0xFF;
                origGreen += (original.Pixels[i] >> 8) & 0xFF;
                slicedGreen += (s.Image.Pixels[i] >> 8) & 0xFF;
                fallbackPixels++;
            }
        }

        Assert.True(fallbackPixels > 1000, $"only {fallbackPixels} fallback pixels");
        Assert.InRange(slicedRed / origRed, 0.4, 0.6);
        Assert.InRange(slicedGreen / origGreen, 0.95, 1.05);
    }

    private static int MaxChannelDiff(uint a, uint b)
    {
        int d = 0;
        for (int shift = 0; shift <= 16; shift += 8)
            d = Math.Max(d, Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)));
        return d;
    }
}
