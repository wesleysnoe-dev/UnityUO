using System.Reflection;
using UOClient.Unity;

namespace UOClient.Tests;

/// <summary>
/// The Display and Video options (GameOptions.Graphics.cs): every default is what the game did before they existed (the
/// display ones "keep"), every field survives the file, and the rules behind them follow TazUO.
/// </summary>
public sealed class OptionsGraphicsTests
{
    [Fact]
    public void Defaults_ChangeNothing_TheDisplayIsKeptAndEveryFeatureStaysOn()
    {
        var d = GameOptions.Defaults();
        Assert.Equal(GameOptions.DisplayKeep, d.displayMode);
        Assert.Equal(0, d.resolutionWidth);
        Assert.Equal(0, d.resolutionHeight);
        Assert.Equal(GameOptions.VSyncKeep, d.vSync);
        Assert.Equal(GameOptions.FrameRateKeep, d.frameRateCap);
        Assert.False(d.reduceFpsWhenInactive); // as TazUO
        Assert.True(d.reproduceSoundsInBackground); // as this client always did (TazUO: off)
        Assert.False(d.useCustomLightLevel);
        Assert.Equal(0, d.lightLevel);
        Assert.Equal(GameOptions.LightLevelAbsolute, d.lightLevelType);
        foreach (bool on in new[] { d.itemLights, d.blackWhiteWhenDead, d.deathScreen, d.shadows, d.sunShadows, d.weather,
                     d.cloudShadows, d.foliageSway, d.seasons, d.ambientLife, d.footprints, d.animatedWater, d.fireEffects, d.bloom, d.colorGrading })
            Assert.True(on);
    }

    [Fact]
    public void AnOptionsFileFromBefore_ParsesToKeep_SoTheWindowIsNeverMoved()
    {
        GameOptions o = GameOptions.Parse("{ \"showNames\": false, \"zoom\": 1.5 }");
        Assert.Equal(GameOptions.DisplayKeep, o.displayMode);
        Assert.Equal(GameOptions.VSyncKeep, o.vSync);
        Assert.Equal(GameOptions.FrameRateKeep, o.frameRateCap);
        Assert.Null(GameOptions.TargetFrameRate(o.frameRateCap));
    }

    /// <summary>
    /// Every public field of GameOptions, set to something not its default, comes back from the file as it was, and each
    /// alone makes SameAs see a difference: a field missing from Equals, the parse switch or the writer fails here.
    /// </summary>
    [Fact]
    public void RoundTrip_EveryField_SurvivesTheFile_AndCountsForSameAs()
    {
        FieldInfo[] fields = typeof(GameOptions).GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Contains(fields, f => f.Name == "colorGrading");
        var changed = GameOptions.Defaults();
        foreach (FieldInfo f in fields)
        {
            var single = GameOptions.Defaults();
            object value = NotDefault(f, single);
            f.SetValue(single, value);
            f.SetValue(changed, value);
            Assert.False(single.SameAs(GameOptions.Defaults()), f.Name + " is not compared by SameAs");
            GameOptions back = GameOptions.Parse(single.ToJson(), out var unknown);
            Assert.Empty(unknown);
            Assert.True(Equals(value, f.GetValue(back)), $"{f.Name}: wrote {value}, read {f.GetValue(back)}");
            Assert.True(back.SameAs(single), f.Name + " did not survive the file");
        }
        GameOptions all = GameOptions.Parse(changed.ToJson());
        Assert.True(all.SameAs(changed));
    }

    /// <summary>A value of the field's type, in its range, that is not the default.</summary>
    private static object NotDefault(FieldInfo f, GameOptions d)
    {
        object v = f.GetValue(d);
        switch (v)
        {
            case bool b:
                return !b;
            case float x:
                return f.Name == "uiScale" ? 1.5f : x == 0.5f ? 0.25f : 0.5f;
            case int i:
                int candidate = f.Name switch
                {
                    "displayMode" => GameOptions.DisplayBorderless,
                    "resolutionWidth" or "resolutionHeight" => 0, // set as a pair below
                    "vSync" => GameOptions.VSyncOff,
                    "frameRateCap" => 144,
                    "lightLevel" => 12,
                    "speechHue" or "whisperHue" or "emoteHue" or "yellHue" or "partyMessageHue" => 0x22,
                    "containersScale" or "gridContainersScale" => 150,
                    "speechDelay" => 300,
                    "autoOpenCorpseRange" => 4,
                    "healthLineSizeMultiplier" => 3,
                    "nameOverheadFlags" => NamePlates.Humanoid,
                    "namePlateFixedWidth" or "namePlateHealthBarFixedWidth" => 200,
                    "namePlateHeight" => 30,
                    _ => i == 1 ? 0 : 1,
                };
                if (f.Name is "resolutionWidth" or "resolutionHeight")
                {
                    d.resolutionWidth = 1280; // a size needs both halves (0 x 0 keeps the window)
                    d.resolutionHeight = 720;
                    return f.Name == "resolutionWidth" ? 1280 : 720;
                }
                return candidate;
            default:
                throw new InvalidOperationException(f.Name + ": no test value for " + v?.GetType());
        }
    }

