using UOClient.Unity.UI;

namespace UOClient.Tests;

public sealed class TooltipPlacementTests
{
    // TazUO: GameCursor.DrawToolTip calls Tooltip.Draw(mouse.X, mouse.Y + 24), which clamps to the logical window
    // and draws at (x - 4, y - 2).
    [Fact]
    public void SitsJustUnderTheCursor()
    {
        (float x, float y) = HoverTooltip.Place(400, 300, 200, 60, 1280, 720);
        Assert.Equal(400 - 4, x);
        Assert.Equal(300 + 24 - 2, y);
    }

    [Fact]
    public void IsPushedBackOntoTheViewAtTheRightAndBottomEdges()
    {
        // No flip to the other side of the cursor: clamped to the edge, as TazUO does.
        (float x, float y) = HoverTooltip.Place(1200, 700, 200, 60, 1280, 720);
        Assert.Equal(1280 - 200 - 4, x);
        Assert.Equal(720 - 60 - 2, y);
    }

    [Fact]
    public void ClampsBeforeTheInset()
    {
        (float x, float y) = HoverTooltip.Place(-10, -50, 100, 40, 1280, 720);
        Assert.Equal(-4, x);
        Assert.Equal(-2, y);
        (x, y) = HoverTooltip.Place(5, 5, 400, 200, 300, 150); // bigger than the view: held at view - size, as TazUO
        Assert.Equal(300 - 400 - 4, x);
        Assert.Equal(150 - 200 - 2, y);
    }

    // The tooltip draws under GUI.matrix = Scale(s) with the mouse and view divided by s once: the screen point it
    // lands on must be the TazUO offset scaled once, at any UI scale (a second scale put it up-left of the cursor).
    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.44f)]
    [InlineData(2.5f)]
    public void ScalesOnceWithTheUi(float scale)
    {
        const float screenW = 2560, screenH = 1440, mouseX = 800, mouseY = 500;
        (float x, float y) = HoverTooltip.Place(mouseX / scale, mouseY / scale, 200, 60, screenW / scale, screenH / scale);
        Assert.Equal(mouseX - 4 * scale, x * scale, 3);
        Assert.Equal(mouseY + 22 * scale, y * scale, 3);
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
