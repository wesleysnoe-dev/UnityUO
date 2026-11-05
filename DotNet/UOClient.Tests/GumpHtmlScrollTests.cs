using UOClient.Core;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Server gump HTML scrolling as TazUO's HtmlControl does it (HtmlControl.cs, ScrollBarBase.cs, ScrollBar.cs,
/// ScrollFlag.cs): which scrollbar shows, the scroll range, the wheel, arrows and thumb, and the wheel kept to the
/// control under the pointer.
/// </summary>
public sealed class GumpHtmlScrollTests
{
    private static GumpCommand Command(string layout) => ServerGump.ParseLayout(layout)[0];

    // Gump sizes as the tests assume them; the logic only needs them consistent.
    private static HtmlScrollParts BarParts(int height) => new HtmlScrollParts(15, 21, 15, 21, 13, 16, 15, height);
    private static HtmlScrollParts FlagParts(int height) => new HtmlScrollParts(10, 14, 10, 14, 10, 14, 10, height);

    [Theory]
    // htmlgump x y w h text background scrollbar: any 2 is the flag, background or not (HtmlControl.cs:36-37).
    [InlineData("{ htmlgump 0 0 200 100 0 0 0 }", HtmlScrollbarKind.None)]
    [InlineData("{ htmlgump 0 0 200 100 0 0 1 }", HtmlScrollbarKind.Bar)]
    [InlineData("{ htmlgump 0 0 200 100 0 1 1 }", HtmlScrollbarKind.Bar)]
    [InlineData("{ htmlgump 0 0 200 100 0 0 2 }", HtmlScrollbarKind.Flag)]
    [InlineData("{ htmlgump 0 0 200 100 0 1 2 }", HtmlScrollbarKind.Flag)]
    [InlineData("{ htmlgump 0 0 200 100 0 0 3 }", HtmlScrollbarKind.Bar)]
    // xmfhtmlgump / xmfhtmlgumpcolor: the flag only with a background part other than 0 (GumpHelpers.cs:304, :332).
    [InlineData("{ xmfhtmlgump 0 0 200 100 1011036 0 2 }", HtmlScrollbarKind.Bar)]
    [InlineData("{ xmfhtmlgump 0 0 200 100 1011036 1 2 }", HtmlScrollbarKind.Flag)]
    [InlineData("{ xmfhtmlgump 0 0 200 100 1011036 1 1 }", HtmlScrollbarKind.Bar)]
    [InlineData("{ xmfhtmlgumpcolor 0 0 200 100 1011036 0 2 32767 }", HtmlScrollbarKind.Bar)]
    [InlineData("{ xmfhtmlgumpcolor 0 0 200 100 1011036 1 2 32767 }", HtmlScrollbarKind.Flag)]
    [InlineData("{ xmfhtmlgumpcolor 0 0 200 100 1011036 1 0 32767 }", HtmlScrollbarKind.None)]
    // xmfhtmltok x y w h background scrollbar colour number @args@ (GumpHelpers.cs:370).
    [InlineData("{ xmfhtmltok 0 0 200 100 0 2 32767 1011036 @a@ }", HtmlScrollbarKind.Bar)]
    [InlineData("{ xmfhtmltok 0 0 200 100 1 2 32767 1011036 @a@ }", HtmlScrollbarKind.Flag)]
    [InlineData("{ xmfhtmltok 0 0 200 100 1 0 32767 1011036 @a@ }", HtmlScrollbarKind.None)]
    public void FlagOrBarAsTazUOChoosesIt(string layout, HtmlScrollbarKind kind)
    {
        GumpCommand c = Command(layout);
        Assert.Equal(kind, ServerGumpWindow.HtmlScrollbar(c));
        Assert.Equal(kind != HtmlScrollbarKind.None, ServerGumpWindow.HtmlParts(c).Scrollbar);
    }

    [Fact]
    public void ScrollbarSitsFourteenInAndTheTextWrapsShortOfIt()
    {
        Assert.Equal(186, ServerGumpWindow.HtmlScrollbarX(200));
        // MaxWidth = Width - 16 for a scrollbar - 8 for a background (HtmlControl.cs:39).
        Assert.Equal(200 - 16 - 8 - 9, ServerGumpWindow.HtmlTextWidth(200, true, true, 0));
        Assert.Equal(200 - 16 - 8, ServerGumpWindow.HtmlTextWidth(200, true, true, 0x7C00));
    }

