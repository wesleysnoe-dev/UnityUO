using System;
using UOClient.Core;

namespace UOClient.Tests;

/// <summary>The shard's clock (ModernUO Clock.GetTime) and the sun it puts in the sky (SunClock).</summary>
public class SunClockTests
{
    [Fact]
    public void Hour_FollowsTheServersClock()
    {
        // 3 UO hours after the world began = 180 UO minutes = 900 real seconds.
        DateTime t = SunClock.WorldStart.AddSeconds(900);
        Assert.Equal(3.0, SunClock.Hour(t, 0, 0), 6);
        Assert.Equal(3.0 + 320 / 60.0, SunClock.Hour(t, 1, 0), 6);   // 320 minutes per map index
        Assert.Equal(3.0 + 10 / 60.0, SunClock.Hour(t, 0, 160), 6);  // a minute per 16 tiles of x
        Assert.Equal(0.0, SunClock.Hour(SunClock.WorldStart.AddHours(2), 0, 0), 6); // a UO day is 2 real hours
    }

    [Fact]
    public void AtTwoPm_TheSunIsTheReferenceLight()
    {
        (float x, float y, float up) = SunClock.Vector(SunClock.At(14));
        Assert.Equal(SunClock.RefX, x, 3);
        Assert.Equal(SunClock.RefY, y, 3);
        Assert.Equal(SunClock.RefUp, up, 3);
        Assert.Equal(1f, SunClock.At(14).Strength, 3);
        Assert.False(SunClock.At(14).Moon);
    }

    [Fact]
    public void Shadows_NeverFallUpTheScreen_AndAreLongestLow()
    {
        for (float h = SunClock.Sunrise; h <= SunClock.Sunset; h += 0.25f)
        {
            SunState s = SunClock.At(h);
            float screenDown = -s.TowardX - s.TowardY; // the shadow's (x + y): down the screen when positive
            Assert.True(screenDown > 0f, $"{h}: shadow falls up the screen");
        }
        Assert.True(SunClock.At(6).Elevation < SunClock.At(10).Elevation);
        Assert.True(SunClock.At(21).Elevation < SunClock.At(17).Elevation);
        Assert.True(SunClock.At(6).Warmth > 0.5f && SunClock.At(14).Warmth == 0f);
    }

    [Fact]
    public void SunAndMoon_HandOverWithNoShadowAtTheHorizon()
    {
        Assert.Equal(0f, SunClock.At(SunClock.Sunrise).Strength, 3);
        Assert.Equal(0f, SunClock.At(SunClock.Sunset).Strength, 3);
        Assert.True(SunClock.At(SunClock.Sunrise - 0.01).Strength < 0.02f);
        Assert.True(SunClock.At(SunClock.Sunset + 0.01).Strength < 0.02f);
        Assert.True(SunClock.At(2).Moon);
        Assert.Equal(SunClock.MoonStrength, SunClock.At(2).Strength, 3);
    }
}
