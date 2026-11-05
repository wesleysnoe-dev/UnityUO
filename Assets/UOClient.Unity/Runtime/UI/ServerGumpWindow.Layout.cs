using System;
using System.Collections.Generic;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout rules.
    public sealed partial class ServerGumpWindow
    {
        /// <summary>TazUO's HtmlControl background (ResizePic 0x2486), narrowed by its 16-pixel scrollbar when there is one.</summary>
        public const int HtmlBackgroundGump = 0x2486, HtmlScrollbarWidth = 16;

        /// <summary>
        /// TazUO's ScrollBar gumps (ScrollBar.cs:15-22): the up and down arrows (pressed / not), the bar's top, tiled
        /// middle and bottom, and the slider.
        /// </summary>
        public const int ScrollBarUpPressed = 250, ScrollBarUp = 251, ScrollBarDownPressed = 252, ScrollBarDown = 253,
            ScrollBarSlider = 254, ScrollBarBottom = 255, ScrollBarMiddle = 256, ScrollBarTop = 257;

        /// <summary>TazUO's ScrollFlag gumps (ScrollFlag.cs:13-15): its (undrawn) arrows and the flag.</summary>
        public const int ScrollFlagUp = 0x0824, ScrollFlagDown = 0x0825, ScrollFlagThumb = 0x0828;

        /// <summary>The 50% black CheckerTrans draws, and the alpha it leaves the controls it covers at (GumpHelpers.ApplyTrans).</summary>
        public const float CheckerAlpha = 0.5f;

        /// <summary>
        /// The client hue of a text, croppedtext or textentry hue: TazUO's Label, CroppedText and StbTextBox add one,
        /// as gump hues are 0-based (a label at 1152 shows as hue 1153, white).
        /// </summary>
        public static int TextHue(int serverHue) => (ushort)(serverHue + 1);

        /// <summary>
        /// The shade of a hue's 32-entry ramp a text, croppedtext or textentry is drawn in. TazUO's Label, CroppedText
        /// and StbTextBox never pass a cell, so RenderedText.Create's default (cell = 30, RenderedText.cs:169) reaches
        /// FontsLoader.GeneratePixelsUnicode → HuesLoader.GetPolygoneColor(30, hue) (FontsLoader.cs:1725, HuesLoader.cs:161).
        /// </summary>
        public const int TextColorCell = 30;

        /// <summary>GetPolygoneColor's answer for hue 0 or a hue past the table, and the BlackBorder colour (0xFF010101).</summary>
        public const uint TextBlackArgb = 0xFF010101;

        /// <summary>
        /// The colour of a gump label in client hue <paramref name="clientHue"/> (already <see cref="TextHue"/>'d), as
        /// GetPolygoneColor picks it: entry hue-1's <see cref="TextColorCell"/> shade when 0 &lt; hue &lt; HuesCount
        /// (HuesCount = every hue in hues.mul), else near-black.
        /// </summary>
        public static uint TextArgb(HueEntry[] hues, int clientHue)
        {
            if (hues == null || clientHue <= 0 || clientHue >= hues.Length)
                return TextBlackArgb;
            ushort[] ramp = hues[clientHue - 1].Colors;
            return ramp != null && ramp.Length > TextColorCell ? Color16.ToArgb32(ramp[TextColorCell]) : TextBlackArgb;
        }

        /// <summary>
        /// Whether a label of this colour gets TazUO's black border. Label, CroppedText and StbTextBox all ask for
        /// FontStyle.BlackBorder; GeneratePixelsUnicode skips it for text that is itself black (every channel 8 or less,
        /// FontsLoader.cs:1878-1881, 2055).
        /// </summary>
        public static bool HasBlackBorder(uint argb) =>
            ((argb >> 16) & 0xFF) > 8 || ((argb >> 8) & 0xFF) > 8 || (argb & 0xFF) > 8;

        /// <summary>The border's pixels around each text pixel: all eight neighbours, one pixel out (FontsLoader.cs:2057-2100).</summary>
        public static readonly (int Dx, int Dy)[] BlackBorderOffsets =
            { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

        /// <summary>The unicode font every server gump text uses: Label's 0xFF, CroppedText's and HtmlControl's 1 (CV_305D and later).</summary>
        public const int GumpFont = 1;

        /// <summary>
        /// The colour GeneratePixelsUnicode draws a client hue in: <see cref="UniText.UnhuedArgb"/> for 0xFFFF
        /// (FontsLoader.cs:1719-1722), else <see cref="TextArgb"/>.
        /// </summary>
        public static uint LabelArgb(HueEntry[] hues, int clientHue) =>
            clientHue == 0xFFFF ? UniText.UnhuedArgb : TextArgb(hues, clientHue);

        /// <summary>
        /// A text command's picture, as TazUO's Label(parts) makes it (Label.cs:58-70): not HTML, no width limit, black
        /// border. It is as wide as the text plus 4 and as tall as its lines plus 4, and the label takes that size.
        /// </summary>
        public static UniTextImage LabelImage(UniText text, HueEntry[] hues, string line, int serverHue) =>
            text.Generate(line, GumpFont, LabelArgb(hues, TextHue(serverHue)), 0, UniTextAlign.Left, UniTextStyle.BlackBorder);

        /// <summary>
        /// A croppedtext's picture, as CroppedText(parts) makes it (CroppedText.cs:13-36): HTML on, black border, and
        /// with a width the Cropped style, so text wider than the box loses its tail to "..." and stays on one line
        /// 18 tall. The picture is the box's width plus 4 and is drawn whole, not clipped to the box.
        /// </summary>
        public static UniTextImage CroppedImage(UniText text, HueEntry[] hues, string line, int serverHue, int width) =>
            text.Generate(line, GumpFont, LabelArgb(hues, TextHue(serverHue)), width, UniTextAlign.Left,
                width > 0 ? UniTextStyle.BlackBorder | UniTextStyle.Cropped : UniTextStyle.BlackBorder, html: true);

        /// <summary>
        /// The style of a textentry's / textentrylimited's text, as StbTextBox(parts) builds its RenderedText
        /// (StbTextBox.cs:25-93): BlackBorder | CropTexture asked for, CropTexture (and Cropped) taken off again before
        /// RenderedText.Create, so the text is plain, bordered, and wraps in the entry's width like any RenderedText.
        /// </summary>
        public const UniTextStyle EntryStyle = UniTextStyle.BlackBorder;

        /// <summary>
        /// A text entry's picture (StbTextBox's _rendererText): font 1, the hue plus one in its <see cref="TextColorCell"/>
        /// shade, laid out in the entry's width (wrapping onto lines the entry's height then clips) and drawn at its
        /// corner. The picture is the width plus 4.
        /// </summary>
        public static UniTextImage EntryImage(UniText text, HueEntry[] hues, string value, int serverHue, int width) =>
            text.Generate(value, GumpFont, LabelArgb(hues, TextHue(serverHue)), width, UniTextAlign.Left, EntryStyle);

        /// <summary>StbTextBox's caret (_rendererCaret): "_" in the same font, hue and border, unwrapped.</summary>
        public static UniTextImage EntryCaretImage(UniText text, HueEntry[] hues, int serverHue) =>
            text.Generate("_", GumpFont, LabelArgb(hues, TextHue(serverHue)), 0, UniTextAlign.Left, EntryStyle);

        /// <summary>
        /// Where the caret before char <paramref name="cursor"/> is drawn inside the entry: RenderedText.GetCaretPosition
        /// → GetCaretPosUnicode over the entry's width (StbTextBox.UpdateCaretScreenPosition, DrawCaret). Text wider than
        /// the entry wraps, so a caret past the first line goes down a line, where the entry's height may clip it.
        /// </summary>
        public static (int X, int Y) EntryCaret(UniText text, string value, int cursor, int width) =>
            text.CaretPosition(GumpFont, value, cursor, width, UniTextAlign.Left, EntryStyle);

        /// <summary>
        /// The char a click at (<paramref name="x"/>, <paramref name="y"/>) inside the entry puts the caret before: stb's
        /// LocateCoord (StbTextedit/TextEdit.cs:148-220) over RenderedText.GetLayoutRow (rows from 0 to the picture's
        /// width, as tall as their line) and GetCharWidthAtIndex: past the middle of a char is after it.
        /// </summary>
        public static int EntryIndexAt(UniText text, string value, int width, float x, float y)
        {
            int n = value?.Length ?? 0;
            if (n == 0)
                return 0;
            List<(int CharStart, int CharCount, int Width, int MaxHeight)> lines =
                text.Lines(GumpFont, value, width, UniTextAlign.Left, EntryStyle);
            int i = 0, row = 0, count = 0;
            float baseY = 0;
            while (i < n)
            {
                if (row >= lines.Count || lines[row].CharCount <= 0)
                    return n;
                count = lines[row].CharCount;
                if (i == 0 && y < baseY)
                    return 0;
                if (y < baseY + lines[row].MaxHeight)
                    break;
                i += count;
                baseY += lines[row].MaxHeight;
                row++;
            }
            if (i >= n)
                return n;
            if (x < 0)
                return i;
            if (x < width + 4)
            {
                float prevX = 0;
                for (int k = 0; k < count && i + k < n; k++)
                {
                    float w = text.CharWidth(GumpFont, value[i + k]);
                    if (x < prevX + w)
                        return x < prevX + w / 2 ? i + k : i + k + 1;
                    prevX += w;
                }
            }
            int end = Math.Min(n, i + count);
            return value[end - 1] == '\n' ? end - 1 : end;
        }

        /// <summary>
        /// StbTextBox.DrawSelection (StbTextBox.cs:920-1010): the selected span as one rectangle per line inside the
        /// entry, from 1 down, each its line's height plus 1; a span running to a line's end takes the rest of that line.
        /// </summary>
        public static List<(int X, int Y, int W, int H)> EntrySelection(UniText text, string value, int width, int selectStart,
            int selectEnd)
        {
            var rects = new List<(int, int, int, int)>();
            int start = Math.Min(selectStart, selectEnd), end = Math.Max(selectStart, selectEnd);
            if (string.IsNullOrEmpty(value) || start >= end)
                return rects;
            int drawY = 1, lineStart = 0;
            foreach ((_, int charCount, int lineWidth, int maxHeight) in text.Lines(GumpFont, value, width, UniTextAlign.Left, EntryStyle))
            {
                if (start >= end)
                    break;
                if (start >= lineStart && start < lineStart + charCount)
                {
                    int drawX = Widths(text, value, lineStart, start - lineStart);
                    if (end >= lineStart && end < lineStart + charCount)
                    {
                        rects.Add((drawX, drawY, Widths(text, value, start, end - start), maxHeight + 1));
                        break;
                    }
                    rects.Add((drawX, drawY, lineWidth - drawX, maxHeight + 1));
                    start = lineStart + charCount;
                }
                lineStart += charCount;
                drawY += maxHeight;
            }
            return rects;
        }

        private static int Widths(UniText text, string value, int from, int count)
        {
            int w = 0;
            for (int i = from; i < from + count && i < value.Length; i++)
                w += text.CharWidth(GumpFont, value[i]);
            return w;
        }

        /// <summary>
        /// The char of an edit StbTextBox.OnTextInput would have refused, or -1: one char typed (the text one longer,
        /// otherwise the same) that font 1 gives no width (no glyph, and not a space) is dropped (StbTextBox.cs:884-893).
        /// Pastes of more than one char go in whole.
        /// </summary>
        public static int EntryRefusedChar(UniText text, string before, string after)
        {
            before ??= "";
            after ??= "";
            if (after.Length != before.Length + 1)
                return -1;
            int k = 0;
            while (k < before.Length && before[k] == after[k])
                k++;
            if (string.CompareOrdinal(before, k, after, k + 1, before.Length - k) != 0)
                return -1;
            char c = after[k];
            return text.CharWidth(GumpFont, c) > 0 || c == '\n' ? -1 : k;
        }

        /// <summary>xmfhtmlgumpcolor's and xmfhtmltok's colour part as GumpHelpers reads it (GumpHelpers.cs:319-322, :344-347): 0x7FFF is white.</summary>
        public static int HtmlColorPart(int color) => color == 0x7FFF ? 0x00FFFFFF : color;

        /// <summary>Where an HtmlControl's text starts inside it: 4 in from a background, else at its corner (HtmlControl.cs:279).</summary>
        public static int HtmlTextOffset(bool background) => background ? 4 : 0;

        /// <summary>
        /// The width an HtmlControl wraps its text in (HtmlControl.cs:39, :84, :144): its own, less 16 for a scrollbar and
        /// 8 for a background, and 9 more for a background when no colour is given.
        /// </summary>
        public static int HtmlTextWidth(int width, bool background, bool scrollbar, int color) =>
            width - (scrollbar ? HtmlScrollbarWidth : 0) - (background ? 8 : 0) - (background && color <= 0 ? 9 : 0);

        /// <summary>
        /// An HtmlControl's text colour and starting HTML colour (InternalBuild, HtmlControl.cs:113-149) for
        /// <paramref name="color"/>, the 15-bit colour of xmfhtmlgumpcolor / xmfhtmltok (0x7FFF already made 0xFFFFFF,
        /// GumpHelpers.cs:321, :346) or 0. A colour is the text's from the start (0xFFFFFF, 0xFFFF and 0xFF: white at alpha
        /// 0xFE); with none, text on a background, or with neither background nor scrollbar, is near-black; text with
        /// only a scrollbar is hue 0xFFFF's white.
        /// </summary>
        public static (uint TextArgb, uint HtmlColor) HtmlColors(HueEntry[] hues, bool background, bool scrollbar, int color)
        {
            if (color > 0)
            {
                uint html = color == 0x00FFFFFF || color == 0xFFFF || color == 0xFF
                    ? UniText.FromRgba(0xFFFFFFFE)
                    : Color16.ToArgb32((ushort)color);
                return (TextArgb(hues, 0), html);
            }
            if (!background)
                return (UniText.UnhuedArgb, scrollbar ? UniText.NoHtmlColor : TextBlackArgb);
            return (TextArgb(hues, 0), TextBlackArgb);
        }

        /// <summary>
        /// An htmlgump / xmfhtml* picture, as HtmlControl builds its RenderedText: font 1, HTML, wrapped in
        /// <see cref="HtmlTextWidth"/>, a body bgcolor filling only without a background, and the black border only when
        /// htmlgump's optional ninth part asks for it.
        /// </summary>
        public static UniTextImage HtmlImage(UniText text, HueEntry[] hues, string html, int width, bool background,
            bool scrollbar, int color, bool blackBorder = false)
        {
            (uint argb, uint htmlColor) = HtmlColors(hues, background, scrollbar, color);
            return text.Generate(html, GumpFont, argb, HtmlTextWidth(width, background, scrollbar, color), UniTextAlign.Left,
                blackBorder ? UniTextStyle.BlackBorder : UniTextStyle.None, html: true, htmlColor: htmlColor,
                htmlBackgroundColored: !background);
        }

        /// <summary>
        /// How far an HtmlControl scrolls (HtmlControl.cs:189-190): its text's height less its own, plus 8 with a
        /// background; never below 0.
        /// </summary>
        public static int HtmlScrollMax(int textHeight, int height, bool background) =>
            Math.Max(0, textHeight - height + (background ? 8 : 0));

        /// <summary>Whether a command is one GumpHelpers turns into an HtmlControl.</summary>
        public static bool IsHtml(string name) =>
            name == "htmlgump" || name == "xmfhtmlgump" || name == "xmfhtmlgumpcolor" || name == "xmfhtmltok";

        /// <summary>
        /// An html command's background, scrollbar and colour as GumpHelpers reads them (GumpHelpers.cs:290-381,
        /// HtmlControl.cs:35-37): a background only for exactly 1, a scrollbar for anything but 0.
        /// </summary>
        public static (bool Background, bool Scrollbar, int Color) HtmlParts(GumpCommand c)
        {
            (int background, int scrollbar) = HtmlRawParts(c);
            int color = c.Name == "xmfhtmlgumpcolor" ? HtmlColorPart(c.Arg(7))
                : c.Name == "xmfhtmltok" ? HtmlColorPart(c.Arg(6)) : 0;
            return (background == 1, scrollbar != 0, color);
        }

        // The background and scrollbar parts as sent: htmlgump/xmfhtmlgump/xmfhtmlgumpcolor x y w h text bg sb,
        // xmfhtmltok x y w h bg sb ...
        private static (int Background, int Scrollbar) HtmlRawParts(GumpCommand c) =>
            c.Name == "xmfhtmltok" ? (c.Arg(4), c.Arg(5)) : (c.Arg(5), c.Arg(6));

        /// <summary>
        /// The scrollbar an HtmlControl shows: none for scrollbar part 0, the ScrollFlag for part 2, else the ScrollBar.
        /// htmlgump takes the flag for any 2 (HtmlControl.cs:36-37); the xmfhtml* ones only with a background part other
        /// than 0 too (GumpHelpers.cs:304, :332, :370: gparams[bg] != "0" &amp;&amp; gparams[sb] == "2").
        /// </summary>
        public static HtmlScrollbarKind HtmlScrollbar(GumpCommand c)
        {
            (int background, int scrollbar) = HtmlRawParts(c);
            if (scrollbar == 0)
                return HtmlScrollbarKind.None;
            bool flag = scrollbar == 2 && (c.Name == "htmlgump" || background != 0);
            return flag ? HtmlScrollbarKind.Flag : HtmlScrollbarKind.Bar;
        }

        /// <summary>Where an HtmlControl's scrollbar sits: Width - 14 across, at the top (HtmlControl.cs:177, :182).</summary>
        public static int HtmlScrollbarX(int width) => width - 14;

        /// <summary>
        /// The html command that takes a wheel turn with the pointer at (<paramref name="x"/>, <paramref name="y"/>)
        /// in the gump, or -1. UIManager.OnMouseWheel (UIManager.cs:362-368) gives it only to the control under the
        /// mouse, so an HtmlControl scrolls only while the pointer is inside it; where two overlap, the later (on top)
        /// one. Only commands on page 0 or the page shown count.
        /// </summary>
        public static int HtmlWheelTarget(IList<GumpCommand> commands, int page, float x, float y)
        {
            int target = -1, at = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                if (c.Name == "page")
                {
                    at = c.Arg(0);
                    continue;
                }
                if ((at != 0 && at != page) || !IsHtml(c.Name))
                    continue;
                int cx = c.Arg(0), cy = c.Arg(1);
                if (x >= cx && x < cx + c.Arg(2) && y >= cy && y < cy + c.Arg(3))
                    target = i;
            }
            return target;
        }

        /// <summary>The client hue of gumppic's hue=N: GumpPic adds one, then TransformHue leaves 2 and below unhued.</summary>
        public static int PicHue(int serverHue)
        {
            int hue = (ushort)(serverHue + 1);
            return hue <= 2 ? 0 : hue;
        }

        /// <summary>
        /// The radio group of each radio by command index. TazUO counts group and endgroup alike (group++), whatever
        /// number the group carries, so radios after an endgroup never join those before it.
        /// </summary>
        public static Dictionary<int, int> RadioGroups(IList<GumpCommand> commands)
        {
            var groups = new Dictionary<int, int>();
            int group = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                if (c.Name == "group" || c.Name == "endgroup")
                    group++;
                else if (c.Name == "radio")
                    groups[i] = group;
            }
            return groups;
        }

        /// <summary>
        /// The page a page button shows: TazUO's Gump.Update turns ActivePage 0 into 1, so a button to page 0 (and a
        /// gump with no page button pressed yet) shows page 1.
        /// </summary>
        public static int PageShown(int page) => page == 0 ? 1 : page;

        /// <summary>
        /// What pressing a button or buttontileart (x y up down type param id ...) does, as TazUO's Button(parts):
        /// type 0 switches to page param locally, any other type replies with id (never param).
        /// </summary>
        public static (bool IsPage, int Value) ButtonPress(GumpCommand c) =>
            c.Arg(4) == 0 ? (true, PageShown(c.Arg(5))) : (false, c.Arg(6));

        /// <summary>
        /// The command index of the text entry that takes the keyboard when the gump opens, or -1. TazUO's
        /// GumpHelpers.CreateGump (Network/PacketHandlers/Helpers/GumpHelpers.cs:107, 412-418) calls SetKeyboardFocus
        /// on the first textentry or textentrylimited in layout order, whatever page it is on, and on no other.
        /// </summary>
        public static int FirstTextEntry(IList<GumpCommand> commands)
        {
            for (int i = 0; i < commands.Count; i++)
                if (commands[i].Name == "textentry" || commands[i].Name == "textentrylimited")
                    return i;
            return -1;
        }

        /// <summary>
        /// Whether right-click closes a server gump. TazUO's GumpHelpers.cs:90 starts every server gump with
        /// CanCloseWithRightClick, and only noclose clears it (:427-430); Gump.CloseWithRightClick (Gump.cs:304-309)
        /// then does nothing. nodispose leaves right-click alone.
        /// </summary>
        public static bool ClosesOnRightClick(ServerGump g) => !g.NoClose;

        /// <summary>The button a right-click close answers with: Gump.cs:311-314 sends OnButtonClick(0) for a server gump.</summary>
        public const int RightClickReplyButton = 0;

        /// <summary>
        /// TazUO's CanCloseWithEsc for a server gump: set at GumpHelpers.cs:91, cleared by nodispose (:431-434).
        /// Kept for fidelity only: nothing in TazUO reads it (Control.cs:93 declares it; see EscCloses).
        /// </summary>
        public static bool CanCloseWithEsc(ServerGump g) => !g.NoDispose;

        /// <summary>
        /// Whether Esc closes a server gump: never, nodispose or not. TazUO's Esc (GameSceneInputHandler.cs:1370-1419)
        /// only cancels a target, releases the chat box and stops auto-walk; Gump has no key handler, a text box's Esc
        /// (StbTextBox.cs:521-528) only drops focus and selection, and CanCloseWithEsc is never read.
        /// </summary>
        public static bool EscCloses(ServerGump g) => false;

        /// <summary>Whether a server gump can be dragged: GumpHelpers.cs:89 starts every one CanMove, nomove clears it (:435-438).</summary>
        public static bool CanMove(ServerGump g) => !g.NoMove;
    }

    /// <summary>
    /// TazUO's server gump positions (UIManager._gumpPositionCache), kept by gump type id for the session. A window the
    /// server closes and sends again (a CastleGump tab, pager or Rename: CloseGump&lt;T&gt; + SendGump at the same fixed x, y)
    /// so comes back where the player left it, not at the server's spot.
    /// </summary>
    public sealed class GumpPositions
    {
        private readonly Dictionary<uint, (int X, int Y)> _at = new Dictionary<uint, (int, int)>();

        /// <summary>
        /// Where a gump opens. GumpHelpers.CreateGump (GumpHelpers.cs:76-82): a remembered spot for its type id wins over
        /// the packet's x, y; with none, the packet's x, y is remembered.
        /// </summary>
        public (int X, int Y) Open(uint typeId, int x, int y)
        {
            if (_at.TryGetValue(typeId, out (int X, int Y) at))
                return at;
            _at[typeId] = (x, y);
            return (x, y);
        }

        /// <summary>
        /// A gump going, by a reply (Gump.OnButtonClick, Gump.cs:291-298: any button, right-click's 0 too) or the
        /// server's close (ExtendedCommand.cs:59-66, button 0): a movable one leaves its spot remembered, a nomove one
        /// forgets it, so its next copy opens at the server's x, y.
        /// </summary>
        public void Closed(uint typeId, bool canMove, int x, int y)
        {
            if (canMove)
                _at[typeId] = (x, y);
            else
                _at.Remove(typeId);
        }
    }

    /// <summary>The scrollbar of an HtmlControl: none, TazUO's ScrollBar, or its ScrollFlag (UseFlagScrollbar).</summary>
    public enum HtmlScrollbarKind
    {
        None,
        Bar,
        Flag,
    }

    /// <summary>
    /// The sizes of a scrollbar's gumps: the up and down buttons, the thumb (ScrollBar's slider, ScrollFlag's flag) and
    /// the bar's width, and the control's height the bar is stretched to (HtmlControl.cs:186).
    /// </summary>
    public readonly struct HtmlScrollParts
    {
        public readonly int UpW, UpH, DownW, DownH, ThumbW, ThumbH, BarW, Height;

        public HtmlScrollParts(int upW, int upH, int downW, int downH, int thumbW, int thumbH, int barW, int height)
        {
            UpW = upW;
            UpH = upH;
            DownW = downW;
            DownH = downH;
            ThumbW = thumbW;
            ThumbH = thumbH;
            BarW = barW;
            Height = height;
        }
    }

    /// <summary>
    /// An HtmlControl's scroll position as TazUO's ScrollBarBase keeps it (ScrollBarBase.cs, ScrollBar.cs, ScrollFlag.cs):
    /// a value from 0 to the control's HtmlScrollMax, moved 50 a wheel notch, by holding an arrow, or by dragging the
    /// thumb. The control draws its text ScrollY = Value up (HtmlControl.cs:192, :262, :288-291). Coordinates given to
    /// <see cref="Press"/> and <see cref="Drag"/> are the bar's own, from its corner at (Width - 14, 0).
    /// </summary>
    public sealed class HtmlScroll
    {
        /// <summary>ScrollBarBase.ScrollStep: what one wheel notch moves (ScrollBarBase.cs:92, HtmlControl.cs:227-241).</summary>
        public const int WheelStep = 50;

        // ScrollBarBase's _StepChanger and _StepsDone (Control.cs:19-20), reset to 1 on mouse up.
        private int _stepChanger = 1, _stepsDone = 1;

        public HtmlScroll(HtmlScrollbarKind kind) => Kind = kind;

        public HtmlScrollbarKind Kind { get; }
        public int Value { get; private set; }
        public int Max { get; private set; }
        public bool UpHeld { get; private set; }
        public bool DownHeld { get; private set; }
        public bool Dragging { get; private set; }

        /// <summary>The scroll range (HtmlControl.Update, :247-254); MaxValue never goes below 0 and pulls Value in.</summary>
        public void SetMax(int max)
        {
            Max = Math.Max(0, max);
            if (Value > Max)
                Value = Max;
        }

        private void Set(int value) => Value = Math.Max(0, Math.Min(Max, value));

        /// <summary>
        /// A wheel notch: HtmlControl.OnMouseWheel moves the bar's Value by ScrollStep whether or not the bar shows, so a
        /// control without one still scrolls text taller than itself.
        /// </summary>
        public void Wheel(bool up) => Set(Value + (up ? -WheelStep : WheelStep));

        /// <summary>How far the thumb can travel: ScrollBar.GetScrollableArea (:177-187), ScrollFlag's (:98-103).</summary>
        public int Area(in HtmlScrollParts p) =>
            Kind == HtmlScrollbarKind.Flag ? p.Height - p.ThumbH : p.Height - p.UpH - p.DownH - p.ThumbH;

        /// <summary>ScrollBarBase.GetSliderYPosition (:200-208): the thumb's offset along its travel.</summary>
        public int SliderPosition(in HtmlScrollParts p) =>
            Max == 0 ? 0 : (int)Math.Round(Area(p) * (Value / (float)Max));

        /// <summary>Where the thumb is drawn in the bar: below the up arrow on a ScrollBar (:163-171), at the top on a flag (:69-74).</summary>
        public int ThumbY(in HtmlScrollParts p) => (Kind == HtmlScrollbarKind.Flag ? 0 : p.UpH) + SliderPosition(p);

        /// <summary>
        /// Whether the bar takes a click at (x, y): all of a ScrollBar (ScrollBar.Contains, :247), only the flag of a
        /// ScrollFlag (ScrollFlag.Contains, :144-156; the flag's box here where TazUO checks its pixels).
        /// </summary>
        public bool Contains(in HtmlScrollParts p, int x, int y)
        {
            switch (Kind)
            {
                case HtmlScrollbarKind.Bar:
                    return x >= 0 && x <= p.BarW && y >= 0 && y <= p.Height;
                case HtmlScrollbarKind.Flag:
                    int top = SliderPosition(p);
                    return x >= 0 && x < p.ThumbW && y >= top && y < top + p.ThumbH;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A left press on the bar (ScrollBarBase.OnMouseDown, :161-185): the down arrow, else the up arrow, holds;
        /// anywhere else on it grabs the thumb there. ScrollFlag keeps its arrows' boxes though it draws no arrows
        /// (_showButtons is false, :26): the down box under the flag's own height, the up box at the top (:48-54).
        /// Returns false when the bar is not under the press.
        /// </summary>
        public bool Press(in HtmlScrollParts p, int x, int y)
        {
            if (!Contains(p, x, y))
                return false;
            int downY = Kind == HtmlScrollbarKind.Flag ? p.ThumbH : p.Height - p.DownH;
            if (x >= 0 && x < p.DownW && y >= downY && y < downY + p.DownH)
                DownHeld = true;
            else if (x >= 0 && x < p.UpW && y >= 0 && y < p.UpH)
                UpHeld = true;
            else
            {
                Dragging = true;
                Drag(p, x, y);
            }
            return true;
        }

        /// <summary>
        /// The thumb following the pointer (CalculateByPosition, ScrollBar.cs:199-245, ScrollFlag.cs:105-142): centred on
        /// it, kept in its travel, and the value in proportion.
        /// </summary>
        public void Drag(in HtmlScrollParts p, int x, int y)
        {
            if (!Dragging)
                return;
            int area = Area(p);
            if (area <= 0)
                return;
            y -= Kind == HtmlScrollbarKind.Flag ? p.ThumbH >> 1 : p.UpH + (p.ThumbH >> 1);
            y = Math.Max(0, Math.Min(area, y));
            Set((int)Math.Round(y / (float)area * Max));
        }

        /// <summary>The left button up (ScrollBarBase.OnMouseUp, :187-198): nothing held, the arrow speed back to 1.</summary>
        public void Release()
        {
            UpHeld = DownHeld = Dragging = false;
            _stepChanger = _stepsDone = 1;
        }

        /// <summary>
        /// One update with an arrow held (ScrollBarBase.Update, :120-142): 1 + the step changer, which grows by one every
        /// 8 steps, so a held arrow speeds up.
        /// </summary>
        public void Tick()
        {
            if (!UpHeld && !DownHeld)
                return;
            int step = 1 + _stepChanger;
            Set(Value + (UpHeld ? -step : step));
            if (++_stepsDone % 8 == 0)
                _stepChanger++;
        }
    }

    /// <summary>
    /// The checkboxes, radios and text entries of a server gump and what the 0xB1 reply carries from them, kept per
    /// control (command) as TazUO keeps one Checkbox/RadioButton/StbTextBox each. Gump.OnButtonClick walks the
    /// controls in layout order, every page alike: each checked one adds its switch id, each text box its (id, text).
    /// </summary>
    public sealed class GumpControls
    {
        private readonly List<GumpCommand> _commands;
        private readonly Dictionary<int, int> _radioGroup;
        private readonly Dictionary<int, bool> _checked = new Dictionary<int, bool>();
        private readonly Dictionary<int, string> _texts = new Dictionary<int, string>();

        public GumpControls(ServerGump gump)
        {
            _commands = gump.Commands;
            _radioGroup = ServerGumpWindow.RadioGroups(_commands);
            for (int i = 0; i < _commands.Count; i++)
            {
                GumpCommand c = _commands[i];
                switch (c.Name)
                {
                    case "checkbox":
                    case "radio":
                        // Checkbox(parts): IsChecked = parts[5] == "1". Set before the radio has a parent, so a
                        // group the server sends with two radios on keeps both on.
                        _checked[i] = c.Arg(4) == 1;
                        break;
                    case "textentry":
                    case "textentrylimited":
                        // StbTextBox.SetText cuts the starting text to its limit, as typing is.
                        string text = gump.Line(c.Arg(6));
                        _texts[i] = text.Length > c.TextLimit ? text.Substring(0, c.TextLimit) : text;
                        break;
                }
            }
        }

        public bool IsChecked(int index) => _checked.TryGetValue(index, out bool on) && on;

        /// <summary>
        /// A left click on a checkbox or radio: Checkbox.OnMouseUp flips it, radios included, so a checked radio
        /// clicked again goes off. A radio turning on (RadioButton.HandleClick) turns off the rest of its group.
        /// </summary>
        public void Click(int index)
        {
            if (!_checked.ContainsKey(index))
                return;
            bool on = !_checked[index];
            _checked[index] = on;
            if (on && _radioGroup.TryGetValue(index, out int group))
                foreach (KeyValuePair<int, int> other in _radioGroup)
                    if (other.Value == group && other.Key != index)
                        _checked[other.Key] = false;
        }

        public string Text(int index) => _texts.TryGetValue(index, out string t) ? t : "";

        public void SetText(int index, string text)
        {
            if (_texts.ContainsKey(index))
                _texts[index] = text ?? "";
        }

        /// <summary>The switch ids of the checked checkboxes and radios, in layout order.</summary>
        public List<int> Switches()
        {
            var on = new List<int>();
            for (int i = 0; i < _commands.Count; i++)
                if (IsChecked(i))
                    on.Add(_commands[i].Arg(5));
            return on;
        }

        /// <summary>Every text entry as (id, text), in layout order.</summary>
        public List<(int Id, string Text)> Entries()
        {
            var entries = new List<(int, string)>();
            for (int i = 0; i < _commands.Count; i++)
                if (_texts.TryGetValue(i, out string t))
                    entries.Add((_commands[i].Arg(5), t));
            return entries;
        }
    }

    public sealed partial class ServerGumpWindow
    {

        /// <summary>Commands TazUO turns into a control of the gump (the ones checkertrans and tooltips can reach).</summary>
        public static bool IsControl(string name)
        {
            switch (name)
            {
                case "page":
                case "group":
                case "endgroup":
                case "tooltip":
                case "itemproperty":
                case "noclose":
                case "nodispose":
                case "nomove":
                case "noresize":
                case "mastergump":
                case "togglelimitgumpscale":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Indices of the commands a checkertrans fades to <see cref="CheckerAlpha"/>: GumpHelpers.ApplyTrans makes every
        /// control already added that overlaps it, on page 0 or the checkertrans' own page, half transparent. Controls
        /// added after it are untouched, so an AddAlphaRegion over a background lets the world show through the
        /// background but not through the labels and buttons laid on it afterwards.
        /// </summary>
        public static HashSet<int> CheckerFaded(IList<GumpCommand> commands, Func<GumpCommand, (int X, int Y, int W, int H)> bounds)
        {
            var faded = new HashSet<int>();
            var pages = new int[commands.Count];
            int page = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                pages[i] = page;
                if (c.Name == "page")
                {
                    page = c.Arg(0);
                    continue;
                }
                if (c.Name != "checkertrans")
                    continue;

                int x = c.Arg(0), y = c.Arg(1), x2 = x + c.Arg(2), y2 = y + c.Arg(3);
                for (int j = 0; j < i; j++)
                {
                    GumpCommand o = commands[j];
                    if (!IsControl(o.Name) || o.Name == "checkertrans" || (pages[j] != 0 && pages[j] != page))
                        continue;
                    (int ox, int oy, int ow, int oh) = bounds(o);
                    if (x < ox + ow && ox < x2 && y < oy + oh && oy < y2)
                        faded.Add(j);
                }
            }
            return faded;
        }
    }
}
