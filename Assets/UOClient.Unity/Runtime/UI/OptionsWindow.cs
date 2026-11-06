using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Client options in TazUO's ModernOptionsGump pages (<see cref="Pages"/>). The controls change an
    /// <see cref="OptionsDraft"/> only: Apply puts it into the game now, Okay applies it and saves
    /// UserSettings/unity-uo-options.json, Cancel (or closing the window) drops it, Defaults resets the draft to the code
    /// defaults. Nothing else writes the file.
    /// </summary>
    public sealed partial class OptionsWindow : UiWindow
    {
        private static readonly Color Heading = new Color(1f, 0.9f, 0.6f);
        private static readonly Color Note = new Color(0.8f, 0.78f, 0.7f);
        private static readonly Color Greyed = new Color(0.55f, 0.52f, 0.47f);

        private readonly IGameOptions _game;
        private readonly OptionsDraft _draft;
        private Page _page = Page.General;
        private Vector2 _macroScroll;

        public OptionsWindow(IGameOptions game)
        {
            _game = game;
            _draft = new OptionsDraft(game.CurrentOptions, game.ApplyOptions, game.SaveOptions);
            Rect = new Rect(260, 120, Width, Height);
        }

        public override string Key => "options";

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(0, 12, Rect.width, 22), "Options", Heading, 16, TextAnchor.UpperCenter, true);

            DrawMenu();
            GameOptions d = _draft.Draft;
            var area = new Rect(ContentX, 50, Rect.width - ContentX - 20, Rect.height - 100);
            GUI.BeginGroup(area);
            switch (_page)
            {
                case Page.General: DrawGeneral(d); break;
                case Page.Sound: DrawSound(d); break;
                case Page.Video: DrawVideo(d); break;
                case Page.Macros: DrawMacros(area); break;
                case Page.Tooltip: DrawTooltip(d); break;
                case Page.CombatSpells: DrawCombat(d); break;
            }
            GUI.EndGroup();
            DrawButtons();
        }

        private void DrawMenu()
        {
            float y = 50;
            foreach (var (page, title) in Pages)
            {
                if (GUI.Toggle(new Rect(MenuX, y, MenuWidth, 24), _page == page, title, GUI.skin.button) && _page != page)
                    _page = page;
                y += 28;
            }
            y += 8;
            Text(new Rect(MenuX, y, MenuWidth, 18), "Not in this client yet:", Greyed, 11);
            y += 16;
            foreach (string title in UnavailablePages)
            {
                Text(new Rect(MenuX + 6, y, MenuWidth - 6, 16), title + " (unavailable)", Greyed, 11);
                y += 15;
            }
        }

        private void DrawButtons()
        {
            float y = Rect.height - 42;
            if (GUI.Button(new Rect(MenuX, y, 90, 26), "Defaults"))
                _draft.Defaults();
            if (_draft.Changed)
                Text(new Rect(MenuX + 100, y + 5, 150, 20), "Not applied yet", Note, 12);
            float x = Rect.width - 20 - 3 * 86;
            if (GUI.Button(new Rect(x, y, 80, 26), "Cancel"))
            {
                _draft.Cancel();
                Close();
            }
            if (GUI.Button(new Rect(x + 86, y, 80, 26), "Apply"))
                _draft.Apply();
            if (GUI.Button(new Rect(x + 172, y, 80, 26), "Okay"))
            {
                _draft.Okay();
                Close();
            }
        }

        // ---- page helpers: y runs down the page ----

        private float _y;

        private bool Toggle(string label, bool value)
        {
            bool v = GUI.Toggle(new Rect(0, _y, 400, 22), value, " " + label);
            _y += 28;
            return v;
        }

        private float Slider(string label, float value, float min, float max, string shown)
        {
            Text(new Rect(0, _y + 2, 110, 20), label);
            float v = GUI.HorizontalSlider(new Rect(115, _y + 6, 200, 20), value, min, max);
            Text(new Rect(325, _y + 2, 80, 20), shown);
            _y += 30;
            return v;
        }

        private void Line(string text, float height = 20)
        {
            Text(new Rect(0, _y, 400, height), text, Note, 12);
            _y += height + 4;
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100) + "%";

        // ---- pages ----

        private void DrawGeneral(GameOptions d)
        {
            _y = 0;
            d.showNames = Toggle("Show names over heads", d.showNames);
            d.showJournal = Toggle("Show the journal", d.showJournal);
            d.showHud = Toggle("Show the HUD (H)", d.showHud);
            d.autoRoofs = Toggle("Hide roofs automatically indoors (R overrides)", d.autoRoofs);
            d.highlightGameObjects = Toggle("Highlight game objects", d.highlightGameObjects);
            Line("With it on, characters, corpses and fixtures get the mouse-over\noutline too; other items always do.", 34);
        }

        private void DrawSound(GameOptions d)
        {
            _y = 0;
            d.soundVolume = Slider("Sound volume", d.soundVolume, 0f, 1f, Percent(d.soundVolume));
            d.musicVolume = Slider("Music volume", d.musicVolume, 0f, 1f, Percent(d.musicVolume));
            if (_game.Sound == null)
                Line("Sound starts when you are online; these take effect then.");
        }

        private void DrawVideo(GameOptions d)
        {
            _y = 0;
            d.zoom = Slider("Zoom", d.zoom, GameOptions.MinZoom, GameOptions.MaxZoom, d.zoom.ToString("0.00") + "x");
            Line("The mouse wheel changes the zoom too.");
            bool auto = d.uiScale <= 0;
            bool nowAuto = Toggle("UI scale follows the screen height", auto);
            if (auto && !nowAuto)
                d.uiScale = GameOptions.ResolveUiScale(Screen.height, GameOptions.AutoUiScale);
            else if (!auto && nowAuto)
                d.uiScale = GameOptions.AutoUiScale;
            if (d.uiScale > 0)
                d.uiScale = Slider("UI scale", d.uiScale, GameOptions.MinUiScale, GameOptions.MaxUiScale, d.uiScale.ToString("0.00") + "x");
            else
                Line($"UI scale: {GameOptions.ResolveUiScale(Screen.height, GameOptions.AutoUiScale):0.00}x (auto)");
        }

        private void DrawTooltip(GameOptions d)
        {
            _y = 0;
            d.tooltipDelay = Slider("Delay", d.tooltipDelay, 0f, GameOptions.MaxTooltipDelay, d.tooltipDelay.ToString("0.00") + " s");
            Line("How long the mouse rests on something before its tooltip shows.");
        }

        private void DrawCombat(GameOptions d)
        {
            _y = 0;
            d.criminalActionQuery = Toggle("Query before attacking an innocent", d.criminalActionQuery);
            d.beneficialCriminalActionQuery = Toggle("Query before beneficial acts on criminals or grays", d.beneficialCriminalActionQuery);
            Line("Both ask \"This may flag you criminal!\" before anything is sent.");
        }

        private void DrawMacros(Rect area)
        {
            _y = 0;
            Hotkeys hk = _game.Hotkeys;
            if (hk == null)
            {
                Line("Macros load when you are online.");
                return;
            }
            Text(new Rect(0, 0, 200, 20), "Key", Heading, 13, TextAnchor.UpperLeft, true);
            Text(new Rect(100, 0, 200, 20), "Action", Heading, 13, TextAnchor.UpperLeft, true);
            if (GUI.Button(new Rect(area.width - 80, -2, 80, 22), "Reload"))
                hk.Load();
            var list = new Rect(0, 24, area.width, area.height - 90);
            var bindings = hk.Bindings;
            GUI.BeginGroup(list);
            _macroScroll = GUI.BeginScrollView(new Rect(0, 0, list.width, list.height), _macroScroll,
                new Rect(0, 0, list.width - 20, bindings.Count * 18));
            float y = 0;
            foreach (Hotkeys.Binding b in bindings)
            {
                Text(new Rect(0, y, 95, 18), b.key, new Color(0.75f, 0.85f, 1f), 12);
                Text(new Rect(100, y, list.width - 120, 18), b.action, new Color(0.9f, 0.88f, 0.8f), 12);
                y += 18;
            }
            GUI.EndScrollView();
            GUI.EndGroup();
            _y = list.yMax + 6;
            Line("Edit UserSettings/unity-uo-hotkeys.json, then Reload.");
            Line("Keys: P paperdoll, B backpack, K skills, C status, Tab war mode,\nF1 help, Enter talk, Esc cancel/close, Shift+click context menu.", 34);
        }
    }
}
