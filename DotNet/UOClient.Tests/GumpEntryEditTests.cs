using UOClient.Core;
using UOClient.Unity.UI;
using static UOClient.Unity.UI.ServerGumpWindow;

namespace UOClient.Tests;

/// <summary>
/// A server text entry's caret keys, double-click and length, as TazUO's StbTextBox(parts) and its stb TextEdit handle
/// them (single-line, StbTextBox.cs:44, :99, :788), against ModernUO's DisplayGumpResponse 239 cap.
/// (Cursor, Anchor): TextEditor's cursorIndex and selectIndex; equal means nothing selected.
/// </summary>
public sealed class GumpEntryEditTests
{
    private const string Text = "tame the dragon";

    [Theory]
    [InlineData(EntryKey.Up, 4)]
    [InlineData(EntryKey.Left, 4)]
    [InlineData(EntryKey.Down, 6)]
    [InlineData(EntryKey.Right, 6)]
    public void UpAndDownAreLeftAndRightAsStbSingleLine(EntryKey key, int to)
    {
        // TextEdit.Key: SingleLine turns Up into Left and Down into Right (TextEdit.cs:561-566, :623-628), wrapped
        // text or not.
        Assert.Equal((to, to), EntryKeyMove(Text, 5, 5, key, shift: false, ctrl: false));
    }

    [Fact]
    public void ArrowsStopAtTheEnds()
    {
        Assert.Equal((0, 0), EntryKeyMove(Text, 0, 0, EntryKey.Up, false, false));
        Assert.Equal((Text.Length, Text.Length), EntryKeyMove(Text, Text.Length, Text.Length, EntryKey.Down, false, false));
        Assert.Equal((0, 0), EntryKeyMove("", 0, 0, EntryKey.Right, false, false));
        Assert.Equal((0, 0), EntryKeyMove(null, 3, 1, EntryKey.Left, false, false));
    }

    [Fact]
    public void WithoutShiftASelectionCollapsesToItsNearEnd()
    {
        // MoveToFirst / MoveToLast (TextEdit.cs:452-479, :55-64, :312-322), whichever end the caret is at.
        Assert.Equal((2, 2), EntryKeyMove(Text, 2, 8, EntryKey.Left, false, false));
        Assert.Equal((2, 2), EntryKeyMove(Text, 8, 2, EntryKey.Up, false, false));
        Assert.Equal((8, 8), EntryKeyMove(Text, 2, 8, EntryKey.Right, false, false));
        Assert.Equal((8, 8), EntryKeyMove(Text, 8, 2, EntryKey.Down, false, false));
        Assert.Equal((2, 2), EntryKeyMove(Text, 8, 2, EntryKey.Left, false, ctrl: true));
        Assert.Equal((8, 8), EntryKeyMove(Text, 2, 8, EntryKey.Right, false, ctrl: true));
    }

    [Fact]
    public void ShiftMovesTheCaretAndKeepsTheAnchor()
    {
        Assert.Equal((4, 5), EntryKeyMove(Text, 5, 5, EntryKey.Left, shift: true, false));
        Assert.Equal((6, 5), EntryKeyMove(Text, 5, 5, EntryKey.Down, shift: true, false));
        Assert.Equal((9, 5), EntryKeyMove(Text, 8, 5, EntryKey.Right, shift: true, false));
        // Back over the anchor: nothing selected (SelectStart == SelectEnd).
        Assert.Equal((5, 5), EntryKeyMove(Text, 6, 5, EntryKey.Up, shift: true, false));
        Assert.Equal((0, 0), EntryKeyMove(Text, 0, 0, EntryKey.Left, shift: true, false));
    }

    [Fact]
    public void HomeAndEndGoToTheTextsEndsWhateverTheWrap()
    {
        // LineStart/LineEnd on a single-line TextEdit (TextEdit.cs:744-830); Ctrl's TextStart/TextEnd land the same.
        foreach (bool ctrl in new[] { false, true })
        {
            Assert.Equal((0, 0), EntryKeyMove(Text, 9, 9, EntryKey.Home, false, ctrl));
            Assert.Equal((Text.Length, Text.Length), EntryKeyMove(Text, 3, 3, EntryKey.End, false, ctrl));
            Assert.Equal((0, 0), EntryKeyMove(Text, 9, 2, EntryKey.Home, false, ctrl));
            Assert.Equal((0, 9), EntryKeyMove(Text, 9, 9, EntryKey.Home, true, ctrl));
            Assert.Equal((Text.Length, 2), EntryKeyMove(Text, 9, 2, EntryKey.End, true, ctrl));
        }
    }

