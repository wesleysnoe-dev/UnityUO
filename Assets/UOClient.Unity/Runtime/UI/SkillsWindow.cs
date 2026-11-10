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
    /// The groups can be edited as StandardSkillsGump does: New Group adds one, a double-click on a name renames it (Enter
    /// keeps, Esc or clicking away drops the edit), x deletes one (not the first; its skills join the first), a skill
    /// dragged by its name onto another group moves there, and Reset brings back the defaults. The edits last only while
    /// the client runs: nothing is written (TazUO saves them to the profile's skillsgroups.xml), skillgrp.mul is only read.
    /// </summary>
    public sealed class SkillsWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        /// <summary>
        /// The edited groups, kept for the whole run so closing the window keeps them; never saved. A future save would go
        /// to its own file (e.g. UserSettings/skill-groups.json) written only on an explicit Save, never into
        /// unity-uo-settings.json or the hotkeys.
        /// </summary>
        private static List<SkillGroup> _sessionGroups;

        private Vector2 _scroll;
        private bool _showBase;
        private List<SkillGroup> _groups;

        public SkillsWindow()
        {
            Rect = new Rect(360, 80, 330, 486);
        }

        public override string Key => "skills";

        public override void OnOpened()
        {
            _sessionGroups ??= Art?.Data?.GetSkillGroups(GameSession.SkillNames.Length);
            _groups = _sessionGroups;
            Session.RequestSkills();
        }

        public override void OnClosed()
        {
            StopRename();
            _press = null;
            _dragging = false;
        }

        private static string NameOf(int id) => id < GameSession.SkillNames.Length ? GameSession.SkillNames[id] : $"Skill {id}";

        /// <summary>
        /// The groups' skills the server listed, each group in name order (SkillsGroup.Sort); a listed skill no group
        /// holds goes in the first, where TazUO moves the skills of a deleted group.
        /// </summary>
        private List<(SkillGroup Group, List<SkillEntry> Skills)> Arrange()
        {
            var result = new List<(SkillGroup, List<SkillEntry>)>();
            var placed = new HashSet<int>();
            foreach (SkillGroup g in _groups)
            {
                var skills = new List<SkillEntry>();
                foreach (int id in g.Skills)
                    if (Session.Skills.TryGetValue(id, out SkillEntry s) && placed.Add(id))
                        skills.Add(s);
                result.Add((g, skills));
            }
            if (result.Count > 0)
                result[0].Item2.AddRange(Session.Skills.Values.Where(s => !placed.Contains(s.Id)));
            foreach (var (_, skills) in result)
                skills.Sort((a, b) => string.Compare(NameOf(a.Id), NameOf(b.Id), System.StringComparison.InvariantCulture));
            return result;
        }

        private const float Row = 20;
        private const float DragStart = 5; // pixels the mouse moves before a press on a skill name becomes a drag

        // Last layout, in scroll-content coordinates: each group's rows from its header to its last shown skill, and each
        // shown skill's name box.
        private readonly List<(SkillGroup Group, float Top, float Bottom)> _groupRows = new List<(SkillGroup, float, float)>();
        private readonly List<(int Skill, Rect Name)> _skillRows = new List<(int, Rect)>();
        private Rect _view;

        private (int Skill, Vector2 At)? _press; // a left press on a skill name, window coordinates
        private bool _dragging, _dropped;

        private Vector2 ToContent(Vector2 window) => window - _view.position + _scroll;

        /// <summary>
        /// A press on a skill's name, carried 5 pixels, drags it; the release over another group's rows moves it there
        /// (SkillsGroupControl.OnMouseOver). Read before the controls draw, so the use button under the name does not take
        /// the release, and a press on a plain name is used so it does not move the window.
        /// </summary>
        private void TrackSkillDrag(Event e)
        {
            _dropped = false;
            if (e.type == EventType.MouseDown && e.button == 0 && _view.Contains(e.mousePosition))
            {
                Vector2 c = ToContent(e.mousePosition);
                foreach (var (id, name) in _skillRows)
                    if (name.Contains(c))
                    {
                        _press = (id, e.mousePosition);
                        _dragging = false;
                        if (!GameSession.UsableSkills.Contains(id))
                            e.Use();
                        return;
                    }
                return;
            }
            if (_press == null)
                return;
            var (skill, at) = _press.Value;
            // Released: this window's mouse-up, or a repaint with the button up (the release went to another window).
            // Layout passes are skipped, as the mouse-up that follows them must still see the drop.
            bool released = (e.type == EventType.MouseUp && e.button == 0) || (e.type == EventType.Repaint && !Input.GetMouseButton(0));
            if (!released)
            {
                if ((e.type == EventType.MouseDrag || e.type == EventType.Repaint) && !_dragging
                    && (e.mousePosition - at).sqrMagnitude > DragStart * DragStart)
                    _dragging = true;
                return;
            }
            _press = null;
            if (!_dragging)
                return;
            // The mouse-up is left to the use button, so it lets go of the mouse; _dropped keeps it from using the skill.
            _dragging = false;
            _dropped = true;
            if (_view.Contains(e.mousePosition))
            {
                float y = ToContent(e.mousePosition).y;
                foreach (var (group, top, bottom) in _groupRows)
                    if (y >= top && y < bottom)
                    {
                        if (SkillGroups.MoveSkill(_groups, skill, group))
                            group.Open = true;
                        break;
                    }
            }
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

            Event e = Event.current;
            bool grouped = _groups != null && _groups.Count > 0;
            _view = new Rect(14, 40, Rect.width - 28, Rect.height - 116);
            if (grouped)
                TrackSkillDrag(e);

            List<(SkillGroup Group, List<SkillEntry> Skills)> groups = grouped
                ? Arrange()
                : new List<(SkillGroup, List<SkillEntry>)> { (null, Session.Skills.Values.OrderBy(s => NameOf(s.Id)).ToList()) };
            float height = 0;
            foreach (var (group, skills) in groups)
                height += (group != null ? Row : 0) + (group == null || group.Open ? skills.Count * Row : 0);
            _scroll = GUI.BeginScrollView(_view, _scroll, new Rect(0, 0, _view.width - 18, height));
            _groupRows.Clear();
            _skillRows.Clear();
            float y = 0;
            SkillGroup delete = null;
            foreach (var (group, skills) in groups)
            {
                float top = y;
                if (group != null)
                {
                    if (DrawGroupHeader(group, skills.Count, y, group == _groups[0]))
                        delete = group;
                    y += Row;
                }
                if (group == null || group.Open)
                    foreach (SkillEntry entry in skills)
                    {
                        _skillRows.Add((entry.Id, new Rect(18, y, 190, Row)));
                        DrawSkill(entry, y, Row);
                        y += Row;
                    }
                if (group != null)
                    _groupRows.Add((group, top, y));
            }
            GUI.EndScrollView();
            if (delete != null)
            {
                if (_renaming == delete)
                    StopRename();
                SkillGroups.Delete(_groups, delete); // the first group never offers x (SkillsGroupManager.Remove refuses it)
            }

            if (_dragging && _press.HasValue)
                Text(new Rect(e.mousePosition.x + 12, e.mousePosition.y, 200, Row), NameOf(_press.Value.Skill), new Color(1f, 0.9f, 0.6f));

            if (grouped)
            {
                // StandardSkillsGump's new group button and reset (OnButtonClick 0 and 1).
                if (GUI.Button(new Rect(20, Rect.height - 70, 100, 22), "New Group"))
                    SkillGroups.AddGroup(_groups);
                if (GUI.Button(new Rect(130, Rect.height - 70, 100, 22), "Reset groups"))
                    AskReset();
            }

            double total = Session.Skills.Values.Sum(s => s.Base);
            Text(new Rect(20, Rect.height - 42, 200, 20), $"Total: {total:F1}");
            if (GUI.Button(new Rect(Rect.width - 120, Rect.height - 44, 100, 22), _showBase ? "Show real" : "Show base"))
                _showBase = !_showBase;
        }

        /// <summary>
        /// StandardSkillsGump.SkillsGroupControl: arrow 0x0827 closed / 0x0826 open (only with skills in it), the name, and
        /// the 0x0835 rule out to the edge; here also x to delete it (not the first group). True when x was pressed.
        /// </summary>
        private bool DrawGroupHeader(SkillGroup group, int count, float y, bool first)
        {
            float right = _view.width - 18 - (first ? 0 : 18);
            if (_renaming == group)
            {
                DrawRenameBox(new Rect(16, y, right - 16, Row));
                return false;
            }

            Event e = Event.current;
            var nameBox = new Rect(16, y, 200, Row);
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && nameBox.Contains(e.mousePosition))
            {
                // The first click of the pair already opened or closed the group; undo that, then edit the name.
                if (count > 0)
                    group.Open = !group.Open;
                StartRename(group);
                e.Use();
                return false;
            }

            bool open = group.Open;
            bool toggle = count > 0 && GumpButton(open ? 0x0826 : 0x0827, open ? 0x0826 : 0x0827, 0, y + 4);
            Text(new Rect(16, y + 1, 200, Row), group.Name, new Color(1f, 0.9f, 0.6f), 13, TextAnchor.UpperLeft, true);
            float ruleX = 16 + GroupNameWidth(group.Name) + 11; // TazUO: name width + 11 past the name's x
            UiImage rule = Art.Gump(0x0835);
            DrawTiled(rule, ruleX, y + 9, right - ruleX, rule?.Height ?? 0);
            toggle |= count > 0 && GUI.Button(nameBox, GUIContent.none, GUIStyle.none);
            if (toggle)
                group.Open = !open;
            return !first && GUI.Button(new Rect(right + 2, y + 2, 16, 16), "x");
        }

        private SkillGroup _renaming;
        private bool _renameFocused;
        private int _renameKeyboard; // the text field's keyboard control id once it has the keys
        private string _renameText;
        private const string RenameControl = "skillgroup-rename";

        private void StartRename(SkillGroup group)
        {
            _renaming = group;
            _renameFocused = false;
            _renameKeyboard = 0;
            _renameText = group.Name ?? "";
        }

        /// <summary>Ends an edit and gives the keys back if the box still has them (no GUI call: OnClosed runs outside OnGUI).</summary>
        private void StopRename()
        {
            if (_renaming == null)
                return;
            _renaming = null;
            if (_renameKeyboard != 0 && GUIUtility.keyboardControl == _renameKeyboard)
                GUIUtility.keyboardControl = 0;
            _renameKeyboard = 0;
        }

        /// <summary>
        /// SkillsGroupControl's name box: Enter keeps the name (OnKeyboardReturn; blank becomes "No Name"); Escape or the
        /// keys going elsewhere ends the edit with the old name (FocusLost).
        /// </summary>
        private void DrawRenameBox(Rect box)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                e.Use();
                SkillGroups.Rename(_renaming, _renameText);
                StopRename();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                StopRename();
                return;
            }
            GUI.SetNextControlName(RenameControl);
            _renameText = GUI.TextField(box, _renameText, 40);
            if (!_renameFocused)
            {
                GUI.FocusControl(RenameControl);
                _renameFocused = true;
            }
            else if (e.type == EventType.Repaint)
            {
                if (_renameKeyboard == 0 && GUI.GetNameOfFocusedControl() == RenameControl)
                    _renameKeyboard = GUIUtility.keyboardControl;
                else if (_renameKeyboard != 0 && GUIUtility.keyboardControl != _renameKeyboard)
                    StopRename(); // the keys went elsewhere (a press on the world, another field)
            }
        }

        /// <summary>StandardSkillsGump's reset (OnButtonClick 1): asks first, then the defaults come back (skillgrp.mul or the built-in seven).</summary>
        private void AskReset()
        {
            if (Ui.Find(QuestionWindow.KeyFor("skillgroups")) is UiWindow old)
                Ui.Close(old);
            Ui.Open(new QuestionWindow("skillgroups", "Put every skill back\nin the default groups?", yes =>
            {
                if (!yes)
                    return;
                StopRename();
                _sessionGroups = Art?.Data?.GetSkillGroups(GameSession.SkillNames.Length);
                _groups = _sessionGroups;
            }));
        }

        private void DrawSkill(SkillEntry s, float y, float row)
        {
            string name = NameOf(s.Id);
            bool usable = GameSession.UsableSkills.Contains(s.Id);

            // Usable skills have the classic blue gem button; click it (or the name) to use the skill. A release that ends
            // a drag is not a click.
            if (usable && (GumpButton(0x0837, 0x0838, 0, y + 3) || GUI.Button(new Rect(18, y, 190, row), GUIContent.none, GUIStyle.none))
                && !_dropped)
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
    /// The status window as TazUO's StatusGumpModern draws it for our AoS-era client (layout in StatusWindow.Layout): the
    /// 0x2A6C background with the name, str / dex / int and their gain locks, hits / stamina / mana over their maxima,
    /// stat cap, luck, weight, damage, gold, followers and resists from the full status (0x11). A lock click cycles it
    /// and tells the server (0xBF 0x1A); a click from (389, 152) to the corner turns the status into our health bar,
    /// and opening it closes that bar (StatusGumpBarMutuallyExclusive, on by default). TazUO's other styles (Old,
    /// vertical / horizontal, compact, bars) are profile options, not the Standard one, so they are not offered.
    /// </summary>
    public sealed partial class StatusWindow : UiWindow
    {
        private readonly CachedLabel _name = new CachedLabel();
        private readonly CachedLabel[] _labels = Enumerable.Range(0, Labels(new Mobile()).Length).Select(_ => new CachedLabel()).ToArray();
        private Vector2? _pressedAt; // the window's position when the corner was pressed

        public StatusWindow()
        {
            Rect = new Rect(100, 100, 432, 184); // Profile.StatusGumpPosition's default; sized to 0x2A6C on open
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

            UiImage name = _name.Get(Art, m.Name ?? "", LabelHue);
            if (name != null)
                DrawImage(name, new Label(NameX, NameY, null, NameWidth).TextX(name.Width), NameY);

            // The buff button (CV_5020 and later): opens the buff bar, or brings it forward.
            if (HasBuffButton(Session.Version) && GumpButton(BuffButton, BuffButtonPressed, BuffButtonX, BuffButtonY, BuffButtonPressed))
                Ui.Open(new BuffBarWindow());

            for (int stat = 0; stat < 3; stat++)
            {
                byte state = LockOf(m, stat);
                UiImage lockArt = Art.Gump(LockGump(state));
                if (lockArt == null)
                    continue;
                var r = new Rect(LockX, LockY[stat], lockArt.Width, lockArt.Height);
                DrawImage(lockArt, r.x, r.y);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    Session.SetStatLock(stat, NextLock(state));
            }

            Label[] labels = Labels(m);
            for (int i = 0; i < labels.Length; i++)
            {
                UiImage img = _labels[i].Get(Art, labels[i].Text, LabelHue);
                if (img != null)
                    DrawImage(img, labels[i].TextX(img.Width), labels[i].Y);
            }

            // The 1-pixel rules between each value and its maximum.
            if (e.type == EventType.Repaint)
            {
                var ruleColor = unchecked(new Color32((byte)(RuleArgb >> 16), (byte)(RuleArgb >> 8), (byte)RuleArgb, (byte)(RuleArgb >> 24)));
                foreach (Rule rule in Rules)
                    GUI.DrawTexture(new Rect(rule.X, rule.Y, rule.Width, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, ruleColor, 0, 0);
            }

            // StatusGumpBase.OnMouseUp: a click (dragged under 5 pixels) from (389, 152) on swaps the status for our health bar.
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
