using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The public moongate's menu in the game's own look (IronSkin) instead of the server's stone gump: the facets down
    /// the left, the chosen facet's towns on the right, Travel and Cancel underneath. It is the same gump underneath
    /// (<see cref="MoongateMenu"/> reads it): Travel, or a double-click on a town, answers OKAY with that town's switch;
    /// Cancel, a right-click or Esc answer CANCEL. The server closing or replacing the gump closes it.
    /// </summary>
    public sealed class MoongateWindow : UiWindow
    {
        public override bool Themed => true;

        private const float Pad = 18f, TitleH = 30f, RowH = 26f, FacetW = 130f, TownW = 190f, Gap = 16f, ButtonH = 28f;

        private readonly ServerGump _gump;
        private readonly MoongateMenu _menu;
        private MoongateMenu.Facet _facet;
        private int _chosen = -1; // the chosen town's switch, or -1
        private bool _answered;
        private GUIStyle _title, _row;

        public MoongateWindow(ServerGump gump, MoongateMenu menu)
        {
            _gump = gump;
            _menu = menu;
            _facet = menu.First;
            Rect = new Rect(gump.X, gump.Y, 1, 1);
        }

        public static Vector2 SizeFor(MoongateMenu menu)
        {
            int rows = menu.Facets.Count;
            foreach (MoongateMenu.Facet f in menu.Facets)
                rows = Math.Max(rows, f.Destinations.Count);
            return new Vector2(Pad + FacetW + Gap + TownW + Pad, Pad + TitleH + rows * RowH + 12f + ButtonH + Pad);
        }

        // The server gump's own key, so its replacement or a second gate's menu takes this window's place.
        public override string Key => $"gump:{_gump.TypeId}:{_gump.Serial}";
        public override bool StepsAsideOnOpen => false;
        public override bool StillValid => Session.World.Gumps.Contains(_gump);

        public override void OnOpened()
        {
            (int x, int y) = Ui.ServerGumpPositions.Open(_gump.TypeId, _gump.X, _gump.Y);
            Rect = new Rect(x, y, 1, 1) { size = SizeFor(_menu) };
        }

        public override void OnClosed()
        {
            if (!_answered && Session.World.Gumps.Contains(_gump))
                Reply(0);
            Ui.ServerGumpPositions.Closed(_gump.TypeId, Movable, (int)Math.Round(Rect.x), (int)Math.Round(Rect.y));
        }

        private void Reply(int button)
        {
            _answered = true;
            Session.ReplyGump(_gump, button, button == 0 || _chosen < 0 ? new List<int>() : new List<int> { _chosen },
                new List<(int, string)>());
        }

        private string Name(int cliloc) => Ui.Cliloc?.Get(cliloc) ?? $"#{cliloc}";

        public override void Draw()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
                _title.normal.textColor = PaperdollSkin.Gold;
                if (PaperdollSkin.SerifFont != null)
                    _title.font = PaperdollSkin.SerifFont;
                _row = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 6, 2, 2), fontSize = 14 };
            }
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _chosen >= 0)
            {
                e.Use();
                Reply(MoongateMenu.OkButton);
                Close();
                return;
            }

            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            GUI.Label(new Rect(Pad, Pad - 4f, Rect.width - 2 * Pad, TitleH - 6f), Name(MoongateMenu.HeadingCliloc).TrimEnd(':', ' '), _title);
            float top = Pad + TitleH;
            Line(new Rect(Pad, top - 6f, Rect.width - 2 * Pad, 1f));

            // The facets.
            for (int i = 0; i < _menu.Facets.Count; i++)
            {
                MoongateMenu.Facet f = _menu.Facets[i];
                bool on = ReferenceEquals(f, _facet);
                if (GUI.Toggle(new Rect(Pad, top + i * RowH, FacetW, RowH - 3f), on, Name(f.NameCliloc), _row) && !on)
                {
                    _facet = f;
                    _chosen = -1;
                }
            }
            float townX = Pad + FacetW + Gap;
            Line(new Rect(townX - Gap / 2f, top, 1f, Rect.height - top - Pad - ButtonH - 12f));

            // Its towns: a click chooses, a double-click goes.
            if (_facet != null)
                for (int i = 0; i < _facet.Destinations.Count; i++)
                {
                    MoongateMenu.Destination d = _facet.Destinations[i];
                    var r = new Rect(townX, top + i * RowH, TownW, RowH - 3f);
                    bool twice = e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && r.Contains(e.mousePosition);
                    bool on = _chosen == d.Switch;
                    if (GUI.Toggle(r, on, Name(d.NameCliloc), _row) != on || twice)
                        _chosen = d.Switch;
                    if (twice)
                    {
                        e.Use();
                        Reply(MoongateMenu.OkButton);
                        Close();
                        return;
                    }
                }

            float by = Rect.height - Pad - ButtonH;
            bool was = GUI.enabled;
            GUI.enabled = was && _chosen >= 0;
            bool go = GUI.Button(new Rect(townX, by, 100f, ButtonH), "Travel");
            GUI.enabled = was;
            if (go)
            {
                Reply(MoongateMenu.OkButton);
                Close();
                return;
            }
            if (GUI.Button(new Rect(Rect.width - Pad - 80f, by, 80f, ButtonH), "Cancel"))
                Close(); // OnClosed answers CANCEL
        }

        private static void Line(Rect r)
        {
            Color was = GUI.color;
            GUI.color = PaperdollSkin.GoldDim;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
