using System.Collections.Generic;
using System.Linq;
using UOClient.Core;
using UOClient.Unity;
using UOClient.Unity.UI;
using Xunit;

namespace UOClient.Tests;

/// <summary>The tenth batch: saved bar places, cooldown trigger kinds, dark nights, chat hues, health line colours.</summary>
public class OptionsBatch10Tests
{
    [Fact]
    public void Defaults_ChangeNothingYouSee()
    {
        GameOptions d = GameOptions.Defaults();
        Assert.True(d.counterBarHighlightOnUse);
        Assert.True(d.enableLoginMusic);
        Assert.False(d.useDarkNights);
        Assert.Equal((0, 0, 0), (d.tooltipTextHue, d.guildMessageHue, d.allyMessageHue));
        Assert.Equal(14, d.namePlateFontSize);
    }

    [Fact]
    public void BarPlaces_RoundTrip_AndSkipRubbish()
    {
        Dictionary<string, (int X, int Y)> at = HudBars.ParseBarPositions("counterbar=540,56\nrubbish\ninfobar= 12 , -4 \nx=1\nfar=999999,1");
        Assert.Equal(2, at.Count);
        Assert.Equal((540, 56), at["counterbar"]);
        Assert.Equal((12, -4), at["infobar"]);
        Assert.Equal("counterbar=540,56\ninfobar=12,-4", HudBars.FormatBarPositions(at));
        Assert.Empty(HudBars.ParseBarPositions(null));
    }

    [Fact]
    public void Triggers_HaveThreeKinds()
    {
        Assert.Equal((Cooldowns.TriggerKind.Text, "You begin"), Cooldowns.Trigger(" You begin "));
        Assert.Equal((Cooldowns.TriggerKind.OwnText, "In Mani"), Cooldowns.Trigger("me: In Mani"));
        Assert.Equal((Cooldowns.TriggerKind.Buff, "Bless"), Cooldowns.Trigger("BUFF:Bless"));

        var own = new CooldownDef { Name = "Heal", Trigger = "me:In Mani", Seconds = 5 };
        Assert.True(Cooldowns.Starts(own, "In Mani", true));
        Assert.False(Cooldowns.Starts(own, "In Mani", false));     // someone else cast it
        Assert.False(Cooldowns.StartsOnBuff(own, "In Mani"));

        var buff = new CooldownDef { Name = "Bless", Trigger = "buff:bless", Seconds = 60 };
        Assert.True(Cooldowns.StartsOnBuff(buff, "Bless"));
        Assert.False(Cooldowns.Starts(buff, "bless", true));        // a journal line never starts a buff trigger
        Assert.False(Cooldowns.StartsOnBuff(new CooldownDef { Trigger = "buff:" }, "Bless"));
    }

    [Fact]
    public void Timers_StartFromBuffs_AndOnlyOwnLines()
    {
        var c = new Cooldowns { Defs = Cooldowns.Parse("Bless|buff:bless|60|5\nHeal|me:In Mani|5|63") };
        Assert.Equal(0, c.OnText("In Mani", 10, false));
        Assert.Equal(1, c.OnText("In Mani", 10, true));
        Assert.Equal(0, c.OnBuff("Curse", 10));
        Assert.Equal(1, c.OnBuff("Bless", 10));
        Assert.Equal(new[] { "Heal", "Bless" }, c.Active(11).Select(a => a.Def.Name));
    }

    [Fact]
    public void DarkNights_DarkenNightOnly_AndStopAtTheDarkest()
    {
        Assert.Equal(0, GameOptions.DarkNight(0, true));                         // day stays day
        Assert.Equal(18, GameOptions.DarkNight(12, true));                       // the shard's night
        Assert.Equal(GameOptions.MaxLightLevel, GameOptions.DarkNight(26, true)); // a dungeon: capped
        Assert.Equal(12, GameOptions.DarkNight(12, false));
    }

    [Fact]
    public void GuildAndAllianceHues_RecolourOnlyTheirLines_AndZeroKeepsTheServers()
    {
        var s = new SpeechSettings();
        Assert.Equal(0x59, s.Recolour(13, 0x59));
        s.Guild = 0x44;
        s.Alliance = 0x57;
        Assert.Equal(0x44, s.Recolour(13, 0x59));
        Assert.Equal(0x57, s.Recolour(14, 0x59));
        Assert.Equal(0x59, s.Recolour(0, 0x59));
    }

    [Fact]
    public void HealthLine_ShowsPoisonThenYellow()
    {
        Assert.Null(GameOptions.HealthLineStatusColor(false, false));
        var poison = GameOptions.HealthLineStatusColor(true, true);   // poison wins
        var yellow = GameOptions.HealthLineStatusColor(false, true);
        Assert.True(poison.Value.G > poison.Value.R);
        Assert.True(yellow.Value.R > 0.9f && yellow.Value.B < 0.3f);
    }

    [Fact]
    public void TheGround_StartsFromTheSwitches_ElseTheOption()
    {
        Assert.False(GameOptions.Defaults().naturalGround);
        Assert.True(GameOptions.Defaults().groundGrass);
        Assert.Equal((false, (string)null), GameOptions.GroundLaunch(false, false, null));   // the classic land
        Assert.Equal((true, "terrain"), GameOptions.GroundLaunch(true, false, null));         // the option alone
        Assert.Equal((true, "mesh"), GameOptions.GroundLaunch(true, true, "mesh"));           // the switches win
        Assert.Equal((true, (string)null), GameOptions.GroundLaunch(false, true, null));
        var o = GameOptions.Defaults();
        o.naturalGround = true;
        o.groundGrass = false;
        GameOptions back = GameOptions.Parse(o.ToJson());
        Assert.True(back.naturalGround);
        Assert.False(back.groundGrass);
    }

    [Fact]
    public void Pages_DropTheNotesNowDone()
    {
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.Contains("Saved bar position"));
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.StartsWith("Guild / alliance hues"));
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.Contains("Dark nights"));
        Assert.DoesNotContain(OptionsWindow.NotOffered, n => n.Text.Contains("HP colours"));
    }
}
