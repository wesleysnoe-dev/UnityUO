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
            // The row is centred: as much room before the first slot as after the last.
            float before = BottomHudLayout.SlotX(width, 0) - px, after = px + pw - BottomHudLayout.SlotX(width, n - 1) - BottomHudLayout.Slot;
            Assert.Equal(before, after, 2);
        }
        Assert.True(BottomHudLayout.SlotCount(1040f) > BottomHudLayout.SlotCount(700f), "a wider screen has more slots");
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
