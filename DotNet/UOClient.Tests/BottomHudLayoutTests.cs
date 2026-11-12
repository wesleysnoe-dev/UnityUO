using System.Collections.Generic;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>The bottom HUD's measures: its width follows the screen's, the slots fill the panel, the orbs' levels glide.</summary>
public class BottomHudLayoutTests
{
    [Theory]
    [InlineData(1920f, 960f)]   // half the screen
    [InlineData(3440f, 1040f)]  // an ultrawide: no wider than the most
    [InlineData(1280f, 700f)]   // a small screen: no narrower than the least
    [InlineData(640f, 640f)]    // a tiny one: never wider than the screen
    public void Width_FollowsTheScreen(float screen, float width) => Assert.Equal(width, BottomHudLayout.Width(screen));

    [Fact]
    public void Slots_FillThePanel_WithinTheLimits_AndStayInsideIt()
    {
        foreach (float width in new[] { 640f, 700f, 820f, 960f, 1040f })
        {
            int n = BottomHudLayout.SlotCount(width);
            Assert.InRange(n, BottomHudLayout.MinSlots, BottomHudLayout.MaxSlots);
            (float px, float pw) = BottomHudLayout.Panel(width);
            if (width >= BottomHudLayout.MinWidth)
            {
                Assert.True(BottomHudLayout.SlotX(width, 0) >= px, $"the first slot starts inside the panel at {width}");
                Assert.True(BottomHudLayout.SlotX(width, n - 1) + BottomHudLayout.Slot <= px + pw, $"the last slot ends inside the panel at {width}");
            }
            // The row (the slots, then the pager on the mana orb's side) is centred: as much room before as after.
            float before = BottomHudLayout.SlotX(width, 0) - px, after = px + pw - BottomHudLayout.PagerX(width) - BottomHudLayout.Pager;
            Assert.Equal(before, after, 2);
            Assert.True(BottomHudLayout.PagerX(width) > BottomHudLayout.SlotX(width, n - 1) + BottomHudLayout.Slot, "the pager comes after the last slot");
        }
        Assert.True(BottomHudLayout.SlotCount(1040f) > BottomHudLayout.SlotCount(700f), "a wider screen has more slots");
    }

    [Theory]
    [InlineData(1, 0, 0, 2.5f)]       // Clumsy, first circle: (3 + 1) / 4 + 1.5
    [InlineData(1, 64, 0, 4.25f)]     // an eighth-circle spell: (3 + 8) / 4 + 1.5
    [InlineData(1, 101, 0, 2.5f)]     // Necromancy
    [InlineData(2, 21, 0, 10f)]       // a skill
    [InlineData(0, 0, 0, 0f)]         // empty
    public void Cooldown_BySlotKind(int kind, int id, int dex, float seconds) =>
        Assert.Equal(seconds, BottomHudLayout.Cooldown(new HudSlot { kind = kind, id = id == 0 ? 1 : id }, dex) * (kind == 0 ? 0 : 1), 3);

    [Theory]
    [InlineData(0x0E21, 0, 11f)]      // bandages, no dexterity
    [InlineData(0x0E21, 100, 6f)]     // 100 dexterity: 11 - 5
    [InlineData(0x0E21, 200, 3f)]     // never under 3
    [InlineData(0x0F0C, 50, 10f)]     // a heal potion
    [InlineData(0x1F14, 50, 1f)]      // anything else: the action delay
    public void Cooldown_ForItems(int graphic, int dex, float seconds) =>
        Assert.Equal(seconds, BottomHudLayout.Cooldown(new HudSlot { kind = 3, graphic = graphic }, dex), 3);

    [Fact]
    public void Pages_TurnBothWays_AndComeRound()
    {
        Assert.Equal(1, BottomHudLayout.NextPage(0, 1));
        Assert.Equal(BottomHudLayout.Pages - 1, BottomHudLayout.NextPage(0, -1)); // up from the first: the last
        Assert.Equal(0, BottomHudLayout.NextPage(BottomHudLayout.Pages - 1, 1));  // down from the last: the first
    }

    [Fact]
    public void Ease_GlidesTowardTheValue_AndLands()
    {
        float shown = 1f;
        float first = BottomHudLayout.Ease(shown, 0.5f, 0.016f);
        Assert.InRange(first, 0.5f, 1f);
        Assert.True(first < 1f, "it moves at once");
        for (int i = 0; i < 120; i++)
            shown = BottomHudLayout.Ease(shown, 0.5f, 0.016f);
        Assert.Equal(0.5f, shown);                                   // two seconds on: there, exactly
        Assert.Equal(0.3f, BottomHudLayout.Ease(0.3f, 0.9f, 0f));    // no time, no move
        Assert.True(BottomHudLayout.Ease(0.2f, 0.9f, 0.016f) > 0.2f); // it rises as it falls
    }

    [Fact]
    public void Ratio_AndFit()
    {
        Assert.Equal(0.8f, BottomHudLayout.Ratio(80, 100));
        Assert.Equal(0f, BottomHudLayout.Ratio(50, 0));   // the maximum not known yet
        Assert.Equal(1f, BottomHudLayout.Ratio(120, 100));
        var saved = new List<HudSlot> { new HudSlot { kind = 1, id = 4 }, null };
        List<HudSlot> fit = BottomHudLayout.Fit(saved, 4);
        Assert.Equal(4, fit.Count);
        Assert.Equal(HudSlotKind.Spell, fit[0].Kind);
        Assert.Equal(HudSlotKind.Empty, fit[1].Kind);
        Assert.Equal(HudSlotKind.Empty, fit[3].Kind);
        Assert.Equal(HudSlotKind.Empty, new HudSlot { kind = 9 }.Kind); // a bad file: empty, not a crash
    }
}
