using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A gump the server sent (menus, dialogs, help, vendors' extras...), drawn from its layout commands.
    /// Pages switch locally; reply buttons answer the server with the switches and text entries.
    /// Right-click closes it (answering button 0) unless the server marked it noclose.
    /// </summary>
    public sealed class ServerGumpWindow : UiWindow
    {
        private readonly ServerGump _gump;
        private readonly Dictionary<int, bool> _switches = new Dictionary<int, bool>();
        private readonly Dictionary<int, int> _radioGroup = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _texts = new Dictionary<int, string>();
        private readonly Dictionary<int, Vector2> _scroll = new Dictionary<int, Vector2>();
        private int _page = 1;
        private bool _answered;
        private GUIStyle _text, _html, _entry;

        public ServerGumpWindow(ServerGump gump)
        {
            _gump = gump;

            // Initial state of switches and text entries, and the first page shown.
            int group = 0;
            bool hasPage1 = false;
            foreach (GumpCommand c in gump.Commands)
            {
                switch (c.Name)
                {
                    case "page":
                        hasPage1 |= c.Arg(0) == 1;
                        break;
                    case "group":
                        group = c.Arg(0);
                        break;
                    case "endgroup":
                        group = 0;
                        break;
                    case "checkbox":
                        _switches[c.Arg(5)] = c.Arg(4) != 0;
                        break;
                    case "radio":
                        _switches[c.Arg(5)] = c.Arg(4) != 0;
                        _radioGroup[c.Arg(5)] = group;
                        break;
                    case "textentry":
                    case "textentrylimited":
                        _texts[c.Arg(5)] = gump.Line(c.Arg(6));
                        break;
                }
            }
            _page = hasPage1 ? 1 : 0;
            Rect = new Rect(gump.X, gump.Y, 1, 1);
        }

        public override void OnOpened() => Rect.size = MeasureSize();

        public ServerGump Gump => _gump;
        public override string Key => $"gump:{_gump.TypeId}:{_gump.Serial}";
        public override bool CanClose => !_gump.NoClose;

        // The server can close or replace it; then this window goes too.
        public override bool StillValid => Session.World.Gumps.Contains(_gump);

        public override void OnClosed()
        {
            if (!_answered && Session.World.Gumps.Contains(_gump))
                Reply(0);
        }

        private void Reply(int button)
        {
            _answered = true;
            var on = new List<int>();
            foreach (KeyValuePair<int, bool> s in _switches)
                if (s.Value)
                    on.Add(s.Key);
            var texts = new List<(int, string)>();
            foreach (KeyValuePair<int, string> t in _texts)
                texts.Add((t.Key, t.Value ?? ""));
            Session.ReplyGump(_gump, button, on, texts);
        }

        public override void Draw()
        {
            EnsureStyles();
            int page = 0;
            foreach (GumpCommand c in _gump.Commands)
            {
                if (c.Name == "page")
                {
                    page = c.Arg(0);
                    continue;
                }
                if (page != 0 && page != _page)
                    continue;
                if (DrawCommand(c))
                    return; // the window closed itself
            }
            if (Event.current.type == EventType.Repaint)
            {
                Hovered = _hoverInGump;
                _hoverInGump = 0;
            }
        }

        private Rect _lastArea;
        private uint _hoverInGump;

        /// <summary>Draws one command; returns true if it closed the gump.</summary>
        private bool DrawCommand(GumpCommand c)
        {
            switch (c.Name)
            {
                case "resizepic":
                    DrawResizePic(c.Arg(2), c.Arg(0), c.Arg(1), c.Arg(3), c.Arg(4));
                    break;

                case "gumppic":
                {
                    UiImage img = Art.Gump(c.Arg(2), c.Named("hue"));
                    DrawImage(img, c.Arg(0), c.Arg(1));
                    _lastArea = new Rect(c.Arg(0), c.Arg(1), img?.Width ?? 0, img?.Height ?? 0);
                    break;
                }

                case "gumppictiled":
                    DrawTiled(Art.Gump(c.Arg(4)), c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
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

                case "checkertrans":
                    // Classic clients make the area see-through; a light veil reads the same here.
                    GUI.color = new Color(0, 0, 0, 0.25f);
                    GUI.DrawTexture(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3)), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    break;

                case "text":
                    _text.normal.textColor = Art.HueColor(c.Arg(2), new Color(0.1f, 0.1f, 0.1f));
                    GUI.Label(new Rect(c.Arg(0), c.Arg(1), 600, 22), _gump.Line(c.Arg(3)), _text);
                    break;

                case "croppedtext":
                    _text.normal.textColor = Art.HueColor(c.Arg(4), new Color(0.1f, 0.1f, 0.1f));
                    GUI.Label(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3)), _gump.Line(c.Arg(5)), _text);
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
                {
                    string args = c.Words.Length > 8 ? c.Words[8].Trim('@') : "";
                    DrawHtml(c, ClilocText(c.Arg(7), args), c.Arg(4) != 0, c.Arg(5) != 0, Rgb15(c.Arg(6)));
                    break;
                }

                case "button":
                case "buttontileart":
                    if (DrawButton(c))
                        return true;
                    break;

                case "checkbox":
                case "radio":
                    DrawSwitch(c);
                    break;

                case "textentry":
                case "textentrylimited":
                {
                    int id = c.Arg(5);
                    _entry.normal.textColor = _entry.focused.textColor = Art.HueColor(c.Arg(4), new Color(0.1f, 0.1f, 0.1f));
                    string value = _texts.TryGetValue(id, out string v) ? v : "";
                    int max = c.Name == "textentrylimited" ? Math.Max(1, c.Arg(7)) : 239;
                    _texts[id] = GUI.TextField(new Rect(c.Arg(0), c.Arg(1), c.Arg(2), Math.Max(18, c.Arg(3))), value, max, _entry);
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
            bool pressed = r.Contains(Event.current.mousePosition) && Input.GetMouseButton(0);
            DrawImage(pressed && down != null ? down : up, x, y);
            if (c.Name == "buttontileart")
                DrawImage(Art.Item(c.Arg(7), c.Arg(8)), x + c.Arg(9), y + c.Arg(10));

            if (!GUI.Button(r, GUIContent.none, GUIStyle.none))
                return false;
            int type = c.Arg(4), param = c.Arg(5), id = c.Arg(6);
            if (type == 0)
            {
                _page = param; // page switch, local only
                return false;
            }
            Reply(id);
            Close();
            return true;
        }

        private void DrawSwitch(GumpCommand c)
        {
            int id = c.Arg(5);
            bool on = _switches.TryGetValue(id, out bool v) && v;
            UiImage img = Art.Gump(on ? c.Arg(3) : c.Arg(2));
            if (img == null)
                return;
            var r = new Rect(c.Arg(0), c.Arg(1), img.Width, img.Height);
            DrawImage(img, r.x, r.y);
            if (!GUI.Button(r, GUIContent.none, GUIStyle.none))
                return;

            if (c.Name == "radio")
            {
                int group = _radioGroup.TryGetValue(id, out int g) ? g : 0;
                foreach (KeyValuePair<int, int> other in _radioGroup)
                    if (other.Value == group)
                        _switches[other.Key] = false;
                _switches[id] = true;
            }
            else
            {
                _switches[id] = !on;
            }
        }

        private void DrawHtml(GumpCommand c, string html, bool background, bool scrollbar, Color? color)
        {
            var r = new Rect(c.Arg(0), c.Arg(1), c.Arg(2), c.Arg(3));
            if (background)
                DrawResizePic(0x0BB8, r.x, r.y, r.width, r.height);

            string rich = GumpHtml.ToRichText(html, out bool centered);
            _html.alignment = centered ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;
            _html.normal.textColor = color ?? (background ? new Color(0.05f, 0.05f, 0.05f) : new Color(0.08f, 0.06f, 0.04f));
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
                float x = c.Arg(0), y = c.Arg(1), cw = 0, ch = 0;
                switch (c.Name)
                {
                    case "resizepic": cw = c.Arg(3); ch = c.Arg(4); break;
                    case "gumppictiled":
                    case "checkertrans":
                    case "htmlgump":
                    case "xmfhtmlgump":
                    case "xmfhtmlgumpcolor":
                    case "xmfhtmltok":
                    case "croppedtext":
                    case "textentry":
                    case "textentrylimited":
                        cw = c.Arg(2); ch = c.Arg(3); break;
                    case "gumppic":
                    {
                        UiImage img = Art.Gump(c.Arg(2));
                        cw = img?.Width ?? 0;
                        ch = img?.Height ?? 0;
                        break;
                    }
                    case "button":
                    case "checkbox":
                    case "radio":
                    {
                        UiImage img = Art.Gump(c.Arg(2));
                        cw = img?.Width ?? 20;
                        ch = img?.Height ?? 20;
                        break;
                    }
                    case "text": cw = 120; ch = 20; break;
                    default: continue;
                }
                w = Math.Max(w, x + cw);
                h = Math.Max(h, y + ch);
            }
            return new Vector2(w, h);
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