    [Theory]
    // MaxValue = text height - control height (+ 8 with a background), never below 0 (HtmlControl.cs:189-190).
    [InlineData(400, 100, false, 300)]
    [InlineData(400, 100, true, 308)]
    [InlineData(94, 100, true, 2)]
    [InlineData(80, 100, false, 0)]
    [InlineData(100, 100, false, 0)]
    public void ScrollRangeIsTextLessControl(int textHeight, int height, bool background, int max)
    {
        Assert.Equal(max, ServerGumpWindow.HtmlScrollMax(textHeight, height, background));
        var s = new HtmlScroll(HtmlScrollbarKind.Bar);
        s.SetMax(ServerGumpWindow.HtmlScrollMax(textHeight, height, background));
        for (int i = 0; i < 20; i++)
            s.Wheel(up: false);
        Assert.Equal(max, s.Value);
    }

    [Fact]
    public void WheelMovesFiftyAndStaysInRange()
    {
        var s = new HtmlScroll(HtmlScrollbarKind.Bar);
        s.SetMax(120);
        s.Wheel(up: false);
        Assert.Equal(50, s.Value);
        s.Wheel(up: false);
        s.Wheel(up: false);
        Assert.Equal(120, s.Value);
        s.Wheel(up: true);
        Assert.Equal(70, s.Value);
        s.Wheel(up: true);
        s.Wheel(up: true);
        Assert.Equal(0, s.Value);

        // A shrinking range pulls the value in (ScrollBarBase.MaxValue).
        s.SetMax(120);
        s.Wheel(up: false);
        s.SetMax(30);
        Assert.Equal(30, s.Value);
        s.SetMax(-5);
        Assert.Equal((0, 0), (s.Max, s.Value));
    }

    [Fact]
    public void WheelScrollsEvenWithoutAShownBar()
    {
        // HtmlControl.OnMouseWheel moves its (hidden) bar's value whatever HasScrollbar says.
        var s = new HtmlScroll(HtmlScrollbarKind.None);
        s.SetMax(40);
        s.Wheel(up: false);
        Assert.Equal(40, s.Value);
        Assert.False(s.Press(BarParts(100), 5, 50));
    }

    [Fact]
    public void WheelGoesOnlyToTheHtmlUnderThePointer()
    {
        List<GumpCommand> commands = ServerGump.ParseLayout(
            "{ resizepic 0 0 9200 400 300 }" +
            "{ htmlgump 10 10 200 100 0 0 1 }" +           // 0: page 0
            "{ page 1 }" +
            "{ htmlgump 10 150 200 100 1 1 2 }" +          // page 1
            "{ htmlgump 100 50 200 40 2 0 1 }" +           // page 1, over the first
            "{ page 2 }" +
            "{ xmfhtmlgump 10 150 200 100 1011036 0 1 }"); // page 2, where page 1's is
        int first = commands.FindIndex(c => c.Name == "htmlgump");
        int second = commands.FindIndex(c => c.Name == "htmlgump" && c.Arg(1) == 150);
        int over = commands.FindIndex(c => c.Name == "htmlgump" && c.Arg(0) == 100);
        int xmf = commands.FindIndex(c => c.Name == "xmfhtmlgump");

        Assert.Equal(first, ServerGumpWindow.HtmlWheelTarget(commands, 1, 15, 15));
        Assert.Equal(-1, ServerGumpWindow.HtmlWheelTarget(commands, 1, 350, 15));   // on the gump, off every html
        Assert.Equal(-1, ServerGumpWindow.HtmlWheelTarget(commands, 1, 210, 15));   // the right edge is outside
        Assert.Equal(-1, ServerGumpWindow.HtmlWheelTarget(commands, 1, 15, 110));   // and the bottom
        Assert.Equal(-1, ServerGumpWindow.HtmlWheelTarget(commands, 1, 500, 500));  // off the gump
        Assert.Equal(over, ServerGumpWindow.HtmlWheelTarget(commands, 1, 150, 60)); // the later one is on top
        Assert.Equal(first, ServerGumpWindow.HtmlWheelTarget(commands, 2, 150, 60)); // not on page 2
        Assert.Equal(second, ServerGumpWindow.HtmlWheelTarget(commands, 1, 50, 200));
        Assert.Equal(xmf, ServerGumpWindow.HtmlWheelTarget(commands, 2, 50, 200));
    }

