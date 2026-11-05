using UOClient.Core;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Which window, gump control or the world a press, wheel turn or key goes to, as TazUO routes them: the front gump
/// under the pointer (UIManager.GetMouseOverControl), its control by Control.HitTest and ClickPriority, the wheel to
/// that control alone, and keys kept from the world while a text box has them (GameSceneInputHandler.cs:1405).
/// </summary>
public sealed class InputRoutingTests
{
    // Pictures, buttons and resizepics carry no plain w h in the layout: the tests give each its box.
    private static Func<int, bool> At(List<GumpCommand> commands, Dictionary<int, (int W, int H)> sizes, float x, float y) => i =>
    {
        GumpCommand c = commands[i];
        (int w, int h) = sizes.TryGetValue(i, out var s) ? s : (c.Arg(2), c.Arg(3));
        return x >= c.Arg(0) && x < c.Arg(0) + w && y >= c.Arg(1) && y < c.Arg(1) + h;
    };

    [Fact]
    public void WheelDoesNotScrollAnHtmlUnderAHigherControl()
    {
        List<GumpCommand> commands = ServerGump.ParseLayout(
            "{ resizepic 0 0 9200 400 300 }" +        // 0: below everything
            "{ htmlgump 10 10 200 100 0 0 1 }" +       // 1
            "{ gumppic 150 60 5100 }" +                // 2: a picture laid over the html's corner
            "{ text 20 80 0 0 }" +                     // 3: a label over it (takes no mouse)
            "{ text 20 20 0 1 }" +                     // 4: a label with a tooltip (then it does)
            "{ tooltip 1011036 }");
        var sizes = new Dictionary<int, (int, int)> { [0] = (400, 300), [2] = (40, 40), [3] = (60, 16), [4] = (60, 16) };
        int Wheel(float x, float y) => ServerGumpWindow.HtmlWheelTarget(commands, 1, x, y, At(commands, sizes, x, y));

        Assert.Equal(1, Wheel(100, 50));   // the html, nothing on it
        Assert.Equal(-1, Wheel(160, 70));  // the picture takes it and passes it to the gump
        Assert.Equal(1, Wheel(30, 85));    // a plain label lets it through
        Assert.Equal(-1, Wheel(30, 25));   // a tooltip makes the label take the mouse
        Assert.Equal(1, Wheel(205, 15));   // the scrollbar (Width - 14 on) is the html's own
        Assert.Equal(-1, Wheel(350, 250)); // the background is not an html
    }

    [Fact]
    public void ButtonsKeepThePointerFromLaterDefaultControls()
    {
        List<GumpCommand> commands = ServerGump.ParseLayout(
            "{ button 10 10 4005 4007 1 0 1 }" +       // 0
            "{ htmlgump 0 0 200 100 0 0 1 }" +         // 1: an html laid over the button
            "{ gumppic 10 10 5100 }" +                 // 2: and a picture
            "{ button 15 15 4005 4007 1 0 2 }" +       // 3: a later button over the first
            "{ textentry 100 50 80 20 0 0 }" +         // 4
            "{ gumppic 110 50 5100 }" +                // 5: a picture over the entry
            "{ page 2 }" +
            "{ button 100 80 4005 4007 1 0 3 }");      // 7: not on page 1
        var sizes = new Dictionary<int, (int, int)> { [0] = (30, 30), [2] = (30, 30), [3] = (30, 30), [5] = (20, 20), [7] = (30, 30) };
        int Over(float x, float y, int page = 1) => ServerGumpWindow.MouseOverCommand(commands, page, At(commands, sizes, x, y));

        Assert.Equal(0, Over(12, 12));    // the button wins over the later html and picture (ClickPriority.High)
        Assert.Equal(3, Over(20, 20));    // a later button covers it
        Assert.Equal(-1, ServerGumpWindow.HtmlWheelTarget(commands, 1, 12, 12, At(commands, sizes, 12, 12)));
        Assert.Equal(1, Over(150, 20));   // the html alone
        Assert.Equal(4, Over(102, 55));   // the entry
        Assert.Equal(5, Over(115, 55));   // the picture on it takes the press
        Assert.Equal(1, Over(110, 85));   // page 2's button is not there on page 1
        Assert.Equal(7, Over(110, 85, 2));
    }

