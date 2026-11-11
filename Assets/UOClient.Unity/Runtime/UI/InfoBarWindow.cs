using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The info bar (TazUO's InfoBarGump; rules in HudBars): one line of labelled numbers of the player's, each in its
    /// hue. Low hits, mana or stamina and a heavy pack are shown either by the text turning orange then red, or by a
    /// thin bar under the entry (the options' highlight type). Right-click does nothing: the options switch it off.
    /// </summary>
    public sealed class InfoBarWindow : UiWindow
    {
        public const string WindowKey = "infobar";
        private const int Height = 20, Pad = 8, Gap = 14;

        /// <summary>The options' info bar settings (GameOptions.More.cs).</summary>
        public static bool Enabled { get; set; }
        public static int HighlightType { get; set; }

        /// <summary>The entries; setting them takes effect on the open bar at once.</summary>
        public static List<InfoBarItem> Items { get; set; } = HudBars.ParseInfoBar(HudBars.DefaultInfoBar);

        private static Vector2 _lastPosition = new Vector2(540, 30);
        private static GUIStyle _measure;

        public InfoBarWindow()
        {
            Rect = new Rect(_lastPosition.x, _lastPosition.y, 200, Height);
        }

        public override string Key => WindowKey;
        public override bool CanClose => false;
        public override bool StepsAsideOnOpen => false;

        private static InfoStats StatsOf(Mobile m) => m == null ? new InfoStats() : new InfoStats
        {
            Name = m.Name, Hits = m.Hits, HitsMax = m.HitsMax, Mana = m.Mana, ManaMax = m.ManaMax, Stamina = m.Stamina, StaminaMax = m.StaminaMax,
            Weight = m.Weight, MaxWeight = m.MaxWeight, Followers = m.Followers, FollowersMax = m.FollowersMax, Gold = m.Gold,
            DamageMin = m.DamageMin, DamageMax = m.DamageMax, Armor = m.Armor, Luck = m.Luck, FireResist = m.FireResist, ColdResist = m.ColdResist,
            PoisonResist = m.PoisonResist, EnergyResist = m.EnergyResist, StatCap = m.StatCap, Tithe = m.Tithe, Str = m.Str, Dex = m.Dex, Int = m.Int
        };

        public override void Draw()
        {
            _measure ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = false };
            InfoStats stats = StatsOf(Session.World.Player);
            _lastPosition = Rect.position;

            // Laid out first, for the width.
            var parts = new List<(string Text, float Width, InfoBarItem Item)>();
            float width = Pad;
            foreach (InfoBarItem item in Items)
            {
                string text = (string.IsNullOrEmpty(item.Label) ? "" : item.Label + " ") + HudBars.InfoText(item.Var, stats);
                float w = _measure.CalcSize(new GUIContent(text)).x;
                parts.Add((text, w, item));
                width += w + Gap;
            }
            Rect.size = new Vector2(Mathf.Max(60, width - Gap + Pad), Height);

            Color saved = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Rect.width, Rect.height), Texture2D.whiteTexture);
            GUI.color = saved;

            float x = Pad;
            foreach (var (text, w, item) in parts)
            {
                Color colour = Art.HueColor(item.Hue, new Color(0.93f, 0.91f, 0.84f));
                int level = HudBars.InfoLevel(item.Var, stats);
                Color warn = level == 2 ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.65f, 0.2f);
                if (HighlightType == HudBars.HighlightTextColour && level > 0)
                    colour = warn;
                Text(new Rect(x + 1, 2, w + 4, Height - 3), text, Color.black, 12);
                Text(new Rect(x, 1, w + 4, Height - 3), text, colour, 12);
                if (HighlightType == HudBars.HighlightBars && HudBars.InfoFraction(item.Var, stats) is float f)
                {
                    GUI.color = new Color(1, 1, 1, 0.18f);
                    GUI.DrawTexture(new Rect(x, Height - 3, w, 2), Texture2D.whiteTexture);
                    GUI.color = level > 0 ? warn : colour;
                    GUI.DrawTexture(new Rect(x, Height - 3, w * f, 2), Texture2D.whiteTexture);
                    GUI.color = saved;
                }
                x += w + Gap;
            }
        }
    }
}
