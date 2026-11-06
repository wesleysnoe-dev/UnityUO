namespace UOClient.Unity.UI
{
    /// <summary>The customize window's words (kept apart from Unity so the tests can read them).</summary>
    public sealed partial class HouseCustomizeWindow
    {
        public const string WindowKey = "housecustomize";

        /// <summary>What Exit does on ModernUO (Designer_Close): no commit, the committed house stays, the draft is kept.</summary>
        public const string Explanation =
            "You are customizing this house. This client has no house designer: you cannot place, remove or commit anything " +
            "here.\n\nExit leaves customize mode without committing. The house keeps the design it had; any unfinished " +
            "design stays saved on the server as a draft for the next time you customize, and nothing is discarded.";
    }
}