namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the pages.

    /// <summary>
    /// The options window's pages, named and ordered as TazUO's ModernOptionsGump side menu (ModernOptionsGump.cs PAGE).
    /// Only pages with working code behind every control are offered; the others are listed as unavailable, with no
    /// controls (nothing that looks like a setting and does nothing).
    /// </summary>
    public sealed partial class OptionsWindow
    {
        public enum Page { General, Sound, Video, Macros, Tooltip, CombatSpells }

        public static readonly (Page Page, string Title)[] Pages =
        {
            (Page.General, "General"),
            (Page.Sound, "Sound"),
            (Page.Video, "Video"),
            (Page.Macros, "Macros"),
            (Page.Tooltip, "Tooltip"),
            (Page.CombatSpells, "Combat & Spells"),
        };

        /// <summary>TazUO pages this client has no system for yet: shown greyed in the side menu, never opened.</summary>
        public static readonly string[] UnavailablePages =
        {
            "Speech", "Counters", "Info Bar", "Containers", "Ignore List", "Nameplates", "Cooldowns", "Experimental", "TazUO",
        };

        /// <summary>What Defaults, Cancel, Apply and Okay do, shown while the mouse is on each (in that order).</summary>
        public static readonly string[] ButtonHints =
        {
            "Defaults: resets this window to the defaults; nothing changes until Apply or Okay.",
            "Cancel: closes, dropping changes not applied. Nothing is saved.",
            "Apply: uses the changes now, for this run only. Nothing is saved.",
            "Okay: uses the changes and saves them (UserSettings/unity-uo-options.json).",
        };

        public const int Width = 600, Height = 440, MenuX = 16, MenuWidth = 140, ContentX = 172;
    }
}
