using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A gump the server sent (menus, dialogs, help, vendors' extras...), drawn from its layout commands.
    /// Pages switch locally; reply buttons answer the server with the switches and text entries.
    /// Right-click closes it (answering button 0) unless the server marked it noclose; Esc never does, as in TazUO.
    /// </summary>
    public sealed partial class ServerGumpWindow : UiWindow
    {
        private readonly ServerGump _gump;
        private readonly GumpControls _controls;
        private readonly Dictionary<int, Vector2> _scroll = new Dictionary<int, Vector2>();
        private HashSet<int> _faded; // commands a later checkertrans covers, worked out on the first draw
        private int _page = 1;
        private int _focusEntry; // the text entry still to be given the keyboard (FirstTextEntry), or -1 once done
        private bool _answered, _ownsKeyboard;
        private GUIStyle _entry;

        // Each text, croppedtext and html command's rendered text (null: nothing to draw), made on first use; the
        // server replaces the whole gump to change one, so they live as long as the window.
        private readonly Dictionary<GumpCommand, UiImage> _texts = new Dictionary<GumpCommand, UiImage>();

        public ServerGumpWindow(ServerGump gump)
        {
            _gump = gump;
            _controls = new GumpControls(gump);
            _focusEntry = FirstTextEntry(gump.Commands);
            Rect = new Rect(gump.X, gump.Y, 1, 1);
        }

        public override void OnOpened()
        {
            // Where the last gump of this type was left (GumpPositions), else the packet's x, y.
            (int x, int y) = Ui.ServerGumpPositions.Open(_gump.TypeId, _gump.X, _gump.Y);
            Rect = new Rect(x, y, 1, 1) { size = MeasureSize() };
        }

        public ServerGump Gump => _gump;
        public override string Key => $"gump:{_gump.TypeId}:{_gump.Serial}";
        public override bool CanClose => ClosesOnRightClick(_gump);
        public override bool ClosesOnEsc => EscCloses(_gump);
        public override bool Movable => CanMove(_gump);

        // TazUO puts a server gump exactly where it opens, even over another window.
        public override bool StepsAsideOnOpen => false;

        // The server can close or replace it; then this window goes too.
        public override bool StillValid => Session.World.Gumps.Contains(_gump);

        public override void OnClosed()
        {
            if (!_answered && Session.World.Gumps.Contains(_gump))
                Reply(0);
            // Replied to or closed by the server: either way TazUO remembers (or, nomove, forgets) where it was.
            Ui.ServerGumpPositions.Closed(_gump.TypeId, Movable, (int)Math.Round(Rect.x), (int)Math.Round(Rect.y));
            // Give the keyboard back to the world, as TazUO's focused StbTextBox goes with its gump.
            if (_ownsKeyboard)
                GUIUtility.keyboardControl = 0;
            foreach (UiImage img in _texts.Values)
                GumpArt.Release(img);
            _texts.Clear();
            foreach ((string _, UiImage img) in _entryTexts.Values)
                GumpArt.Release(img);
            _entryTexts.Clear();
            foreach (UiImage img in _entryCarets.Values)
                GumpArt.Release(img);
            _entryCarets.Clear();
        }

        private string EntryName(int index) => $"{Key}:entry:{index}";

        private void Reply(int button)
        {
            _answered = true;
            Session.ReplyGump(_gump, button, _controls.Switches(), _controls.Entries());
        }

        public override void Draw()
        {
            EnsureStyles();
            _faded ??= CheckerFaded(_gump.Commands, Bounds);
            int page = 0;
            List<GumpCommand> commands = _gump.Commands;
            for (int i = 0; i < commands.Count; i++)
            {
                GumpCommand c = commands[i];
                if (c.Name == "page")
                {
                    page = c.Arg(0);
                    continue;
                }
                if (page != 0 && page != _page)
                    continue;
                GUI.color = _faded.Contains(i) ? new Color(1f, 1f, 1f, CheckerAlpha) : Color.white;
                bool closed = DrawCommand(c, i);
                GUI.color = Color.white;
                if (closed)
                    return; // the window closed itself
            }
            if (Event.current.type == EventType.Repaint)
            {
                Hovered = _hoverInGump;
                HoveredText = _hoverTextInGump;
                _ownsKeyboard = GUI.GetNameOfFocusedControl().StartsWith(Key + ":entry:", StringComparison.Ordinal);
                _hoverInGump = 0;
                _hoverTextInGump = null;
            }
        }

        // The element drawn last: itemproperty and tooltip describe it, as in TazUO, where they attach to the
        // control added just before them.
        private Rect _lastArea;
        private uint _hoverInGump;
        private string _hoverTextInGump;
        private Rect _hoverTextArea;

        /// <summary>Draws one command (the index-th of the layout); returns true if it closed the gump.</summary>
        private bool DrawCommand(GumpCommand c, int index)
        {
            switch (c.Name)
            {
                case "resizepic":
                    DrawResizePic(c.Arg(2), c.Arg(0), c.Arg(1), c.Arg(3), c.Arg(4));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), c.Arg(3), c.Arg(4));
                    break;

                case "gumppic":
                case "gumppichued":
                case "gumppicphued":
                {
                    UiImage img = Art.Gump(c.Arg(2), PicHue(c.Named("hue")));
                    DrawImage(img, c.Arg(0), c.Arg(1));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), img?.Width ?? 0, img?.Height ?? 0);
                    break;
                }

                case "picinpic":
                case "picinpichued":
                case "picinpicphued":
                {
                    // x y gump width height sx sy: the width x height part of the gump starting at (sx, sy).
                    var r = new Rect(c.Arg(0), c.Arg(1), c.Arg(3), c.Arg(4));
                    UiImage img = Art.Gump(c.Arg(2));
                    if (img != null && r.width > 0 && r.height > 0)
                    {
                        float sx = c.Arg(5), sy = c.Arg(6);
                        // Textures are stored bottom-up, so v counts from the bottom of the picture.
                        GUI.DrawTextureWithTexCoords(r, img.Texture, new Rect(sx / img.Width, 1f - (sy + r.height) / img.Height,
                            r.width / img.Width, r.height / img.Height));
                    }
                    _lastArea = r;
                    break;
                }

                case "gumppictiled":
                    DrawTiled(Art.Gump(c.Arg(4)), c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
                    break;

                case "tilepic":
                case "tilepichue":
                {
                    UiImage img = Art.Item(c.Arg(2), c.Name == "tilepichue" ? c.Arg(3) : 0);
                    DrawImage(img, c.Arg(0), c.Arg(1));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), img?.Width ?? 0, img?.Height ?? 0);
                    break;
                }

                case "itemproperty":
                    // The tooltip of an item (by serial) belongs to the element drawn just before (an auction lot's picture).
                    if (Event.current.type == EventType.Repaint && _lastArea.Contains(Event.current.mousePosition))
                        _hoverInGump = (uint)c.Arg(0);
                    break;

                case "tooltip":
                    // tooltip number [@args@]: a cliloc shown over the element drawn just before; a second tooltip
                    // on the same element goes under the first, as TazUO appends it on a new line.
                    if (Event.current.type == EventType.Repaint && _lastArea.Contains(Event.current.mousePosition))
                    {
                        string text = ClilocText(c.Arg(0), c.ClilocArgs);
                        _hoverTextInGump = _hoverTextInGump != null && _hoverTextArea == _lastArea ? _hoverTextInGump + "\n" + text : text;
                        _hoverTextArea = _lastArea;
                    }
                    break;

                case "checkertrans":
                    // TazUO's CheckerTrans: 50% black; what it covers is already faded to half (CheckerFaded).
                    GUI.color = new Color(0, 0, 0, CheckerAlpha);
                    GUI.DrawTexture(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3)), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    break;

                case "text":
                {
                    // TazUO's Label: the whole picture at x, y, and the label as big as it.
                    UiImage img = TextImage(c);
                    DrawImage(img, c.Arg(0), c.Arg(1));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), img?.Width ?? 0, img?.Height ?? 0);
                    break;
                }

                case "croppedtext":
                    // CroppedText draws its whole picture (the box's width + 4) unclipped; the crop already fit the text.
                    DrawImage(TextImage(c), c.Arg(0), c.Arg(1));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
                    break;

                case "htmlgump":
                case "xmfhtmlgump":
                case "xmfhtmlgumpcolor":
                case "xmfhtmltok":
                {
                    (bool background, bool scrollbar, _) = HtmlParts(c);
                    DrawHtml(c, TextImage(c), background, scrollbar);
                    break;
                }

                case "button":
                case "buttontileart":
                    if (DrawButton(c))
                        return true;
                    break;

                case "checkbox":
                case "radio":
                    DrawSwitch(c, index);
                    break;

                case "textentry":
                case "textentrylimited":
                    DrawEntry(c, index);
                    break;
            }
            return false;
        }

        /// <summary>
        /// A text entry as TazUO's StbTextBox shows it: its text in unicode font 1 (EntryImage), wrapped in its width and
        /// clipped to its width x height, the selection behind and the "_" caret on it while it has the keyboard. The
        /// editing itself (keys, clipboard, undo, focus) stays IMGUI's TextField, laid over the entry with its own text,
        /// cursor and selection made invisible; clicks and drags place the caret by font 1's widths (EntryIndexAt).
        /// Still IMGUI's, not stb's: double-click word selection, Up/Down/Home/End over wrapped lines, and the char
        /// limit (TextField's maxLength = TextLimit, where StbTextBox takes 255 for a plain textentry).
        /// </summary>
        private void DrawEntry(GumpCommand c, int index)
        {
            var r = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
            int width = c.Arg(2);
            string name = EntryName(index);
            Event e = Event.current;
            EventType type = e.type;
            Vector2 at = e.mousePosition - r.position;
            bool click = type == EventType.MouseDown && e.button == 0 && e.clickCount == 1 && r.Contains(e.mousePosition);
            string before = _controls.Text(index);

            GUISettings settings = GUI.skin.settings;
            Color cursorColor = settings.cursorColor, selectionColor = settings.selectionColor;
            settings.cursorColor = settings.selectionColor = Color.clear;
            GUI.SetNextControlName(name);
            string after = GUI.TextField(r, before, c.TextLimit, _entry);
            settings.cursorColor = cursorColor;
            settings.selectionColor = selectionColor;

            // The first entry takes the keyboard as the gump opens (FirstTextEntry). IMGUI can only focus a
            // drawn field, so one on a page not shown yet takes it when its page first shows.
            if (index == _focusEntry)
            {
                GUI.FocusControl(name);
                _focusEntry = -1;
            }

            TextEditor editor = GUI.GetNameOfFocusedControl() == name
                ? (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl)
                : null;
            int refused = EntryRefusedChar(Art.Text, before, after);
            if (refused >= 0)
            {
                after = before;
                if (editor != null)
                {
                    editor.text = before;
                    editor.cursorIndex = editor.selectIndex = refused;
                }
            }
            _controls.SetText(index, after);
            string text = _controls.Text(index);

            if (editor != null)
            {
                if (click)
                    editor.cursorIndex = editor.selectIndex = EntryIndexAt(Art.Text, text, width, at.x, at.y);
                else if (type == EventType.MouseDrag && GUIUtility.hotControl == GUIUtility.keyboardControl)
                    editor.cursorIndex = EntryIndexAt(Art.Text, text, width, at.x, at.y);
            }

            if (type == EventType.Repaint)
            {
                GUI.BeginGroup(r);
                if (editor != null)
                {
                    Color tint = GUI.color;
                    GUI.color = new Color(EntrySelectionColor.r, EntrySelectionColor.g, EntrySelectionColor.b,
                        EntrySelectionColor.a * tint.a);
                    foreach ((int x, int y, int w, int h) in EntrySelection(Art.Text, text, width, editor.selectIndex, editor.cursorIndex))
                        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
                    GUI.color = tint;
                }
                DrawImage(EntryText(c, index, text), 0, 0);
                if (editor != null)
                {
                    (int x, int y) = EntryCaret(Art.Text, text, editor.cursorIndex, width);
                    DrawImage(EntryCaretTexture(c, index), x, y);
                }
                GUI.EndGroup();
            }
            _lastArea = r;
        }

        // StbTextBox's SELECTION_COLOR (packed 0x80a06020: r 0x20, g 0x60, b 0xa0, a 0x80) drawn at half alpha.
        private static readonly Color EntrySelectionColor = new Color(0x20 / 255f, 0x60 / 255f, 0xa0 / 255f, 0x80 / 255f * 0.5f);

        // Each entry's picture for the text it last drew, and its caret; remade when the text changes.
        private readonly Dictionary<int, (string Text, UiImage Image)> _entryTexts = new Dictionary<int, (string, UiImage)>();
        private readonly Dictionary<int, UiImage> _entryCarets = new Dictionary<int, UiImage>();

        private UiImage EntryText(GumpCommand c, int index, string text)
        {
            if (_entryTexts.TryGetValue(index, out (string Text, UiImage Image) drawn) && drawn.Text == text)
                return drawn.Image;
            GumpArt.Release(drawn.Image);
            UiImage img = GumpArt.TextImage(EntryImage(Art.Text, Art.Data.Hues.Entries, text, c.Arg(4), c.Arg(2)));
            _entryTexts[index] = (text, img);
            return img;
        }

        private UiImage EntryCaretTexture(GumpCommand c, int index)
        {
            if (!_entryCarets.TryGetValue(index, out UiImage img))
                _entryCarets[index] = img = GumpArt.TextImage(EntryCaretImage(Art.Text, Art.Data.Hues.Entries, c.Arg(4)));
            return img;
        }

        private bool DrawButton(GumpCommand c)
        {
            int x = c.Arg(0), y = c.Arg(1);
            UiImage up = Art.Gump(c.Arg(2)), down = Art.Gump(c.Arg(3));
            if (up == null)
                return false;
            var r = new Rect(x, y, up.Width, up.Height);
            _lastArea = r;
            bool pressed = r.Contains(Event.current.mousePosition) && Input.GetMouseButton(0);
            DrawImage(pressed && down != null ? down : up, x, y);
            if (c.Name == "buttontileart")
                DrawImage(Art.Item(c.Arg(7), c.Arg(8)), x + c.Arg(9), y + c.Arg(10));

            if (!GUI.Button(r, GUIContent.none, GUIStyle.none))
                return false;
            (bool isPage, int value) = ButtonPress(c);
            if (isPage)
            {
                _page = value; // page switch, local only
                return false;
            }
            Reply(value);
            Close();
            return true;
        }

        private void DrawSwitch(GumpCommand c, int index)
        {
            UiImage img = Art.Gump(_controls.IsChecked(index) ? c.Arg(3) : c.Arg(2));
            if (img == null)
                return;
            var r = new Rect(c.Arg(0), c.Arg(1), img.Width, img.Height);
            _lastArea = r;
            DrawImage(img, r.x, r.y);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                _controls.Click(index);
        }

        /// <summary>
        /// The rendered text of a text, croppedtext or html command (UniText, TazUO's FontsLoader port): the hue's
        /// TextColorCell shade with the black border, laid out in unicode font 1's own widths and line heights.
        /// </summary>
        private UiImage TextImage(GumpCommand c)
        {
            if (_texts.TryGetValue(c, out UiImage img))
                return img;
            HueEntry[] hues = Art.Data.Hues.Entries;
            UniTextImage text = null;
            switch (c.Name)
            {
                case "text":
                    text = LabelImage(Art.Text, hues, _gump.Line(c.Arg(3)), c.Arg(2));
                    break;
                case "croppedtext":
                    text = CroppedImage(Art.Text, hues, _gump.Line(c.Arg(5)), c.Arg(4), c.Arg(2));
                    break;
                case "htmlgump":
                case "xmfhtmlgump":
                case "xmfhtmlgumpcolor":
                case "xmfhtmltok":
                {
                    (bool background, bool scrollbar, int color) = HtmlParts(c);
                    string html = c.Name == "htmlgump" ? _gump.Line(c.Arg(4))
                        : c.Name == "xmfhtmltok" ? ClilocText(c.Arg(7), c.ClilocArgs)
                        : ClilocText(c.Arg(4), null);
                    // htmlgump's optional ninth part 1 asks for the black border (HtmlControl.cs:42-43).
                    text = HtmlImage(Art.Text, hues, html, c.Arg(2), background, scrollbar, color,
                        c.Name == "htmlgump" && c.Arg(7) == 1);
                    break;
                }
            }
            img = GumpArt.TextImage(text);
            _texts[c] = img;
            return img;
        }

        /// <summary>
        /// An html command's background, scrollbar and colour as GumpHelpers reads them (GumpHelpers.cs:290-381,
        /// HtmlControl.cs:35-37): a background only for exactly 1, a scrollbar for anything but 0.
        /// </summary>
        private static (bool Background, bool Scrollbar, int Color) HtmlParts(GumpCommand c)
        {
            switch (c.Name)
            {
                case "xmfhtmlgumpcolor":
                    return (c.Arg(5) == 1, c.Arg(6) != 0, HtmlColorPart(c.Arg(7)));
                case "xmfhtmltok":
                    return (c.Arg(4) == 1, c.Arg(5) != 0, HtmlColorPart(c.Arg(6)));
                default:
                    return (c.Arg(5) == 1, c.Arg(6) != 0, 0);
            }
        }

        /// <summary>
        /// TazUO's HtmlControl: the background (ResizePic 0x2486, short of the scrollbar), and the text 4 in from it,
        /// clipped to the control and scrolled by up to HtmlScrollMax.
        /// </summary>
        private void DrawHtml(GumpCommand c, UiImage text, bool background, bool scrollbar)
        {
            var r = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
            _lastArea = r;
            if (background)
                DrawResizePic(HtmlBackgroundGump, r.x, r.y, r.width - (scrollbar ? HtmlScrollbarWidth : 0), r.height);
            if (text == null)
                return;

            int offset = HtmlTextOffset(background);
            if (!scrollbar)
            {
                GUI.BeginGroup(r);
                DrawImage(text, offset, offset);
                GUI.EndGroup();
                return;
            }

            // The scroll range is TazUO's: the text's height less the control's, plus 8 with a background.
            int key = c.Arg(0) * 10007 + c.Arg(1);
            float content = r.height + HtmlScrollMax(text.Height, (int)r.height, background);
            _scroll.TryGetValue(key, out Vector2 pos);
            pos = GUI.BeginScrollView(r, pos, new Rect(0, 0, r.width - HtmlScrollbarWidth, content), false, false,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            DrawImage(text, offset, offset);
            GUI.EndScrollView();
            _scroll[key] = pos;
        }

        private string ClilocText(int number, string args)
        {
            if (Ui.Cliloc == null)
                return $"#{number}";
            return string.IsNullOrEmpty(args) ? Ui.Cliloc.Get(number) ?? $"#{number}" : Ui.Cliloc.Format(number, args);
        }

        /// <summary>Size from the layout: the furthest extent of any drawn command.</summary>
        private Vector2 MeasureSize()
        {
            float w = 50, h = 50;
            foreach (GumpCommand c in _gump.Commands)
            {
                (int x, int y, int cw, int ch) = Bounds(c);
                if (cw == 0 && ch == 0)
                    continue;
                w = Math.Max(w, x + cw);
                h = Math.Max(h, y + ch);
            }
            return new Vector2(w, h);
        }

        /// <summary>Where a command draws (0 x 0 for ones that draw nothing).</summary>
        private (int X, int Y, int W, int H) Bounds(GumpCommand c)
        {
            int x = c.Arg(0), y = c.Arg(1);
            switch (c.Name)
            {
                case "resizepic":
                case "picinpic":
                case "picinpichued":
                case "picinpicphued":
                    return (x, y, c.Arg(3), c.Arg(4));
                case "gumppictiled":
                case "checkertrans":
                case "htmlgump":
                case "xmfhtmlgump":
                case "xmfhtmlgumpcolor":
                case "xmfhtmltok":
                case "croppedtext":
                case "textentry":
                case "textentrylimited":
                    return (x, y, c.Arg(2), c.Arg(3));
                case "gumppic":
                case "gumppichued":
                case "gumppicphued":
                {
                    UiImage img = Art.Gump(c.Arg(2));
                    return (x, y, img?.Width ?? 0, img?.Height ?? 0);
                }
                case "tilepic":
                case "tilepichue":
                {
                    UiImage img = Art.Item(c.Arg(2));
                    return (x, y, img?.Width ?? 0, img?.Height ?? 0);
                }
                case "button":
                case "buttontileart":
                case "checkbox":
                case "radio":
                {
                    UiImage img = Art.Gump(c.Arg(2));
                    return (x, y, img?.Width ?? 20, img?.Height ?? 20);
                }
                case "text":
                {
                    // TazUO's Label is the size of its rendered text.
                    UiImage img = TextImage(c);
                    return (x, y, img?.Width ?? 0, img?.Height ?? 0);
                }
                default:
                    return (x, y, 0, 0);
            }
        }

        private void EnsureStyles()
        {
            if (_entry != null)
                return;
            // Only IMGUI's editing is used; DrawEntry draws the text in font 1.
            _entry = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            foreach (GUIStyleState state in new[] { _entry.normal, _entry.focused, _entry.hover, _entry.active,
                         _entry.onNormal, _entry.onFocused, _entry.onHover, _entry.onActive })
            {
                state.background = null;
                state.textColor = Color.clear;
            }
        }
    }
}
