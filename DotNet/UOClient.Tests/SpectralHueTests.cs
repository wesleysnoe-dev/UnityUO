using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// Hue flag 0x4000 as TazUO draws it: GetHueVector strips 0x8000, then 0x4000 picks SHADER_SPECTRAL whatever the index
/// (ShaderHueTranslator.cs:46-60), which draws black at alpha 1 - 1.5 x red (IsometricWorld.fx:238-244). ModernUO's
/// ethereal mounts use 0x4001 (Ethereals.cs:71).
/// </summary>
public sealed class SpectralHueTests
{
    [Theory]
    [InlineData(0x4001, true)]
    [InlineData(0x4000, true)]
    [InlineData(0xC001, true)]  // partial bit plus spectral
    [InlineData(0x0001, false)]
    [InlineData(0x8021, false)] // partial only
    [InlineData(0x3FFF, false)]
    [InlineData(0, false)]
    public void TheFlag(int hue, bool spectral) => Assert.Equal(spectral, MobileLook.IsSpectral(hue));

    [Theory]
    [InlineData(0xFF000000u, 255)] // black: an opaque shadow
    [InlineData(0xFF550000u, 128)] // red 0.333: alpha 0.5
    [InlineData(0xFF808080u, 63)]  // grey 0.502: alpha 0.247
    [InlineData(0xFFAAAAAAu, 1)]   // red 0.667 and over: TazUO's alpha <= 0, kept at 1 for the silhouette
    [InlineData(0xFFFFFFFFu, 1)]
    [InlineData(0x00FFFFFFu, 0)]   // a clear texel stays clear
    public void SpectralTexel(uint argb, int alpha)
    {
        uint p = MobileLook.SpectralPixel(argb);
        Assert.Equal(alpha, (int)(p >> 24));
        Assert.Equal(0u, p & 0xFFFFFF); // always black
    }

    [Fact]
    public void ItBlendsOverWhatIsUnder()
    {
        Assert.Equal(0x80000000u, MobileLook.SpectralOver(0x80000000, 0)); // over nothing: itself
        uint o = MobileLook.SpectralOver(0x80000000, 0xFFFF8040);         // half black over an opaque colour
        Assert.Equal(255u, o >> 24);
        Assert.InRange((o >> 16) & 0xFF, 126u, 128u);
        Assert.Equal(0xFFFF8040u, MobileLook.SpectralOver(0, 0xFFFF8040)); // a clear spectral texel changes nothing
    }
}

/// <summary>Spectral looks against the installed 7.0.23.1 data.</summary>
[Collection("UO install")]
public sealed class SpectralHueInstallTests
{
    private readonly UOData _data;

    public SpectralHueInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    private static readonly (byte, int, int) Robe = (22, 0x1F03, 0x0021);

    [Fact]
    public void AnEtherealMountIsSpectralAndTheRiderIsNot()
    {
        // ModernUO EtherealHorse: mounted item 0x3EAA on layer 25, hue 0x4001.
        MobileLook look = MobileLook.From(_data, 400, 0x83EA, new[] { (MobileLook.MountLayer, 0x3EAA, 0x4001), Robe });
        Assert.True(look.Mounted);
        MobileLook.Layer mount = look.Layers.Single(l => l.IsMount);
        Assert.True(mount.Spectral);
        Assert.Equal(1, mount.Hue); // the index is kept as sent (0x4001 & 0x3FFF)
        Assert.All(look.Layers.Where(l => !l.IsMount), l => Assert.False(l.Spectral));
        Assert.True(look.HasSpectral);

        MobileLook plain = MobileLook.From(_data, 400, 0x83EA, new[] { (MobileLook.MountLayer, 0x3EAA, 0x0001), Robe });
        Assert.NotEqual(plain.Key, look.Key); // frames are not shared with the solid look
        Assert.False(plain.HasSpectral);
    }

    [Fact]
    public void SpectralBodiesAndItemsKeepPartialAndIndex()
    {
        // A wraith (body 26, hue 0x4001) and a robe sent 0xC021: spectral, partial and index 0x21 all kept.
        MobileLook wraith = MobileLook.From(_data, 26, 0x4001, Array.Empty<(byte, int, int)>());
        Assert.True(wraith.Layers[0].Spectral);
        MobileLook robed = MobileLook.From(_data, 400, 0x83EA, new[] { ((byte)22, 0x1F03, 0xC021) });
        MobileLook.Layer robe = robed.Layers.Single(l => l.Slot == 22);
        Assert.True(robe.Spectral);
        Assert.True(robe.Partial);
        Assert.Equal(0x21, robe.Hue);
        Assert.False(robed.Layers.Single(l => l.Slot == 0).Spectral);
    }

    [Fact]
    public void ADeadBondedEtherealIsGreyNotSpectral()
    {
        MobileLook dead = MobileLook.From(_data, 0xCC, 0x4001, Array.Empty<(byte, int, int)>(), bondedDead: true);
        Assert.Equal(MobileLook.DeadHue, dead.Layers[0].Hue);
        Assert.False(dead.Layers[0].Spectral);
    }

    [Fact]
    public void TheRealHorseFrameKeepsItsSilhouette()
    {
        // The ethereal horse's mount body, stand frame, file direction 2: every covered texel stays covered (alpha >= 1),
        // dark texels stay dark and solid, and no texel keeps any colour.
        int body = AnimationLibrary.MountBody(0x3EAA);
        if (body == 0)
            body = _data.TileData.GetStatic(0x3EAA).AnimId;
        Assert.NotEqual(0, body);
        AnimationFrame[] frames = _data.AnimationLibrary.Read(body, _data.AnimationLibrary.Actions(body).Stand, 2);
        Assert.NotEmpty(frames);
        Bitmap32 img = frames[0].Image;
        int covered = 0, solid = 0, faint = 0;
        foreach (uint p in img.Pixels)
        {
            uint s = MobileLook.SpectralPixel(p);
            Assert.Equal(p >> 24 != 0, s >> 24 != 0);
            Assert.Equal(0u, s & 0xFFFFFF);
            if (p >> 24 == 0)
                continue;
            covered++;
            if (s >> 24 >= 128) solid++;
            if (s >> 24 < 32) faint++;
        }
        Assert.True(covered > 500);
        Assert.True(solid > 0 && faint > 0); // both dark shadow and see-through parts, not one flat alpha
    }
}