    [Fact]
    public void CtrlArrowsMoveByStbWords()
    {
        // MoveToPreviousWord / MoveToNextWord (TextEdit.cs:324-361): a word starts after whitespace; Ctrl+Right goes
        // to the next word's start, not the current one's end.
        Assert.Equal((5, 5), EntryKeyMove(Text, 7, 7, EntryKey.Left, false, ctrl: true));
        Assert.Equal((5, 5), EntryKeyMove(Text, 9, 9, EntryKey.Left, false, ctrl: true));
        Assert.Equal((0, 0), EntryKeyMove(Text, 3, 3, EntryKey.Left, false, ctrl: true));
        Assert.Equal((9, 9), EntryKeyMove(Text, 5, 5, EntryKey.Right, false, ctrl: true));
        Assert.Equal((Text.Length, Text.Length), EntryKeyMove(Text, 10, 10, EntryKey.Right, false, ctrl: true));
        Assert.Equal((9, 5), EntryKeyMove(Text, 5, 5, EntryKey.Right, shift: true, ctrl: true));
        // Ctrl does nothing to Up and Down (ApplyShiftIfNecessary only).
        Assert.Equal((6, 6), EntryKeyMove(Text, 7, 7, EntryKey.Up, false, ctrl: true));
    }

    [Theory]
    [InlineData(0, 0, 4)]   // "tame"
    [InlineData(2, 0, 4)]
    [InlineData(3, 0, 4)]
    [InlineData(5, 5, 8)]   // "the", from its first char
    [InlineData(9, 9, 15)]  // "dragon", the last word: to the end
    [InlineData(14, 9, 15)]
    public void DoubleClickSelectsStbsWordAroundTheCaret(int caret, int start, int end)
    {
        Assert.Equal((start, end), EntryWordAt(Text, caret));
    }

    [Fact]
    public void DoubleClickOnWhitespaceOrPastTheEndSelectsNothing()
    {
        Assert.Null(EntryWordAt(Text, 4));
        Assert.Null(EntryWordAt(Text, Text.Length));
        Assert.Null(EntryWordAt("", 0));
        Assert.Null(EntryWordAt(null, 0));
        Assert.Equal((0, 1), EntryWordAt("a b", 0));
        // Only one of several spaces is left off.
        Assert.Equal((0, 3), EntryWordAt("ab  cd", 1));
    }

    [Fact]
    public void EscLetsTheKeyboardGoOnlyWithNothingSelected()
    {
        Assert.True(EntryEscapeDropsFocus(4, 4));
        Assert.False(EntryEscapeDropsFocus(4, 9));
    }

    [Fact]
    public void EditedTextIsHeldToTheEntrysLimitAndTheServersCap()
    {
        // StbTextBox's OnTextInput and SetText (StbTextBox.cs:776-785, :834-851) hold the text to its limit;
        // ModernUO's DisplayGumpResponse refuses a reply text over 239 (GumpSystem.IncomingPackets.cs:139).
        var gump = new ServerGump
        {
            Commands = ServerGump.ParseLayout(
                "{ textentrylimited 0 0 100 20 0 1 0 16 }{ textentry 0 30 100 20 0 2 0 }{ textentrylimited 0 60 100 20 0 3 0 255 }"),
            Lines = new[] { "" }
        };
        var controls = new GumpControls(gump);
        string pasted = new string('x', 300);
        controls.SetText(0, pasted);
        controls.SetText(1, pasted);
        controls.SetText(2, pasted);
        controls.SetText(1, null); // a cleared field
        controls.SetText(1, pasted);
        Assert.Equal(16, controls.Text(0).Length);
        Assert.Equal(GumpCommand.MaxReplyText, controls.Text(1).Length);
        Assert.Equal(GumpCommand.MaxReplyText, controls.Text(2).Length); // a size above the server's cap is held to it
        Assert.Equal(239, GumpCommand.MaxReplyText);

        byte[] reply = GameSession.GumpReply(1, 2, 0, Array.Empty<int>(), new List<(int, string)> { (2, pasted) });
        // 3 header, serial, type, button, switch count, text count = 23; then id, length.
        Assert.Equal(239, (reply[25] << 8) | reply[26]);
        Assert.Equal(23 + 4 + 239 * 2, reply.Length);
    }
}
