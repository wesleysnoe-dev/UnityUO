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
        public enum Page { General, Sound, Video, Macros, Tooltip, Speech, CombatSpells, Containers, Nameplates }

        public static readonly (Page Page, string Title)[] Pages =
        {
            (Page.General, "General"),
            (Page.Sound, "Sound"),
            (Page.Video, "Video"),
            (Page.Macros, "Macros"),
            (Page.Tooltip, "Tooltip"),
            (Page.Speech, "Speech"),
            (Page.CombatSpells, "Combat & Spells"),
            (Page.Containers, "Containers"),
            (Page.Nameplates, "Nameplates"),
        };

        /// <summary>TazUO pages this client has no system for yet: shown greyed in the side menu, never opened.</summary>
        public static readonly string[] UnavailablePages =
        {
            "Counters", "Info Bar", "Ignore List", "Cooldowns", "Experimental", "TazUO",
        };

        /// <summary>
        /// TazUO options on the offered pages that are left out, each with why: listed greyed at the foot of its page so
        /// nothing looks like a setting and does nothing.
        /// </summary>
        public static readonly (Page Page, string Text)[] NotOffered =
        {
            (Page.General, "Pathfinding, smooth doors: this client has no pathfinder."),
            (Page.General, "Block walking into doors: walks are left to the server to refuse."),
            (Page.Sound, "Footsteps: the client plays no footstep sounds of its own."),
            (Page.Sound, "Music / sound on-off, combat music: volume only."),
            (Page.Tooltip, "Center mobile tooltips: TazUO reads that option nowhere."),
            (Page.Tooltip, "Tooltip font, colours, background: one fixed style."),
            (Page.Speech, "Chat without Enter: always on here (WASD and keys need the keyboard)."),
            (Page.Speech, "Guild / alliance hues: this client sends no guild chat (AoS)."),
            (Page.Speech, "Override party hue: TazUO's recolours members' bodies, not chat."),
            (Page.CombatSpells, "Spell hues, cursor spell names, HP colours: not drawn."),
            (Page.Containers, "Grid search, sort, quick loot, auto loot, lock slots, bands: not in the grid yet."),
            (Page.Containers, "Grid borders, colours, columns and rows, list view, highlights: default look only."),
            (Page.Containers, "Corpse style, grid loot, nearby loot: corpses follow the original-view option."),
            (Page.Nameplates, "Filter entries (new, delete, hotkeys, search), hold-to-show key: one filter."),
            (Page.Nameplates, "Item and corpse plates: characters only."),
            (Page.Nameplates, "Presets, saved presets, wiki link: set each option here."),
            (Page.Nameplates, "Word of Death icon: it casts a spell from the plate; not added."),
            (Page.Nameplates, "Corner radius, plate font and font size: square plates, the HUD font."),
            (Page.Nameplates, "New character name flash (ShowNewMobileNameIncoming): not drawn."),
        };

        /// <summary>What Defaults, Cancel, Apply and Okay do, shown while the mouse is on each (in that order).</summary>
        public static readonly string[] ButtonHints =
        {
            "Defaults: resets the options (not macros) to the defaults; nothing changes until Apply or Okay.",
            "Cancel: closes, dropping changes not applied. Nothing is saved.",
            "Apply: uses the changes now, for this run only. Nothing is saved.",
            "Okay: uses the changes and saves them (unity-uo-options.json; edited macros: unity-uo-hotkeys.json).",
        };

        public const int Width = 660, Height = 560, MenuX = 16, MenuWidth = 140, ContentX = 172;
    }
}
