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

        // ---- the counter bar and the info bar (the eighth batch; rules in HudBars) ----

        /// <summary>TazUO's CounterBarEnabled: the bar of item counts is shown. Its cells are kept in their own file.</summary>
        public bool counterBarEnabled = false;

        /// <summary>TazUO's CounterBarCellSize, CounterBarColumns and CounterBarRows.</summary>
        public int counterBarCellSize = 40, counterBarColumns = 5, counterBarRows = 1;

        /// <summary>TazUO's CounterBarDisplayAbbreviatedAmount / CounterBarAbbreviatedAmount: counts from that number up are shown in thousands.</summary>
        public bool counterBarDisplayAbbreviatedAmount = false;
        public int counterBarAbbreviatedAmount = 1000;

        /// <summary>TazUO's CounterBarHighlightOnAmount / CounterBarHighlightAmount: a count below that number is red.</summary>
        public bool counterBarHighlightOnAmount = false;
        public int counterBarHighlightAmount = 5;

        /// <summary>TazUO's ShowInfoBar and InfoBarHighlightType (text colour or bars), and the bar's entries ("Label|Var|Hue" a line).</summary>
        public bool showInfoBar = false;
        public int infoBarHighlightType = HudBars.HighlightTextColour;
        public string infoBarItems = HudBars.DefaultInfoBar;

        // ---- the ground ----

        /// <summary>
        /// The natural ground: blended ground materials on a lit terrain instead of the map's flat tile art (the launch
        /// switches -liveground -groundbackend terrain -terrainstyle natural). Takes effect at the next start. Off: the
        /// classic land.
        /// </summary>
        public bool naturalGround = false;

        /// <summary>Grass standing on the natural ground (-grass). Only with <see cref="naturalGround"/>; at the next start.</summary>
        public bool groundGrass = true;

        /// <summary>The bottom HUD (BottomHudWindow): health and mana orbs, stamina, action slots. Replaces the three bars top-left.</summary>
        public bool bottomHud = true;

        // ---- pathfinding ----

        /// <summary>TazUO's EnablePathfind: a double right-click on the world walks there. On here (TazUO: off).</summary>
        public bool enablePathfind = true;

        /// <summary>TazUO's UseShiftToPathfind (the click only counts with Shift held) and PathfindSingleClick (one click is enough).</summary>
        public bool useShiftToPathfind = false, pathfindSingleClick = false;

        // ---- the tenth batch ----

        /// <summary>TazUO's HighlightOnUse for the counter bar: the cell flashes when its item is used.</summary>
        public bool counterBarHighlightOnUse = true;

        /// <summary>The login screen's music, apart from the game's (TazUO's EnableMusic covers both there).</summary>
        public bool enableLoginMusic = true;

        /// <summary>TazUO's UseDarkNights: the server's night is half as dark again (GameOptions.DarkNight). Not with a custom light level.</summary>
        public bool useDarkNights = false;

        /// <summary>The tooltip's text hue; 0 keeps this client's parchment colour.</summary>
        public int tooltipTextHue = 0;

        /// <summary>TazUO's GuildMessageHue / AllyMessageHue: guild and alliance chat in these hues; 0 keeps the server's colour.</summary>
        public int guildMessageHue = 0, allyMessageHue = 0;

        /// <summary>The nameplates' text size (TazUO's plate font size).</summary>
        public int namePlateFontSize = 14;

        public const int MinNamePlateFontSize = 10, MaxNamePlateFontSize = 24;

        /// <summary>TazUO's cooldown bars: "Name|Trigger text|Seconds|Hue" a line (rules in Cooldowns). None by default.</summary>
        public string cooldowns = "";

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
            to.counterBarEnabled = from.counterBarEnabled;
            to.counterBarCellSize = from.counterBarCellSize;
            to.counterBarColumns = from.counterBarColumns;
            to.counterBarRows = from.counterBarRows;
            to.counterBarDisplayAbbreviatedAmount = from.counterBarDisplayAbbreviatedAmount;
            to.counterBarAbbreviatedAmount = from.counterBarAbbreviatedAmount;
            to.counterBarHighlightOnAmount = from.counterBarHighlightOnAmount;
            to.counterBarHighlightAmount = from.counterBarHighlightAmount;
            to.showInfoBar = from.showInfoBar;
            to.infoBarHighlightType = from.infoBarHighlightType;
            to.infoBarItems = from.infoBarItems;
            to.cooldowns = from.cooldowns;
            to.counterBarHighlightOnUse = from.counterBarHighlightOnUse;
            to.naturalGround = from.naturalGround;
            to.groundGrass = from.groundGrass;
            to.bottomHud = from.bottomHud;
            to.enablePathfind = from.enablePathfind;
            to.useShiftToPathfind = from.useShiftToPathfind;
            to.pathfindSingleClick = from.pathfindSingleClick;
            to.enableLoginMusic = from.enableLoginMusic;
            to.useDarkNights = from.useDarkNights;
            to.tooltipTextHue = from.tooltipTextHue;
            to.guildMessageHue = from.guildMessageHue;
            to.allyMessageHue = from.allyMessageHue;
            to.namePlateFontSize = from.namePlateFontSize;
        }

        private bool SameMore(GameOptions o) =>
            enableSound == o.enableSound && enableMusic == o.enableMusic && enableFootstepsSound == o.enableFootstepsSound
            && enableCombatMusic == o.enableCombatMusic && tooltipBackgroundOpacity == o.tooltipBackgroundOpacity
            && tooltipFontSize == o.tooltipFontSize && ignoreList == o.ignoreList
            && gridContainerSearchMode == o.gridContainerSearchMode && gridAutoSort == o.gridAutoSort
            && gridBorderAlpha == o.gridBorderAlpha && gridHideBorder == o.gridHideBorder
            && gridUseContainerHue == o.gridUseContainerHue && gridDefaultColumns == o.gridDefaultColumns
            && gridDefaultRows == o.gridDefaultRows && corpseContainerStyle == o.corpseContainerStyle
            && counterBarEnabled == o.counterBarEnabled && counterBarCellSize == o.counterBarCellSize
            && counterBarColumns == o.counterBarColumns && counterBarRows == o.counterBarRows
            && counterBarDisplayAbbreviatedAmount == o.counterBarDisplayAbbreviatedAmount
            && counterBarAbbreviatedAmount == o.counterBarAbbreviatedAmount
            && counterBarHighlightOnAmount == o.counterBarHighlightOnAmount && counterBarHighlightAmount == o.counterBarHighlightAmount
            && showInfoBar == o.showInfoBar && infoBarHighlightType == o.infoBarHighlightType && infoBarItems == o.infoBarItems
            && cooldowns == o.cooldowns
            && naturalGround == o.naturalGround && groundGrass == o.groundGrass && bottomHud == o.bottomHud
            && enablePathfind == o.enablePathfind && useShiftToPathfind == o.useShiftToPathfind && pathfindSingleClick == o.pathfindSingleClick
            && counterBarHighlightOnUse == o.counterBarHighlightOnUse && enableLoginMusic == o.enableLoginMusic
            && useDarkNights == o.useDarkNights && tooltipTextHue == o.tooltipTextHue && guildMessageHue == o.guildMessageHue
            && allyMessageHue == o.allyMessageHue && namePlateFontSize == o.namePlateFontSize;

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
            counterBarCellSize = Math.Max(HudBars.MinCounterCell, Math.Min(HudBars.MaxCounterCell, counterBarCellSize));
            counterBarColumns = Math.Max(1, Math.Min(HudBars.MaxCounterColumns, counterBarColumns));
            counterBarRows = Math.Max(1, Math.Min(HudBars.MaxCounterRows, counterBarRows));
            counterBarAbbreviatedAmount = Math.Max(1000, Math.Min(1000000, counterBarAbbreviatedAmount));
            counterBarHighlightAmount = Math.Max(1, Math.Min(60000, counterBarHighlightAmount));
            infoBarHighlightType = Math.Max(HudBars.HighlightTextColour, Math.Min(HudBars.HighlightBars, infoBarHighlightType));
            infoBarItems = HudBars.FormatInfoBar(HudBars.ParseInfoBar(infoBarItems));
            cooldowns = Cooldowns.Format(Cooldowns.Parse(cooldowns));
            tooltipTextHue = ClampHue(tooltipTextHue);
            guildMessageHue = ClampHue(guildMessageHue);
            allyMessageHue = ClampHue(allyMessageHue);
            namePlateFontSize = Math.Max(MinNamePlateFontSize, Math.Min(MaxNamePlateFontSize, namePlateFontSize));
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
                case "counterBarEnabled": Bool(raw, ref counterBarEnabled); return true;
                case "counterBarCellSize": Integer(raw, ref counterBarCellSize); return true;
                case "counterBarColumns": Integer(raw, ref counterBarColumns); return true;
                case "counterBarRows": Integer(raw, ref counterBarRows); return true;
                case "counterBarDisplayAbbreviatedAmount": Bool(raw, ref counterBarDisplayAbbreviatedAmount); return true;
                case "counterBarAbbreviatedAmount": Integer(raw, ref counterBarAbbreviatedAmount); return true;
                case "counterBarHighlightOnAmount": Bool(raw, ref counterBarHighlightOnAmount); return true;
                case "counterBarHighlightAmount": Integer(raw, ref counterBarHighlightAmount); return true;
                case "showInfoBar": Bool(raw, ref showInfoBar); return true;
                case "infoBarHighlightType": Integer(raw, ref infoBarHighlightType); return true;
                case "naturalGround": Bool(raw, ref naturalGround); return true;
                case "groundGrass": Bool(raw, ref groundGrass); return true;
                case "bottomHud": Bool(raw, ref bottomHud); return true;
                case "enablePathfind": Bool(raw, ref enablePathfind); return true;
                case "useShiftToPathfind": Bool(raw, ref useShiftToPathfind); return true;
                case "pathfindSingleClick": Bool(raw, ref pathfindSingleClick); return true;
                case "counterBarHighlightOnUse": Bool(raw, ref counterBarHighlightOnUse); return true;
                case "enableLoginMusic": Bool(raw, ref enableLoginMusic); return true;
                case "useDarkNights": Bool(raw, ref useDarkNights); return true;
                case "tooltipTextHue": Integer(raw, ref tooltipTextHue); return true;
                case "guildMessageHue": Integer(raw, ref guildMessageHue); return true;
                case "allyMessageHue": Integer(raw, ref allyMessageHue); return true;
                case "namePlateFontSize": Integer(raw, ref namePlateFontSize); return true;
                case "cooldowns":
                    if (TryReadStrings(raw, out List<string> timers))
                        cooldowns = string.Join("\n", timers);
                    return true;
                case "infoBarItems":
                    if (TryReadStrings(raw, out List<string> entries))
                        infoBarItems = string.Join("\n", entries);
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
            add("gridContainerSearchMode", i(gridContainerSearchMode));
            add("gridAutoSort", b(gridAutoSort));
            add("gridBorderAlpha", i(gridBorderAlpha));
            add("gridHideBorder", b(gridHideBorder));
            add("gridUseContainerHue", b(gridUseContainerHue));
            add("gridDefaultColumns", i(gridDefaultColumns));
            add("gridDefaultRows", i(gridDefaultRows));
            add("corpseContainerStyle", i(corpseContainerStyle));
            add("counterBarEnabled", b(counterBarEnabled));
            add("counterBarCellSize", i(counterBarCellSize));
            add("counterBarColumns", i(counterBarColumns));
            add("counterBarRows", i(counterBarRows));
            add("counterBarDisplayAbbreviatedAmount", b(counterBarDisplayAbbreviatedAmount));
            add("counterBarAbbreviatedAmount", i(counterBarAbbreviatedAmount));
            add("counterBarHighlightOnAmount", b(counterBarHighlightOnAmount));
            add("counterBarHighlightAmount", i(counterBarHighlightAmount));
            add("showInfoBar", b(showInfoBar));
            add("infoBarHighlightType", i(infoBarHighlightType));
            var entries = new List<string>();
            foreach (string line in (infoBarItems ?? "").Split('\n'))
                if (line.Length > 0)
                    entries.Add("\"" + FlatJson.Escape(line) + "\"");
            add("infoBarItems", "[" + string.Join(", ", entries) + "]");
            var timers = new List<string>();
            foreach (string line in (cooldowns ?? "").Split('\n'))
                if (line.Length > 0)
                    timers.Add("\"" + FlatJson.Escape(line) + "\"");
            add("cooldowns", "[" + string.Join(", ", timers) + "]");
            add("counterBarHighlightOnUse", b(counterBarHighlightOnUse));
            add("naturalGround", b(naturalGround));
            add("groundGrass", b(groundGrass));
            add("bottomHud", b(bottomHud));
            add("enablePathfind", b(enablePathfind));
            add("useShiftToPathfind", b(useShiftToPathfind));
            add("pathfindSingleClick", b(pathfindSingleClick));
            add("enableLoginMusic", b(enableLoginMusic));
            add("useDarkNights", b(useDarkNights));
            add("tooltipTextHue", i(tooltipTextHue));
            add("guildMessageHue", i(guildMessageHue));
            add("allyMessageHue", i(allyMessageHue));
            add("namePlateFontSize", i(namePlateFontSize));
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
        /// The land backend the game starts with: what the launch switches ask for when -liveground is given, else the
        /// terrain backend when the natural ground option is on, else none (the classic land).
        /// </summary>
        public static (bool LiveGround, string Backend) GroundLaunch(bool optionNaturalGround, bool liveGroundArgument, string backendArgument) =>
            liveGroundArgument ? (true, backendArgument) : optionNaturalGround ? (true, "terrain") : (false, (string)null);

        /// <summary>
        /// Whether a right-click on the world asks for a path: pathfinding is on, Shift is held if it must be, and the
        /// click is the second of a double-click unless one click is enough.
        /// </summary>
        public static bool PathfindClick(bool enabled, bool useShift, bool singleClick, bool shiftHeld, bool doubleClick) =>
            enabled && (!useShift || shiftHeld) && (singleClick || doubleClick);

        /// <summary>
        /// TazUO's UseDarkNights, for this client: a light level darker than day is half as dark again, never past
        /// <see cref="MaxLightLevel"/>. The shard's night is level 12 of 30, so it becomes 18.
        /// </summary>
        public static int DarkNight(int overall, bool darkNights) =>
            darkNights && overall > 0 ? Math.Min(MaxLightLevel, overall + (overall + 1) / 2) : overall;

        /// <summary>
        /// The overhead health line's fill: green while poisoned, yellow while the hits are "yellow" (blessed,
        /// invulnerable), as TazUO's health lines; null for the usual red-to-blue.
        /// </summary>
        public static (float R, float G, float B)? HealthLineStatusColor(bool poisoned, bool yellowHits) =>
            poisoned ? (0.25f, 0.8f, 0.3f) : yellowHits ? (0.95f, 0.8f, 0.2f) : ((float, float, float)?)null;

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
