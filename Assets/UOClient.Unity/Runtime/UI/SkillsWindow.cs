using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The skills window: every skill with its value, a use button for skills that can be used directly,
    /// and the gain lock (up / down / locked; click to cycle). Shows real or base values.
    /// Skills sit in TazUO's default groups (StandardSkillsGump over SkillsGroupManager.MakeDefault: the client's
    /// skillgrp.mul, else the built-in seven), each starting collapsed; click a group's arrow or name to open it.
    /// The groups are not editable or saved (TazUO keeps them in the profile's skillsgroups.xml).
    /// </summary>
    public sealed class SkillsWindow : UiWindow
    {
        private Vector2 _scroll;
        private bool _showBase;
        private List<SkillGroup> _groups;
        private readonly HashSet<int> _open = new HashSet<int>();

        public SkillsWindow()
        {
            Rect = new Rect(360, 80, 330, 460);
        }

        public override string Key => "skills";

        public override void OnOpened()
        {
            _groups = Art?.Data?.GetSkillGroups(GameSession.SkillNames.Length);
            Session.RequestSkills();
        }

        private static string NameOf(int id) => id < GameSession.SkillNames.Length ? GameSession.SkillNames[id] : $"Skill {id}";

        /// <summary>
        /// The groups' skills the server listed, each group in name order (SkillsGroup.Sort); a listed skill no group
        /// holds goes in the first, where TazUO moves the skills of a deleted group.
        /// </summary>
        private List<(string Name, List<SkillEntry> Skills)> Arrange()
        {
            var result = new List<(string, List<SkillEntry>)>();
            var placed = new HashSet<int>();
            foreach (SkillGroup g in _groups)
            {
                var skills = new List<SkillEntry>();
                foreach (int id in g.Skills)
                    if (Session.Skills.TryGetValue(id, out SkillEntry s) && placed.Add(id))
                        skills.Add(s);
                result.Add((g.Name, skills));
            }
            if (result.Count > 0)
                result[0].Item2.AddRange(Session.Skills.Values.Where(s => !placed.Contains(s.Id)));
            foreach (var (_, skills) in result)
                skills.Sort((a, b) => string.Compare(NameOf(a.Id), NameOf(b.Id), System.StringComparison.InvariantCulture));
            return result;
        }

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 10, Rect.width, 22), "Skills", new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            if (Session.Skills.Count == 0)
            {
                Text(new Rect(20, 50, 300, 20), "Waiting for the server…");
                return;
            }

            List<(string Name, List<SkillEntry> Skills)> groups = _groups != null && _groups.Count > 0
                ? Arrange()
                : new List<(string, List<SkillEntry>)> { (null, Session.Skills.Values.OrderBy(s => NameOf(s.Id)).ToList()) };
            var view = new Rect(14, 40, Rect.width - 28, Rect.height - 90);
            const float row = 20;
            float height = 0;
            for (int g = 0; g < groups.Count; g++)
                height += (groups[g].Name != null ? row : 0) + (groups[g].Name == null || _open.Contains(g) ? groups[g].Skills.Count * row : 0);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, height));
            float y = 0;
            for (int g = 0; g < groups.Count; g++)
            {
                var (groupName, skills) = groups[g];
                if (groupName != null)
                {
                    // StandardSkillsGump.SkillsGroupControl: arrow 0x0827 closed / 0x0826 open (only with skills in it),
                    // the name, and the 0x0835 rule out to the edge.
                    bool open = _open.Contains(g);
                    bool toggle = skills.Count > 0 && GumpButton(open ? 0x0826 : 0x0827, open ? 0x0826 : 0x0827, 0, y + 4);
                    Text(new Rect(16, y + 1, 200, row), groupName, new Color(1f, 0.9f, 0.6f), 13, TextAnchor.UpperLeft, true);
                    float ruleX = 16 + GroupNameWidth(groupName) + 11; // TazUO: name width + 11 past the name's x
                    UiImage rule = Art.Gump(0x0835);
                    DrawTiled(rule, ruleX, y + 9, view.width - 18 - ruleX, rule?.Height ?? 0);
                    toggle |= skills.Count > 0 && GUI.Button(new Rect(16, y, 200, row), GUIContent.none, GUIStyle.none);
                    if (toggle && !_open.Remove(g))
                        _open.Add(g);
                    y += row;
                    if (!open)
                        continue;
                }
                foreach (SkillEntry entry in skills)
                {
                    DrawSkill(entry, y, row);
                    y += row;
                }
            }
            GUI.EndScrollView();

            double total = Session.Skills.Values.Sum(s => s.Base);
            Text(new Rect(20, Rect.height - 42, 200, 20), $"Total: {total:F1}");
            if (GUI.Button(new Rect(Rect.width - 120, Rect.height - 44, 100, 22), _showBase ? "Show real" : "Show base"))
                _showBase = !_showBase;
        }

        private void DrawSkill(SkillEntry s, float y, float row)
        {
            string name = NameOf(s.Id);
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

        private static GUIStyle _groupStyle;

        /// <summary>The drawn width of a group name (where its 0x0835 rule starts).</summary>
        private static float GroupNameWidth(string name)
        {
            _groupStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, padding = new RectOffset() };
            return _groupStyle.CalcSize(new GUIContent(name)).x;
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
