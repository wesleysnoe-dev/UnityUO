using System.Linq;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The skills window: every skill with its value, a use button for skills that can be used directly,
    /// and the gain lock (up / down / locked; click to cycle). Shows real or base values.
    /// </summary>
    public sealed class SkillsWindow : UiWindow
    {
        private Vector2 _scroll;
        private bool _showBase;

        public SkillsWindow()
        {
            Rect = new Rect(360, 80, 330, 460);
        }

        public override string Key => "skills";

        public override void OnOpened() => Session.RequestSkills();

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 10, Rect.width, 22), "Skills", new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            if (Session.Skills.Count == 0)
            {
                Text(new Rect(20, 50, 300, 20), "Waiting for the server…");
                return;
            }

            var list = Session.Skills.Values.OrderBy(s => GameSession.SkillNames.ElementAtOrDefault(s.Id) ?? "").ToList();
            var view = new Rect(14, 40, Rect.width - 28, Rect.height - 90);
            const float row = 20;
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, list.Count * row));
            for (int i = 0; i < list.Count; i++)
            {
                SkillEntry s = list[i];
                float y = i * row;
                string name = s.Id < GameSession.SkillNames.Length ? GameSession.SkillNames[s.Id] : $"Skill {s.Id}";
                bool usable = GameSession.UsableSkills.Contains(s.Id);

                // Usable skills have the classic blue gem button; click it (or the name) to use the skill.
                if (usable && (GumpButton(0x0837, 0x0838, 0, y + 3) || GUI.Button(new Rect(18, y, 190, row), GUIContent.none, GUIStyle.none)))
                    Session.UseSkill(s.Id);
                Text(new Rect(18, y + 2, 200, row), name, usable ? new Color(0.75f, 0.85f, 1f) : new Color(0.9f, 0.88f, 0.8f));

                double value = _showBase ? s.Base : s.Value;
                Text(new Rect(200, y + 2, 60, row), value.ToString("F1"), null, 13, TextAnchor.UpperRight);

                // Lock: up 0x0984, down 0x0986, locked 0x082C.
                int lockGump = s.Lock == 0 ? 0x0984 : s.Lock == 1 ? 0x0986 : 0x082C;
                if (GumpButton(lockGump, lockGump, 268, y + 3))
                {
                    byte next = (byte)((s.Lock + 1) % 3);
                    Session.SetSkillLock(s.Id, next);
                    s.Lock = next;
                    Session.Skills[s.Id] = s;
                }
            }
            GUI.EndScrollView();

            double total = Session.Skills.Values.Sum(s => s.Base);
            Text(new Rect(20, Rect.height - 42, 200, 20), $"Total: {total:F1}");
            if (GUI.Button(new Rect(Rect.width - 120, Rect.height - 44, 100, 22), _showBase ? "Show real" : "Show base"))
                _showBase = !_showBase;
        }
    }

    /// <summary>
    /// The status window as TazUO's StatusGumpOld draws it (layout in StatusWindow.Layout): the 0x0802 scroll with the name,
    /// str / dex / int and their gain locks, sex, armor, hits, mana, stamina, gold and weight from the full status (0x11).
    /// A lock click cycles it and tells the server (0xBF 0x1A); a click in the bottom-right corner turns the status into
    /// our health bar, and opening it closes that bar (StatusGumpBarMutuallyExclusive, on by default). TazUO's default
    /// style is the modern one; the classic one is used here. TazUO's compact styles are drawn with plain colour boxes,
    /// not gump art, so they are not offered.
    /// </summary>
    public sealed partial class StatusWindow : UiWindow
    {
        private readonly CachedLabel _name = new CachedLabel();
        private readonly CachedLabel[] _labels = Enumerable.Range(0, 10).Select(_ => new CachedLabel()).ToArray();
        private Vector2? _pressedAt; // the window's position when the corner was pressed

        public StatusWindow()
        {
            Rect = new Rect(100, 100, 260, 130); // Profile.StatusGumpPosition's default; sized to 0x0802 on open
        }

        public override string Key => "status";

        public override void OnOpened()
        {
            if (Ui.Find(HealthBarWindow.KeyFor(Session.World.PlayerSerial)) is UiWindow bar)
                Ui.Close(bar);
            UiImage bg = Art.Gump(Background);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
            Session.RequestStatus(Session.World.PlayerSerial); // ModernUO answers with 0x11 and the stat locks (0xBF 0x19)
        }

        public override void OnClosed()
        {
            _name.Release();
            foreach (CachedLabel l in _labels)
                l.Release();
        }

        public override void Draw()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && TryTarget(Session.World.PlayerSerial))
                return;

            DrawImage(Art.Gump(Background), 0, 0);
            Mobile m = Session.World.Player;
            if (m == null)
                return;

            (int nx, int ny) = NamePos;
            DrawImage(_name.Get(Art, m.Name ?? "", LabelHue), nx, ny);

            bool uop = Art.Data.GumpFile is UOClient.IO.Files.UopFile;
            for (int stat = 0; stat < 3; stat++)
            {
                byte state = LockOf(m, stat);
                UiImage lockArt = Art.Gump(LockGump(state));
                if (lockArt == null)
                    continue;
                var r = new Rect(LockX(uop), LockY[stat], lockArt.Width, lockArt.Height);
                DrawImage(lockArt, r.x, r.y);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    Session.SetStatLock(stat, NextLock(state));
            }

            Label[] labels = Labels(m);
            for (int i = 0; i < labels.Length; i++)
                DrawImage(_labels[i].Get(Art, labels[i].Text, LabelHue), labels[i].X, labels[i].Y);

            // StatusGumpBase.OnMouseUp: a click (dragged under 5 pixels) from (244, 112) on swaps the status for our health bar.
            bool inCorner = e.mousePosition.x >= BarCornerX && e.mousePosition.y >= BarCornerY;
            if (e.type == EventType.MouseDown && e.button == 0)
                _pressedAt = inCorner ? Rect.position : (Vector2?)null;
            else if (e.type == EventType.MouseUp && e.button == 0 && _pressedAt is Vector2 from)
            {
                _pressedAt = null;
                if (inCorner && (Rect.position - from).sqrMagnitude < 25)
                {
                    uint player = Session.World.PlayerSerial;
                    UiManager ui = Ui;
                    Vector2 at = Rect.position;
                    Close();
                    if (ui.Find(HealthBarWindow.KeyFor(player)) is UiWindow old)
                        ui.Close(old);
                    ui.Open(new HealthBarWindow(player, at.x, at.y));
                    e.Use();
                }
            }
        }
    }
}
