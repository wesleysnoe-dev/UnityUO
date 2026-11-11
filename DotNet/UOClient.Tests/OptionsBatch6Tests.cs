using System.Collections.Generic;
using System.Linq;
using UOClient.Core;
using UOClient.Unity;
using UOClient.Unity.UI;
using Xunit;

namespace UOClient.Tests;

/// <summary>The sixth batch of options (GameOptions.More.cs): sound and music switches, tooltip look, ignore list.</summary>
public class OptionsBatch6Tests
{
    [Fact]
    public void Defaults_KeepTheOldBehaviour()
    {
        GameOptions d = GameOptions.Defaults();
        Assert.True(d.enableSound);
        Assert.True(d.enableMusic);
        Assert.True(d.enableFootstepsSound);
        Assert.True(d.enableCombatMusic);
        Assert.Equal(88, d.tooltipBackgroundOpacity);
        Assert.Equal(14, d.tooltipFontSize);
        Assert.Equal("", d.ignoreList);
    }

    [Fact]
    public void File_RoundTrips_AndClamps()
    {
        var o = GameOptions.Defaults();
        o.enableSound = false;
        o.enableMusic = false;
        o.enableFootstepsSound = false;
        o.enableCombatMusic = false;
        o.tooltipBackgroundOpacity = 40;
        o.tooltipFontSize = 18;
        o.ignoreList = "Bob \"the\" Bad\nA\\B";
        GameOptions back = GameOptions.Parse(o.ToJson(), out List<KeyValuePair<string, string>> unknown);
        Assert.Empty(unknown);
        Assert.True(o.SameAs(back));
        Assert.False(back.SameAs(GameOptions.Defaults()));

        GameOptions wild = GameOptions.Parse("{\"tooltipBackgroundOpacity\": 400, \"tooltipFontSize\": 2, \"ignoreList\": 7}");
        Assert.Equal(100, wild.tooltipBackgroundOpacity);
        Assert.Equal(GameOptions.MinTooltipFontSize, wild.tooltipFontSize);
        Assert.Equal("", wild.ignoreList); // not an array: left alone
    }

    [Fact]
    public void IgnoreList_TrimsDedupesAndRemoves()
    {
        string list = GameOptions.AddIgnored("", "  Bob ");
        list = GameOptions.AddIgnored(list, "bob");
        list = GameOptions.AddIgnored(list, "");
        list = GameOptions.AddIgnored(list, "Alice");
        Assert.Equal(new[] { "Bob", "Alice" }, GameOptions.IgnoredNames(list));
        Assert.Equal(new[] { "Alice" }, GameOptions.IgnoredNames(GameOptions.RemoveIgnored(list, "BOB")));
        string many = string.Join("\n", Enumerable.Range(0, 300).Select(i => "n" + i));
        Assert.Equal(GameOptions.MaxIgnoredNames, GameOptions.IgnoredNames(many).Count);
    }

    [Fact]
    public void Strings_ReadOnlyFromAnArray()
    {
        Assert.True(GameOptions.TryReadStrings("[\"a\", \"b\\\"c\"]", out List<string> items));
        Assert.Equal(new[] { "a", "b\"c" }, items);
        Assert.True(GameOptions.TryReadStrings("[]", out items));
        Assert.Empty(items);
        Assert.False(GameOptions.TryReadStrings("\"a\"", out _));
        Assert.False(GameOptions.TryReadStrings("[\"a]", out _));
        Assert.False(GameOptions.TryReadStrings("[1]", out _));
    }

    [Fact]
    public void SpeechSettings_IgnoreByName_CaseIgnored()
    {
        var s = new SpeechSettings();
        Assert.False(s.Ignores("Bob"));
        s.IgnoredNames.Add("Bob");
        Assert.True(s.Ignores("bob"));
        Assert.True(s.Ignores(" Bob "));
        Assert.False(s.Ignores("Bobby"));
        Assert.False(s.Ignores(null));
    }

    [Fact]
    public void Switches_SilenceWithoutLosingTheVolume()
    {
        Assert.Equal(0.6f, GameOptions.EffectiveVolume(true, 0.6f));
        Assert.Equal(0f, GameOptions.EffectiveVolume(false, 0.6f));
    }

    [Fact]
    public void CombatTracks_ComeFromTheFileNames()
    {
        Assert.Equal(new[] { 65, 75 }, GameOptions.CombatTracks(new[]
            { (75, "GenericCombat.mp3"), (38, "goodevil"), (65, "UltimaVIISerpentIsleCombat.mp3") }));
        Assert.Equal(new[] { 38, 39, 40 }, GameOptions.CombatTracks(new[] { (1, "britain1") }));
    }

    [Fact]
    public void Pages_OfferTheIgnoreList_AndNoStaleNotes()
    {
        Assert.Contains(OptionsWindow.Pages, p => p.Page == OptionsWindow.Page.IgnoreList && p.Title == "Ignore List");
        Assert.DoesNotContain("Ignore List", OptionsWindow.UnavailablePages);
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.StartsWith("Footsteps"));
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.Contains("volume only"));
    }
}
