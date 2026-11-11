using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The assistant's window (IronSkin): the tools listed down the left under their headings, the chosen tool's own tab
    /// on the right, scrolling. Opened by the "assistant" hotkey action or the paperdoll's menu; closing it changes
    /// nothing (each tool saves as it is edited and keeps working with the window shut).
    /// </summary>
    public sealed class AssistantWindow : UI.UiWindow
    {
        public override bool Themed => true;
        public override string Key => "assistant";

        private const float Width = 720f, Height = 500f, Pad = 16f, ListW = 150f, TitleH = 30f;
        private static readonly string[] Groups = { "General", "Agents", "Filters" };

        private readonly AssistantHost _host;
        private static string _chosen; // remembered while the game runs
        private Vector2 _scroll;
        private GUIStyle _title, _group, _item;

        public AssistantWindow(AssistantHost host)
        {
            _host = host;
            Rect = new Rect(140, 90, Width, Height);
        }

        public override bool StillValid => _host.Session != null;

        public override void Draw()
        {
            if (_title == null)
            {
                _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
                _title.normal.textColor = UI.PaperdollSkin.Gold;
                if (UI.PaperdollSkin.SerifFont != null)
                    _title.font = UI.PaperdollSkin.SerifFont;
                _group = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
                _group.normal.textColor = UI.PaperdollSkin.GoldFaded;
                _item = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 4, 2, 2), fontSize = 13 };
            }
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            GUI.Label(new Rect(Pad, Pad - 4f, Rect.width - 2 * Pad, TitleH - 6f), "Assistant", _title);
            Line(new Rect(Pad, Pad + TitleH - 6f, Rect.width - 2 * Pad, 1f));

            List<AssistantAgent> agents = _host.Agents;
            AssistantAgent chosen = agents.Find(a => a.Name == _chosen) ?? (agents.Count > 0 ? agents[0] : null);
            float top = Pad + TitleH, bottom = Rect.height - Pad;

            // The tools, under their headings; a mark beside the ones switched on.
            GUILayout.BeginArea(new Rect(Pad, top, ListW, bottom - top));
            foreach (string group in Groups)
            {
                bool any = false;
                foreach (AssistantAgent a in agents)
                {
                    if (a.Group != group)
                        continue;
                    if (!any)
                    {
                        GUILayout.Label(group.ToUpperInvariant(), _group);
                        any = true;
                    }
                    bool on = ReferenceEquals(a, chosen);
                    if (GUILayout.Toggle(on, (a.Active ? "◆ " : "   ") + a.Name, _item, GUILayout.Height(24)) && !on)
                    {
                        _chosen = a.Name;
                        chosen = a;
                        _scroll = Vector2.zero;
                    }
                }
                if (any)
                    GUILayout.Space(8);
            }
            GUILayout.EndArea();

            float x = Pad + ListW + 12f;
            Line(new Rect(x - 6f, top, 1f, bottom - top));
            if (chosen == null)
                return;
            var area = new Rect(x, top, Rect.width - x - Pad, bottom - top);
            GUILayout.BeginArea(area);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true);
            chosen.DrawTab(area.width - 24f);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void Line(Rect r)
        {
            Color was = GUI.color;
            GUI.color = UI.PaperdollSkin.GoldDim;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
