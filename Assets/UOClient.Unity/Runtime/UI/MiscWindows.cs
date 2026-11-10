using System;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>A yes/no question (log out, etc.).</summary>
    public sealed class ConfirmWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        private readonly string _title, _text;
        private readonly Action _yes;
        private GUIStyle _wrap;

        public ConfirmWindow(string title, string text, Action yes)
        {
            _title = title;
            _text = text;
            _yes = yes;
            Rect = new Rect(Screen.width / 2f / UiScale.Current - 160, 200, 320, 140);
        }

        public override string Key => "confirm:" + _title;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 12, Rect.width, 22), _title, new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);
            // Its own style: changing GUI.skin.label would make every label in the game wrap from now on.
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14, normal = { textColor = new Color(0.95f, 0.92f, 0.84f) } };
            GUI.Label(new Rect(24, 42, Rect.width - 48, 40), _text, _wrap);
            if (GUI.Button(new Rect(40, Rect.height - 44, 100, 26), "Yes"))
            {
                Close();
                _yes();
            }
            if (GUI.Button(new Rect(Rect.width - 140, Rect.height - 44, 100, 26), "No"))
                Close();
        }
    }

    /// <summary>
    /// The server asked to open a link (0xA5). TazUO launches it at once; here the player sees the host and the whole URL
    /// first, and only Open hands it to the browser, checked again as http/https (<see cref="GameSession.TryParseSafeUrl"/>).
    /// Cancel, a right-click or Esc opens nothing.
    /// </summary>
    public sealed class OpenUrlWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        public const string WindowKey = "openurl";

        private readonly string _url;
        private GUIStyle _wrap;

        /// <summary><paramref name="url"/> must already have passed <see cref="GameSession.TryParseSafeUrl"/>.</summary>
        public OpenUrlWindow(string url)
        {
            _url = url;
            Rect = new Rect(Screen.width / 2f / UiScale.Current - 200, 180, 400, 190);
        }

        public override string Key => WindowKey;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 12, Rect.width, 22), "Open link?", new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);
            bool safe = GameSession.TryParseSafeUrl(_url, out Uri uri);
            // Plain text, never rich text: the server chose these characters.
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 13, clipping = TextClipping.Clip, normal = { textColor = new Color(0.95f, 0.92f, 0.84f) } };
            GUI.Label(new Rect(24, 38, Rect.width - 48, 20), "The server wants to open a page on " + (uri?.Host ?? "?") + ":", _wrap);
            GUI.Label(new Rect(24, 60, Rect.width - 48, 64), _url, _wrap);
            if (safe && GUI.Button(new Rect(40, Rect.height - 44, 120, 26), "Open in browser"))
            {
                Close();
                Application.OpenURL(uri.AbsoluteUri);
            }
            if (GUI.Button(new Rect(Rect.width - 160, Rect.height - 44, 120, 26), "Cancel"))
                Close();
        }
    }
}
