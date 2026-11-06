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
    public sealed class GameOptions
    {
        public const string FileName = "unity-uo-options.json";

        public const float MinZoom = 0.25f, MaxZoom = 4f;

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
            && criminalActionQuery == o.criminalActionQuery && beneficialCriminalActionQuery == o.beneficialCriminalActionQuery;

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
