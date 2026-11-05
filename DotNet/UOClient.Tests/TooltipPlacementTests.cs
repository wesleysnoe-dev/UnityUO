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

public sealed class ContextMenuPlacementTests
{
    [Fact]
    public void OpensWithItsCornerAtTheCursor()
    {
        (float x, float y) = ContextMenuWindow.Place(400, 300, 160, 104, 1280, 720);
        Assert.Equal((400f, 300f), (x, y));
    }

    [Fact]
    public void FlipsSoTheLastEntryIsOnScreenAtTheBottomRight()
    {
        // A vendor clicked near the bottom-right corner: Buy, Sell, Train... all have to be reachable.
        (float x, float y) = ContextMenuWindow.Place(1250, 700, 160, 104, 1280, 720);
        Assert.Equal(1250 - 160, x);
        Assert.Equal(700 - 104, y);
        Assert.True(x + 160 <= 1280 && y + 104 <= 720);
    }

    [Fact]
    public void ATallMenuIsKeptOnTheView()
    {
        (float x, float y) = ContextMenuWindow.Place(10, 200, 160, 500, 1280, 600); // fits neither above nor below the cursor
        Assert.Equal(10, x);
        Assert.Equal(0, y); // flipped above, then held at the top: all 500 rows on the 600-high view
        (x, y) = ContextMenuWindow.Place(10, 10, 160, 900, 1280, 600); // taller than the view: the top stays visible
        Assert.Equal(0, y);
    }
}
