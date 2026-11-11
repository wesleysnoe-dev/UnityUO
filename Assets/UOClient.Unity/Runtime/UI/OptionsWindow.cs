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
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        private static readonly Color Heading = new Color(1f, 0.9f, 0.6f);
        private static readonly Color Note = new Color(0.8f, 0.78f, 0.7f);
        private static readonly Color Greyed = new Color(0.55f, 0.52f, 0.47f);
        private static readonly Color ProblemColor = new Color(1f, 0.55f, 0.45f);

        private readonly IGameOptions _game;
        private readonly OptionsDraft _draft;
        private MacroDraft _macros; // made when the hotkeys exist (online)
        private Page _page = Page.General;
        private Vector2 _macroScroll, _pageScroll;
        private float _pageHeight;

        public OptionsWindow(IGameOptions game)
        {
            _game = game;
            _draft = new OptionsDraft(game.CurrentOptions, game.ApplyOptions, game.SaveOptions);
            Rect = new Rect(240, 100, Width, Height);
            if (LaunchPage != null && System.Enum.TryParse(LaunchPage, true, out Page page))
                _page = page;
        }

        /// <summary>-options-page NAME: the options window opens on that page once in the world (for captures).</summary>
        public static readonly string LaunchPage = PageArgument(System.Environment.GetCommandLineArgs());

        private static string PageArgument(string[] args)
        {
            int at = System.Array.FindIndex(args, a => string.Equals(a, "-options-page", System.StringComparison.OrdinalIgnoreCase));
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
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
            if (_page == Page.Macros)
                DrawMacros(area);
            else
            {
                // A page taller than the area (last frame's height) scrolls; the macro page has its own list.
                bool scrolls = _pageHeight > area.height;
                _pageScroll = GUI.BeginScrollView(new Rect(0, 0, area.width, area.height), _pageScroll,
                    new Rect(0, 0, area.width - (scrolls ? 20 : 0), Mathf.Max(_pageHeight, area.height)));
                switch (_page)
                {
                    case Page.General: DrawGeneral(d); break;
                    case Page.Sound: DrawSound(d); break;
                    case Page.Video: DrawVideo(d); break;
                    case Page.Display: DrawDisplay(d); break;
                    case Page.Tooltip: DrawTooltip(d); break;
                    case Page.Speech: DrawSpeech(d); break;
                    case Page.CombatSpells: DrawCombat(d); break;
                    case Page.Containers: DrawContainers(d); break;
                    case Page.Nameplates: DrawNameplates(d); break;
                    case Page.IgnoreList: DrawIgnoreList(d); break;
                    case Page.Counters: DrawCounters(d); break;
                    case Page.InfoBar: DrawInfoBar(d); break;
                    case Page.Cooldowns: DrawCooldowns(d); break;
                }
                _pageHeight = DrawNotOffered(area);
                GUI.EndScrollView();
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
                {
                    _page = page;
                    _pageScroll = Vector2.zero;
                    _pageHeight = 0;
                }
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

        /// <summary>The page's TazUO options left out, greyed with the reason, at the foot of the page; returns the page's height.</summary>
        private float DrawNotOffered(Rect area)
        {
            var lines = NotOffered.Where(n => n.Page == _page).Select(n => n.Text).ToList();
            if (lines.Count == 0)
                return _y;
            float foot = 18 + 15 * lines.Count;
            float y = Mathf.Max(_y + 6, area.height - foot);
            float end = y + foot;
            Text(new Rect(0, y, area.width, 16), "Not in this client:", Greyed, 11);
            foreach (string line in lines)
                Text(new Rect(8, y += 15, area.width - 8, 16), line, Greyed, 11);
            return end;
        }

        /// <summary>One of a few choices in a row of buttons (TazUO's combo boxes).</summary>
        private int Choice(string label, int value, params string[] options) => Choice(label, value, 110, options);

        /// <summary>As <see cref="Choice(string, int, string[])"/>, each button <paramref name="buttonWidth"/> wide.</summary>
        private int Choice(string label, int value, float buttonWidth, params string[] options)
        {
            Text(new Rect(_indent, _y + 2, 120 - _indent, 20), label);
            int v = GUI.Toolbar(new Rect(125, _y, buttonWidth * options.Length, 22), value, options);
            _y += 30;
            return v;
        }

        /// <summary>A hue number (0-2999) typed in, with a swatch of its text colour.</summary>
        private int HueField(float x, string label, int hue)
        {
            Text(new Rect(x, _y + 2, 76, 20), label);
            string typed = GUI.TextField(new Rect(x + 78, _y, 56, 22), hue.ToString(), 4);
            int v = typed.Length == 0 ? 0 : int.TryParse(typed, out int n) ? n : hue;
            v = GameOptions.ClampHue(v);
            GUI.color = v == 0 ? Greyed : Art.HueColor(v, Greyed);
            GUI.DrawTexture(new Rect(x + 140, _y + 3, 26, 16), Texture2D.whiteTexture);
            GUI.color = Color.white;
            return v;
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

            d.showMobilesHP = Toggle("Show characters' health", d.showMobilesHP);
            _indent = 20;
            GUI.enabled = d.showMobilesHP;
            d.mobileHPType = Choice("Type", d.mobileHPType, "Percent", "Line", "Both");
            d.mobileHPShowWhen = Choice("Show", d.mobileHPShowWhen, "Always", "When damaged", "Smart");
            d.healthLineSizeMultiplier = IntSlider("Line size", d.healthLineSizeMultiplier,
                GameOptions.MinHealthLineSize, GameOptions.MaxHealthLineSize, "x");
            Line(d.mobileHPShowWhen == GameOptions.MobileHPAlways ? "Everyone in view whose health is known."
                : d.mobileHPShowWhen == GameOptions.MobileHPWhenDamaged ? "Below full health, plus whoever you fight and yourself in war mode."
                : "Everyone in view; the [%] text only below full health.");
            Line("The [%] text sits over the head, the line under the feet. Whoever you fight,\nlast attacked or last targeted keeps a line even with Percent.", 34);
            GUI.enabled = true;
            _indent = 0;

            d.autoOpenDoors = Toggle("Open doors automatically", d.autoOpenDoors);
            _indent = 20;
            GUI.enabled = d.autoOpenDoors;
            d.autoOpenDoorsIfHidden = Toggle("Also while hidden", d.autoOpenDoorsIfHidden);
            Line("Off until you turn it on. Facing a closed door sends the server's open-door\nmacro; open doors are left alone, and nothing opens for a ghost.", 34);
            GUI.enabled = true;
            _indent = 0;

            d.autoOpenCorpses = Toggle("Open corpses automatically", d.autoOpenCorpses);
            _indent = 20;
            GUI.enabled = d.autoOpenCorpses;
            d.autoOpenCorpseRange = IntSlider("Distance", d.autoOpenCorpseRange, 0, GameOptions.MaxAutoOpenCorpseRange, " tiles");
            d.corpseOpenOptions = Choice("Unless", d.corpseOpenOptions, 76, "(none)", "Targeting", "Hidden", "Both");
            Line("Off until you turn it on. Each corpse within the distance opens once, after\nyour own queued uses; held off while targeting and/or hidden.", 34);
            GUI.enabled = true;
            _indent = 0;
        }

        private void DrawSound(GameOptions d)
        {
            // Sound effects only exist with the session's Audio: greyed until then (the draft keeps its value).
            bool effects = _game.Sound != null;
            d.enableSound = Toggle("Sound effects", d.enableSound);
            _indent = 20;
            GUI.enabled = effects && d.enableSound;
            d.soundVolume = Slider("Volume", d.soundVolume, 0f, 1f, Percent(d.soundVolume));
            GUI.enabled = d.enableSound;
            d.enableFootstepsSound = Toggle("Footsteps", d.enableFootstepsSound);
            GUI.enabled = true;
            _indent = 0;
            if (!effects)
                Line("Sound effects start when you log in; unavailable until then.");
            d.enableMusic = Toggle("Music", d.enableMusic);
            _indent = 20;
            GUI.enabled = d.enableMusic;
            d.musicVolume = Slider("Volume", d.musicVolume, 0f, 1f, Percent(d.musicVolume));
            d.enableCombatMusic = Toggle("Combat music in war mode", d.enableCombatMusic);
            d.enableLoginMusic = Toggle("Music on the login screen", d.enableLoginMusic);
            GUI.enabled = true;
            _indent = 0;
            Line("Music: the login music now, the region music in the world; in war mode\none of the three combat tracks, until you are at peace again.", 34);
            d.reproduceSoundsInBackground = Toggle("Play sound and music in the background", d.reproduceSoundsInBackground);
            Line("Off: silent while another window has the focus (TazUO's background sound).");
        }

        // ---- Video: the world's look (TazUO's Video > Lighting, Misc and Shadows, and this client's own effects) ----

        private void DrawVideo(GameOptions d)
        {
            Section("Lighting");
            d.useCustomLightLevel = Toggle("Use a custom light level", d.useCustomLightLevel);
            _indent = 20;
            GUI.enabled = d.useCustomLightLevel;
            // TazUO's slider shows 0x1E - LightLevel: right is brighter.
            d.lightLevel = GameOptions.MaxLightLevel - IntSlider("Brightness", GameOptions.MaxLightLevel - d.lightLevel, 0, GameOptions.MaxLightLevel);
            d.lightLevelType = Choice("Type", d.lightLevelType, "Absolute", "Minimum");
            Line(d.lightLevelType == GameOptions.LightLevelAbsolute
                ? "The world is always lit this bright, day or night (T still picks a time)."
                : "Never darker than this; brighter when the server's light is.");
            GUI.enabled = true;
            _indent = 0;
            GUI.enabled = !d.useCustomLightLevel;
            d.useDarkNights = Toggle("Dark nights", d.useDarkNights);
            GUI.enabled = true;
            Line("Night and dungeons are half as dark again (not with a custom light level).");
            d.itemLights = Toggle("Light sources (lamps, torches, candles, lit windows)", d.itemLights);
            d.blackWhiteWhenDead = Toggle("Grey world while dead", d.blackWhiteWhenDead);
            d.deathScreen = Toggle("Death screen", d.deathScreen);

            Section("Shadows");
            d.shadows = Toggle("Shadows (after restart)", d.shadows);
            Line("Characters, items, trees and buildings cast them, by sun, moon and lamps.");
            if (CharacterShadow.OffFromLaunch)
                Line("-nocharshadows on the command line: off for this run.");
            else if (d.shadows != CharacterShadow.Enabled)
                Line(d.shadows ? "On from the next start (saved with Okay)." : "Off from the next start (saved with Okay).");
            _indent = 20;
            GUI.enabled = d.shadows;
            d.sunShadows = Toggle("Sun and moon shadows", d.sunShadows);
            Line("Off: only lamps cast shadows, indoors; cloud shadows go with the sun's.");
            GUI.enabled = true;
            _indent = 0;

            Section("Weather and sky");
            d.weather = Toggle("Weather (rain, snow, fog, morning mist, lightning)", d.weather);
            LaunchOff(!WeatherView.Enabled, "-noweather");
            Line("Off: the server's rain and snow as plain streaks, as the classic client.");
            d.cloudShadows = Toggle("Cloud shadows", d.cloudShadows);
            LaunchOff(!WeatherView.Clouds, "-noclouds");

            Section("Nature");
            d.foliageSway = Toggle("Trees and plants sway in the wind", d.foliageSway);
            LaunchOff(FoliageWind.StillFromLaunch, "-nofoliagesway");
            d.seasons = Toggle("Show seasons (leaf colours, light tint)", d.seasons);
            Line("Off: the world always looks as in summer, whatever the server's season.");
            d.ambientLife = Toggle("Ambient life (fireflies, falling leaves, pollen)", d.ambientLife);
            d.footprints = Toggle("Footprints, dust and splashes", d.footprints);
            LaunchOff(!AmbientLife.Enabled, "-noambient");
            d.seeThroughTrees = Toggle("See-through trees around you", d.seeThroughTrees);
            Line("Trees and plants in front of you thin out round your character.");
            d.animatedWater = Toggle("Animated water (shimmer, shore foam)", d.animatedWater);

            Section("Effects");
            d.fireEffects = Toggle("Fire and spell effects (flames, flicker, glow)", d.fireEffects);
            d.bloom = Toggle("Bloom (bright light bleeds softly)", d.bloom);
            LaunchOff(Fx.FxCatalog.OffFromLaunch, "-no-fx");
            d.colorGrading = Toggle("Colour grading and vignette", d.colorGrading);
            LaunchOff(!Fx.FxGrade.Enabled, "-nograde");
        }

        // ---- Display: the window (TazUO's Video > Game window) and the view ----

        private List<(int Width, int Height)> _resolutions;

        private void DrawDisplay(GameOptions d)
        {
            Section("Window");
            // "Keep" values show what the window is now; touching a control makes it a choice.
            int liveMode = Screen.fullScreenMode == FullScreenMode.Windowed || Screen.fullScreenMode == FullScreenMode.MaximizedWindow
                ? GameOptions.DisplayWindowed
                : Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? GameOptions.DisplayExclusive : GameOptions.DisplayBorderless;
            int mode = d.displayMode == GameOptions.DisplayKeep ? liveMode : d.displayMode;
            int chosen = Choice("Mode", mode, 100, GameOptions.DisplayModeNames);
            if (chosen != mode)
                d.displayMode = chosen;

            _resolutions ??= GameOptions.DistinctResolutions(Screen.resolutions.Select(r => (r.width, r.height)));
            (int w, int h) = d.resolutionWidth > 0 ? (d.resolutionWidth, d.resolutionHeight) : (Screen.width, Screen.height);
            if (Stepper("Resolution", GameOptions.ResolutionName(w, h), out int step) && _resolutions.Count > 0)
            {
                int at = _resolutions.IndexOf((w, h));
                int next = at >= 0 ? at + step
                    : step > 0 ? _resolutions.FindIndex(r => r.Width * r.Height > w * h) // between two: the next one up or down
                    : _resolutions.FindLastIndex(r => r.Width * r.Height < w * h);
                if (next >= 0 && next < _resolutions.Count)
                    (d.resolutionWidth, d.resolutionHeight) = _resolutions[next];
            }
            Line(mode == GameOptions.DisplayWindowed
                ? "A window can also be resized by its edge. Borderless: the desktop's size."
                : mode == GameOptions.DisplayBorderless
                    ? "Borderless fills the screen; other sizes are scaled to it."
                    : "Fullscreen switches the monitor to this resolution.");

            bool liveVSync = QualitySettings.vSyncCount > 0;
            bool vSync = d.vSync == GameOptions.VSyncKeep ? liveVSync : d.vSync == GameOptions.VSyncOn;
            bool nowVSync = Toggle("VSync (wait for the monitor's refresh)", vSync);
            if (nowVSync != vSync)
                d.vSync = nowVSync ? GameOptions.VSyncOn : GameOptions.VSyncOff;

            _indent = 20;
            GUI.enabled = !nowVSync;
            int cap = d.frameRateCap == GameOptions.FrameRateKeep
                ? (Application.targetFrameRate <= 0 ? GameOptions.FrameRateUnlimited : Application.targetFrameRate)
                : d.frameRateCap;
            int capAt = System.Array.IndexOf(GameOptions.FrameRateCaps, cap);
            int nowCapAt = Choice("Frame cap", capAt, 54, GameOptions.FrameRateCaps.Select(GameOptions.FrameRateName).ToArray());
            if (nowCapAt != capAt && nowCapAt >= 0)
                d.frameRateCap = GameOptions.FrameRateCaps[nowCapAt];
            GUI.enabled = true;
            _indent = 0;
            Line(nowVSync ? "With VSync on, the monitor's refresh rate is the cap." : "Frames per second at most (TazUO's FPS setting).");
            d.reduceFpsWhenInactive = Toggle("Reduce frame rate in the background", d.reduceFpsWhenInactive);
            Line($"{GameOptions.BackgroundFrameRate} fps while another window has the focus (as TazUO).");

            Section("View");
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

        /// <summary>A heading over a group of options.</summary>
        private void Section(string title)
        {
            if (_y > 0)
                _y += 6;
            Text(new Rect(0, _y, 440, 20), title, Heading, 13, TextAnchor.UpperLeft, true);
            _y += 24;
        }

        /// <summary>The note under a toggle that a launch argument turns off for this run (the choice is still saved).</summary>
        private void LaunchOff(bool off, string argument)
        {
            if (off)
                Line(argument + " on the command line: off for this run.");
        }

        /// <summary>A value between "&lt;" and "&gt;" buttons; true when one was pressed, <paramref name="step"/> -1 or +1.</summary>
        private bool Stepper(string label, string shown, out int step)
        {
            Text(new Rect(_indent, _y + 2, 120 - _indent, 20), label);
            step = 0;
            if (GUI.Button(new Rect(125, _y, 28, 22), "<"))
                step = -1;
            Text(new Rect(158, _y + 2, 120, 20), shown, Color.white, 13, TextAnchor.UpperCenter);
            if (GUI.Button(new Rect(283, _y, 28, 22), ">"))
                step = 1;
            _y += 30;
            return step != 0;
        }

        private void DrawTooltip(GameOptions d)
        {
            d.useTooltip = Toggle("Enable tooltips", d.useTooltip);
            Line("Off hides the text tooltips of buffs, gump parts and spell icons;\nitem and character properties still show (as TazUO).", 34);
            d.tooltipDelay = Slider("Delay", d.tooltipDelay, 0f, GameOptions.MaxTooltipDelay, d.tooltipDelay.ToString("0.00") + " s");
            Line("How long the mouse rests on something before its tooltip shows.");
            d.tooltipBackgroundOpacity = IntSlider("Background", d.tooltipBackgroundOpacity, 0, 100, "%");
            d.tooltipFontSize = IntSlider("Text size", d.tooltipFontSize, GameOptions.MinTooltipFontSize, GameOptions.MaxTooltipFontSize, " px");
            d.tooltipTextHue = HueField(0, "Text hue", d.tooltipTextHue);
            _y += 28;
            Line("0 keeps the usual parchment colour; the first line stays gold.");
        }

        private void DrawCounters(GameOptions d)
        {
            d.counterBarEnabled = Toggle("Show the counter bar", d.counterBarEnabled);
            Line("Drag an item from a bag onto a cell: the cell counts that kind in your\nbackpack, bags inside it included. Double-click a cell to use one,\nright-click to clear it. The item itself goes back where it was.", 50);
            GUI.enabled = d.counterBarEnabled;
            d.counterBarColumns = IntSlider("Columns", d.counterBarColumns, 1, HudBars.MaxCounterColumns);
            d.counterBarRows = IntSlider("Rows", d.counterBarRows, 1, HudBars.MaxCounterRows);
            d.counterBarCellSize = IntSlider("Cell size", d.counterBarCellSize, HudBars.MinCounterCell, HudBars.MaxCounterCell, " px");
            d.counterBarDisplayAbbreviatedAmount = Toggle("Shorten large counts", d.counterBarDisplayAbbreviatedAmount);
            _indent = 20;
            GUI.enabled = d.counterBarEnabled && d.counterBarDisplayAbbreviatedAmount;
            d.counterBarAbbreviatedAmount = IntSlider("From", d.counterBarAbbreviatedAmount / 1000, 1, 100, "K") * 1000;
            Line($"{d.counterBarAbbreviatedAmount} and more shows as {HudBars.CounterText(d.counterBarAbbreviatedAmount, true, d.counterBarAbbreviatedAmount)}.");
            _indent = 0;
            GUI.enabled = d.counterBarEnabled;
            d.counterBarHighlightOnUse = Toggle("Flash a cell when its item is used", d.counterBarHighlightOnUse);
            d.counterBarHighlightOnAmount = Toggle("Red when few are left", d.counterBarHighlightOnAmount);
            _indent = 20;
            GUI.enabled = d.counterBarEnabled && d.counterBarHighlightOnAmount;
            d.counterBarHighlightAmount = IntSlider("Fewer than", d.counterBarHighlightAmount, 1, 100);
            _indent = 0;
            GUI.enabled = true;
        }

        private void DrawCooldowns(GameOptions d)
        {
            Line("When a journal line contains the trigger text, a bar with that name counts\ndown the seconds. The same trigger again starts it over. Case is ignored.\nStart the text with me: for your own lines only (your spell words), or with\nbuff: to start when a buff of that name appears on you.", 66);
            List<CooldownDef> defs = Cooldowns.Parse(d.cooldowns);
            bool changed = false;
            int remove = -1;
            for (int i = 0; i < defs.Count; i++)
            {
                CooldownDef def = defs[i];
                Text(new Rect(0, _y + 2, 44, 20), "Name");
                string name = GUI.TextField(new Rect(46, _y, 130, 22), def.Name ?? "", Cooldowns.MaxNameLength);
                Text(new Rect(186, _y + 2, 58, 20), "Seconds");
                string secs = GUI.TextField(new Rect(246, _y, 44, 22), def.Seconds.ToString(), 4);
                Text(new Rect(300, _y + 2, 30, 20), "Hue");
                string hueText = GUI.TextField(new Rect(332, _y, 44, 22), def.Hue.ToString(), 4);
                int hue = hueText.Length == 0 ? 0 : int.TryParse(hueText, out int hn) ? GameOptions.ClampHue(hn) : def.Hue;
                GUI.color = hue == 0 ? Greyed : Art.HueColor(hue, Greyed);
                GUI.DrawTexture(new Rect(382, _y + 3, 18, 16), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(408, _y, 26, 22), "X"))
                    remove = i;
                _y += 25;
                Text(new Rect(0, _y + 2, 44, 20), "Text");
                string trigger = GUI.TextField(new Rect(46, _y, 388, 22), def.Trigger ?? "", Cooldowns.MaxTriggerLength);
                _y += 31;
                int seconds = secs.Length == 0 ? Cooldowns.MinSeconds
                    : int.TryParse(secs, out int sn) ? Mathf.Clamp(sn, Cooldowns.MinSeconds, Cooldowns.MaxSeconds) : def.Seconds;
                if (name != (def.Name ?? "") || trigger != (def.Trigger ?? "") || seconds != def.Seconds || hue != def.Hue)
                {
                    defs[i] = new CooldownDef { Name = name.Replace("|", ""), Trigger = trigger.Replace("|", ""), Seconds = seconds, Hue = hue };
                    changed = true;
                }
            }
            if (remove >= 0)
            {
                defs.RemoveAt(remove);
                changed = true;
            }
            if (defs.Count == 0)
                Line("No cooldowns yet.");
            GUI.enabled = defs.Count < Cooldowns.MaxDefs;
            if (GUI.Button(new Rect(0, _y + 2, 70, 22), "Add"))
            {
                defs.Add(new CooldownDef { Name = "Bandage", Trigger = "You begin applying the bandages", Seconds = 10, Hue = 63 });
                changed = true;
            }
            GUI.enabled = true;
            _y += 32;
            if (changed)
                d.cooldowns = Cooldowns.Format(defs);
        }

        private void DrawInfoBar(GameOptions d)
        {
            d.showInfoBar = Toggle("Show the info bar", d.showInfoBar);
            d.infoBarHighlightType = Choice("Warnings", d.infoBarHighlightType, 110, "Text colour", "Bars");
            Line(d.infoBarHighlightType == HudBars.HighlightTextColour
                ? "Hits, mana and stamina turn orange at half and red at a quarter;\nweight at 80% and when full."
                : "A thin bar under hits, mana, stamina and weight shows how full each is.", 34);
            List<InfoBarItem> items = HudBars.ParseInfoBar(d.infoBarItems);
            string[] vars = System.Enum.GetNames(typeof(InfoVar));
            Text(new Rect(0, _y, 100, 20), "Label", Heading, 13, TextAnchor.UpperLeft, true);
            Text(new Rect(108, _y, 150, 20), "Shows", Heading, 13, TextAnchor.UpperLeft, true);
            Text(new Rect(286, _y, 60, 20), "Hue", Heading, 13, TextAnchor.UpperLeft, true);
            _y += 24;
            bool changed = false;
            int remove = -1;
            for (int i = 0; i < items.Count; i++)
            {
                InfoBarItem item = items[i];
                string label = GUI.TextField(new Rect(0, _y, 100, 22), item.Label ?? "", HudBars.MaxInfoLabelLength);
                int at = (int)item.Var;
                if (GUI.Button(new Rect(108, _y, 24, 22), "<"))
                    at = (at + vars.Length - 1) % vars.Length;
                Text(new Rect(134, _y + 2, 118, 20), vars[(int)item.Var], Color.white, 13, TextAnchor.UpperCenter);
                if (GUI.Button(new Rect(254, _y, 24, 22), ">"))
                    at = (at + 1) % vars.Length;
                string typed = GUI.TextField(new Rect(286, _y, 50, 22), item.Hue.ToString(), 4);
                int hue = typed.Length == 0 ? 0 : int.TryParse(typed, out int n) ? GameOptions.ClampHue(n) : item.Hue;
                GUI.color = hue == 0 ? Greyed : Art.HueColor(hue, Greyed);
                GUI.DrawTexture(new Rect(342, _y + 3, 22, 16), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(new Rect(372, _y, 26, 22), "X"))
                    remove = i;
                if (label != (item.Label ?? "") || at != (int)item.Var || hue != item.Hue)
                {
                    items[i] = new InfoBarItem { Label = label.Replace("|", ""), Var = (InfoVar)at, Hue = hue };
                    changed = true;
                }
                _y += 26;
            }
            if (remove >= 0)
            {
                items.RemoveAt(remove);
                changed = true;
            }
            GUI.enabled = items.Count < HudBars.MaxInfoBarItems;
            if (GUI.Button(new Rect(0, _y + 2, 70, 22), "Add"))
            {
                items.Add(new InfoBarItem { Label = "Gold", Var = InfoVar.Gold, Hue = 53 });
                changed = true;
            }
            GUI.enabled = true;
            _y += 32;
            if (changed)
                d.infoBarItems = HudBars.FormatInfoBar(items);
        }

        private string _ignoreName = "";

        private void DrawIgnoreList(GameOptions d)
        {
            Line("What these characters say is dropped as it arrives: overhead, journal,\nchat window and file. Names as shown over their heads; case is ignored.", 34);
            Text(new Rect(0, _y + 2, 60, 20), "Name");
            _ignoreName = GUI.TextField(new Rect(62, _y, 220, 22), _ignoreName, GameOptions.MaxIgnoredNameLength);
            List<string> names = GameOptions.IgnoredNames(d.ignoreList);
            GUI.enabled = _ignoreName.Trim().Length > 0 && names.Count < GameOptions.MaxIgnoredNames;
            if (GUI.Button(new Rect(290, _y, 60, 22), "Add"))
            {
                d.ignoreList = GameOptions.AddIgnored(d.ignoreList, _ignoreName);
                _ignoreName = "";
            }
            GUI.enabled = true;
            _y += 32;
            if (names.Count == 0)
                Line("Nobody is ignored.");
            foreach (string name in names)
            {
                Text(new Rect(8, _y + 2, 270, 20), name);
                if (GUI.Button(new Rect(290, _y, 60, 22), "Remove"))
                    d.ignoreList = GameOptions.RemoveIgnored(d.ignoreList, name);
                _y += 26;
            }
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
            d.showSystemMessagesInJournal = Toggle("Show server messages in the journal", d.showSystemMessagesInJournal);
            Line("Off: the server's own lines (no one said them) go to the chat window\nand the journal file only; speech and party lines stay.", 34);

            d.ignoreGuildMessages = Toggle("Hide guild chat", d.ignoreGuildMessages);
            d.ignoreAllianceMessages = Toggle("Hide alliance chat", d.ignoreAllianceMessages);
            Line("Dropped as they arrive: journal, chat window and file (as TazUO).\nOn this shard: the world chat's Guild and Faction channels.", 34);

            Text(new Rect(0, _y, 300, 20), "Speech colours (hue 0-2999)", Heading, 13, TextAnchor.UpperLeft, true);
            _y += 24;
            d.speechHue = HueField(0, "Speech", d.speechHue);
            d.whisperHue = HueField(230, "Whisper", d.whisperHue);
            _y += 28;
            d.emoteHue = HueField(0, "Emote", d.emoteHue);
            d.yellHue = HueField(230, "Yell", d.yellHue);
            _y += 28;
            d.partyMessageHue = HueField(0, "Party", d.partyMessageHue);
            _y += 28;
            d.guildMessageHue = HueField(0, "Guild", d.guildMessageHue);
            d.allyMessageHue = HueField(230, "Alliance", d.allyMessageHue);
            _y += 28;
            Line("Guild and Alliance: 0 keeps the colour the server sends.");
            Line("What you type goes out in these: plain, \"; \" whisper, \": \" emote, \"! \" yell\n(macros too). Party: how party lines show in the chat window.", 34);
        }

        private void DrawCombat(GameOptions d)
        {
            d.criminalActionQuery = Toggle("Query before attacking an innocent", d.criminalActionQuery);
            d.beneficialCriminalActionQuery = Toggle("Query before beneficial acts on criminals or grays", d.beneficialCriminalActionQuery);
            Line("Both ask \"This may flag you criminal!\" before anything is sent.");
            d.buffBarTime = Toggle("Show the time left on buff icons", d.buffBarTime);
            d.castSpellsByOneClick = Toggle("Cast spells with one click on a spell icon", d.castSpellsByOneClick);
            Line("Spell icons are the ones dragged out of a spellbook; off, a double-click casts.");
            d.holdDownKeyTab = Toggle("Hold Tab for war mode", d.holdDownKeyTab);
            Line(d.holdDownKeyTab
                ? "War mode while Tab is held down; peace again when you let go."
                : "Off: each press of Tab switches between war and peace.");
            d.showTargetIndicator = Toggle("Keep your target's health line", d.showTargetIndicator);
            Line("Whoever you fight, last attacked or last targeted keeps a health line\neven with characters' health off (General).", 34);
        }

        private void DrawContainers(GameOptions d)
        {
            d.containersScale = IntSlider("Container scale", d.containersScale, GameOptions.MinContainersScale, GameOptions.MaxContainersScale, "%");
            Line("Container gumps and their items; game boards keep their size.");
            d.doubleClickToLootInsideContainers = Toggle("Double-click to loot items inside containers", d.doubleClickToLootInsideContainers);
            Line("In a corpse or a chest on the ground, a double-click moves a plain item\nto your backpack (Ctrl+double-click still uses it).", 34);
            d.animateBooksAndScrolls = Toggle("Animate books and scrolls opening", d.animateBooksAndScrolls);
            Line("Books fly up and open; party, profile and vendor windows unroll.");
            d.modernPaperdoll = Toggle("Modern paperdoll", d.modernPaperdoll);
            Line(d.modernPaperdoll ? "Its buttons take the top bar's place (Alt+P opens it)." : "The classic paperdoll, and the top bar.");
            d.holdShiftToSplitStack = Toggle("Hold Shift to split a stack", d.holdShiftToSplitStack);
            Line(d.holdShiftToSplitStack
                ? "Shift+drag asks how many; a plain drag takes the whole stack."
                : "A plain drag asks how many; Shift+drag takes the whole stack.");
            d.highlightContainerWhenSelected = Toggle("Highlight a container while the mouse is over its window", d.highlightContainerWhenSelected);
            Line("Outlined on the ground, or in the window of the bag it sits in.");
            d.hueContainerGumps = Toggle("Hue container gumps", d.hueContainerGumps);
            Line("A dyed bag or chest draws its window in its own colour (corpses never).");

            d.gridContainersDefaultToOldStyleView = Toggle("Open containers in the original (classic) view", d.gridContainersDefaultToOldStyleView);
            Line(d.gridContainersDefaultToOldStyleView
                ? "Off: containers and corpses open as grid windows (game boards stay classic).\nWindows already open keep their view."
                : "Containers and corpses open as grid windows (game boards stay classic).\nWindows already open keep their view.", 34);
            _indent = 20;
            GUI.enabled = !d.gridContainersDefaultToOldStyleView;
            d.gridContainersScale = IntSlider("Grid scale", d.gridContainersScale,
                GameOptions.MinGridContainersScale, GameOptions.MaxGridContainersScale, "%");
            d.gridContainerScaleItems = Toggle("Also scale items", d.gridContainerScaleItems);
            d.containerOpacity = IntSlider("Opacity", d.containerOpacity, GameOptions.MinContainerOpacity, GameOptions.MaxContainerOpacity, "%");
            Line($"Grid windows only: {GameOptions.GridItemSize(d.gridContainersScale)}-pixel cells; the background and border\nat this opacity, items always solid. Classic gumps use Container scale.", 34);
            d.gridDefaultColumns = IntSlider("Columns", d.gridDefaultColumns, GameOptions.MinGridCells, GameOptions.MaxGridCells);
            d.gridDefaultRows = IntSlider("Rows", d.gridDefaultRows, GameOptions.MinGridCells, GameOptions.MaxGridCells);
            d.gridBorderAlpha = IntSlider("Cell lines", d.gridBorderAlpha, 0, 100, "%");
            d.gridHideBorder = Toggle("Hide the window border", d.gridHideBorder);
            d.gridUseContainerHue = Toggle("Background in the container's colour", d.gridUseContainerHue);
            d.gridAutoSort = Toggle("Sort new windows by kind and colour", d.gridAutoSort);
            d.gridContainerSearchMode = Choice("Search", d.gridContainerSearchMode, 130, "Only show matches", "Dim the others");
            Line("Each grid has a search box (name and tooltip words), a Sort switch and,\non a corpse, Loot: everything shown goes to your backpack.", 34);
            GUI.enabled = true;
            _indent = 0;
            d.corpseContainerStyle = Choice("Corpses open", d.corpseContainerStyle, 100, "As containers", "As a grid", "Classic");
        }

        private void DrawNameplates(GameOptions d)
        {
            d.nameOverheadToggled = Toggle("Show nameplates over characters", d.nameOverheadToggled);
            Line("Off by default (as TazUO). Uses the name, notoriety and hits the server sent.\nPlain \"Show names over heads\" (General) stays separate: both can be on.", 34);
            GUI.enabled = d.nameOverheadToggled;
            d.namePlateOpacity = IntSlider("Plate opacity", d.namePlateOpacity, 0, 100, "%");
            d.namePlateBorderOpacity = IntSlider("Border opacity", d.namePlateBorderOpacity, 0, 100, "%");
            d.namePlateBackgroundMode = Choice("Background", d.namePlateBackgroundMode, NamePlates.BackgroundModeNames);
            if (d.namePlateBackgroundMode == NamePlates.BackgroundFixed)
            {
                _indent = 20;
                d.namePlateBackgroundR = IntSlider("Red", d.namePlateBackgroundR, 0, 255);
                d.namePlateBackgroundG = IntSlider("Green", d.namePlateBackgroundG, 0, 255);
                d.namePlateBackgroundB = IntSlider("Blue", d.namePlateBackgroundB, 0, 255);
                _indent = 0;
            }
            d.namePlateUseNotorietyText = Toggle("Name in notoriety colour", d.namePlateUseNotorietyText);
            d.namePlateHealthBar = Toggle("Health bar on the plate", d.namePlateHealthBar);
            _indent = 20;
            GUI.enabled = d.nameOverheadToggled && d.namePlateHealthBar;
            d.namePlateHealthBarOpacity = IntSlider("Bar opacity", d.namePlateHealthBarOpacity, 0, 100, "%");
            d.namePlateShowMissingHealth = Toggle("Show missing health (dark empty)", d.namePlateShowMissingHealth);
            d.namePlateHealthBarMode = Choice("Bar colour", d.namePlateHealthBarMode, NamePlates.FillNames);
            d.namePlateSplitHealthBar = Toggle("Bars under the name (split)", d.namePlateSplitHealthBar);
            d.namePlateUseFixedHealthBarWidth = Toggle("Fixed health bar width", d.namePlateUseFixedHealthBarWidth);
            if (d.namePlateUseFixedHealthBarWidth)
                d.namePlateHealthBarFixedWidth = IntSlider("Bar width", d.namePlateHealthBarFixedWidth, NamePlates.MinFixedWidth, NamePlates.MaxFixedWidth, "px");
            GUI.enabled = d.nameOverheadToggled;
            _indent = 0;
            d.namePlateHideAtFullHealth = Toggle("Hide at full health", d.namePlateHideAtFullHealth);
            _indent = 20;
            GUI.enabled = d.nameOverheadToggled && d.namePlateHideAtFullHealth;
            d.namePlateHideAtFullHealthInWarmode = Toggle("Only in war mode", d.namePlateHideAtFullHealthInWarmode);
            GUI.enabled = d.nameOverheadToggled;
            _indent = 0;
            d.namePlateUseFixedWidth = Toggle("Fixed name width", d.namePlateUseFixedWidth);
            _indent = 20;
            GUI.enabled = d.nameOverheadToggled && d.namePlateUseFixedWidth;
            d.namePlateFixedWidth = IntSlider("Name width", d.namePlateFixedWidth, NamePlates.MinFixedWidth, NamePlates.MaxFixedWidth, "px");
            GUI.enabled = d.nameOverheadToggled;
            _indent = 0;
            d.namePlateHeight = IntSlider("Plate height (0 = fit text)", d.namePlateHeight, 0, NamePlates.MaxHeight, "px");
            d.namePlateFontSize = IntSlider("Text size", d.namePlateFontSize, GameOptions.MinNamePlateFontSize, GameOptions.MaxNamePlateFontSize, " px");
            d.namePlateAvoidOverlap = Toggle("Avoid overlapping plates", d.namePlateAvoidOverlap);
            Line("Right-click a plate to hide it until that character leaves view.");
            GUI.enabled = true;
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
            var list = new Rect(0, 24, area.width, area.height - 150);
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
            // Labels don't wrap: the verbs go nine to a line.
            var verbLines = MacroDraft.Verbs.Select((v, i) => (v, i)).GroupBy(p => p.i / 9, p => p.v).Select(g => string.Join(", ", g)).ToList();
            Line("Actions: " + string.Join(",\n", verbLines) + ".", 16 * verbLines.Count + 2);
            Line("Apply binds the edits now; Okay also saves them; Cancel drops them.");
        }

        private static MacroDraft NewMacroDraft(Hotkeys hk) => new MacroDraft(
            () => hk.AllBindings.Select(b => new MacroDraft.Row { Key = b.key, Action = b.action }).ToList(),
            rows => hk.SetBindings(rows.Select(r => new Hotkeys.Binding { key = r.Key, action = r.Action })),
            rows => hk.SaveBindings(rows.Select(r => new Hotkeys.Binding { key = r.Key, action = r.Action })));
    }
}
