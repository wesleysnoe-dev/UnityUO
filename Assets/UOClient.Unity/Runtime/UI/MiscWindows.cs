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
            Rect = new Rect(Screen.width / 2f / Mathf.Max(0.75f, Screen.height / 1000f) - 160, 200, 320, 140);
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

    /// <summary>Client options: what to show, roofs, zoom.</summary>
    public sealed class OptionsWindow : UiWindow
    {
        private readonly IGameOptions _game;

        public OptionsWindow(IGameOptions game)
        {
            _game = game;
            Rect = new Rect(300, 140, 360, 330 + (game.Hotkeys?.Bindings.Count ?? 0) * 18 + 60);
        }

        public override string Key => "options";

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 12, Rect.width, 22), "Options", new Color(1f, 0.9f, 0.6f), 16, TextAnchor.UpperCenter, true);

            float y = 50;
            bool Toggle(string label, bool value)
            {
                bool v = GUI.Toggle(new Rect(30, y, 280, 22), value, " " + label);
                y += 28;
                return v;
            }
            _game.ShowNames = Toggle("Show names over heads", _game.ShowNames);
            _game.ShowJournal = Toggle("Show the journal", _game.ShowJournal);
            _game.AutoRoofs = Toggle("Hide roofs automatically indoors", _game.AutoRoofs);
            _game.ShowHud = Toggle("Show the HUD (H)", _game.ShowHud);

            Text(new Rect(30, y + 2, 80, 20), "Zoom");
            _game.Zoom = GUI.HorizontalSlider(new Rect(90, y + 6, 200, 20), _game.Zoom, 0.5f, 3f);
            y += 30;
            if (_game.Sound != null)
            {
                Text(new Rect(30, y + 2, 80, 20), "Sound");
                _game.Sound.SoundVolume = GUI.HorizontalSlider(new Rect(90, y + 6, 200, 20), _game.Sound.SoundVolume, 0f, 1f);
                y += 26;
                Text(new Rect(30, y + 2, 80, 20), "Music");
                _game.Sound.MusicVolume = GUI.HorizontalSlider(new Rect(90, y + 6, 200, 20), _game.Sound.MusicVolume, 0f, 1f);
                y += 30;
            }

            Text(new Rect(30, y, 300, 40), "Keys: P paperdoll, B backpack, K skills, C status,\nTab war mode, F1 help, Enter talk, Esc cancel/close,\nShift+click for a context menu", new Color(0.8f, 0.78f, 0.7f), 12);
            y += 54;

            Hotkeys hk = _game.Hotkeys;
            if (hk == null)
                return;
            Text(new Rect(30, y, 200, 20), "Macros", new Color(1f, 0.9f, 0.6f), 14, TextAnchor.UpperLeft, true);
            if (GUI.Button(new Rect(Rect.width - 110, y - 2, 80, 22), "Reload"))
            {
                hk.Load();
                Rect.height = 330 + hk.Bindings.Count * 18 + 60;
            }
            y += 22;
            foreach (Hotkeys.Binding b in hk.Bindings)
            {
                Text(new Rect(30, y, 90, 18), b.key, new Color(0.75f, 0.85f, 1f), 12);
                Text(new Rect(120, y, 220, 18), b.action, new Color(0.9f, 0.88f, 0.8f), 12);
                y += 18;
            }
            Text(new Rect(30, y + 4, Rect.width - 50, 30), "Edit UserSettings/unity-uo-hotkeys.json, then Reload.", new Color(0.7f, 0.66f, 0.55f), 11);
        }
    }
}
