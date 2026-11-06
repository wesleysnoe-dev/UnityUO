using System;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>A yes/no question (log out, etc.).</summary>
    public sealed class ConfirmWindow : UiWindow
    {
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
}
