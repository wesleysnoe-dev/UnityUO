namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's QuestionGump (QuestionGump.cs:15-59): the 0x0816 box centered on screen, the question at (33, 30) in hue
    /// 0x0386 (font 1, 165 wide), Cancel at (37, 75) and Okay at (100, 75). TazUO's label is the ASCII font 1; ours is the
    /// unicode font 1, as every other label here.
    /// </summary>
    public sealed partial class QuestionWindow
    {
        public const int Background = 0x0816;
        public const int LabelHue = 0x0386;
        public const int LabelX = 33, LabelY = 30, LabelWidth = 165;

        /// <summary>Button art: normal, pressed, over (TazUO's Button(id, normal, pressed, over)).</summary>
        public const int CancelNormal = 0x0817, CancelPressed = 0x0818, CancelOver = 0x0819;
        public const int OkNormal = 0x081A, OkPressed = 0x081B, OkOver = 0x081C;
        public const int CancelX = 37, OkX = 100, ButtonY = 75;

        /// <summary>The size used when 0x0816 is missing from the art.</summary>
        public const int FallbackWidth = 220, FallbackHeight = 110;

        /// <summary>TazLang's this_may_flag_you_criminal (language.ini:2640).</summary>
        public const string CriminalText = "This may flag\nyou criminal!";

        /// <summary>The screen dims under the question: TazUO's AlphaBlendControl(0.15f), black at 15%.</summary>
        public const float DimAlpha = 0.15f;

        /// <summary>CenterXInScreen / CenterYInScreen: where the box goes on a screen (UO pixels).</summary>
        public static (int X, int Y) Centered(int screenW, int screenH, int boxW, int boxH) =>
            ((screenW - boxW) / 2, (screenH - boxH) / 2);
    }
}
