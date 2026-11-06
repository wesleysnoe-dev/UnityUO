using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A legacy menu (0x7C), as TazUO's MenuGump and GrayMenuGump. An item menu shows the 0x0910 frame with the question at
    /// (39, 18) and the entries' art in a 217 x 49 strip at (40, 42) that scrolls sideways; double-clicking one chooses it.
    /// A question menu is a 400-wide 0x13EC box with a radio per answer from x 50, Cancel (0x1450) at x 70 and Continue
    /// (0x13B2) at x 200. Closing it any other way (right-click, Esc) cancels, as MenuGump's right-click does: the answer
    /// goes exactly once.
    /// </summary>
    public sealed class LegacyMenuWindow : UiWindow
    {
        public const int ItemBackground = 0x0910;
        public const int StripX = 40, StripY = 42, StripWidth = 217, StripHeight = 49, QuestionX = 39, QuestionY = 18;
        public const int ItemFallbackWidth = 300, ItemFallbackHeight = 130;

        public const int GrayBackground = 0x13EC, GrayWidth = 400, GrayRow = 21, RadioX = 50;
        public const int RadioOff = 0x138A, RadioOn = 0x138B;
        public const int CancelNormal = 0x1450, CancelPressed = 0x1451, CancelX = 70;
        public const int ContinueNormal = 0x13B2, ContinuePressed = 0x13B3, ContinueX = 200;

        private readonly LegacyMenu _menu;
        private float _scroll;
        private int _chosen = -1;
        private bool _forgotten;
        private GUIStyle _wrap;

        public LegacyMenuWindow(LegacyMenu menu)
        {
            _menu = menu;
            if (menu.IsQuestion)
            {
                float h = GrayHeight(menu.Entries.Count);
                Rect = new Rect(Screen.width / 2f / UiScale.Current - GrayWidth / 2f, Screen.height / 2f / UiScale.Current - h / 2f, GrayWidth, h);
            }
            else
            {
                Rect = new Rect(100, 100, ItemFallbackWidth, ItemFallbackHeight);
            }
        }

        public override string Key => KeyFor(_menu.Serial);

        public static string KeyFor(uint serial) => "legacymenu:" + serial;

        /// <summary>The question, two lines at most, then a row per answer and the buttons.</summary>
        private static float GrayHeight(int entries) => 16 + 36 + 35 + entries * GrayRow + 5 + 30 + 15;

        /// <summary>A newer menu with the same serial replaces this one: it goes without answering (that would answer the new one).</summary>
        public void Forget() => _forgotten = true;

        public override void OnOpened()
        {
            if (!_menu.IsQuestion && Art.Gump(ItemBackground) is UiImage bg)
                Rect = new Rect(Rect.x, Rect.y, bg.Width, bg.Height);
        }

        public override void OnClosed()
        {
            if (!_forgotten)
                Session.AnswerMenu(_menu, null);
        }

        public override void Draw()
        {
            HoveredText = null;
            if (_menu.IsQuestion)
                DrawQuestion();
            else
                DrawItems();
        }

        private void Choose(LegacyMenuEntry entry)
        {
            Session.AnswerMenu(_menu, entry);
            Close();
        }

        private void DrawItems()
        {
            UiImage bg = Art.Gump(ItemBackground);
            if (bg != null)
                DrawImage(bg, 0, 0);
            else
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(QuestionX, QuestionY, 220, 18), _menu.Question ?? "", new Color(0.1f, 0.08f, 0.05f), 13);

            var strip = new Rect(StripX, StripY, StripWidth, StripHeight);
            Color saved = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(strip, Texture2D.whiteTexture);
            GUI.color = saved;

            Event e = Event.current;
            float total = 0;
            GUI.BeginGroup(strip);
            foreach (LegacyMenuEntry entry in _menu.Entries)
            {
                UiImage art = entry.Graphic != 0 ? Art.Item(entry.Graphic, entry.Hue) : null;
                float w = art != null ? art.Width : 44;
                float h = art != null ? art.Height : StripHeight;
                // MenuGump centres art shorter than 47 vertically.
                float y = h >= 47 ? 0 : (47 - h) / 2f;
                var r = new Rect(total - _scroll, 0, w, StripHeight);
                if (art != null)
                    DrawImage(art, r.x, y);
                else
                    Text(new Rect(r.x + 2, 16, w - 4, 18), entry.Text ?? "", Color.white, 11);
                if (r.Contains(e.mousePosition) && MouseIsOver)
                {
                    HoveredText = entry.Text;
                    if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
                    {
                        e.Use();
                        GUI.EndGroup();
                        Choose(entry);
                        return;
                    }
                }
                total += w;
            }
            GUI.EndGroup();

            float max = Mathf.Max(0, total - StripWidth);
            if (max > 0)
            {
                if (GUI.RepeatButton(new Rect(StripX - 16, StripY + 18, 14, 16), "<"))
                    _scroll -= 4;
                if (GUI.RepeatButton(new Rect(StripX + StripWidth + 2, StripY + 18, 14, 16), ">"))
                    _scroll += 4;
                _scroll = GUI.HorizontalSlider(new Rect(StripX, StripY + StripHeight + 10, StripWidth, 12), _scroll, 0, max);
                if (e.type == EventType.ScrollWheel && MouseIsOver)
                {
                    _scroll += e.delta.y * 16;
                    e.Use();
                }
            }
            _scroll = Mathf.Clamp(_scroll, 0, max);

            if (_menu.Entries.Count == 0)
                Text(new Rect(StripX, StripY + 16, StripWidth, 18), "(nothing to choose)", Color.gray, 12, TextAnchor.UpperCenter);
        }

        private void DrawQuestion()
        {
            DrawResizePic(GrayBackground, 0, 0, Rect.width, Rect.height);
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, clipping = TextClipping.Clip, normal = { textColor = new Color(0.1f, 0.08f, 0.05f) } };
            GUI.Label(new Rect(20, 16, 370, 36), _menu.Question ?? "", _wrap);

            float y = 16 + 36 + 10;
            for (int i = 0; i < _menu.Entries.Count; i++)
            {
                LegacyMenuEntry entry = _menu.Entries[i];
                bool on = _chosen == i;
                var row = new Rect(RadioX, y, 330, GrayRow);
                UiImage radio = Art.Gump(on ? RadioOn : RadioOff);
                if (radio != null)
                    DrawImage(radio, RadioX, y);
                else
                    Text(new Rect(RadioX, y, 18, GrayRow), on ? "(o)" : "( )", new Color(0.1f, 0.08f, 0.05f));
                Text(new Rect(RadioX + 25, y + 2, 305, GrayRow), entry.Text ?? "", new Color(0.1f, 0.08f, 0.05f), 13);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                    _chosen = i;
                y += GrayRow;
            }

            float buttons = Rect.height - 45;
            if (GumpButton(CancelNormal, CancelPressed, CancelX, buttons)
                || (Art.Gump(CancelNormal) == null && GUI.Button(new Rect(CancelX, buttons, 80, 24), "Cancel")))
            {
                Close(); // OnClosed sends the cancel
                return;
            }
            if (GumpButton(ContinueNormal, ContinuePressed, ContinueX, buttons)
                || (Art.Gump(ContinueNormal) == null && GUI.Button(new Rect(ContinueX, buttons, 80, 24), "Continue")))
            {
                // GrayMenuGump: Continue with nothing checked does nothing.
                if (_chosen >= 0 && _chosen < _menu.Entries.Count)
                    Choose(_menu.Entries[_chosen]);
            }
        }
    }
}
