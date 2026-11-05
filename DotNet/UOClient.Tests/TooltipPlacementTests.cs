using UOClient.Unity.UI;

namespace UOClient.Tests;

public sealed class TooltipPlacementTests
{
    [Fact]
    public void SitsDownRightOfTheCursor()
    {
        (float x, float y) = HoverTooltip.Place(400, 300, 200, 60, 1280, 720);
        Assert.Equal(400 + HoverTooltip.CursorOffset, x);
        Assert.Equal(300 + HoverTooltip.CursorOffset, y);
    }

    [Fact]
    public void FlipsToTheOtherSideAtTheRightAndBottomEdges()
    {
        (float x, float y) = HoverTooltip.Place(1200, 700, 200, 60, 1280, 720);
        Assert.Equal(1200 - 200 - HoverTooltip.FlipGap, x);
        Assert.Equal(700 - 60 - HoverTooltip.FlipGap, y);
    }

    [Fact]
    public void StaysOnTheViewWhenNeitherSideFits()
    {
        (float x, float y) = HoverTooltip.Place(250, 20, 300, 40, 400, 300); // too wide for either side of the cursor
        Assert.Equal(0, x);
        Assert.Equal(20 + HoverTooltip.CursorOffset, y);
        (x, y) = HoverTooltip.Place(5, 5, 400, 200, 300, 150);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
