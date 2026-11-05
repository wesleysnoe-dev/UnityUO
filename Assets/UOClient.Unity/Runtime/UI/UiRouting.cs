using System;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Which of the windows and the world a pointer or key goes to, kept free of UnityEngine so the rules can be tested
    /// (UiManager, WorldInput, ChatLine and WorldDemo route by these).
    /// </summary>
    public static class UiRouting
    {
        /// <summary>
        /// A Unity screen point (bottom-left origin, display pixels) in UI pixels (top-left origin), as IMGUI sees it
        /// under UiManager's GUI.matrix scale: Event.mousePosition is (x, height - y), then divided by the scale.
        /// </summary>
        public static (float X, float Y) ScreenToUi(float screenX, float screenY, float screenHeight, float scale) =>
            (screenX / scale, (screenHeight - screenY) / scale);

        /// <summary>
        /// The front window at a UI point: the last in stacking order (back to front) whose rect holds it, by Rect.Contains
        /// (left and top edges in, right and bottom out), or -1. TazUO's GetMouseOverControl (UIManager.cs:941-970) also
        /// takes the first gump front to back that the pointer hits, and nothing behind it.
        /// </summary>
        public static int TopAt(int count, Func<int, (float X, float Y, float W, float H)> rect, float x, float y)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                (float rx, float ry, float rw, float rh) = rect(i);
                if (x >= rx && x < rx + rw && y >= ry && y < ry + rh)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether keys go to the world (walking, view keys, macros, P/B/Tab...): not while the speech line or a window's
        /// text box has the keyboard. TazUO's OnKeyDown stops there for any focused box but the chat's
        /// (GameSceneInputHandler.cs:1405-1408), so a gump's entry swallows them. A held item or an open context menu
        /// does not stop them there.
        /// </summary>
        public static bool WorldTakesKeys(bool chatTyping, bool fieldFocused) => !chatTyping && !fieldFocused;

        /// <summary>Whether Enter opens (or sends) the speech line: not while another text box has the keyboard, whose Enter it is.</summary>
        public static bool EnterGoesToChat(bool chatTyping, bool fieldFocused) => chatTyping || !fieldFocused;

        public enum Press
        {
            /// <summary>Not the world's: a window has it, or it drops a held item.</summary>
            None,
            /// <summary>Over the world with a window's text box focused: gives the keyboard back, and does nothing else.</summary>
            ReleaseField,
            /// <summary>A world click (look, use, attack, target, pick up).</summary>
            World
        }

        /// <summary>
        /// What a mouse press does. Over a window it is the window's (TazUO's IsMouseOverWorld is false with a control under
        /// the pointer, UIManager.cs:91-105), and with an item held the release is its drop. Over the world with a gump's
        /// text box focused, TazUO's press gives the keyboard back (RestoreSystemChatFocus, GameSceneInputHandler.cs:440-442);
        /// here that press does only that, so no use or attack fires from the click that ends typing.
        /// </summary>
        public static Press WorldPress(bool overUi, bool dragging, bool fieldFocused)
        {
            if (overUi || dragging)
                return Press.None;
            return fieldFocused ? Press.ReleaseField : Press.World;
        }
    }
}