    [Fact]
    public void Clamp_KeepsTheDisplayWhenAValueIsNonsense()
    {
        var o = new GameOptions { displayMode = 9, resolutionWidth = 1920, resolutionHeight = 0, vSync = 5, frameRateCap = -7, lightLevel = 99, lightLevelType = 4 }.Clamp();
        Assert.Equal(GameOptions.DisplayExclusive, o.displayMode);
        Assert.Equal(0, o.resolutionWidth);
        Assert.Equal(0, o.resolutionHeight);
        Assert.Equal(GameOptions.VSyncOn, o.vSync);
        Assert.Equal(GameOptions.FrameRateKeep, o.frameRateCap);
        Assert.Equal(GameOptions.MaxLightLevel, o.lightLevel);
        Assert.Equal(GameOptions.LightLevelMinimum, o.lightLevelType);
        Assert.Equal(GameOptions.DisplayKeep, new GameOptions { displayMode = -5 }.Clamp().displayMode);
        Assert.Equal(0, new GameOptions { resolutionWidth = 99999, resolutionHeight = 1080 }.Clamp().resolutionWidth);
    }

    [Fact]
    public void CustomLightLevel_IsTazUOs_AbsoluteOrOnlyEverBrighter()
    {
        Assert.Equal((20, 5), GameOptions.LightLevels(20, 5, false, 3, GameOptions.LightLevelAbsolute));
        Assert.Equal((3, 0), GameOptions.LightLevels(20, 5, true, 3, GameOptions.LightLevelAbsolute));
        Assert.Equal((25, 0), GameOptions.LightLevels(2, 5, true, 25, GameOptions.LightLevelAbsolute));
        Assert.Equal((2, 0), GameOptions.LightLevels(2, 5, true, 25, GameOptions.LightLevelMinimum)); // never darker than the server's
        Assert.Equal((10, 0), GameOptions.LightLevels(20, 5, true, 10, GameOptions.LightLevelMinimum));
    }

    [Fact]
    public void FrameRate_Focus_AndBackgroundAudio()
    {
        Assert.Null(GameOptions.TargetFrameRate(GameOptions.FrameRateKeep));
        Assert.Equal(-1, GameOptions.TargetFrameRate(GameOptions.FrameRateUnlimited));
        Assert.Equal(60, GameOptions.TargetFrameRate(60));
        Assert.Null(GameOptions.InactiveFrameRate(true, focused: true));
        Assert.Null(GameOptions.InactiveFrameRate(false, focused: false));
        Assert.Equal(GameOptions.BackgroundFrameRate, GameOptions.InactiveFrameRate(true, focused: false));
        Assert.Equal(1f, GameOptions.BackgroundAudioVolume(false, focused: true));
        Assert.Equal(0f, GameOptions.BackgroundAudioVolume(false, focused: false));
        Assert.Equal(1f, GameOptions.BackgroundAudioVolume(true, focused: false));
    }

    [Fact]
    public void Resolutions_OnceEach_SmallestFirst()
    {
        var list = GameOptions.DistinctResolutions(new[] { (1920, 1080), (1280, 720), (1920, 1080), (1280, 1024), (0, 0) });
        Assert.Equal(new[] { (1280, 720), (1280, 1024), (1920, 1080) }, list);
        Assert.Equal("1920 x 1080", GameOptions.ResolutionName(1920, 1080));
        Assert.Equal("Unlimited", GameOptions.FrameRateName(GameOptions.FrameRateUnlimited));
    }
}
