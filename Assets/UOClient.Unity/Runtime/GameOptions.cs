using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The player's client options (the options window's settings), kept in their own file,
    /// <c>UserSettings/unity-uo-options.json</c> (<see cref="FileName"/>), so the connection settings in
    /// unity-uo-settings.json are never rewritten for them. Only options with working code behind them are here. Every
    /// value is clamped to its range by <see cref="Clamp"/> on load and before it is applied.
    /// </summary>
    [Serializable]
    public sealed partial class GameOptions
    {
        public const string FileName = "unity-uo-options.json";

        public const float MinZoom = 0.25f, MaxZoom = 4f;

        /// <summary>TazUO's Constants.MIN/MAX_CONTAINER_SIZE_PERC (ContainersScale, percent).</summary>
        public const int MinContainersScale = 50, MaxContainersScale = 200;

        /// <summary>TazUO's speech delay slider range (ModernOptionsGump.BuildSpeech, 0-1000).</summary>
        public const int MinSpeechDelay = 0, MaxSpeechDelay = 1000;

        /// <summary>The fixed UI scale's range; 0 (<see cref="AutoUiScale"/>) follows the screen height.</summary>
        public const float MinUiScale = 0.75f, MaxUiScale = 2.5f;

        public const float AutoUiScale = 0f;
        public const float MaxTooltipDelay = 2f;

        public bool showNames = true;
        public bool showJournal = true;
        public bool showHud = true;
        public bool autoRoofs = true;
        public float zoom = 1f;

        /// <summary>0: Screen.height / 1000 (the default); otherwise the scale every window and the HUD draw at.</summary>
        public float uiScale = AutoUiScale;

        /// <summary>
        /// TazUO's HighlightGameObjects: characters, corpses and fixtures get the mouse-over outline too (other ground
        /// items always do). On by default here (ObjectHighlight). The cue is the outline only; there is no fill to choose.
        /// </summary>
        public bool highlightGameObjects = true;

        /// <summary>Seconds the mouse rests on something before its tooltip shows.</summary>
        public float tooltipDelay = 0.4f;

        public float soundVolume = 0.8f;
        public float musicVolume = 0.45f;

        /// <summary>TazUO's EnabledCriminalActionQuery (GameSession.CriminalActionQuery): ask before attacking an innocent. On by default.</summary>
        public bool criminalActionQuery = true;

        /// <summary>TazUO's EnabledBeneficialCriminalActionQuery (GameSession.BeneficialCriminalActionQuery): ask before helping a criminal or gray. Off by default.</summary>
        public bool beneficialCriminalActionQuery = false;

        /// <summary>
        /// TazUO's AlwaysRun (PlayerMovement): every step runs, Shift or not. Off by default here (TazUO: on), so walking
        /// stays as it was until chosen.
        /// </summary>
        public bool alwaysRun = false;

        /// <summary>TazUO's AlwaysRunUnlessHidden: with always run on, a hidden player walks (so stealth isn't broken). On, as TazUO.</summary>
        public bool alwaysRunUnlessHidden = true;

        /// <summary>TazUO's ScaleSpeechDelay: overhead speech stays longer for longer lines (<see cref="SpeechSeconds"/>). On, as TazUO.</summary>
        public bool scaleSpeechDelay = true;

        /// <summary>TazUO's SpeechDelay (0-1000, default 100): how long overhead speech stays (<see cref="SpeechSeconds"/>).</summary>
        public int speechDelay = 100;

        /// <summary>TazUO's SaveJournalToFile: every journal line is also appended to Logs/JournalLogs (JournalFile). Off, as TazUO.</summary>
        public bool saveJournalToFile = false;

        /// <summary>TazUO's ContainersScale (percent, 50-200): container gumps and their items draw at this size (ContainerWindow).</summary>
        public int containersScale = 100;

        /// <summary>
        /// TazUO's DoubleClickToLootInsideContainers: a double-click on a plain item in a corpse or a chest on the ground moves
        /// it to the backpack instead of using it (<see cref="LootsOnDoubleClick"/>). Off, as TazUO.
        /// </summary>
        public bool doubleClickToLootInsideContainers = false;

        /// <summary>
        /// TazUO's HoldShiftToSplitStack (<see cref="OpensSplitMenu"/>): on, a stack dragged with Shift asks how many; off, a
        /// plain drag asks and Shift lifts the lot. On by default here (TazUO: off), as this client always worked.
        /// </summary>
        public bool holdShiftToSplitStack = true;

        /// <summary>TazUO's UseTooltip (Tooltip.SetText): off hides the text tooltips of windows (buffs, gump elements, spell icons), not item or mobile properties.</summary>
        public bool useTooltip = true;

        /// <summary>TazUO's BuffBarTime: the time left drawn over each timed buff icon. On by default here (TazUO: off), as this client always drew it.</summary>
        public bool buffBarTime = true;

        /// <summary>TazUO's CastSpellsByOneClick (GlobalSettings.SingleClickIconUse): a single click on a floating spell icon casts it. Off, as TazUO.</summary>
        public bool castSpellsByOneClick = false;

        public static GameOptions Defaults() => new GameOptions();

        public GameOptions Clone() => (GameOptions)MemberwiseClone();

        /// <summary>Brings every value into its range (NaN takes the default); returns this.</summary>
        public GameOptions Clamp()
        {
            var d = Defaults();
            zoom = Range(zoom, MinZoom, MaxZoom, d.zoom);
            uiScale = float.IsNaN(uiScale) || uiScale <= 0 ? AutoUiScale : Range(uiScale, MinUiScale, MaxUiScale, d.uiScale);
            tooltipDelay = Range(tooltipDelay, 0f, MaxTooltipDelay, d.tooltipDelay);
            soundVolume = Range(soundVolume, 0f, 1f, d.soundVolume);
            musicVolume = Range(musicVolume, 0f, 1f, d.musicVolume);
            speechDelay = Math.Max(MinSpeechDelay, Math.Min(MaxSpeechDelay, speechDelay));
            containersScale = Math.Max(MinContainersScale, Math.Min(MaxContainersScale, containersScale));
            return this;
        }

        private static float Range(float v, float min, float max, float fallback) =>
            float.IsNaN(v) ? fallback : Math.Max(min, Math.Min(max, v));

        /// <summary>The UI scale to draw at: the fixed one, or (auto) Screen.height / 1000 within the same range.</summary>
        public static float ResolveUiScale(float screenHeight, float uiScale) =>
            uiScale > 0 ? Math.Max(MinUiScale, Math.Min(MaxUiScale, uiScale))
                        : Math.Max(MinUiScale, Math.Min(MaxUiScale, screenHeight / 1000f));

        public bool SameAs(GameOptions o) =>
            o != null && showNames == o.showNames && showJournal == o.showJournal && showHud == o.showHud && autoRoofs == o.autoRoofs
            && zoom == o.zoom && uiScale == o.uiScale && highlightGameObjects == o.highlightGameObjects
            && tooltipDelay == o.tooltipDelay && soundVolume == o.soundVolume && musicVolume == o.musicVolume
            && criminalActionQuery == o.criminalActionQuery && beneficialCriminalActionQuery == o.beneficialCriminalActionQuery
            && alwaysRun == o.alwaysRun && alwaysRunUnlessHidden == o.alwaysRunUnlessHidden
            && scaleSpeechDelay == o.scaleSpeechDelay && speechDelay == o.speechDelay && saveJournalToFile == o.saveJournalToFile
            && containersScale == o.containersScale && doubleClickToLootInsideContainers == o.doubleClickToLootInsideContainers
            && holdShiftToSplitStack == o.holdShiftToSplitStack && useTooltip == o.useTooltip
            && buffBarTime == o.buffBarTime && castSpellsByOneClick == o.castSpellsByOneClick;

        // ---- the file: a flat JSON object; keys this version doesn't know are kept as they were ----

        /// <summary>The options in <paramref name="json"/> (missing keys keep their defaults, bad values too), clamped.</summary>
        public static GameOptions Parse(string json) => Parse(json, out _);

        /// <summary>As <see cref="Parse(string)"/>, also returning the keys it doesn't know, raw, to write back unchanged.</summary>
        public static GameOptions Parse(string json, out List<KeyValuePair<string, string>> unknown)
        {
            var o = Defaults();
            unknown = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryRead(json, out var fields))
                return o;
            foreach (var (key, raw) in fields)
            {
                switch (key)
                {
                    case "showNames": Bool(raw, ref o.showNames); break;
                    case "showJournal": Bool(raw, ref o.showJournal); break;
                    case "showHud": Bool(raw, ref o.showHud); break;
                    case "autoRoofs": Bool(raw, ref o.autoRoofs); break;
                    case "highlightGameObjects": Bool(raw, ref o.highlightGameObjects); break;
                    case "zoom": Number(raw, ref o.zoom); break;
                    case "uiScale": Number(raw, ref o.uiScale); break;
                    case "tooltipDelay": Number(raw, ref o.tooltipDelay); break;
                    case "soundVolume": Number(raw, ref o.soundVolume); break;
                    case "musicVolume": Number(raw, ref o.musicVolume); break;
                    case "criminalActionQuery": Bool(raw, ref o.criminalActionQuery); break;
                    case "beneficialCriminalActionQuery": Bool(raw, ref o.beneficialCriminalActionQuery); break;
                    case "alwaysRun": Bool(raw, ref o.alwaysRun); break;
                    case "alwaysRunUnlessHidden": Bool(raw, ref o.alwaysRunUnlessHidden); break;
                    case "scaleSpeechDelay": Bool(raw, ref o.scaleSpeechDelay); break;
                    case "speechDelay": Integer(raw, ref o.speechDelay); break;
                    case "saveJournalToFile": Bool(raw, ref o.saveJournalToFile); break;
                    case "containersScale": Integer(raw, ref o.containersScale); break;
                    case "doubleClickToLootInsideContainers": Bool(raw, ref o.doubleClickToLootInsideContainers); break;
                    case "holdShiftToSplitStack": Bool(raw, ref o.holdShiftToSplitStack); break;
                    case "useTooltip": Bool(raw, ref o.useTooltip); break;
                    case "buffBarTime": Bool(raw, ref o.buffBarTime); break;
                    case "castSpellsByOneClick": Bool(raw, ref o.castSpellsByOneClick); break;
                    default: unknown.Add(new KeyValuePair<string, string>(key, raw)); break;
                }
            }
            return o.Clamp();
        }

        private static void Bool(string raw, ref bool field)
        {
            if (raw == "true") field = true;
            else if (raw == "false") field = false;
        }

        private static void Number(string raw, ref float field)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                field = v;
        }

        /// <summary>A whole number; a fraction is rounded, anything out of int's range or not a number is ignored (Clamp then ranges it).</summary>
        private static void Integer(string raw, ref int field)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v)
                && v >= int.MinValue && v <= int.MaxValue)
                field = (int)Math.Round(v);
        }

        /// <summary>The file's text: these options, then <paramref name="unknown"/> as they were read.</summary>
        public string ToJson(IEnumerable<KeyValuePair<string, string>> unknown = null)
        {
            var sb = new StringBuilder("{\n");
            void Add(string key, string raw) => sb.Append("  \"").Append(key).Append("\": ").Append(raw).Append(",\n");
            string B(bool b) => b ? "true" : "false";
            string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
            Add("showNames", B(showNames));
            Add("showJournal", B(showJournal));
            Add("showHud", B(showHud));
            Add("autoRoofs", B(autoRoofs));
            Add("zoom", F(zoom));
            Add("uiScale", F(uiScale));
            Add("highlightGameObjects", B(highlightGameObjects));
            Add("tooltipDelay", F(tooltipDelay));
            Add("soundVolume", F(soundVolume));
            Add("musicVolume", F(musicVolume));
            Add("criminalActionQuery", B(criminalActionQuery));
            Add("beneficialCriminalActionQuery", B(beneficialCriminalActionQuery));
            Add("alwaysRun", B(alwaysRun));
            Add("alwaysRunUnlessHidden", B(alwaysRunUnlessHidden));
            Add("scaleSpeechDelay", B(scaleSpeechDelay));
            Add("speechDelay", speechDelay.ToString(CultureInfo.InvariantCulture));
            Add("saveJournalToFile", B(saveJournalToFile));
            Add("containersScale", containersScale.ToString(CultureInfo.InvariantCulture));
            Add("doubleClickToLootInsideContainers", B(doubleClickToLootInsideContainers));
            Add("holdShiftToSplitStack", B(holdShiftToSplitStack));
            Add("useTooltip", B(useTooltip));
            Add("buffBarTime", B(buffBarTime));
            Add("castSpellsByOneClick", B(castSpellsByOneClick));
            if (unknown != null)
                foreach (var kv in unknown)
                    Add(FlatJson.Escape(kv.Key), kv.Value);
            sb.Length -= 2; // the last ",\n"
            return sb.Append("\n}\n").ToString();
        }
    }

    /// <summary>
    /// The options window's working copy (TazUO's ModernOptionsGump edits the profile straight away; here edits wait):
    /// controls change <see cref="Draft"/>; <see cref="Apply"/> puts it into the game now, <see cref="Okay"/> applies and
    /// saves, <see cref="Cancel"/> drops it, <see cref="Defaults"/> resets it to the code defaults. Nothing is written
    /// except by <see cref="Okay"/>.
    /// </summary>
    public sealed class OptionsDraft
    {
        private readonly Func<GameOptions> _read;
        private readonly Action<GameOptions> _apply;
        private readonly Action<GameOptions> _save;

        /// <summary><paramref name="read"/> captures the game's options as they are now (keys may have changed them).</summary>
        public OptionsDraft(Func<GameOptions> read, Action<GameOptions> apply, Action<GameOptions> save)
        {
            _read = read;
            _apply = apply;
            _save = save;
            Draft = _read().Clone().Clamp();
        }

        public GameOptions Draft { get; private set; }

        /// <summary>The draft differs from what the game uses now.</summary>
        public bool Changed => !Draft.SameAs(_read());

        public void Apply()
        {
            Draft.Clamp();
            _apply(Draft.Clone());
            Draft = _read().Clone().Clamp();
        }

        public void Okay()
        {
            Apply();
            _save(Draft.Clone());
        }

        public void Cancel() => Draft = _read().Clone().Clamp();

        public void Defaults() => Draft = GameOptions.Defaults();
    }

    /// <summary>A flat JSON object's members as raw value text (strings keep their quotes); nested values are kept whole.</summary>
    internal static class FlatJson
    {
        public static bool TryRead(string s, out List<(string Key, string Raw)> fields)
        {
            fields = new List<(string, string)>();
            int i = 0;
            Skip(s, ref i);
            if (i >= s.Length || s[i] != '{')
                return false;
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}')
                return true;
            while (i < s.Length)
            {
                Skip(s, ref i);
                int keyStart = i;
                if (!ScanString(s, ref i))
                    return false;
                string key = Unescape(s.Substring(keyStart + 1, i - keyStart - 2));
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    return false;
                i++;
                Skip(s, ref i);
                int valueStart = i;
                if (!ScanValue(s, ref i))
                    return false;
                fields.Add((key, s.Substring(valueStart, i - valueStart).Trim()));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') return true;
                return false;
            }
            return false;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static bool ScanString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
                return false;
            for (i++; i < s.Length; i++)
            {
                if (s[i] == '\\') i++;
                else if (s[i] == '"') { i++; return true; }
            }
            return false;
        }

        private static bool ScanValue(string s, ref int i)
        {
            if (i >= s.Length)
                return false;
            if (s[i] == '"')
                return ScanString(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"') { if (!ScanString(s, ref i)) return false; continue; }
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') depth--;
                    i++;
                    if (depth == 0) return true;
                }
                return false;
            }
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i]))
                i++;
            return i > start;
        }

        private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

        public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
