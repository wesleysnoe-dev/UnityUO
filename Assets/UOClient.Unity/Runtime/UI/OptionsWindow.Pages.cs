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
        public enum Page { General, Sound, Video, Display, Macros, Tooltip, Speech, CombatSpells, Containers, Nameplates, Counters, InfoBar, Cooldowns, IgnoreList }

        public static readonly (Page Page, string Title)[] Pages =
        {
            (Page.General, "General"),
            (Page.Sound, "Sound"),
            (Page.Video, "Video"),
            (Page.Display, "Display"),
            (Page.Macros, "Macros"),
            (Page.Tooltip, "Tooltip"),
            (Page.Speech, "Speech"),
            (Page.CombatSpells, "Combat & Spells"),
            (Page.Containers, "Containers"),
            (Page.Nameplates, "Nameplates"),
            (Page.Counters, "Counters"),
            (Page.InfoBar, "Info Bar"),
            (Page.Cooldowns, "Cooldowns"),
            (Page.IgnoreList, "Ignore List"),
        };

        /// <summary>TazUO pages this client has no system for yet: shown greyed in the side menu, never opened.</summary>
        public static readonly string[] UnavailablePages =
        {
            "Experimental", "TazUO",
        };

        /// <summary>
        /// TazUO options on the offered pages that are left out, each with why: listed greyed at the foot of its page so
        /// nothing looks like a setting and does nothing.
        /// </summary>
        public static readonly (Page Page, string Text)[] NotOffered =
        {
            (Page.Counters, "Per-cell hotkeys: the Macros page binds keys."),
            (Page.Cooldowns, "Replace-on-trigger choice: a new trigger always starts the bar over."),
            (Page.InfoBar, "Casting and combat modifiers (lower reagent cost, faster casting...): the server sends none."),
            (Page.General, "Smooth doors, auto-follow: not in this client."),
            (Page.General, "Block walking into doors: walks are left to the server to refuse."),
            (Page.Sound, "Heartbeat at low health: not played."),
            (Page.Video, "Coloured / alternative lights: lights keep light.mul's colours."),
            (Page.Video, "Rock and tree shadows apart, terrain shadow level: one shadows switch."),
            (Page.Video, "Fixed sun: its place sets day, night and the shadows' side; Sun shadows off instead."),
            (Page.Video, "Natural terrain, grass, smooth ground: experimental, launch arguments only."),
            (Page.Video, "Target aura, mouse thread, upscaling filters: not in this client."),
            (Page.Display, "Viewport size, position and lock: the world always fills the window."),
            (Page.Display, "Shadow quality, anti-aliasing: shadows are the client's own, no setting behind."),
            (Page.Tooltip, "Center mobile tooltips: TazUO reads that option nowhere."),
            (Page.Tooltip, "Tooltip font face, border colour: one font, no border."),
            (Page.Speech, "Chat without Enter: always on here (WASD and keys need the keyboard)."),
            (Page.Speech, "Override party hue: TazUO's recolours members' bodies, not chat."),
            (Page.CombatSpells, "Spell hues, cursor spell names: not drawn."),
            (Page.Containers, "Auto loot lists, locked slots, bands, saved window sizes: not in the grid yet."),
            (Page.Containers, "Border styles, property highlights, list view: one border style, no highlights."),
            (Page.Containers, "Nearby loot window: not in this client."),
            (Page.Nameplates, "Filter entries (new, delete, hotkeys, search), hold-to-show key: one filter."),
            (Page.Nameplates, "Item and corpse plates: characters only."),
            (Page.Nameplates, "Presets, saved presets, wiki link: set each option here."),
            (Page.Nameplates, "Word of Death icon: it casts a spell from the plate; not added."),
            (Page.Nameplates, "Corner radius, plate font face: square plates, the HUD font."),
            (Page.Nameplates, "New character name flash: names over heads already show everyone in view."),
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
