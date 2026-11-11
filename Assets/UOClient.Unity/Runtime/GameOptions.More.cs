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

        // ---- the grid containers' tools and look (the seventh batch) ----

        /// <summary>TazUO's GridContainerSearchMode: what the grid's search box does with items that do not match.</summary>
        public int gridContainerSearchMode = GridSearchOnlyShow;

        public const int GridSearchOnlyShow = 0, GridSearchHighlight = 1;

        /// <summary>New grid windows start sorted by kind and colour (the window's Sort button switches it).</summary>
        public bool gridAutoSort = false;

        /// <summary>TazUO's GridBorderAlpha, percent: the line round each cell. 75 is the look this client always had.</summary>
        public int gridBorderAlpha = 75;

        /// <summary>TazUO's Grid_HideBorder: the window's own border is not drawn.</summary>
        public bool gridHideBorder = false;

        /// <summary>TazUO's Grid_UseContainerHue: the background takes a dyed container's colour.</summary>
        public bool gridUseContainerHue = false;

        /// <summary>TazUO's Grid_DefaultColumns / Grid_DefaultRows: the grid window's size in cells.</summary>
        public int gridDefaultColumns = 5, gridDefaultRows = 5;

        public const int MinGridCells = 3, MaxGridCells = 12;

        /// <summary>TazUO's corpse container style: corpses open as other containers do, always as a grid, or always classic.</summary>
        public int corpseContainerStyle = CorpseStyleAsContainers;

        public const int CorpseStyleAsContainers = 0, CorpseStyleGrid = 1, CorpseStyleClassic = 2;

        public static void CopyMore(GameOptions from, GameOptions to)
        {
            to.enableSound = from.enableSound;
            to.enableMusic = from.enableMusic;
            to.enableFootstepsSound = from.enableFootstepsSound;
            to.enableCombatMusic = from.enableCombatMusic;
            to.tooltipBackgroundOpacity = from.tooltipBackgroundOpacity;
            to.tooltipFontSize = from.tooltipFontSize;
            to.ignoreList = from.ignoreList;
            to.gridContainerSearchMode = from.gridContainerSearchMode;
            to.gridAutoSort = from.gridAutoSort;
            to.gridBorderAlpha = from.gridBorderAlpha;
            to.gridHideBorder = from.gridHideBorder;
            to.gridUseContainerHue = from.gridUseContainerHue;
            to.gridDefaultColumns = from.gridDefaultColumns;
            to.gridDefaultRows = from.gridDefaultRows;
            to.corpseContainerStyle = from.corpseContainerStyle;
        }

        private bool SameMore(GameOptions o) =>
            enableSound == o.enableSound && enableMusic == o.enableMusic && enableFootstepsSound == o.enableFootstepsSound
            && enableCombatMusic == o.enableCombatMusic && tooltipBackgroundOpacity == o.tooltipBackgroundOpacity
            && tooltipFontSize == o.tooltipFontSize && ignoreList == o.ignoreList
            && gridContainerSearchMode == o.gridContainerSearchMode && gridAutoSort == o.gridAutoSort
            && gridBorderAlpha == o.gridBorderAlpha && gridHideBorder == o.gridHideBorder
            && gridUseContainerHue == o.gridUseContainerHue && gridDefaultColumns == o.gridDefaultColumns
            && gridDefaultRows == o.gridDefaultRows && corpseContainerStyle == o.corpseContainerStyle;

        private void ClampMore()
        {
            tooltipBackgroundOpacity = Math.Max(0, Math.Min(100, tooltipBackgroundOpacity));
            tooltipFontSize = Math.Max(MinTooltipFontSize, Math.Min(MaxTooltipFontSize, tooltipFontSize));
            ignoreList = string.Join("\n", IgnoredNames(ignoreList));
            gridContainerSearchMode = Math.Max(GridSearchOnlyShow, Math.Min(GridSearchHighlight, gridContainerSearchMode));
            gridBorderAlpha = Math.Max(0, Math.Min(100, gridBorderAlpha));
            gridDefaultColumns = Math.Max(MinGridCells, Math.Min(MaxGridCells, gridDefaultColumns));
            gridDefaultRows = Math.Max(MinGridCells, Math.Min(MaxGridCells, gridDefaultRows));
            corpseContainerStyle = Math.Max(CorpseStyleAsContainers, Math.Min(CorpseStyleClassic, corpseContainerStyle));
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
                case "gridContainerSearchMode": Integer(raw, ref gridContainerSearchMode); return true;
                case "gridAutoSort": Bool(raw, ref gridAutoSort); return true;
                case "gridBorderAlpha": Integer(raw, ref gridBorderAlpha); return true;
                case "gridHideBorder": Bool(raw, ref gridHideBorder); return true;
                case "gridUseContainerHue": Bool(raw, ref gridUseContainerHue); return true;
                case "gridDefaultColumns": Integer(raw, ref gridDefaultColumns); return true;
                case "gridDefaultRows": Integer(raw, ref gridDefaultRows); return true;
                case "corpseContainerStyle": Integer(raw, ref corpseContainerStyle); return true;
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
            add("gridContainerSearchMode", i(gridContainerSearchMode));
            add("gridAutoSort", b(gridAutoSort));
            add("gridBorderAlpha", i(gridBorderAlpha));
            add("gridHideBorder", b(gridHideBorder));
            add("gridUseContainerHue", b(gridUseContainerHue));
            add("gridDefaultColumns", i(gridDefaultColumns));
            add("gridDefaultRows", i(gridDefaultRows));
            add("corpseContainerStyle", i(corpseContainerStyle));
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

        /// <summary>
        /// Whether the grid's search box finds an item: every word typed is somewhere in its name or its tooltip lines
        /// (case ignored). An empty search finds everything.
        /// </summary>
        public static bool GridMatches(string search, string name, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(search))
                return true;
            foreach (string word in search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool found = name != null && name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!found && lines != null)
                    foreach (string line in lines)
                        if (line != null && line.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = true;
                            break;
                        }
                if (!found)
                    return false;
            }
            return true;
        }

        /// <summary>The grid's sort (TazUO's: by graphic, then hue), the serial last so equal items keep one order.</summary>
        public static int GridSortCompare(int graphicA, int hueA, uint serialA, int graphicB, int hueB, uint serialB) =>
            graphicA != graphicB ? graphicA.CompareTo(graphicB) : hueA != hueB ? hueA.CompareTo(hueB) : serialA.CompareTo(serialB);

        /// <summary>
        /// Whether a container opens as a grid: <see cref="OpensGrid(bool, ushort)"/>, except that a corpse follows
        /// <paramref name="corpseStyle"/> when that is not "as containers".
        /// </summary>
        public static bool OpensGrid(bool defaultToOldStyleView, ushort gump, bool corpse, int corpseStyle)
        {
            if (corpse && corpseStyle == CorpseStyleGrid)
                return !GridNeverOpens(gump);
            if (corpse && corpseStyle == CorpseStyleClassic)
                return false;
            return OpensGrid(defaultToOldStyleView, gump);
        }

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
