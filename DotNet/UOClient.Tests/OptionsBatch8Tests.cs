using System.Collections.Generic;
using System.Linq;
using UOClient.Unity;
using UOClient.Unity.UI;
using Xunit;

namespace UOClient.Tests;

/// <summary>The counter bar and the info bar (HudBars.Rules.cs, GameOptions.More.cs).</summary>
public class OptionsBatch8Tests
{
    private static readonly InfoStats Stats = new InfoStats
    {
        Name = "Aldric", Hits = 30, HitsMax = 100, Mana = 50, ManaMax = 100, Stamina = 90, StaminaMax = 100,
        Weight = 400, MaxWeight = 400, Followers = 2, FollowersMax = 5, Gold = 1234567, DamageMin = 11, DamageMax = 15, Armor = 40
    };

    [Fact]
    public void Defaults_ShowNeitherBar()
    {
        GameOptions d = GameOptions.Defaults();
        Assert.False(d.counterBarEnabled);
        Assert.False(d.showInfoBar);
        Assert.Equal((40, 5, 1), (d.counterBarCellSize, d.counterBarColumns, d.counterBarRows));
        Assert.Equal(new[] { InfoVar.Hits, InfoVar.Mana, InfoVar.Stamina, InfoVar.Weight }, HudBars.ParseInfoBar(d.infoBarItems).Select(i => i.Var));
    }

    [Fact]
    public void InfoBar_RoundTrips_AndDropsBadLines()
    {
        List<InfoBarItem> items = HudBars.ParseInfoBar("HP|Hits|33\nnonsense\nX|NoSuchVar|1\n|gold|9999\nLong label that goes on|Luck|5");
        Assert.Equal(3, items.Count);
        Assert.Equal(("HP", InfoVar.Hits, 33), (items[0].Label, items[0].Var, items[0].Hue));
        Assert.Equal(("", InfoVar.Gold, GameOptions.MaxHue), (items[1].Label, items[1].Var, items[1].Hue));
        Assert.Equal("Long label that", items[2].Label);
        Assert.Equal(items.Select(i => (i.Label, i.Var, i.Hue)), HudBars.ParseInfoBar(HudBars.FormatInfoBar(items)).Select(i => (i.Label, i.Var, i.Hue)));
        Assert.Equal(HudBars.MaxInfoBarItems, HudBars.ParseInfoBar(string.Join("\n", Enumerable.Repeat("a|Hits|1", 40))).Count);
    }

    [Fact]
    public void InfoBar_SurvivesTheOptionsFile()
    {
        var o = GameOptions.Defaults();
        o.showInfoBar = true;
        o.infoBarItems = "Gold|Gold|53\nHP|Hits|33";
        GameOptions back = GameOptions.Parse(o.ToJson());
        Assert.True(back.showInfoBar);
        Assert.Equal("Gold|Gold|53\nHP|Hits|33", back.infoBarItems);
        Assert.Equal(HudBars.DefaultInfoBar, GameOptions.Parse("{\"infoBarItems\": 3}").infoBarItems);
    }

    [Fact]
    public void InfoText_AndWarnings()
    {
        Assert.Equal("30/100", HudBars.InfoText(InfoVar.Hits, Stats));
        Assert.Equal("400/400", HudBars.InfoText(InfoVar.Weight, Stats));
        Assert.Equal("2/5", HudBars.InfoText(InfoVar.Followers, Stats));
        Assert.Equal("1,234,567", HudBars.InfoText(InfoVar.Gold, Stats));
        Assert.Equal("11-15", HudBars.InfoText(InfoVar.Damage, Stats));
        Assert.Equal("Aldric", HudBars.InfoText(InfoVar.Name, Stats));
        Assert.Equal(1, HudBars.InfoLevel(InfoVar.Hits, Stats));      // 30%: below half
        Assert.Equal(1, HudBars.InfoLevel(InfoVar.Mana, Stats));      // exactly half
        Assert.Equal(0, HudBars.InfoLevel(InfoVar.Stamina, Stats));
        Assert.Equal(2, HudBars.InfoLevel(InfoVar.Weight, Stats));    // full pack
        Assert.Equal(0, HudBars.InfoLevel(InfoVar.Gold, Stats));      // no maximum: never a warning
        Assert.Null(HudBars.InfoFraction(InfoVar.Gold, Stats));
        Assert.Equal(0.3f, HudBars.InfoFraction(InfoVar.Hits, Stats));
        Assert.Equal(2, HudBars.InfoLevel(InfoVar.Hits, new InfoStats())); // nothing known yet: empty
    }

    [Fact]
    public void Counters_RoundTrip_KeepGaps_DropTrailingEmpties()
    {
        List<CounterCell> cells = HudBars.ParseCounters("3821:0\n-\n3617:5\nrubbish\n-\n");
        Assert.Equal(3, cells.Count);
        Assert.Equal((3821, 0), (cells[0].Graphic, cells[0].Hue));
        Assert.True(cells[1].Empty);
        Assert.Equal((3617, 5), (cells[2].Graphic, cells[2].Hue));
        Assert.Equal("3821:0\n-\n3617:5", HudBars.FormatCounters(cells));
        Assert.Empty(HudBars.ParseCounters(null));
    }

    [Fact]
    public void CounterText_ShortensFromTheThreshold_AndFlagsLowCounts()
    {
        Assert.Equal("999", HudBars.CounterText(999, true, 1000));
        Assert.Equal("1K", HudBars.CounterText(1000, true, 1000));
        Assert.Equal("12K", HudBars.CounterText(12999, true, 1000));
        Assert.Equal("4999", HudBars.CounterText(4999, true, 5000));
        Assert.Equal("12999", HudBars.CounterText(12999, false, 1000));
        Assert.True(HudBars.CounterIsLow(4, true, 5));
        Assert.False(HudBars.CounterIsLow(5, true, 5));
        Assert.False(HudBars.CounterIsLow(0, false, 5));
    }

    [Fact]
    public void CounterBar_SizeAndHitTest()
    {
        Assert.Equal((8 + 5 * 40 + 4, 8 + 40), HudBars.CounterBarSize(40, 5, 1));
        Assert.Equal(0, HudBars.CounterCellAt(4, 4, 40, 5, 2));
        Assert.Equal(0, HudBars.CounterCellAt(43, 43, 40, 5, 2));
        Assert.Equal(-1, HudBars.CounterCellAt(44, 10, 40, 5, 2));   // the pixel between two cells
        Assert.Equal(1, HudBars.CounterCellAt(45, 10, 40, 5, 2));
        Assert.Equal(5, HudBars.CounterCellAt(10, 50, 40, 5, 2));    // second row
        Assert.Equal(-1, HudBars.CounterCellAt(2, 10, 40, 5, 2));    // the frame
        Assert.Equal(-1, HudBars.CounterCellAt(10, 90, 40, 5, 2));   // below the last row
        Assert.Equal(-1, HudBars.CounterCellAt(300, 10, 40, 5, 2));  // right of the last column
    }

    [Fact]
    public void Pages_OfferBothBars()
    {
        Assert.Contains(OptionsWindow.Pages, p => p.Page == OptionsWindow.Page.Counters && p.Title == "Counters");
        Assert.Contains(OptionsWindow.Pages, p => p.Page == OptionsWindow.Page.InfoBar && p.Title == "Info Bar");
        Assert.DoesNotContain("Counters", OptionsWindow.UnavailablePages);
        Assert.DoesNotContain("Info Bar", OptionsWindow.UnavailablePages);
    }
}
