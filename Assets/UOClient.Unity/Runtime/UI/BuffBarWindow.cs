using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The buff bar as TazUO's classic BuffGump draws it (rules in BuffBarWindow.Layout): the player's buffs from 0xDF in
    /// the order the server added them, each its BuffTable gump art 31 px apart on the 0x7580 bar, with the time left
    /// over a timed one (Profile.BuffBarTime) and a pulse through its last 10 seconds. The button turns the bar through
    /// its four backgrounds; hovering an icon shows its title, description and time left; right-click closes it. Nothing
    /// is removed here: an icon goes when the server's remove comes, as in TazUO (a run-out timer only stops counting).
    /// </summary>
    public sealed partial class BuffBarWindow : UiWindow
    {
        private readonly Dictionary<int, (byte Alpha, bool Decreasing)> _fade = new Dictionary<int, (byte, bool)>();
        private int _background = DefaultBackground;

        public BuffBarWindow()
        {
            Rect = new Rect(OpenX, OpenY, 120, 40);
        }

        public override string Key => "buffs";

        public override bool ClosesOnEsc => false;

        public override bool StepsAsideOnOpen => false;

        public override void Draw()
        {
            UiImage bg = Art.Gump(_background);
            Rect.width = bg?.Width ?? 120;
            Rect.height = bg?.Height ?? 40;
            DrawImage(bg, 0, 0);

            (int bx, int by) = ButtonPos(_background);
            if (GumpButton(ButtonNormal, ButtonPressed, bx, by, ButtonPressed))
                _background = NextBackground(_background);

            Event e = Event.current;
            HoveredText = null;
            HoveredTextId = null;
            int n = 0;
            var seen = new HashSet<int>();
            foreach (Buff b in Session.Buffs)
            {
                int graphic = Graphic(b.Icon);
                if (b.Serial != Session.World.PlayerSerial || graphic < 0)
                    continue;
                seen.Add(b.Icon);
                (int x, int y) = IconPos(_background, n++, (int)Rect.width, (int)Rect.height);
                double left = Session.BuffSecondsLeft(b);

                (byte alpha, bool decreasing) = _fade.TryGetValue(b.Icon, out var f) ? f : ((byte)255, true);
                if (e.type == EventType.Repaint)
                    _fade[b.Icon] = (alpha, decreasing) = Fade(alpha, decreasing, left);

                UiImage icon = graphic > 0 ? Art.Gump(graphic) : null;
                var r = new Rect(x, y, icon?.Width ?? 28, icon?.Height ?? 28);
                GUI.color = new Color(1f, 1f, 1f, alpha / 255f);
                DrawImage(icon, x, y);
                string timer = TimerText(left);
                if (timer.Length > 0)
                {
                    var t = new Rect(x - 3, y + r.height / 2 - 3, r.width + 6, 14);
                    Color c = GUI.color;
                    Text(new Rect(t.x + 1, t.y + 1, t.width, t.height), timer, new Color(0, 0, 0, c.a), 10, TextAnchor.UpperCenter, true);
                    Text(t, timer, new Color(1f, 1f, 1f, c.a), 10, TextAnchor.UpperCenter, true);
                }
                GUI.color = Color.white;

                // Hover only: no event is used, so a press on an icon still drags the bar and a right-click closes it.
                if (r.Contains(e.mousePosition))
                {
                    HoveredText = TooltipText(b.Title, b.Description, left);
                    HoveredTextId = "buff:" + b.Icon;
                }
            }
            if (_fade.Count > seen.Count)
                foreach (int gone in new List<int>(_fade.Keys))
                    if (!seen.Contains(gone))
                        _fade.Remove(gone); // the server removed it
        }
    }
}
