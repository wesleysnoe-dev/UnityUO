namespace UOClient.Unity
{
    /// <summary>What the capture tour reads from the game, so it never reaches into WorldDemo's private fields.</summary>
    internal interface ICaptureHost
    {
        bool Ready { get; }
        string LoadError { get; }
        int? CutZ { get; }
        bool TryStep(int dir, bool run);
        void RunCommand(string cmd);
        FrameStats Frames { get; }
        FrameSections Sections { get; }
        EntityViews Entities { get; }
        EffectsLayer Effects { get; }
        OverheadText Overheads { get; }
        UI.HoverTooltip Hover { get; }
        SessionUiBinder Ui { get; }
    }
}
