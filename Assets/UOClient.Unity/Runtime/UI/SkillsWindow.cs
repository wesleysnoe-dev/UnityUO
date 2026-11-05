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

    /// <summary>The status window: stats, resistances, gold, weight, followers (from the full status packet).</summary>
    public sealed class StatusWindow : UiWindow
    {
        public StatusWindow()
        {
            Rect = new Rect(40, 400, 400, 210);
        }

        public override string Key => "status";

        public override void OnOpened() => Session.RequestStatus(Session.World.PlayerSerial);

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Mobile m = Session.World.Player;
            if (m == null)
                return;

            Text(new Rect(0, 10, Rect.width, 22), m.Name ?? "", new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);
            Color gold = new Color(1f, 0.85f, 0.45f);
            float y = 42;
            void Row(float x, string label, string value)
            {
                Text(new Rect(x, y, 90, 20), label, gold);
                Text(new Rect(x + 70, y, 80, 20), value);
            }

            Row(20, "Strength", m.Str.ToString()); Row(150, "Hits", $"{m.Hits}/{m.HitsMax}"); Row(280, "Physical", m.Armor.ToString()); y += 22;
            Row(20, "Dexterity", m.Dex.ToString()); Row(150, "Stamina", $"{m.Stamina}/{m.StaminaMax}"); Row(280, "Fire", m.FireResist.ToString()); y += 22;
            Row(20, "Intellect", m.Int.ToString()); Row(150, "Mana", $"{m.Mana}/{m.ManaMax}"); Row(280, "Cold", m.ColdResist.ToString()); y += 22;
            Row(20, "Stat cap", m.StatCap.ToString()); Row(150, "Luck", m.Luck.ToString()); Row(280, "Poison", m.PoisonResist.ToString()); y += 22;
            Row(20, "Gold", m.Gold.ToString("N0")); Row(150, "Weight", $"{m.Weight}/{m.MaxWeight}"); Row(280, "Energy", m.EnergyResist.ToString()); y += 22;
            Row(20, "Damage", $"{m.DamageMin}-{m.DamageMax}"); Row(150, "Followers", $"{m.Followers}/{m.FollowersMax}"); Row(280, "Tithe", m.Tithe.ToString()); y += 22;
            if (!m.HasFullStatus)
                Text(new Rect(20, y + 4, 300, 20), "Waiting for the server…");
        }
    }
}
