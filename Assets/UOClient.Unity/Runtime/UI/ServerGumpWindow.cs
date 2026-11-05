using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

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
        private GUIStyle _text, _html, _entry;

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
                    var content = new GUIContent(_gump.Line(c.Arg(3)));
                    DrawLabel(new Rect(c.Arg(0), c.Arg(1), 600, 22), content, TextHue(c.Arg(2)));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), _text.CalcSize(content).x, 22);
                    break;
                }

                case "croppedtext":
                    // The border stays inside the box, as TazUO's is drawn into the cropped texture.
                    GUI.BeginGroup(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3)));
                    DrawLabel(new Rect(0, 0, c.Arg(2), c.Arg(3)), new GUIContent(_gump.Line(c.Arg(5))), TextHue(c.Arg(4)));
                    GUI.EndGroup();
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
                    break;

                case "htmlgump":
                    DrawHtml(c, _gump.Line(c.Arg(4)), c.Arg(5) != 0, c.Arg(6) != 0, null);
                    break;

                case "xmfhtmlgump":
                    DrawHtml(c, ClilocText(c.Arg(4), null), c.Arg(5) != 0, c.Arg(6) != 0, null);
                    break;

                case "xmfhtmlgumpcolor":
                    DrawHtml(c, ClilocText(c.Arg(4), null), c.Arg(5) != 0, c.Arg(6) != 0, Rgb15(c.Arg(7)));
                    break;

                case "xmfhtmltok":
                    DrawHtml(c, ClilocText(c.Arg(7), c.ClilocArgs), c.Arg(4) != 0, c.Arg(5) != 0, Rgb15(c.Arg(6)));
                    break;

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
                {
                    _entry.normal.textColor = _entry.focused.textColor = Art.GumpTextColor(TextHue(c.Arg(4)));
                    GUI.SetNextControlName(EntryName(index));
                    _controls.SetText(index, GUI.TextField(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), Math.Max(18, c.Arg(3))),
                        _controls.Text(index), c.TextLimit, _entry));
                    // The first entry takes the keyboard as the gump opens (FirstTextEntry). IMGUI can only focus a
                    // drawn field, so one on a page not shown yet takes it when its page first shows.
                    if (index == _focusEntry)
                    {
                        GUI.FocusControl(EntryName(index));
                        _focusEntry = -1;
                    }
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
                    break;
                }
            }
            return false;
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
        /// A label as TazUO's unicode font draws it: the hue's TextColorCell shade, with a one-pixel black border on
        /// all eight sides unless the text is itself black (TextArgb, HasBlackBorder).
        /// </summary>
        private void DrawLabel(Rect r, GUIContent content, int clientHue)
        {
            uint argb = Art.GumpTextArgb(clientHue);
            if (HasBlackBorder(argb))
            {
                _text.normal.textColor = GumpArt.ToColor(TextBlackArgb);
                foreach ((int dx, int dy) in BlackBorderOffsets)
                    GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), content, _text);
            }
            _text.normal.textColor = GumpArt.ToColor(argb);
            GUI.Label(r, content, _text);
        }

        private void DrawHtml(GumpCommand c, string html, bool background, bool scrollbar, Color? color)
        {
            var r = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
            _lastArea = r;
            if (background)
                DrawResizePic(HtmlBackgroundGump, r.x, r.y, r.width - (scrollbar ? HtmlScrollbarWidth : 0), r.height);

            string rich = GumpHtml.ToRichText(html, out bool centered);
            _html.alignment = centered ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            // Uncolored HTML is TazUO's 0x010101 (HtmlControl.InternalBuild). Its scrolling-without-background case
            // (hue 0xFFFF) is left the same here: not checked against TazUO's font renderer.
            _html.normal.textColor = color ?? new Color(1f / 255, 1f / 255, 1f / 255);
            var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            if (!scrollbar)
            {
                GUI.Label(inner, rich, _html);
                return;
            }

            int key = c.Arg(0) * 10007 + c.Arg(1);
            float height = _html.CalcHeight(new GUIContent(rich), inner.width - 16);
            _scroll.TryGetValue(key, out Vector2 pos);
            pos = GUI.BeginScrollView(inner, pos, new Rect(0, 0, inner.width - 16, Math.Max(height, inner.height)));
            GUI.Label(new Rect(0, 0, inner.width - 16, height), rich, _html);
            GUI.EndScrollView();
            _scroll[key] = pos;
        }

        private string ClilocText(int number, string args)
        {
            if (Ui.Cliloc == null)
                return $"#{number}";
            return string.IsNullOrEmpty(args) ? Ui.Cliloc.Get(number) ?? $"#{number}" : Ui.Cliloc.Format(number, args);
        }

        private static Color? Rgb15(int c)
        {
            if (c == 0)
                return null;
            return new Color(((c >> 10) & 0x1F) / 31f, ((c >> 5) & 0x1F) / 31f, (c & 0x1F) / 31f, 1f);
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
                    // The label's own width, as TazUO's Label sizes to its text (a guess before styles exist).
                    return (x, y, _text != null ? (int)_text.CalcSize(new GUIContent(_gump.Line(c.Arg(3)))).x : 120, 20);
                default:
                    return (x, y, 0, 0);
            }
        }

        private void EnsureStyles()
        {
            if (_text != null)
                return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset() };
            _html = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true, padding = new RectOffset() };
            _entry = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            _entry.normal.background = _entry.focused.background = _entry.hover.background = null;
        }
    }
}
