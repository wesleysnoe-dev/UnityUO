using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Customize mode's way out (0xBF 0x20 begin to end). Not TazUO's HouseCustomizationGump: there are no design tools
    /// here, and no Commit, which would need the full designer and its validation. It says what the server keeps and
    /// offers one button, Exit (<see cref="Core.GameSession.ExitHouseCustomization"/>, ModernUO's Designer_Close), which
    /// commits nothing and discards nothing. Closing the window any other way would leave the player in customize mode
    /// with no way out, so it cannot be closed: it goes when the server ends customize mode.
    /// </summary>
    public sealed partial class HouseCustomizeWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        private readonly uint _house;
        private GUIStyle _wrap;

        public HouseCustomizeWindow(uint house)
        {
            _house = house;
            Rect = new Rect(40, 120, 300, 230);
        }

        public override string Key => WindowKey;

        public uint House => _house;

        public override bool CanClose => false;

        /// <summary>Only while the server has us customizing this house, in the world.</summary>
        public override bool StillValid =>
            Session.CustomizingHouse == _house && Session.State == Core.SessionState.InWorld;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(12, 10, Rect.width - 24, 20), "House customization", new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperCenter, true);
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12, normal = { textColor = new Color(0.95f, 0.92f, 0.84f) } };
            GUI.Label(new Rect(14, 36, Rect.width - 28, 140), Explanation, _wrap);

            bool pending = Session.HouseExitPending;
            Text(new Rect(14, Rect.height - 62, Rect.width - 28, 18), pending ? "Exit asked: waiting for the server..." : $"House 0x{_house:X8}",
                new Color(0.75f, 0.72f, 0.62f), 11, TextAnchor.UpperCenter);
            GUI.enabled = !pending;
            if (GUI.Button(new Rect(Rect.width / 2 - 50, Rect.height - 40, 100, 26), "Exit"))
                Session.ExitHouseCustomization();
            GUI.enabled = true;
        }
    }
}
