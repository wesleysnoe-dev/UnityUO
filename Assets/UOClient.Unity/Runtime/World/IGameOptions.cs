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
    }
}