    [Fact]
    public void ScrollBarArrowsHoldAndSpeedUp()
    {
        HtmlScrollParts p = BarParts(100);
        var s = new HtmlScroll(HtmlScrollbarKind.Bar);
        s.SetMax(500);
        Assert.True(s.Press(p, 5, 90)); // the down arrow, at the bottom
        Assert.True(s.DownHeld);
        // Steps of 1 + the changer (from 1), which grows as the step count (from 1) reaches each 8: 2 x 7, 3 x 8, then 4.
        for (int i = 0; i < 7; i++)
            s.Tick();
        Assert.Equal(14, s.Value);
        for (int i = 0; i < 8; i++)
            s.Tick();
        Assert.Equal(14 + 24, s.Value);
        s.Tick();
        Assert.Equal(42, s.Value);
        s.Release();
        s.Tick();
        Assert.Equal(42, s.Value);

        Assert.True(s.Press(p, 5, 3)); // the up arrow, back at 2 a step
        Assert.True(s.UpHeld);
        s.Tick();
        Assert.Equal(40, s.Value);
        s.Release();
    }

    [Fact]
    public void ScrollBarThumbFollowsTheDrag()
    {
        HtmlScrollParts p = BarParts(100); // travel 100 - 21 - 21 - 16 = 42, from 21 down
        var s = new HtmlScroll(HtmlScrollbarKind.Bar);
        s.SetMax(420);
        Assert.Equal(42, s.Area(p));
        Assert.Equal(21, s.ThumbY(p));

        // Pressing mid-bar centres the thumb there: 21 + 8 + 21 = 50 is halfway.
        Assert.True(s.Press(p, 5, 50));
        Assert.True(s.Dragging);
        Assert.Equal(210, s.Value);
        Assert.Equal(21 + 21, s.ThumbY(p));
        s.Drag(p, 5, 1000);
        Assert.Equal(420, s.Value);
        s.Drag(p, 5, -50);
        Assert.Equal(0, s.Value);
        s.Release();
        s.Drag(p, 5, 70);
        Assert.Equal(0, s.Value);

        Assert.False(s.Press(p, 16, 50)); // past the bar's width
        Assert.False(s.Press(p, 5, 101));
    }

    [Fact]
    public void ScrollFlagIsGrabbedOnlyOnTheFlag()
    {
        HtmlScrollParts p = FlagParts(100); // travel 100 - 14 = 86, from the top
        var s = new HtmlScroll(HtmlScrollbarKind.Flag);
        s.SetMax(86);
        Assert.Equal(86, s.Area(p));
        s.Wheel(up: false);
        Assert.Equal(50, s.ThumbY(p));

        Assert.False(s.Press(p, 5, 20)); // the bar under the flag is not the flag
        Assert.True(s.Press(p, 5, 57));  // on it: y - 7 = 50
        Assert.True(s.Dragging);
        Assert.Equal(50, s.Value);
        s.Drag(p, 5, 30);
        Assert.Equal(23, s.Value);
        s.Release();

        // At the top the flag lies over its undrawn up arrow's box, and just under that over the down arrow's
        // (ScrollFlag.cs:48-54): pressing there holds them, as in TazUO.
        s.Wheel(up: true);
        Assert.Equal(0, s.ThumbY(p));
        Assert.True(s.Press(p, 5, 3));
        Assert.True(s.UpHeld);
        s.Release();
    }

    [Fact]
    public void ScrollFlagDownBoxSitsUnderTheFlagsHeight()
    {
        HtmlScrollParts p = new HtmlScrollParts(10, 4, 10, 20, 10, 14, 10, 100);
        var s = new HtmlScroll(HtmlScrollbarKind.Flag);
        s.SetMax(86);
        s.Wheel(up: false);
        s.Wheel(up: true); // back to 0 with the flag at the top, covering y 0-13; the down box starts at 14
        Assert.False(s.Press(p, 5, 14)); // not on the flag, so not on the bar
        Assert.True(s.Press(p, 5, 8));   // the flag, past the 4-tall up box: a drag
        Assert.True(s.Dragging);
    }
}
