using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// TazUO's AnimatedStaticsManager clock (AnimatedStaticsManager.cs:51-120): a frame lasts Interval x 100 ms + 1, or 100 ms
/// at interval 0; frames step through animdata offsets and wrap; the drawn art is base + offset.
/// </summary>
public sealed class ArtAnimationTests
{
    private static AnimDataEntry Entry(int interval, params sbyte[] offsets) => new AnimDataEntry(offsets, interval, 0);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 101)]
    [InlineData(2, 201)]
    [InlineData(10, 1001)]
    public void FrameLength(int interval, int ms) => Assert.Equal(ms, ArtAnimation.PeriodMs(Entry(interval, 0, 1)));

    [Fact]
    public void StepsWrapAndStartAtZero()
    {
        AnimDataEntry e = Entry(2, 0, 1, 2, 3); // 201 ms a frame
        Assert.Equal(0, ArtAnimation.Frame(e, 0));
        Assert.Equal(0, ArtAnimation.Frame(e, 200));
        Assert.Equal(1, ArtAnimation.Frame(e, 201));
        Assert.Equal(3, ArtAnimation.Frame(e, 603));
        Assert.Equal(0, ArtAnimation.Frame(e, 804)); // wrapped
        Assert.Equal(0x0DE3 + 2, ArtAnimation.Graphic(0x0DE3, e, 450));
    }

    [Fact]
    public void OffsetsAreSignedAndOneFrameIsAFixedOffset()
    {
        Assert.Equal(0x1000 - 3, ArtAnimation.Graphic(0x1000, Entry(0, -3), 12345));
        Assert.Equal(0x1000, ArtAnimation.Graphic(0x1000, default, 999)); // no entry: itself
    }

    [Fact]
    public void OnlyTheAnimationFlagAnimates()
    {
        AnimDataEntry e = Entry(1, 0, 1);
        Assert.True(ArtAnimation.Animates(TileFlags.Animation | TileFlags.LightSource, e));
        Assert.False(ArtAnimation.Animates(TileFlags.LightSource, e));
        Assert.False(ArtAnimation.Animates(TileFlags.Animation, default));
    }
}

/// <summary>The real 7.0.23.1 animdata/tiledata/art for the audited ids.</summary>
[Collection("UO install")]
public sealed class ArtAnimationInstallTests
{
    private readonly UOData _data;

    public ArtAnimationInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Theory]
    [InlineData(0x0DE3, 4)]  // campfire
    [InlineData(0x19AB, 12)] // fire
    [InlineData(0x0E31, 3)]  // brazier
    [InlineData(0x0F6C, 8)]  // blue moongate
    [InlineData(0x398C, 10)] // fire field
    [InlineData(0x3946, 31)] // energy field
    [InlineData(0x3915, 12)] // poison field
    [InlineData(0x3967, 18)] // paralysis field
    [InlineData(0x0A15, 8)]  // lantern
    public void AuditedArtAnimatesThroughRealFrames(int graphic, int frames)
    {
        StaticTileData td = _data.TileData.GetStatic(graphic);
        AnimDataEntry e = _data.AnimData.Get(graphic);
        Assert.True(ArtAnimation.Animates(td.Flags, e));
        Assert.Equal(frames, e.Offsets.Length);
        // Over one loop every frame's art exists, and at least two different arts are drawn.
        var seen = new HashSet<int>();
        int period = ArtAnimation.PeriodMs(e);
        for (int i = 0; i < frames; i++)
        {
            int g = ArtAnimation.Graphic(graphic, e, i * period + period / 2);
            Assert.NotNull(_data.GetStatic(g));
            seen.Add(g);
        }
        Assert.True(seen.Count >= 2, $"0x{graphic:X4} draws {seen.Count} distinct arts");
    }

    [Fact]
    public void TheCompositorHandsOutEveryFrameAtScale()
    {
        var c = new IsoCompositor(_data) { Scale = 2 };
        AnimDataEntry e = _data.AnimData.Get(0x0DE3);
        foreach (sbyte o in e.Offsets)
        {
            Bitmap32 b = c.StaticSprite(0x0DE3 + o);
            Assert.NotNull(b);
            Assert.Equal(0, b.Width % 2);
            Assert.Same(b, c.StaticSprite(0x0DE3 + o)); // cached: claimed once into the shared atlas
        }
    }

    [Fact]
    public void ACandleIsLitButNotAnimated()
    {
        // 0x0A0F candle: LightSource; it is not in the animated set unless tiledata says Animation.
        StaticTileData td = _data.TileData.GetStatic(0x0A0F);
        Assert.Equal((td.Flags & TileFlags.Animation) != 0, ArtAnimation.Animates(td.Flags, _data.AnimData.Get(0x0A0F)));
    }
}
