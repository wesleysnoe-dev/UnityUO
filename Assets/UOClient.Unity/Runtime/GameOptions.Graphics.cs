using System;
using System.Collections.Generic;
using System.Linq;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The Display and Video pages' options: the window (display mode, resolution, vsync, frame cap), TazUO's lighting
    /// options, and switches for this client's own graphics (weather, shadows, plants, ambient life, FX). Every default is
    /// what the game did before these options existed, so nothing changes for a player until a setting is changed; the
    /// display ones default to "keep" (<see cref="DisplayKeep"/>, 0 sizes, <see cref="VSyncKeep"/>,
    /// <see cref="FrameRateKeep"/>) so an options file written before them never moves the window. Launch arguments
    /// (-noweather, -nocharshadows, -no-fx, ...) still turn a feature off for that run whatever is saved here.
    /// </summary>
    public sealed partial class GameOptions
    {
        /// <summary>Display modes: as the player last left the window (Unity remembers it), windowed, borderless fullscreen, exclusive fullscreen.</summary>
        public const int DisplayKeep = -1, DisplayWindowed = 0, DisplayBorderless = 1, DisplayExclusive = 2;

        public static readonly string[] DisplayModeNames = { "Windowed", "Borderless", "Fullscreen" };

        /// <summary>VSync: keep the quality level's own (on in this project's levels), off, on (TazUO's EnableVSync).</summary>
        public const int VSyncKeep = -1, VSyncOff = 0, VSyncOn = 1;

        /// <summary>Frame-rate cap: keep Unity's default (no cap), or a cap; 0 = unlimited (TazUO's FPS setting).</summary>
        public const int FrameRateKeep = -1, FrameRateUnlimited = 0;

        /// <summary>The caps offered (frames per second; 0 unlimited).</summary>
        public static readonly int[] FrameRateCaps = { 30, 60, 120, 144, 240, FrameRateUnlimited };

        /// <summary>TazUO's background frame rate with ReduceFPSWhenInactive (GameController._intervalFixedUpdate[1], "5 FPS").</summary>
        public const int BackgroundFrameRate = 5;

        /// <summary>TazUO's light level range (World.Light.Overall: 0 full day .. 0x1E darkest; the slider shows 0x1E - level).</summary>
        public const int MaxLightLevel = 0x1E;

        /// <summary>TazUO's LightLevelType: the custom level as it is, or only as a floor on the darkness (Math.Min with the server's).</summary>
        public const int LightLevelAbsolute = 0, LightLevelMinimum = 1;

        // ---- Display page ----

        /// <summary><see cref="DisplayKeep"/> or a mode (Screen.fullScreenMode); applied when it differs from the window's.</summary>
        public int displayMode = DisplayKeep;

        /// <summary>The resolution (Screen.SetResolution); 0 x 0 keeps the window's size (fullscreen: the desktop's).</summary>
        public int resolutionWidth = 0, resolutionHeight = 0;

        /// <summary><see cref="VSyncKeep"/>, <see cref="VSyncOff"/> or <see cref="VSyncOn"/> (QualitySettings.vSyncCount).</summary>
        public int vSync = VSyncKeep;

        /// <summary><see cref="FrameRateKeep"/>, <see cref="FrameRateUnlimited"/> or a cap (Application.targetFrameRate). Ignored while vsync is on.</summary>
        public int frameRateCap = FrameRateKeep;

        /// <summary>TazUO's ReduceFPSWhenInactive: <see cref="BackgroundFrameRate"/> while the window is not focused. Off, as TazUO.</summary>
        public bool reduceFpsWhenInactive = false;

        /// <summary>
        /// TazUO's ReproduceSoundsInBackground: sound and music go on while the window is not focused. On by default here
        /// (TazUO: off), as this client always played them (AudioListener.volume goes to 0 when off and unfocused).
        /// </summary>
        public bool reproduceSoundsInBackground = true;

        // ---- Video page: lighting (TazUO's Video > Lighting and Misc) ----

        /// <summary>TazUO's UseCustomLightLevel: the world is lit at <see cref="lightLevel"/> instead of the server's light (<see cref="LightLevels"/>). Off, as TazUO.</summary>
        public bool useCustomLightLevel = false;

        /// <summary>TazUO's LightLevel (0 full day .. <see cref="MaxLightLevel"/> darkest). 0, as TazUO.</summary>
        public int lightLevel = 0;

        /// <summary>TazUO's LightLevelType (<see cref="LightLevelAbsolute"/>, as TazUO, or <see cref="LightLevelMinimum"/>).</summary>
        public int lightLevelType = LightLevelAbsolute;

        /// <summary>Item and map lights (lamps, torches, candles) and lit windows at night (ItemLights). On, as always.</summary>
        public bool itemLights = true;

        /// <summary>TazUO's EnableBlackWhiteEffect: a ghost sees the world drained of colour (Lighting's _UODead). On, as TazUO.</summary>
        public bool blackWhiteWhenDead = true;

        /// <summary>TazUO's EnableDeathScreen: "You are dead." over the screen after dying (DemoHud). On, as TazUO.</summary>
        public bool deathScreen = true;

        // ---- Video page: shadows (TazUO's Video > Shadows) ----

        /// <summary>
        /// TazUO's ShadowsEnabled: characters, items and map objects cast outline shadows (CharacterShadow). On, as always.
        /// Read once at startup (shadows are built into characters and map chunks): takes effect after a restart.
        /// </summary>
        public bool shadows = true;

        /// <summary>The sun's (and moon's) shadows, cloud shadows included; lamp shadows indoors stay (CharacterShadow.SetSun). On, as always.</summary>
        public bool sunShadows = true;

        // ---- Video page: weather and nature ----

        /// <summary>
        /// TazUO's EnableEnhancedWeather: rain, snow, fog, morning mist and lightning drawn over the world (WeatherView); off,
        /// the plain screen streaks the HUD drew before. On, as always here (TazUO: off).
        /// </summary>
        public bool weather = true;

        /// <summary>Cloud shadows drifting over the ground by day (WeatherView). On, as always.</summary>
        public bool cloudShadows = true;

        /// <summary>Trees and plants lean in the wind (FoliageWind). On, as always.</summary>
        public bool foliageSway = true;

        /// <summary>The server's season shows: leaf colours, the light's tint, falling leaves (FoliageWind). On, as always; off: summer look.</summary>
        public bool seasons = true;

        /// <summary>Fireflies, falling leaves and pollen around the player (AmbientLife). On, as always.</summary>
        public bool ambientLife = true;

        /// <summary>Footprints in sand and snow, dust puffs and rain splashes under steps (AmbientLife). On, as always.</summary>
        public bool footprints = true;

        /// <summary>TazUO's AnimatedWaterEffect: water shimmers and foams at the shore (WorldSprite). On, as always here (TazUO: off).</summary>
        public bool animatedWater = true;

        // ---- Video page: effects (TazUO's "post processing effects") ----

        /// <summary>Living flames on fire art, flickering lights and glowing spell effects (Fx). On, as always.</summary>
        public bool fireEffects = true;

        /// <summary>Bright light bleeds softly, mostly at night (Fx.FxBloom). On, as always.</summary>
        public bool bloom = true;

        /// <summary>Colour grading and vignette on the world (Fx.FxGrade). On, as always.</summary>
        public bool colorGrading = true;

        /// <summary>The Display and Video options above, from <paramref name="from"/> into <paramref name="to"/>.</summary>
        public static void CopyGraphics(GameOptions from, GameOptions to)
        {
            to.displayMode = from.displayMode;
            to.resolutionWidth = from.resolutionWidth;
            to.resolutionHeight = from.resolutionHeight;
            to.vSync = from.vSync;
            to.frameRateCap = from.frameRateCap;
            to.reduceFpsWhenInactive = from.reduceFpsWhenInactive;
            to.reproduceSoundsInBackground = from.reproduceSoundsInBackground;
            to.useCustomLightLevel = from.useCustomLightLevel;
            to.lightLevel = from.lightLevel;
            to.lightLevelType = from.lightLevelType;
            to.itemLights = from.itemLights;
            to.blackWhiteWhenDead = from.blackWhiteWhenDead;
            to.deathScreen = from.deathScreen;
            to.shadows = from.shadows;
            to.sunShadows = from.sunShadows;
            to.weather = from.weather;
            to.cloudShadows = from.cloudShadows;
            to.foliageSway = from.foliageSway;
            to.seasons = from.seasons;
            to.ambientLife = from.ambientLife;
            to.footprints = from.footprints;
            to.animatedWater = from.animatedWater;
            to.fireEffects = from.fireEffects;
            to.bloom = from.bloom;
            to.colorGrading = from.colorGrading;
        }

        private bool SameGraphics(GameOptions o) =>
            displayMode == o.displayMode && resolutionWidth == o.resolutionWidth && resolutionHeight == o.resolutionHeight
            && vSync == o.vSync && frameRateCap == o.frameRateCap && reduceFpsWhenInactive == o.reduceFpsWhenInactive
            && reproduceSoundsInBackground == o.reproduceSoundsInBackground
            && useCustomLightLevel == o.useCustomLightLevel && lightLevel == o.lightLevel && lightLevelType == o.lightLevelType
            && itemLights == o.itemLights && blackWhiteWhenDead == o.blackWhiteWhenDead && deathScreen == o.deathScreen
            && shadows == o.shadows && sunShadows == o.sunShadows && weather == o.weather && cloudShadows == o.cloudShadows
            && foliageSway == o.foliageSway && seasons == o.seasons && ambientLife == o.ambientLife && footprints == o.footprints
            && animatedWater == o.animatedWater && fireEffects == o.fireEffects && bloom == o.bloom && colorGrading == o.colorGrading;

        private void ClampGraphics()
        {
            displayMode = Math.Max(DisplayKeep, Math.Min(DisplayExclusive, displayMode));
            if (resolutionWidth <= 0 || resolutionHeight <= 0 || resolutionWidth > MaxResolution || resolutionHeight > MaxResolution)
                resolutionWidth = resolutionHeight = 0;
            vSync = Math.Max(VSyncKeep, Math.Min(VSyncOn, vSync));
            frameRateCap = frameRateCap < 0 ? FrameRateKeep : Math.Min(MaxFrameRateCap, frameRateCap);
            lightLevel = Math.Max(0, Math.Min(MaxLightLevel, lightLevel));
            lightLevelType = Math.Max(LightLevelAbsolute, Math.Min(LightLevelMinimum, lightLevelType));
        }

        /// <summary>The largest width or height kept (anything bigger in the file is a typo: keep the window).</summary>
        public const int MaxResolution = 16384;

        /// <summary>The highest frame-rate cap kept.</summary>
        public const int MaxFrameRateCap = 1000;

        /// <summary>The parse switch's graphics keys; false for a key that is not one.</summary>
        private bool ParseGraphics(string key, string raw)
        {
            switch (key)
            {
                case "displayMode": Integer(raw, ref displayMode); return true;
                case "resolutionWidth": Integer(raw, ref resolutionWidth); return true;
                case "resolutionHeight": Integer(raw, ref resolutionHeight); return true;
                case "vSync": Integer(raw, ref vSync); return true;
                case "frameRateCap": Integer(raw, ref frameRateCap); return true;
                case "reduceFpsWhenInactive": Bool(raw, ref reduceFpsWhenInactive); return true;
                case "reproduceSoundsInBackground": Bool(raw, ref reproduceSoundsInBackground); return true;
                case "useCustomLightLevel": Bool(raw, ref useCustomLightLevel); return true;
                case "lightLevel": Integer(raw, ref lightLevel); return true;
                case "lightLevelType": Integer(raw, ref lightLevelType); return true;
                case "itemLights": Bool(raw, ref itemLights); return true;
                case "blackWhiteWhenDead": Bool(raw, ref blackWhiteWhenDead); return true;
                case "deathScreen": Bool(raw, ref deathScreen); return true;
                case "shadows": Bool(raw, ref shadows); return true;
                case "sunShadows": Bool(raw, ref sunShadows); return true;
                case "weather": Bool(raw, ref weather); return true;
                case "cloudShadows": Bool(raw, ref cloudShadows); return true;
                case "foliageSway": Bool(raw, ref foliageSway); return true;
                case "seasons": Bool(raw, ref seasons); return true;
                case "ambientLife": Bool(raw, ref ambientLife); return true;
                case "footprints": Bool(raw, ref footprints); return true;
                case "animatedWater": Bool(raw, ref animatedWater); return true;
                case "fireEffects": Bool(raw, ref fireEffects); return true;
                case "bloom": Bool(raw, ref bloom); return true;
                case "colorGrading": Bool(raw, ref colorGrading); return true;
                default: return false;
            }
        }

        /// <summary>The graphics keys for the file, in the pages' order.</summary>
        private void WriteGraphics(Action<string, string> add, Func<bool, string> b, Func<int, string> i)
        {
            add("displayMode", i(displayMode));
            add("resolutionWidth", i(resolutionWidth));
            add("resolutionHeight", i(resolutionHeight));
            add("vSync", i(vSync));
            add("frameRateCap", i(frameRateCap));
            add("reduceFpsWhenInactive", b(reduceFpsWhenInactive));
            add("reproduceSoundsInBackground", b(reproduceSoundsInBackground));
            add("useCustomLightLevel", b(useCustomLightLevel));
            add("lightLevel", i(lightLevel));
            add("lightLevelType", i(lightLevelType));
            add("itemLights", b(itemLights));
            add("blackWhiteWhenDead", b(blackWhiteWhenDead));
            add("deathScreen", b(deathScreen));
            add("shadows", b(shadows));
            add("sunShadows", b(sunShadows));
            add("weather", b(weather));
            add("cloudShadows", b(cloudShadows));
            add("foliageSway", b(foliageSway));
            add("seasons", b(seasons));
            add("ambientLife", b(ambientLife));
            add("footprints", b(footprints));
            add("animatedWater", b(animatedWater));
            add("fireEffects", b(fireEffects));
            add("bloom", b(bloom));
            add("colorGrading", b(colorGrading));
        }

        // ---- rules ----

        /// <summary>
        /// The light levels the world is lit at: TazUO's UseCustomLightLevel (ModernOptionsGump.BuildVideo) sets
        /// Overall to the custom level (<see cref="LightLevelMinimum"/>: Math.Min(RealOverall, level), so it only ever
        /// brightens) and Personal to 0; off, the server's levels as they came.
        /// </summary>
        public static (int Overall, int Personal) LightLevels(int overall, int personal, bool custom, int level, int type)
        {
            if (!custom)
                return (overall, personal);
            level = Math.Max(0, Math.Min(MaxLightLevel, level));
            return (type == LightLevelMinimum ? Math.Min(overall, level) : level, 0);
        }

        /// <summary>Application.targetFrameRate for a cap: -1 (the platform's own, no cap) for unlimited, null for keep.</summary>
        public static int? TargetFrameRate(int cap) => cap < 0 ? (int?)null : cap == FrameRateUnlimited ? -1 : cap;

        /// <summary>The frame rate while the window is unfocused: <see cref="BackgroundFrameRate"/> with the option on, else null (as focused).</summary>
        public static int? InactiveFrameRate(bool reduceFpsWhenInactive, bool focused) =>
            reduceFpsWhenInactive && !focused ? BackgroundFrameRate : (int?)null;

        /// <summary>The listener volume for the focus: 0 while unfocused unless sounds play in the background.</summary>
        public static float BackgroundAudioVolume(bool reproduceSoundsInBackground, bool focused) =>
            focused || reproduceSoundsInBackground ? 1f : 0f;

        /// <summary>The screen's resolutions (Screen.resolutions repeats each size per refresh rate) once each, smallest first.</summary>
        public static List<(int Width, int Height)> DistinctResolutions(IEnumerable<(int Width, int Height)> resolutions) =>
            resolutions.Where(r => r.Width > 0 && r.Height > 0).Distinct()
                .OrderBy(r => r.Width).ThenBy(r => r.Height).ToList();

        /// <summary>A resolution's label, e.g. "1920 x 1080".</summary>
        public static string ResolutionName(int width, int height) => width + " x " + height;

        /// <summary>A frame-rate cap's label.</summary>
        public static string FrameRateName(int cap) => cap == FrameRateUnlimited ? "Unlimited" : cap.ToString();
    }
}
