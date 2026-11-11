using UOClient.Core.Assistant;

namespace UOClient.Tests;

/// <summary>The assistant's item lists (auto loot, scavenger, organizer...): which items a rule means and how many it still wants.</summary>
public class AssistantItemRuleTests
{
    [Theory]
    [InlineData(0x0EED, -1, "", 0x0EED, 0, null, true)]        // gold, any hue
    [InlineData(0x0EED, -1, "", 0x0EEE, 0, null, false)]       // another graphic
    [InlineData(0x0F7A, 33, "", 0x0F7A, 33, null, true)]       // the hue asked for
    [InlineData(0x0F7A, 33, "", 0x0F7A, 0, null, false)]       // another hue
    [InlineData(-1, -1, "slayer", 0x13B9, 0, "a viking sword\nOrc Slayer", true)] // by its tooltip, any case
    [InlineData(-1, -1, "slayer", 0x13B9, 0, "a viking sword", false)]
    [InlineData(-1, -1, "slayer", 0x13B9, 0, null, false)]     // the tooltip not here yet
    [InlineData(0x13B9, -1, "slayer", 0x13B9, 0, "Orc Slayer", true)]
    [InlineData(0x13B9, -1, "slayer", 0x1400, 0, "Orc Slayer", false)]
    [InlineData(-1, -1, "", 0x13B9, 0, "anything", false)]     // an empty rule means nothing, not everything
    public void Matches_ByGraphicHueAndWords(int graphic, int hue, string words, int itemGraphic, int itemHue, string itemText, bool matches) =>
        Assert.Equal(matches, new ItemRule { graphic = graphic, hue = hue, text = words }.Matches(itemGraphic, itemHue, itemText));

    [Fact]
    public void ADisabledRule_MatchesNothing_AndTheFirstEnabledOneWins()
    {
        var off = new ItemRule { name = "off", graphic = 0x0EED, enabled = false };
        var gold = new ItemRule { name = "gold", graphic = 0x0EED };
        var any = new ItemRule { name = "blue", hue = 5 };
        Assert.False(off.Matches(0x0EED, 0, null));
        Assert.Same(gold, ItemRule.First(new[] { off, gold, any }, 0x0EED, 5, null));
        Assert.Same(any, ItemRule.First(new[] { off, gold, any }, 0x1234, 5, null));
        Assert.Null(ItemRule.First(new[] { off, gold, any }, 0x1234, 0, null));
        Assert.Null(ItemRule.First(null, 0x0EED, 0, null));
    }

    [Theory]
    [InlineData(0, 500, 60, 60)]   // no limit: all of it
    [InlineData(100, 30, 60, 60)]  // room for 70: the whole stack
    [InlineData(100, 70, 60, 30)]  // only 30 still missing
    [InlineData(100, 100, 60, 0)]  // already there
    [InlineData(100, 140, 60, 0)]
    public void Wanted_StopsAtTheLimit(int max, int held, int offered, int wanted) =>
        Assert.Equal(wanted, ItemRule.Wanted(max, held, offered));

    [Fact]
    public void Distance_IsTheLongerAxis()
    {
        Assert.Equal(2, ItemRule.Distance(10, 10, 12, 11));
        Assert.Equal(0, ItemRule.Distance(5, 5, 5, 5));
    }
}
