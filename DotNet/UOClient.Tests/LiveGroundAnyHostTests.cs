using System.Collections.Generic;
using UOClient.LiveGroundCompare;
using UOClient.Unity;
using Xunit;

namespace UOClient.Tests;

/// <summary>-liveground-anyhost: the normal client's explicit promotion of the land backends past the loopback-only guard.</summary>
public class LiveGroundAnyHostTests
{
    private static readonly string[] NormalShortcut =
        { "Unity UO.exe", "-server", "uosmallz.duckdns.org:2593", "-liveground", "-liveground-anyhost", "-groundbackend", "terrain" };

    [Fact]
    public void Launch_ParsesAnyHostOnlyWhenGiven()
    {
        Assert.False(LaunchOptions.Parse(new[] { "x.exe", "-liveground" }).LiveGroundAnyHost);
        Assert.False(LaunchOptions.Parse(new[] { "x.exe", "-liveground", "-groundbackend", "terrain" }).LiveGroundAnyHost);

        LaunchOptions o = LaunchOptions.Parse(NormalShortcut);
        Assert.True(o.LiveGround);
        Assert.True(o.LiveGroundAnyHost);
        Assert.Equal("terrain", o.GroundBackend);
        Assert.True(o.ServerFromArguments);
    }

    [Fact]
    public void Launch_AnyHostDoesNotImplyLiveGround()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "x.exe", "-liveground-anyhost", "-groundbackend", "terrain" });
        Assert.False(o.LiveGround); // exact match: "-liveground-anyhost" is not "-liveground"
        Assert.True(o.LiveGroundAnyHost);
        GroundBackend.TryParse(o.GroundBackend, out GroundBackendKind k);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(o.LiveGround, o.LiveGroundAnyHost, "uosmallz.duckdns.org", k, out string why));
        Assert.Contains("-liveground", why);
    }

    [Fact]
    public void Guard_AnyHostLiftsOnlyTheHostCheck()
    {
        Assert.Equal(GroundBackendKind.Terrain, LiveGroundHostGuard.Resolve(true, true, "uosmallz.duckdns.org", GroundBackendKind.Terrain, out string live));
        Assert.Null(live);
        Assert.Equal(GroundBackendKind.Terrain, LiveGroundHostGuard.Resolve(true, true, "127.0.0.1", GroundBackendKind.Terrain, out _));
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(false, true, "uosmallz.duckdns.org", GroundBackendKind.Terrain, out string noFlag));
        Assert.Contains("-liveground", noFlag);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(true, true, "uosmallz.duckdns.org", GroundBackendKind.Original, out string orig));
        Assert.Null(orig);
    }

    [Fact]
    public void Guard_WithoutAnyHostStaysFailClosed()
    {
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(true, false, "uosmallz.duckdns.org", GroundBackendKind.Terrain, out string a));
        Assert.Contains("non-local", a);
        Assert.Equal(GroundBackendKind.Original, LiveGroundHostGuard.Resolve(true, "uosmallz.duckdns.org", GroundBackendKind.Terrain, out string b));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Switch_AnyHostAppliesTerrainOnLiveHostAndTogglesBackToOriginal()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, true, GroundBackendKind.Terrain, applied.Add);
        Assert.Equal("Ground: Terrain", sw.Attach("uosmallz.duckdns.org"));
        Assert.Equal(GroundBackendKind.Terrain, sw.Active);
        Assert.False(LiveGroundStatus.IsFallback(sw.Requested, sw.Active, sw.Reason));

        sw.Toggle(); // Terrain -> Original: the fallback the player can always reach
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Mesh, sw.Active);
        sw.Toggle();
        Assert.Equal(GroundBackendKind.Terrain, sw.Active);
        Assert.Equal(new[] { GroundBackendKind.Terrain, GroundBackendKind.Original, GroundBackendKind.Mesh, GroundBackendKind.Terrain }, applied);

        sw.Dispose();
        Assert.Equal(GroundBackendKind.Original, applied[^1]);
    }

    [Fact]
    public void Switch_AnyHostTerrainFailureStillFallsBackVisibly()
    {
        var sw = new LiveGroundSwitch(true, true, GroundBackendKind.Terrain, k => k == GroundBackendKind.Terrain ? "no terrain shader" : null);
        string status = sw.Attach("uosmallz.duckdns.org");
        Assert.Equal(GroundBackendKind.Original, sw.Active);
        Assert.StartsWith("Ground: Original (Terrain failed: no terrain shader", status);
    }

    [Fact]
    public void Switch_OldConstructorsKeepLiveHostRefused()
    {
        var applied = new List<GroundBackendKind>();
        var sw = new LiveGroundSwitch(true, GroundBackendKind.Terrain, applied.Add);
        Assert.Contains("non-local", sw.Attach("uosmallz.duckdns.org"));
        Assert.Equal(new[] { GroundBackendKind.Original }, applied);
    }
}
