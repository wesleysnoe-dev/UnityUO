using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class TexmapOrientationTests
{
    private readonly UOData _data;

    public TexmapOrientationTests(UOInstallFixture fixture) => _data = fixture.Data;

    /// <summary>
    /// Land art and texmaps are made from the same source, so projecting a texmap onto the flat
    /// diamond with the compositor's mapping (top=(0,0), right=(1,0), left=(0,1), bottom=(1,1))
    /// should match the art much more closely than the transposed mapping does.
    /// </summary>
    [Fact]
    public void CompositorUvMappingMatchesLandArt()
    {
        double chosen = 0, transposed = 0;
        int tiles = 0;

        for (int g = 3; g < 0x4000 && tiles < 300; g++)
        {
            int texId = _data.TileData.Land[g].TexId;
            if (texId == 0)
                continue;
            Bitmap32? art = _data.GetLand(g);
            Bitmap32? tex = _data.GetTexmap(texId);
            if (art == null || tex == null)
                continue;

            chosen += Difference(art, tex, transpose: false);
            transposed += Difference(art, tex, transpose: true);
            tiles++;
        }

        Assert.True(tiles > 50, $"only {tiles} land tiles had texmaps");
        Assert.True(chosen < transposed * 0.9, $"chosen mapping error {chosen / tiles:F1} vs transposed {transposed / tiles:F1}");
    }

    private static double Difference(Bitmap32 art, Bitmap32 tex, bool transpose)
    {
        double total = 0;
        int count = 0;
        for (int py = 0; py < 44; py++)
        {
            for (int px = 0; px < 44; px++)
            {
                uint a = art[px, py];
                if ((a >> 24) == 0)
                    continue;

                // Screen offset from the top corner = u*(22,22) + v*(-22,22).
                float dx = px + 0.5f - 22, dy = py + 0.5f;
                float u = (dy + dx) / 44f, v = (dy - dx) / 44f;
                if (transpose)
                    (u, v) = (v, u);

                int tx = Math.Clamp((int)(u * tex.Width), 0, tex.Width - 1);
                int ty = Math.Clamp((int)(v * tex.Height), 0, tex.Height - 1);
                total += Math.Abs(Luma(a) - Luma(tex[tx, ty]));
                count++;
            }
        }
        return count == 0 ? 0 : total / count;
    }

    private static double Luma(uint p) => 0.3 * ((p >> 16) & 0xFF) + 0.59 * ((p >> 8) & 0xFF) + 0.11 * (p & 0xFF);
}
