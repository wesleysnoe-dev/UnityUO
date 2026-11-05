using UOClient.ArtPipeline;
using UOClient.IO;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class CompositorTests
{
    private readonly UOData _data;

    public CompositorTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void OwnershipMatchesDrawnPixels()
    {
        Composite c = new IsoCompositor(_data).Compose(0, 1424, 1688, 16, 16);

        Assert.NotEmpty(c.Items);
        Assert.Equal(c.Image.Pixels.Length, c.Owner.Length);

        for (int i = 0; i < c.Owner.Length; i++)
        {
            bool drawn = (c.Image.Pixels[i] >> 24) != 0;
            int owner = c.Owner[i];
            Assert.Equal(drawn, owner >= 0);
            if (owner < 0)
                continue;

            // The owning sprite's rectangle must contain the pixel.
            DrawItem it = c.Items[owner];
            int x = i % c.Image.Width, y = i / c.Image.Width;
            Assert.InRange(x, it.Left, it.Left + it.Width - 1);
            Assert.InRange(y, it.Top, it.Top + it.Height - 1);
        }
    }

    [Fact]
    public void ItemsAreInDrawOrder()
    {
        Composite c = new IsoCompositor(_data).Compose(0, 1424, 1688, 16, 16);
        for (int i = 1; i < c.Items.Count; i++)
            Assert.True(c.Items[i - 1].X + c.Items[i - 1].Y <= c.Items[i].X + c.Items[i].Y);
    }
}
