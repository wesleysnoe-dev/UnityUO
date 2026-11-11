using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A server gump shown as a window of our own (IronSkin) instead of drawn from its layout: it keeps the gump's key and
    /// place, goes when the server closes or replaces the gump, and answers it exactly once (closing any other way
    /// answers button 0, the gump's cancel).
    /// </summary>
    public abstract class OwnGumpWindow : UiWindow
    {
        public override bool Themed => true;

        protected const float Pad = 18f, TitleH = 30f, ButtonH = 28f;

        protected readonly ServerGump Gump;
        private bool _answered;
        private GUIStyle _title;

        protected OwnGumpWindow(ServerGump gump)
        {
            Gump = gump;
            Rect = new Rect(gump.X, gump.Y, 1, 1);
        }

        public override string Key => $"gump:{Gump.TypeId}:{Gump.Serial}";
        public override bool StepsAsideOnOpen => false;
        public override bool StillValid => Session.World.Gumps.Contains(Gump);

        protected abstract Vector2 Size { get; }

        public override void OnOpened()
        {
            (int x, int y) = Ui.ServerGumpPositions.Open(Gump.TypeId, Gump.X, Gump.Y);
            Rect = new Rect(x, y, 1, 1) { size = Size };
        }

        public override void OnClosed()
        {
            if (!_answered && Session.World.Gumps.Contains(Gump))
                Reply(0, -1);
            Ui.ServerGumpPositions.Closed(Gump.TypeId, Movable, (int)Math.Round(Rect.x), (int)Math.Round(Rect.y));
        }

        /// <summary>Answers the gump with a button and (when not negative) one switch, and closes the window.</summary>
        protected void Answer(int button, int chosen)
        {
            Reply(button, chosen);
            Close();
        }

        private void Reply(int button, int chosen)
        {
            _answered = true;
            Session.ReplyGump(Gump, button, chosen < 0 ? new List<int>() : new List<int> { chosen }, new List<(int, string)>());
        }

        protected string Cliloc(int number) => Ui.Cliloc?.Get(number) ?? $"#{number}";

        /// <summary>The frame, the heading and the rule under it; returns where the content starts.</summary>
        protected float DrawHead(string heading)
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
                _title.normal.textColor = PaperdollSkin.Gold;
                if (PaperdollSkin.SerifFont != null)
                    _title.font = PaperdollSkin.SerifFont;
            }
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            GUI.Label(new Rect(Pad, Pad - 4f, Rect.width - 2 * Pad, TitleH - 6f), BarberMenus.PlainText(heading).TrimEnd(':', ' '), _title);
            Line(new Rect(Pad, Pad + TitleH - 6f, Rect.width - 2 * Pad, 1f), PaperdollSkin.GoldDim);
            return Pad + TitleH;
        }

        /// <summary>The bottom row: the accepting button (off until something is chosen) and Cancel.</summary>
        protected void DrawButtons(string accept, int chosen)
        {
            float by = Rect.height - Pad - ButtonH;
            Event e = Event.current;
            bool enter = chosen >= 0 && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            bool was = GUI.enabled;
            GUI.enabled = was && chosen >= 0;
            bool go = GUI.Button(new Rect(Pad, by, 110f, ButtonH), accept);
            GUI.enabled = was;
            if (go || enter)
            {
                if (enter)
                    e.Use();
                Answer(BarberMenus.OkButton, chosen);
                return;
            }
            if (GUI.Button(new Rect(Rect.width - Pad - 90f, by, 90f, ButtonH), "Cancel"))
                Close(); // OnClosed answers the gump's cancel
        }

        protected static void Line(Rect r, Color color)
        {
            Color was = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>A frame of the given thickness around <paramref name="r"/>.</summary>
        protected static void Border(Rect r, Color color, float t)
        {
            Line(new Rect(r.x, r.y, r.width, t), color);
            Line(new Rect(r.x, r.yMax - t, r.width, t), color);
            Line(new Rect(r.x, r.y, t, r.height), color);
            Line(new Rect(r.xMax - t, r.y, t, r.height), color);
        }

        /// <summary>A left press on <paramref name="r"/> this event: 0 none, 1 a click, 2 a double-click.</summary>
        protected static int Pressed(Rect r)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition))
                return 0;
            int clicks = e.clickCount >= 2 ? 2 : 1;
            e.Use();
            return clicks;
        }
    }

    /// <summary>The hairstylist's list of services (HairstylistBuyGump): one button each; what the bank cannot pay for is dim.</summary>
    public sealed class BarberServicesWindow : OwnGumpWindow
    {
        private const float RowH = 30f, Width = 380f;
        private readonly BarberMenus.Services _menu;
        private GUIStyle _row, _note;

        public BarberServicesWindow(ServerGump gump, BarberMenus.Services menu) : base(gump) => _menu = menu;

        protected override Vector2 Size =>
            new Vector2(Width, Pad + TitleH + _menu.Items.Count * RowH + (_menu.Footer != null ? 26f : 0f) + 10f + ButtonH + Pad);

        public override void Draw()
        {
            if (_row == null)
            {
                _row = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 6, 2, 2), fontSize = 14 };
                _note = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, wordWrap = false };
                _note.normal.textColor = PaperdollSkin.GoldFaded;
            }
            float y = DrawHead(Cliloc(BarberMenus.ServicesHeading));
            bool was = GUI.enabled;
            foreach (BarberMenus.Service s in _menu.Items)
            {
                GUI.enabled = was && s.Button > 0;
                if (GUI.Button(new Rect(Pad, y, Rect.width - 2 * Pad, RowH - 4f), s.Text ?? Cliloc(s.Cliloc), _row))
                {
                    GUI.enabled = was;
                    Answer(s.Button, -1);
                    return;
                }
                y += RowH;
            }
            GUI.enabled = was;
            if (_menu.Footer != null)
                GUI.Label(new Rect(Pad, y, Rect.width - 2 * Pad, 22f), _menu.Footer, _note);
            if (GUI.Button(new Rect(Rect.width - Pad - 90f, Rect.height - Pad - ButtonH, 90f, ButtonH), "Cancel"))
                Close();
        }
    }

    /// <summary>
    /// The hair or beard styles (ChangeHairstyleGump): each style's picture, larger than the server's box showed it, with
    /// its name. A click chooses, a double-click or Choose takes it.
    /// </summary>
    public sealed class BarberStylesWindow : OwnGumpWindow
    {
        private const float Tile = 92f, Picture = 88f, Zoom = 1.45f, NameH = 30f, Gap = 8f;
        // The head in a paperdoll body gump (0x000C / 0x000D), where every hair and beard picture is drawn around.
        private const float HeadX = 90f, HeadY = 73f;
        private readonly BarberMenus.Styles _menu;
        private readonly int _columns;
        private int _chosen = -1;
        private GUIStyle _name;

        public BarberStylesWindow(ServerGump gump, BarberMenus.Styles menu) : base(gump)
        {
            _menu = menu;
            _columns = menu.Items.Count > 8 ? 4 : menu.Items.Count > 4 ? 3 : Math.Max(2, menu.Items.Count);
        }

        private int Rows => (_menu.Items.Count + _columns - 1) / _columns;

        protected override Vector2 Size =>
            new Vector2(2 * Pad + _columns * Tile + (_columns - 1) * Gap, Pad + TitleH + Rows * (Tile + NameH + Gap) + 4f + ButtonH + Pad);

        public override void Draw()
        {
            if (_name == null)
            {
                _name = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = true };
                _name.normal.textColor = IronSkin.Text;
            }
            float top = DrawHead(Cliloc(_menu.Beard ? BarberMenus.BeardHeading : BarberMenus.HairHeading));
            for (int i = 0; i < _menu.Items.Count; i++)
            {
                BarberMenus.Style s = _menu.Items[i];
                var cell = new Rect(Pad + i % _columns * (Tile + Gap), top + i / _columns * (Tile + NameH + Gap), Tile, Tile);
                bool on = _chosen == s.Switch;
                bool over = cell.Contains(Event.current.mousePosition);
                Line(cell, new Color(0.03f, 0.028f, 0.026f, 0.9f));
                // The style on the character's own head: the paperdoll body in its skin, the style's picture over it in
                // the hair's colour now, the part around the head enlarged and clipped to the tile.
                Mobile me = Session.World.Player;
                bool female = me != null && (me.Body == 401 || me.Body == 403 || me.Body == 606 || me.Body == 745 || me.Body == 667);
                byte layer = _menu.Beard ? (byte)16 : (byte)11;
                int hairHue = me != null && me.Equipment.TryGetValue(layer, out EquippedItem worn) ? worn.Hue : 0;
                var inner = new Rect(cell.x + (Tile - Picture) / 2f, cell.y + (Tile - Picture) / 2f, Picture, Picture);
                GUI.BeginGroup(inner);
                foreach (UiImage img in new[] { Art.Gump(female ? 0x000D : 0x000C, me?.Hue ?? 0),
                                                s.Gump != 0 ? Art.Gump(BarberMenus.PictureFor(s.Gump, female), hairHue) : null })
                    if (img != null)
                        GUI.DrawTexture(new Rect(Picture / 2f - HeadX * Zoom, Picture / 2f - HeadY * Zoom, img.Width * Zoom, img.Height * Zoom), img.Texture);
                GUI.EndGroup();
                Border(cell, on ? PaperdollSkin.Gold : over ? PaperdollSkin.GoldFaded : PaperdollSkin.GoldDim, on ? 2f : 1f);
                _name.normal.textColor = on ? IronSkin.TextHot : IronSkin.Text;
                GUI.Label(new Rect(cell.x - 4f, cell.yMax + 2f, Tile + 8f, NameH), BarberMenus.StyleName(s.Gump) ?? "", _name);
                int press = Pressed(cell);
                if (press > 0)
                    _chosen = s.Switch;
                if (press == 2)
                {
                    Answer(BarberMenus.OkButton, _chosen);
                    return;
                }
            }
            DrawButtons("Choose", _chosen);
        }
    }

    /// <summary>
    /// The hair colours (ChangeHairHueGump): the colour families down the left, the chosen family's shades as swatches on
    /// the right. A click chooses a shade, a double-click or Dye takes it.
    /// </summary>
    public sealed class BarberColoursWindow : OwnGumpWindow
    {
        private const float GroupW = 70f, GroupH = 22f, Swatch = 30f, Gap = 6f, Divider = 20f;
        private const int Columns = 8;
        private readonly BarberMenus.Colours _menu;
        private BarberMenus.ColourGroup _group;
        private int _chosen = -1;

        public BarberColoursWindow(ServerGump gump, BarberMenus.Colours menu) : base(gump)
        {
            _menu = menu;
            _group = menu.Groups[0];
        }

        protected override Vector2 Size
        {
            get
            {
                int rows = 1;
                foreach (BarberMenus.ColourGroup g in _menu.Groups)
                    rows = Math.Max(rows, (g.Shades.Count + Columns - 1) / Columns);
                float h = Math.Max(_menu.Groups.Count * (GroupH + Gap), rows * (Swatch + Gap));
                return new Vector2(2 * Pad + GroupW + Divider + Columns * (Swatch + Gap) - Gap, Pad + TitleH + h + 8f + ButtonH + Pad);
            }
        }

        public override void Draw()
        {
            float top = DrawHead(Cliloc(BarberMenus.ColourHeading));
            for (int i = 0; i < _menu.Groups.Count; i++)
            {
                BarberMenus.ColourGroup g = _menu.Groups[i];
                var r = new Rect(Pad, top + i * (GroupH + Gap), GroupW, GroupH);
                bool on = ReferenceEquals(g, _group);
                Line(r, Art.HueColor(g.Hue, Color.gray));
                Border(r, on ? PaperdollSkin.Gold : r.Contains(Event.current.mousePosition) ? PaperdollSkin.GoldFaded : PaperdollSkin.GoldDim, on ? 2f : 1f);
                if (Pressed(r) > 0 && !on)
                {
                    _group = g;
                    _chosen = -1;
                }
            }
            float x0 = Pad + GroupW + Divider;
            Line(new Rect(x0 - Divider / 2f, top, 1f, Rect.height - top - Pad - ButtonH - 10f), PaperdollSkin.GoldDim);
            for (int i = 0; i < _group.Shades.Count; i++)
            {
                BarberMenus.Shade s = _group.Shades[i];
                var r = new Rect(x0 + i % Columns * (Swatch + Gap), top + i / Columns * (Swatch + Gap), Swatch, Swatch);
                bool on = _chosen == s.Switch;
                Line(r, Art.HueColor(s.Hue, Color.gray));
                Border(r, on ? PaperdollSkin.Gold : r.Contains(Event.current.mousePosition) ? PaperdollSkin.GoldFaded : new Color(0f, 0f, 0f, 0.6f), on ? 3f : 1f);
                int press = Pressed(r);
                if (press > 0)
                    _chosen = s.Switch;
                if (press == 2)
                {
                    Answer(BarberMenus.OkButton, _chosen);
                    return;
                }
            }
            DrawButtons("Dye", _chosen);
        }
    }
}
