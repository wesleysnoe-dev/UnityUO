using System;
using System.Collections.Generic;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own.

    /// <summary>
    /// The sixth batch of options: sound and music switches, footsteps, combat music, the tooltip's look and the
    /// ignore list. Kept apart as the Display and Video ones are (GameOptions.Graphics.cs).
    /// </summary>
    public sealed partial class GameOptions
    {
        /// <summary>TazUO's EnableSound / EnableMusic: off silences them without losing the volume.</summary>
        public bool enableSound = true, enableMusic = true;

        /// <summary>TazUO's EnableFootstepsSound: people on foot or riding are heard stepping (Avatar.Footstep).</summary>
        public bool enableFootstepsSound = true;

        /// <summary>TazUO's EnableCombatMusic: war mode plays one of the three combat tracks; peace brings the region's music back.</summary>
        public bool enableCombatMusic = true;

        /// <summary>TazUO's Tooltip_Background_Opacity, percent. 88 is the look this client always had.</summary>
        public int tooltipBackgroundOpacity = 88;

        /// <summary>The tooltip's text size (TazUO's tooltip font size).</summary>
        public int tooltipFontSize = 14;

        public const int MinTooltipFontSize = 10, MaxTooltipFontSize = 24;

        /// <summary>TazUO's ignore list: character names, one per line; what they say is dropped as it arrives.</summary>
        public string ignoreList = "";

        public const int MaxIgnoredNames = 100, MaxIgnoredNameLength = 40;

        public static void CopyMore(GameOptions from, GameOptions to)
        {
            to.enableSound = from.enableSound;
            to.enableMusic = from.enableMusic;
            to.enableFootstepsSound = from.enableFootstepsSound;
            to.enableCombatMusic = from.enableCombatMusic;
            to.tooltipBackgroundOpacity = from.tooltipBackgroundOpacity;
            to.tooltipFontSize = from.tooltipFontSize;
            to.ignoreList = from.ignoreList;
        }

        private bool SameMore(GameOptions o) =>
            enableSound == o.enableSound && enableMusic == o.enableMusic && enableFootstepsSound == o.enableFootstepsSound
            && enableCombatMusic == o.enableCombatMusic && tooltipBackgroundOpacity == o.tooltipBackgroundOpacity
            && tooltipFontSize == o.tooltipFontSize && ignoreList == o.ignoreList;

        private void ClampMore()
        {
            tooltipBackgroundOpacity = Math.Max(0, Math.Min(100, tooltipBackgroundOpacity));
            tooltipFontSize = Math.Max(MinTooltipFontSize, Math.Min(MaxTooltipFontSize, tooltipFontSize));
            ignoreList = string.Join("\n", IgnoredNames(ignoreList));
        }

        /// <summary>The parse switch's keys of this batch; false for a key that is not one.</summary>
        private bool ParseMore(string key, string raw)
        {
            switch (key)
            {
                case "enableSound": Bool(raw, ref enableSound); return true;
                case "enableMusic": Bool(raw, ref enableMusic); return true;
                case "enableFootstepsSound": Bool(raw, ref enableFootstepsSound); return true;
                case "enableCombatMusic": Bool(raw, ref enableCombatMusic); return true;
                case "tooltipBackgroundOpacity": Integer(raw, ref tooltipBackgroundOpacity); return true;
                case "tooltipFontSize": Integer(raw, ref tooltipFontSize); return true;
                case "ignoreList":
                    // An array of strings; anything else in the file leaves the list as it is.
                    if (TryReadStrings(raw, out List<string> names))
                        ignoreList = string.Join("\n", names);
                    return true;
                default: return false;
            }
        }

        private void WriteMore(Action<string, string> add, Func<bool, string> b, Func<int, string> i)
        {
            add("enableSound", b(enableSound));
            add("enableMusic", b(enableMusic));
            add("enableFootstepsSound", b(enableFootstepsSound));
            add("enableCombatMusic", b(enableCombatMusic));
            add("tooltipBackgroundOpacity", i(tooltipBackgroundOpacity));
            add("tooltipFontSize", i(tooltipFontSize));
            var quoted = new List<string>();
            foreach (string name in IgnoredNames(ignoreList))
                quoted.Add("\"" + FlatJson.Escape(name) + "\"");
            add("ignoreList", "[" + string.Join(", ", quoted) + "]");
        }

        /// <summary>A JSON array of strings (["a", "b"]); false for anything else.</summary>
        public static bool TryReadStrings(string raw, out List<string> items)
        {
            items = new List<string>();
            raw = (raw ?? "").Trim();
            if (raw.Length < 2 || raw[0] != '[' || raw[raw.Length - 1] != ']')
                return false;
            int i = 1;
            while (true)
            {
                while (i < raw.Length - 1 && (char.IsWhiteSpace(raw[i]) || raw[i] == ','))
                    i++;
                if (i >= raw.Length - 1)
                    return true;
                if (raw[i] != '"')
                    return false;
                var sb = new System.Text.StringBuilder();
                for (i++; i < raw.Length - 1 && raw[i] != '"'; i++)
                {
                    if (raw[i] == '\\' && i + 1 < raw.Length - 1)
                        i++;
                    sb.Append(raw[i]);
                }
                if (i >= raw.Length - 1)
                    return false; // no closing quote
                i++;
                items.Add(sb.ToString());
            }
        }

        // ---- rules ----

        /// <summary>The volume a player hears: the slider's, or nothing with its switch off.</summary>
        public static float EffectiveVolume(bool enabled, float volume) => enabled ? volume : 0f;

        /// <summary>The names in an ignore list: trimmed, no blanks, no repeats (case ignored), at most <see cref="MaxIgnoredNames"/>.</summary>
        public static List<string> IgnoredNames(string list)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in (list ?? "").Split('\n', '\r', ';'))
            {
                string name = line.Trim();
                if (name.Length > MaxIgnoredNameLength)
                    name = name.Substring(0, MaxIgnoredNameLength);
                if (name.Length > 0 && names.Count < MaxIgnoredNames && seen.Add(name))
                    names.Add(name);
            }
            return names;
        }

        /// <summary>The list with <paramref name="name"/> added (no change if it is blank or already there).</summary>
        public static string AddIgnored(string list, string name) => string.Join("\n", IgnoredNames(list + "\n" + name));

        /// <summary>The list without <paramref name="name"/> (case ignored).</summary>
        public static string RemoveIgnored(string list, string name)
        {
            List<string> names = IgnoredNames(list);
            names.RemoveAll(n => string.Equals(n, (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            return string.Join("\n", names);
        }

        /// <summary>ClassicUO's combat music ids: used only when Config.txt names no combat track.</summary>
        public const int FirstCombatMusic = 38, LastCombatMusic = 40;

        /// <summary>The music ids to pick the combat music from: the tracks whose file name has "combat" in it, else 38-40.</summary>
        public static List<int> CombatTracks(IEnumerable<(int Id, string File)> tracks)
        {
            var ids = new List<int>();
            foreach ((int id, string file) in tracks)
                if (file != null && file.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0)
                    ids.Add(id);
            ids.Sort();
            if (ids.Count == 0)
                for (int id = FirstCombatMusic; id <= LastCombatMusic; id++)
                    ids.Add(id);
            return ids;
        }
    }
}