    [Fact]
    public void FrontWindowTakesThePointer()
    {
        var stack = new List<(float X, float Y, float W, float H)>
        {
            (0, 0, 300, 200),     // back
            (100, 50, 300, 200),  // middle
            (150, 100, 50, 50)    // front
        };
        int Top(float x, float y) => UiRouting.TopAt(stack.Count, i => stack[i], x, y);

        Assert.Equal(0, Top(50, 50));
        Assert.Equal(1, Top(120, 60));
        Assert.Equal(2, Top(160, 110));
        Assert.Equal(1, Top(200, 100));  // the front one's right edge is outside it
        Assert.Equal(1, Top(399.9f, 249.9f));
        Assert.Equal(-1, Top(400, 100));
        Assert.Equal(-1, Top(-1, 10));
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void WholeGumpIsOverUiAndPressesHitWhatIsDrawnAtAnyScale(float scale)
    {
        const float screenHeight = 1080;
        // A gump window at (120, 90) in UI pixels, 300 x 200: an html with its bar on the right edge, an entry, a button.
        List<GumpCommand> commands = ServerGump.ParseLayout(
            "{ resizepic 0 0 9200 300 200 }" +
            "{ htmlgump 100 0 200 120 0 1 1 }" +
            "{ textentry 10 150 120 20 0 0 }" +
            "{ button 200 160 4005 4007 1 0 1 }");
        var sizes = new Dictionary<int, (int, int)> { [0] = (300, 200), [3] = (30, 22) };
        (float X, float Y, float W, float H) window = (120, 90, 300, 200);

        // Screen point (bottom-left origin) where IMGUI draws a gump-local point under GUI.matrix = scale.
        (float, float) Screen(float lx, float ly) => ((window.X + lx) * scale, screenHeight - (window.Y + ly) * scale);

        foreach ((float lx, float ly, int expect) in new[]
                 {
                     (295f, 5f, 1),      // the html's scrollbar, at the gump's right edge
                     (299.5f, 119.5f, 1),
                     (20f, 160f, 2),     // the entry
                     (210f, 170f, 3),    // the button
                     (0.25f, 199.5f, 0)  // the background's corner
                 })
        {
            (float sx, float sy) = Screen(lx, ly);
            (float ux, float uy) = UiRouting.ScreenToUi(sx, sy, screenHeight, scale);
            Assert.Equal(0, UiRouting.TopAt(1, _ => window, ux, uy)); // over UI: no world click under it
            // The press's window-local point is the one the command was drawn at.
            float px = ux - window.X, py = uy - window.Y;
            Assert.Equal(lx, px, 3);
            Assert.Equal(ly, py, 3);
            Assert.Equal(expect, ServerGumpWindow.MouseOverCommand(commands, 1, At(commands, sizes, px, py)));
        }

        // Just outside the gump the world has the pointer.
        foreach ((float lx, float ly) in new[] { (300.5f, 10f), (10f, 200.5f), (-0.5f, 10f), (10f, -0.5f) })
        {
            (float sx, float sy) = Screen(lx, ly);
            (float ux, float uy) = UiRouting.ScreenToUi(sx, sy, screenHeight, scale);
            Assert.Equal(-1, UiRouting.TopAt(1, _ => window, ux, uy));
        }
    }

    [Fact]
    public void FocusedEntrySwallowsWorldKeysAndEnter()
    {
        Assert.True(UiRouting.WorldTakesKeys(chatTyping: false, fieldFocused: false));
        Assert.False(UiRouting.WorldTakesKeys(chatTyping: false, fieldFocused: true));  // a gump entry: no walking, no macros
        Assert.False(UiRouting.WorldTakesKeys(chatTyping: true, fieldFocused: false));

        Assert.True(UiRouting.EnterGoesToChat(chatTyping: false, fieldFocused: false)); // Enter opens the speech line
        Assert.False(UiRouting.EnterGoesToChat(chatTyping: false, fieldFocused: true)); // but not from a gump's entry
        Assert.True(UiRouting.EnterGoesToChat(chatTyping: true, fieldFocused: true));   // the open line's own Enter sends
    }

    [Fact]
    public void WorldPressIsBlockedOverWindowsAndWhileDraggingAndOnlyReleasesAFocusedEntry()
    {
        Assert.Equal(UiRouting.Press.World, UiRouting.WorldPress(overUi: false, dragging: false, fieldFocused: false));
        Assert.Equal(UiRouting.Press.None, UiRouting.WorldPress(overUi: true, dragging: false, fieldFocused: false));
        Assert.Equal(UiRouting.Press.None, UiRouting.WorldPress(overUi: false, dragging: true, fieldFocused: false));
        Assert.Equal(UiRouting.Press.ReleaseField, UiRouting.WorldPress(overUi: false, dragging: false, fieldFocused: true));
        Assert.Equal(UiRouting.Press.None, UiRouting.WorldPress(overUi: true, dragging: false, fieldFocused: true));
    }
}
