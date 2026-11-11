using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The bar along the top of the screen, as in the UO Smallz client (TazUO's top bar with the shard's
    /// changes): Map, Paperdoll, Inventory, Journal, Chat (the shard chat window), Help, World Map, Skills,
    /// Status, Party, Groups (the server's Group Finder) and Options. Buttons send their command through <see cref="UiManager.Command"/>.
    /// </summary>
    public sealed class TopBarWindow : UiWindow
    {
        public const float Height = 28;
        private const float ButtonWidth = 74;

        private static readonly (string Label, string Command)[] Buttons =
        {
            ("Map", "minimap"), ("Paperdoll", "paperdoll"), ("Inventory", "backpack"), ("Journal", "journal"),
            ("Chat", "worldchat"), ("Help", "help"), ("World Map", "worldmap"), ("Skills", "skills"),
            ("Status", "status"), ("Party", "party"), ("Groups", "groups"), ("Options", "options"), ("Assistant", "assistant")
        };

        private GUIStyle _button;

        public TopBarWindow()
        {
            Rect = new Rect(0, 0, 8 + Buttons.Length * (ButtonWidth + 2) + 6, Height);
        }

        public override string Key => "topbar";
        public override bool CanClose => false; // right-click does not close it; it is part of the screen
        public override bool Movable => false;

        public override void Draw()
        {
            if (_button == null)
            {
                _button = new GUIStyle(GUI.skin.button) { fontSize = 12, padding = new RectOffset(2, 2, 2, 2) };
                _button.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                _button.hover.textColor = new Color(1f, 0.95f, 0.6f);
            }
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            for (int i = 0; i < Buttons.Length; i++)
                if (GUI.Button(new Rect(8 + i * (ButtonWidth + 2), 3, ButtonWidth, Height - 6), Buttons[i].Label, _button))
                    Ui.Command?.Invoke(Buttons[i].Command);
        }
    }
}
