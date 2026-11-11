using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Tests;

/// <summary>The assistant's friends list and filters (journal, sound, music, graphics, season): the rules that decide, without the game.</summary>
public class AssistantFilterTests
{
    // ---- friends ----

    [Fact]
    public void AFriend_IsKnownBySerial_AndListedOnce()
    {
        var friends = new List<FriendEntry>();
        var day = new DateTime(2026, 11, 11);
        Assert.True(FriendList.Add(friends, 0x1234, "  Aldric ", day));
        Assert.False(FriendList.Add(friends, 0x1234, "Aldric the Bold", day)); // the same character under another name
        Assert.False(FriendList.Add(friends, 0, "Nobody", day));
        Assert.Single(friends);
        Assert.Equal("Aldric", friends[0].name);
        Assert.Equal("2026-11-11", friends[0].added);
        Assert.True(FriendList.IsFriend(friends, 0x1234));
        Assert.False(FriendList.IsFriend(friends, 0x1235));
        Assert.False(FriendList.IsFriend(null, 0x1234));
    }

    [Fact]
    public void AFriendWithoutAName_IsStillAdded_AndTakesItsNameWhenSeen()
    {
        var friends = new List<FriendEntry>();
        Assert.True(FriendList.Add(friends, 7, null, DateTime.Now));
        Assert.Equal(FriendList.Unnamed, friends[0].name);
        Assert.True(FriendList.Rename(friends[0], " Brienne "));
        Assert.Equal("Brienne", friends[0].name);
        Assert.False(FriendList.Rename(friends[0], "Brienne")); // nothing new
        Assert.False(FriendList.Rename(friends[0], " "));       // a blank name never replaces a real one
    }

    [Fact]
    public void RemovingAFriend_TakesOnlyThatOne()
    {
        var friends = new List<FriendEntry>();
        FriendList.Add(friends, 1, "A", DateTime.Now);
        FriendList.Add(friends, 2, "B", DateTime.Now);
        Assert.True(FriendList.Remove(friends, 1));
        Assert.False(FriendList.Remove(friends, 1));
        Assert.False(FriendList.IsFriend(friends, 1));
        Assert.True(FriendList.IsFriend(friends, 2));
    }

    // ---- journal ----

