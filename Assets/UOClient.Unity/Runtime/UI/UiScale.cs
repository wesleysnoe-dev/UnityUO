using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The one UI scale the windows (UiManager.Scale, so the mouse picks too), the HUD, the chat line and the login screen
    /// draw at: Screen.height / 1000 within 0.75–2.5, unless the options fix it (<see cref="GameOptions.uiScale"/>).
    /// </summary>
    public static class UiScale
    {
        /// <summary>0: follow the screen height; otherwise the fixed scale.</summary>
        public static float Setting { get; set; } = GameOptions.AutoUiScale;

        public static float Current => GameOptions.ResolveUiScale(Screen.height, Setting);
    }
}
