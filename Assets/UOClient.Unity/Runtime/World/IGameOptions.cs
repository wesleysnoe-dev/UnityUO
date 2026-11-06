namespace UOClient.Unity
{
    /// <summary>The options the options window shows and changes (WorldDemo's facade properties).</summary>
    public interface IGameOptions
    {
        bool ShowNames { get; set; }
        bool ShowHud { get; set; }
        bool ShowJournal { get; set; }
        bool AutoRoofs { get; set; }
        float Zoom { get; set; }

        /// <summary>Sound effects and music (null until online).</summary>
        Audio Sound { get; }

        /// <summary>Keyboard macros (null until online).</summary>
        Hotkeys Hotkeys { get; }

        /// <summary>The options as the game uses them now (keys like R, H and the wheel change some of them).</summary>
        GameOptions CurrentOptions();

        /// <summary>Puts the options into the game now; writes nothing.</summary>
        void ApplyOptions(GameOptions options);

        /// <summary>Writes the options to UserSettings/unity-uo-options.json (the options window's Okay only).</summary>
        void SaveOptions(GameOptions options);
    }
}
