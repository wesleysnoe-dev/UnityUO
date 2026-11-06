using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// The speech line: Enter opens it; Enter again sends it, Escape cancels. ": " emotes, "; " whispers, "! " yells,
    /// "/" talks to the party (<see cref="GameSession.Say"/>, which the chat window's Say box shares).
    /// </summary>
    public sealed class ChatLine
    {
        private string _chat = "";

        /// <summary>The line is open: keys are text, not commands.</summary>
        public bool Typing { get; private set; }

        public void Open()
        {
            Typing = true;
            _chat = "";
        }

        /// <summary>The session ended: the line closes, its text unsent.</summary>
        public void Close()
        {
            Typing = false;
            _chat = "";
        }

        public void OnGUI(GameSession session, UI.UiManager ui)
        {
            if (session == null)
                return;
            Event e = Event.current;
            // A window's text box (the chat window, a prompt) has the keyboard: its Enter is its own.
            bool toChat = UI.UiRouting.EnterGoesToChat(Typing, ui != null && ui.HasKeyboardFocus);
            if (toChat && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (Typing && _chat.Trim().Length > 0)
                    session.Say(_chat.Trim());
                Typing = !Typing;
                _chat = "";
                e.Use();
            }
            else if (Typing && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Typing = false;
                _chat = "";
                e.Use();
            }

            if (!Typing)
                return;
            float s = UI.UiScale.Current;
            GUI.SetNextControlName("uo-chat");
            _chat = GUI.TextField(new Rect(Screen.width * 0.5f - 300 * s, Screen.height - 250 * s, 600 * s, 26 * s), _chat, 200);
            GUI.FocusControl("uo-chat");
        }
    }
}
