using System.Collections.Generic;
using System.Linq;
using UOClient.Unity;
using UOClient.Unity.UI;
using Xunit;

namespace UOClient.Tests;

/// <summary>The cooldown bars (Cooldowns.Rules.cs).</summary>
public class OptionsBatch9Tests
{
    [Fact]
    public void List_RoundTrips_ClampsAndDropsBadLines()
    {
        List<CooldownDef> defs = Cooldowns.Parse("Heal|You begin applying|10|63\nbad line\nX|y|notanumber|1\nLong|t|99999|5000");
        Assert.Equal(2, defs.Count);
        Assert.Equal(("Heal", "You begin applying", 10, 63), (defs[0].Name, defs[0].Trigger, defs[0].Seconds, defs[0].Hue));
        Assert.Equal((Cooldowns.MaxSeconds, GameOptions.MaxHue), (defs[1].Seconds, defs[1].Hue));
        Assert.Equal("Heal|You begin applying|10|63\nLong|t|3600|2999", Cooldowns.Format(defs));
        Assert.Empty(Cooldowns.Parse(null));
        Assert.Equal(Cooldowns.MaxDefs, Cooldowns.Parse(string.Join("\n", Enumerable.Repeat("a|b|1|1", 80))).Count);
    }

    [Fact]
    public void Options_KeepTheList_AndDefaultToNone()
    {
        Assert.Equal("", GameOptions.Defaults().cooldowns);
        var o = GameOptions.Defaults();
        o.cooldowns = "Heal|You begin|10|63\nHide|hidden|45|946";
        GameOptions back = GameOptions.Parse(o.ToJson());
        Assert.Equal(o.cooldowns, back.cooldowns);
        Assert.True(o.SameAs(back));
        Assert.Equal("", GameOptions.Parse("{\"cooldowns\": true}").cooldowns);
    }

    [Fact]
    public void ALine_StartsWhatItContains_CaseIgnored()
    {
        var heal = new CooldownDef { Name = "Heal", Trigger = "begin applying", Seconds = 10 };
        Assert.True(Cooldowns.Starts(heal, "You BEGIN applying the bandages."));
        Assert.False(Cooldowns.Starts(heal, "You finish applying the bandages."));
        Assert.False(Cooldowns.Starts(heal, null));
        Assert.False(Cooldowns.Starts(new CooldownDef { Name = "x", Trigger = "  " }, "anything"));
    }

    [Fact]
    public void Timers_Run_Restart_AndEnd()
    {
        var c = new Cooldowns { Defs = Cooldowns.Parse("Heal|begin applying|10|63\nHide|hidden|4|5") };
        Assert.Equal(0, c.OnText("hello", 100));
        Assert.Empty(c.Active(100));
        Assert.Equal(1, c.OnText("You begin applying the bandages.", 100));
        Assert.Equal(1, c.OnText("You have hidden yourself well.", 101));
        var active = c.Active(102);
        Assert.Equal(new[] { "Hide", "Heal" }, active.Select(a => a.Def.Name)); // the one ending first on top
        Assert.Equal(3, active[0].Left, 6);
        Assert.Equal(0.8f, active[1].Fraction, 4);
        c.OnText("you begin applying again", 104);                             // starts over from full
        Assert.Equal(10, c.Active(104).Single(a => a.Def.Name == "Heal").Left, 6);
        Assert.Equal(new[] { "Heal" }, c.Active(105).Select(a => a.Def.Name)); // Hide ended at 105
        Assert.Empty(c.Active(114));
        c.OnText("hidden", 200);
        c.Clear();
        Assert.Empty(c.Active(200));
    }

    [Fact]
    public void TimeText_AndStackSize()
    {
        Assert.Equal("9.9", Cooldowns.TimeText(9.95));
        Assert.Equal("0.4", Cooldowns.TimeText(0.49));
        Assert.Equal("0.0", Cooldowns.TimeText(-1));
        Assert.Equal("10", Cooldowns.TimeText(10));
        Assert.Equal("45", Cooldowns.TimeText(44.2));
        Assert.Equal("1:00", Cooldowns.TimeText(59.5));
        Assert.Equal("2:05", Cooldowns.TimeText(125));
        Assert.Equal((Cooldowns.BarWidth, 0), Cooldowns.StackSize(0));
        Assert.Equal((Cooldowns.BarWidth, 3 * Cooldowns.BarHeight + 2 * Cooldowns.BarGap), Cooldowns.StackSize(3));
    }

    [Fact]
    public void Page_IsOffered()
    {
        Assert.Contains(OptionsWindow.Pages, p => p.Page == OptionsWindow.Page.Cooldowns && p.Title == "Cooldowns");
        Assert.DoesNotContain("Cooldowns", OptionsWindow.UnavailablePages);
    }
}