    [Theory]
    [InlineData("You must wait to perform another action.", true)]  // contains a listed text
    [InlineData("you MUST WAIT a moment", true)]                    // any case
    [InlineData("The spell fizzles.", true)]
    [InlineData("You see: a spell book", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AJournalLine_IsHidden_WhenItContainsAListedText(string line, bool hidden) =>
        Assert.Equal(hidden, TextFilter.Hides(new[] { "must wait", "FIZZLES" }, line));

    [Fact]
    public void ABlankJournalFilter_HidesNothing()
    {
        Assert.False(TextFilter.Hides(new[] { "", "  ", null }, "anything at all"));
        Assert.False(TextFilter.Hides(null, "anything at all"));
        Assert.False(TextFilter.Hides(new string[0], "anything at all"));
    }

    [Fact]
    public void JournalFilters_AreAddedTrimmed_WithoutBlanksOrRepeats()
    {
        var filters = new List<string>();
        Assert.True(TextFilter.Add(filters, "  You must wait "));
        Assert.False(TextFilter.Add(filters, "you must WAIT"));
        Assert.False(TextFilter.Add(filters, "   "));
        Assert.False(TextFilter.Add(filters, null));
        Assert.Equal(new[] { "You must wait" }, filters);
        Assert.True(TextFilter.Add(filters, new string('x', 500)));
        Assert.Equal(TextFilter.MaxLength, filters[1].Length);
    }

    // ---- sound and music ----

    [Theory]
    [InlineData("84", 0xFFFF, 84)]
    [InlineData(" 0x54 ", 0xFFFF, 0x54)]
    [InlineData("0X1ff", 0xFFFF, 0x1FF)]
    [InlineData("65536", 0xFFFF, -1)]  // past the last id
    [InlineData("-3", 0xFFFF, -1)]
    [InlineData("0x", 0xFFFF, -1)]
    [InlineData("horse", 0xFFFF, -1)]
    [InlineData("", 0xFFFF, -1)]
    [InlineData(null, 0xFFFF, -1)]
    public void AnId_IsReadAsANumberOrHex(string typed, int max, int id) => Assert.Equal(id, IdFilter.Parse(typed, max));

    [Fact]
    public void FilteredIds_StaySortedWithoutRepeats_AndAreFound()
    {
        var ids = new List<int>();
        Assert.True(IdFilter.Add(ids, 300));
        Assert.True(IdFilter.Add(ids, 12));
        Assert.True(IdFilter.Add(ids, 84));
        Assert.False(IdFilter.Add(ids, 84));
        Assert.False(IdFilter.Add(ids, -1));
        Assert.Equal(new[] { 12, 84, 300 }, ids);
        Assert.True(IdFilter.Contains(ids, 84));
        Assert.False(IdFilter.Contains(ids, 85));
        Assert.False(IdFilter.Contains(null, 84));
    }

    [Fact]
    public void ASavedIdList_IsTidiedBeforeItIsSearched()
    {
        var ids = new List<int> { 9, -4, 3, 9, 1, 3 };
        IdFilter.Tidy(ids);
        Assert.Equal(new[] { 1, 3, 9 }, ids);
        Assert.True(IdFilter.Contains(ids, 9));
    }

    [Fact]
    public void IdsHeardLately_AreNewestFirst_OnceEach_AndCapped()
    {
        var recent = new List<int>();
        foreach (int id in new[] { 1, 2, 3, 2, 2, 4 })
            IdFilter.Heard(recent, id, 3);
        Assert.Equal(new[] { 4, 2, 3 }, recent);
    }

    // ---- graphics ----

    [Fact]
    public void AGraphic_IsReplaced_ByKind_AndKeepsItsHueUnlessOneIsGiven()
    {
        var table = new GraphicSwapTable(new[]
        {
            new GraphicSwap { kind = GraphicSwap.Mobile, graphic = 400, to = 0x3CA },            // men drawn as ghosts
            new GraphicSwap { kind = GraphicSwap.Item, graphic = 0x0EED, to = 0x0F0E, hue = 33 } // gold as red bottles
        });
        int g = 400, h = 0x83EA;
        Assert.True(table.Apply(GraphicSwap.Mobile, ref g, ref h));
        Assert.Equal((0x3CA, 0x83EA), (g, h)); // no hue given: its own

        g = 0x0EED;
        h = 0;
        Assert.True(table.Apply(GraphicSwap.Item, ref g, ref h));
        Assert.Equal((0x0F0E, 33), (g, h));

        // The same number as the other kind is another thing.
        g = 400;
        h = 5;
        Assert.False(table.Apply(GraphicSwap.Item, ref g, ref h));
        Assert.Equal((400, 5), (g, h));
        g = 0x0EED;
        Assert.False(table.Apply(GraphicSwap.Mobile, ref g, ref h));
    }

    [Fact]
    public void OnlyUsableLinesReplace_AndTheFirstForAGraphicWins()
    {
        var table = new GraphicSwapTable(new[]
        {
            new GraphicSwap { kind = GraphicSwap.Item, graphic = 1, to = 10, enabled = false },
            new GraphicSwap { kind = GraphicSwap.Item, graphic = 1, to = 11 },
            new GraphicSwap { kind = GraphicSwap.Item, graphic = 1, to = 12 },
            new GraphicSwap { kind = GraphicSwap.Item, graphic = 2, to = -1 },  // nothing to draw instead
            new GraphicSwap { kind = GraphicSwap.Item, graphic = -1, to = 5 },  // nothing to look for
            new GraphicSwap { kind = 7, graphic = 3, to = 5 },                  // no such kind
            null
        });
        Assert.Equal(1, table.Count);
        int g = 1, h = 0;
        Assert.True(table.Apply(GraphicSwap.Item, ref g, ref h));
        Assert.Equal(11, g);
        g = 2;
        Assert.False(table.Apply(GraphicSwap.Item, ref g, ref h));
        g = -1;
        Assert.False(table.Apply(GraphicSwap.Item, ref g, ref h));
    }

    [Fact]
    public void AReplacement_IsNotItselfReplaced_SoTwoLinesCanSwapGraphics()
    {
        var table = new GraphicSwapTable(new[]
        {
            new GraphicSwap { kind = GraphicSwap.Mobile, graphic = 1, to = 2 },
            new GraphicSwap { kind = GraphicSwap.Mobile, graphic = 2, to = 1 }
        });
        int g = 1, h = 0;
        table.Apply(GraphicSwap.Mobile, ref g, ref h);
        Assert.Equal(2, g);
        g = 2;
        table.Apply(GraphicSwap.Mobile, ref g, ref h);
        Assert.Equal(1, g);
    }

    [Fact]
    public void AnEmptyTable_ReplacesNothing()
    {
        int g = 400, h = 1;
        Assert.False(new GraphicSwapTable(null).Apply(GraphicSwap.Mobile, ref g, ref h));
        Assert.Equal((400, 1), (g, h));
    }

    [Fact]
    public void AnItemDrawnAsAnother_IsACopy_AlikeButForItsLooks()
    {
        var item = new WorldItem { Serial = 0x40000001, Graphic = 0x0EED, Hue = 0, Amount = 500, X = 10, Y = 20, Z = 5, Flags = 0x20 };
        WorldItem drawn = item.DrawnAs(0x0F0E, 33);
        Assert.NotSame(item, drawn);
        Assert.Equal((0x0F0E, 33), ((int)drawn.Graphic, (int)drawn.Hue));
        Assert.Equal((item.Serial, item.Amount, item.X, item.Y, item.Z, item.Flags), (drawn.Serial, drawn.Amount, drawn.X, drawn.Y, drawn.Z, drawn.Flags));
        Assert.Equal((0x0EED, 0), ((int)item.Graphic, (int)item.Hue)); // the item itself is as the server sent it
    }

    // ---- season ----

    [Fact]
    public void ASeason_IsShownAsChosen_OrAsSent()
    {
        var showAs = new List<int> { -1, -1, -1, (int)Season.Fall, (int)Season.Summer };
        Assert.Equal(Season.Fall, SeasonMap.Apply(showAs, Season.Winter));
        Assert.Equal(Season.Summer, SeasonMap.Apply(showAs, Season.Desolation));
        Assert.Equal(Season.Spring, SeasonMap.Apply(showAs, Season.Spring));
        Assert.Equal(Season.Winter, SeasonMap.Apply(null, Season.Winter));
        Assert.Equal(Season.Winter, SeasonMap.Apply(new List<int> { 2 }, Season.Winter)); // a short list: as sent
        Assert.Equal(Season.Winter, SeasonMap.Apply(new List<int> { 0, 0, 0, 9, 0 }, Season.Winter)); // not a season: as sent
        // A shown season is not looked up again: winter as fall stays fall even if fall is shown as spring.
        Assert.Equal(Season.Fall, SeasonMap.Apply(new List<int> { -1, -1, 0, 2, -1 }, Season.Winter));
    }

    [Fact]
    public void ASavedSeasonList_IsMadeOneEntryPerSeason()
    {
        var showAs = new List<int> { 3, 99 };
        SeasonMap.Tidy(showAs);
        Assert.Equal(new[] { 3, -1, -1, -1, -1 }, showAs);
        var tooLong = new List<int> { -1, -1, -1, -1, -1, 2, 2 };
        SeasonMap.Tidy(tooLong);
        Assert.Equal(SeasonMap.Seasons, tooLong.Count);
    }

    [Fact]
    public void TheSeasonButton_StepsThroughEverySeason_AndBackToAsSent()
    {
        int shown = SeasonMap.AsSent;
        var seen = new List<int>();
        for (int i = 0; i < 6; i++)
            seen.Add(shown = SeasonMap.Next(shown));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, SeasonMap.AsSent }, seen);
    }

    [Fact]
    public void TheSeasonFilter_IsInUse_OnlyWhenASeasonShowsAsAnother()
    {
        Assert.False(SeasonMap.Any(new List<int> { -1, -1, -1, -1, -1 }));
        Assert.False(SeasonMap.Any(new List<int> { 0, 1, -1, 3, -1 })); // each as itself
        Assert.True(SeasonMap.Any(new List<int> { -1, -1, -1, 2, -1 }));
        Assert.False(SeasonMap.Any(null));
    }
}
