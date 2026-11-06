using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Client options in TazUO's ModernOptionsGump pages (<see cref="Pages"/>). The controls change an
    /// <see cref="OptionsDraft"/> only: Apply puts it into the game now, Okay applies it and saves
    /// UserSettings/unity-uo-options.json, Cancel (or closing the window) drops it, Defaults resets the draft to the code
    /// defaults. Nothing else writes the file. The Macros page edits a <see cref="MacroDraft"/> the same way: Apply binds
    /// it now, Okay also writes UserSettings/unity-uo-hotkeys.json (only if the macros were edited), Cancel drops it.
    /// </summary>
    public sealed partial class OptionsWindow : UiWindow
    {
        private static readonly Color Heading = new Color(1f, 0.9f, 0.6f);
        private static readonly Color Note = new Color(0.8f, 0.78f, 0.7f);
        private static readonly Color Greyed = new Color(0.55f, 0.52f, 0.47f);
        private static readonly Color ProblemColor = new Color(1f, 0.55f, 0.45f);

        private readonly IGameOptions _game;
        private readonly OptionsDraft _draft;
        private MacroDraft _macros; // made when the hotkeys exist (online)
        private Page _page = Page.General;
        private Vector2 _macroScroll;

        public OptionsWindow(IGameOptions game)
        {
            _game = game;
            _draft = new OptionsDraft(game.CurrentOptions, game.ApplyOptions, game.SaveOptions);
            Rect = new Rect(240, 100, Width, Height);
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
            _y = 0;
            switch (_page)
            {
                case Page.General: DrawGeneral(d); break;
                case Page.Sound: DrawSound(d); break;
                case Page.Video: DrawVideo(d); break;
                case Page.Macros: DrawMacros(area); break;
                case Page.Tooltip: DrawTooltip(d); break;
                case Page.Speech: DrawSpeech(d); break;
                case Page.CombatSpells: DrawCombat(d); break;
                case Page.Containers: DrawContainers(d); break;
            }
            if (_page != Page.Macros)
                DrawNotOffered(area);
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
            if (GUI.Button(new Rect(MenuX, y, 90, 26), new GUIContent("Defaults", ButtonHints[0])))
                _draft.Defaults();
            if (_draft.Changed || _macros != null && _macros.Changed)
                Text(new Rect(MenuX + 100, y + 5, 150, 20), "Not applied yet", Note, 12);
            float x = Rect.width - 20 - 3 * 86;
            if (GUI.Button(new Rect(x, y, 80, 26), new GUIContent("Cancel", ButtonHints[1])))
            {
                _draft.Cancel();
                _macros?.Cancel();
                Close();
            }
            if (GUI.Button(new Rect(x + 86, y, 80, 26), new GUIContent("Apply", ButtonHints[2])))
            {
                _draft.Apply();
                _macros?.Apply();
            }
            if (GUI.Button(new Rect(x + 172, y, 80, 26), new GUIContent("Okay", ButtonHints[3])))
            {
                _draft.Okay();
                _macros?.Okay();
                // A macro file that could not be written keeps the window open, with the reason on the page (an unreadable
                // one is never written, so that is no reason to stay).
                if (_macros == null || !_macros.SaveFailed || _game.Hotkeys == null || _game.Hotkeys.ReadOnly)
                    Close();
                else
                    _page = Page.Macros;
            }
            // The hovered button's hint, just above the row.
            if (!string.IsNullOrEmpty(GUI.tooltip))
                Text(new Rect(MenuX, y - 20, Rect.width - 2 * MenuX, 18), GUI.tooltip, Note, 12);
        }

        // ---- page helpers: y runs down the page ----

        private float _y;
        private float _indent;

        private bool Toggle(string label, bool value)
        {
            bool v = GUI.Toggle(new Rect(_indent, _y, 440 - _indent, 22), value, " " + label);
            _y += 28;
            return v;
        }

        private float Slider(string label, float value, float min, float max, string shown)
        {
            Text(new Rect(_indent, _y + 2, 120 - _indent, 20), label);
            float v = GUI.HorizontalSlider(new Rect(125, _y + 6, 200, 20), value, min, max);
            Text(new Rect(335, _y + 2, 90, 20), shown);
            _y += 30;
            return v;
        }

        private int IntSlider(string label, int value, int min, int max, string suffix = "") =>
            Mathf.RoundToInt(Slider(label, value, min, max, value + suffix));

        private void Line(string text, float height = 20)
        {
            Text(new Rect(_indent, _y, 460 - _indent, height), text, Note, 12);
            _y += height + 4;
        }

        private static string Percent(float v) => Mathf.RoundToInt(v * 100) + "%";

        /// <summary>The page's TazUO options left out, greyed with the reason, at the foot of the page.</summary>
        private void DrawNotOffered(Rect area)
        {
            var lines = NotOffered.Where(n => n.Page == _page).Select(n => n.Text).ToList();
            if (lines.Count == 0)
                return;
            float y = Mathf.Max(_y + 6, area.height - 18 - 15 * lines.Count);
            Text(new Rect(0, y, area.width, 16), "Not in this client:", Greyed, 11);
            foreach (string line in lines)
                Text(new Rect(8, y += 15, area.width - 8, 16), line, Greyed, 11);
        }

        // ---- pages ----

        private void DrawGeneral(GameOptions d)
        {
            d.showNames = Toggle("Show names over heads", d.showNames);
            d.showJournal = Toggle("Show the journal", d.showJournal);
            d.showHud = Toggle("Show the HUD (H)", d.showHud);
            d.autoRoofs = Toggle("Hide roofs automatically indoors (R overrides)", d.autoRoofs);
            d.highlightGameObjects = Toggle("Highlight game objects", d.highlightGameObjects);
            Line("With it on, characters, corpses and fixtures get the mouse-over\noutline too; other items always do.", 34);
            d.alwaysRun = Toggle("Always run", d.alwaysRun);
            _indent = 20;
            GUI.enabled = d.alwaysRun;
            d.alwaysRunUnlessHidden = Toggle("Unless hidden", d.alwaysRunUnlessHidden);
            GUI.enabled = true;
            _indent = 0;
        }

        private void DrawSound(GameOptions d)
        {
            // Sound effects only exist with the session's Audio: greyed until then (the draft keeps its value).
            bool effects = _game.Sound != null;
            GUI.enabled = effects;
            d.soundVolume = Slider("Sound volume", d.soundVolume, 0f, 1f, Percent(d.soundVolume));
            GUI.enabled = true;
            if (!effects)
                Line("Sound effects start when you log in; unavailable until then.");
            d.musicVolume = Slider("Music volume", d.musicVolume, 0f, 1f, Percent(d.musicVolume));
            Line("Music: the login music now, the region music in the world.");
        }

        private void DrawVideo(GameOptions d)
        {
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
            d.useTooltip = Toggle("Enable tooltips", d.useTooltip);
            Line("Off hides the text tooltips of buffs, gump parts and spell icons;\nitem and character properties still show (as TazUO).", 34);
            d.tooltipDelay = Slider("Delay", d.tooltipDelay, 0f, GameOptions.MaxTooltipDelay, d.tooltipDelay.ToString("0.00") + " s");
            Line("How long the mouse rests on something before its tooltip shows.");
        }

        private void DrawSpeech(GameOptions d)
        {
            d.scaleSpeechDelay = Toggle("Scale speech delay by length", d.scaleSpeechDelay);
            _indent = 20;
            d.speechDelay = IntSlider("Speech delay", d.speechDelay, GameOptions.MinSpeechDelay, GameOptions.MaxSpeechDelay);
            _indent = 0;
            Line($"Overhead speech stays {GameOptions.SpeechSeconds(40, d.scaleSpeechDelay, d.speechDelay):0.#} s"
                 + (d.scaleSpeechDelay ? $" (a long line {GameOptions.SpeechSeconds(120, true, d.speechDelay):0.#} s)." : "."));
            d.saveJournalToFile = Toggle("Save the journal to a file", d.saveJournalToFile);
            Line("Logs/JournalLogs in the project folder, a new file each login.");
            d.showJournal = Toggle("Show the journal", d.showJournal);
        }

        private void DrawCombat(GameOptions d)
        {
            d.criminalActionQuery = Toggle("Query before attacking an innocent", d.criminalActionQuery);
            d.beneficialCriminalActionQuery = Toggle("Query before beneficial acts on criminals or grays", d.beneficialCriminalActionQuery);
            Line("Both ask \"This may flag you criminal!\" before anything is sent.");
            d.buffBarTime = Toggle("Show the time left on buff icons", d.buffBarTime);
            d.castSpellsByOneClick = Toggle("Cast spells with one click on a spell icon", d.castSpellsByOneClick);
            Line("Spell icons are the ones dragged out of a spellbook; off, a double-click casts.");
        }

        private void DrawContainers(GameOptions d)
        {
            d.containersScale = IntSlider("Container scale", d.containersScale, GameOptions.MinContainersScale, GameOptions.MaxContainersScale, "%");
            Line("Container gumps and their items; game boards keep their size.");
            d.doubleClickToLootInsideContainers = Toggle("Double-click to loot items inside containers", d.doubleClickToLootInsideContainers);
            Line("In a corpse or a chest on the ground, a double-click moves a plain item\nto your backpack (Ctrl+double-click still uses it).", 34);
            d.holdShiftToSplitStack = Toggle("Hold Shift to split a stack", d.holdShiftToSplitStack);
            Line(d.holdShiftToSplitStack
                ? "Shift+drag asks how many; a plain drag takes the whole stack."
                : "A plain drag asks how many; Shift+drag takes the whole stack.");
        }

        private void DrawMacros(Rect area)
        {
            Hotkeys hk = _game.Hotkeys;
            if (hk == null)
            {
                Line("Macros load when you are online.");
                return;
            }
            _macros ??= NewMacroDraft(hk);

            Text(new Rect(0, 0, 200, 20), "Key", Heading, 13, TextAnchor.UpperLeft, true);
            Text(new Rect(112, 0, 200, 20), "Action", Heading, 13, TextAnchor.UpperLeft, true);
            if (GUI.Button(new Rect(area.width - 150, -2, 70, 22), "Add"))
            {
                _macros.Add();
                _macroScroll.y = float.MaxValue;
            }
            if (GUI.Button(new Rect(area.width - 74, -2, 74, 22), new GUIContent("Reload", "Reads unity-uo-hotkeys.json again, dropping edits.")))
            {
                hk.Load();
                _macros.Reloaded();
            }

            const float rowHeight = 40;
            List<MacroDraft.Row> rows = _macros.Rows;
            var list = new Rect(0, 24, area.width, area.height - 112);
            GUI.BeginGroup(list);
            _macroScroll = GUI.BeginScrollView(new Rect(0, 0, list.width, list.height), _macroScroll,
                new Rect(0, 0, list.width - 20, rows.Count * rowHeight));
            float y = 0;
            int remove = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                MacroDraft.Row r = rows[i];
                r.Key = GUI.TextField(new Rect(0, y, 104, 20), r.Key ?? "", 32);
                r.Action = GUI.TextField(new Rect(112, y, list.width - 170, 20), r.Action ?? "", 200);
                if (GUI.Button(new Rect(list.width - 52, y, 26, 20), new GUIContent("X", "Remove this macro")))
                    remove = i;
                string problem = MacroDraft.Problem(r);
                if (problem == null && !string.IsNullOrWhiteSpace(r.Key) && !Hotkeys.TryParseKey(r.Key, out _, out _, out _, out _))
                    problem = $"\"{r.Key.Trim()}\" is not a key (F5, Ctrl+1, Alt+Q, Keypad1...).";
                if (problem != null)
                    Text(new Rect(4, y + 21, list.width - 30, 16), problem, ProblemColor, 11);
                y += rowHeight;
            }
            GUI.EndScrollView();
            GUI.EndGroup();
            if (remove >= 0)
                _macros.Remove(remove);

            _y = list.yMax + 4;
            if (_macros.SaveFailed || hk.ReadOnly)
                Line(hk.ReadOnly ? "The hotkeys file could not be read, so it is not written: Apply only." : "Could not write unity-uo-hotkeys.json (see the log).");
            // Labels don't wrap: the verbs go eight to a line.
            var verbLines = MacroDraft.Verbs.Select((v, i) => (v, i)).GroupBy(p => p.i / 8, p => p.v).Select(g => string.Join(", ", g));
            Line("Actions: " + string.Join(",\n", verbLines) + ".", 50);
            Line("Apply binds the edits now; Okay also saves them; Cancel drops them.");
        }

        private static MacroDraft NewMacroDraft(Hotkeys hk) => new MacroDraft(
            () => hk.AllBindings.Select(b => new MacroDraft.Row { Key = b.key, Action = b.action }).ToList(),
            rows => hk.SetBindings(rows.Select(r => new Hotkeys.Binding { key = r.Key, action = r.Action })),
            rows => hk.SaveBindings(rows.Select(r => new Hotkeys.Binding { key = r.Key, action = r.Action })));
    }
}
