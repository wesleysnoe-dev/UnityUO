using UOClient.IO;
using UOClient.IO.Files;
using UOClient.IO.Formats;

namespace UOClient.Tests;

[Collection("UO install")]
public sealed class AssetTests
{
    private readonly UOData _data;

    public AssetTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void UopHashesResolveMostArtSlots()
    {
        int found = 0;
        for (int i = 0; i < _data.ArtFile.Count; i++)
            if (_data.ArtFile.TryGetEntry(i, out _))
                found++;

        // A wrong hash maps nothing; a healthy install fills tens of thousands of slots.
        Assert.True(found > 30000, $"only {found} art entries resolved");
    }

    [Fact]
    public void LandTileIsOpaqueDiamond()
    {
        Bitmap32 grass = _data.GetLand(0x03)!;
        Assert.Equal(44, grass.Width);
        Assert.Equal(44, grass.Height);
        Assert.Equal(0u, grass[0, 0] >> 24);          // corners outside the diamond
        Assert.Equal(0u, grass[43, 43] >> 24);
        Assert.Equal(0xFFu, grass[22, 22] >> 24);     // center filled
        Assert.Equal(0xFFu, grass[21, 0] >> 24);      // top tip
        Assert.Equal(0xFFu, grass[0, 22] >> 24);      // left tip
    }

    [Fact]
    public void StaticHasSizeAndPixels()
    {
        Bitmap32 anvil = _data.GetStatic(0x0FAF)!;
        Assert.InRange(anvil.Width, 1, 200);
        Assert.InRange(anvil.Height, 1, 200);
        Assert.Contains(anvil.Pixels, p => (p >> 24) != 0);
    }

    [Fact]
    public void CompressedGumpDecodes()
    {
        Assert.True(_data.GumpFile.TryGetEntry(0x07D0, out FileEntry entry));
        Bitmap32 paperdoll = _data.GetGump(0x07D0)!;

        Assert.Equal(262, paperdoll.Width);
        Assert.Equal(324, paperdoll.Height);
        int opaque = paperdoll.Pixels.Count(p => (p >> 24) != 0);
        Assert.True(opaque > paperdoll.Pixels.Length / 2, $"compression {entry.Compression}: only {opaque} opaque pixels");
    }

    [Fact]
    public void TileDataAndHuesLoad()
    {
        TileData td = _data.TileData;
        Assert.True(td.IsNewFormat);
        Assert.Equal(TileData.LandCount, td.Land.Length);
        Assert.True(td.Statics.Length >= 0x10000);

        // 0x0FAF is an anvil: a surface-less, impassable static with a name.
        Assert.Contains("anvil", td.GetStatic(0x0FAF).Name, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(3000, _data.Hues.Entries.Length);
    }

    [Fact]
    public void FeluccaBritainHasLandAndStatics()
    {
        MapFile map = _data.GetMap(0);
        Assert.Equal(7168, map.Width);
        Assert.True(map.LandDataLength >= (long)map.BlockWidth * map.BlockHeight * 196);

        // Britain bank: real land, and the surrounding 32x32 tiles are built up with statics.
        LandCell[] land = map.ReadLandBlock(1434 >> 3, 1699 >> 3);
        Assert.Contains(land, c => c.Graphic != 0 && c.Graphic != 0x244);

        int statics = 0;
        for (int by = 1680 >> 3; by < 1712 >> 3; by++)
            for (int bx = 1416 >> 3; bx < 1448 >> 3; bx++)
                statics += map.ReadStaticBlock(bx, by).Length;
        Assert.True(statics > 200, $"only {statics} statics around Britain bank"); // 446 on the 2026 Classic client
    }
}
