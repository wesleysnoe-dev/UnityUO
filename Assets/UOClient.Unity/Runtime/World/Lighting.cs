using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>Time-of-day lighting: the server's light level, or a fixed time chosen with T, tinted by the server's season.</summary>
    public sealed class Lighting
    {
        // Time of day: index into Times; -1 follows the server's light level.
        private int _timeOverride = StartOverride();

        /// <summary>-time day|golden|dusk|night: start with that light (as the T key sets it).</summary>
        private static int StartOverride()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-time", System.StringComparison.OrdinalIgnoreCase))
                    for (int k = 0; k < Times.Length; k++)
                        if (Times[k].Name.ToLowerInvariant().StartsWith(args[i + 1].ToLowerInvariant()))
                            return k;
            return -1;
        }
        private TimeOfDay _timeCurrent = Times[0];
        private float _dead;

        /// <summary>-hour H (0-24): the sun stands at that UO hour, whatever the clock says (captures, checks).</summary>
        private static float StartHour()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-hour", System.StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                    return Mathf.Repeat(h, 24f);
            return float.NaN;
        }

        private readonly float _fixedHour = StartHour();

        /// <summary>-sunspin S: the sun moves one baked step every S seconds through the day (checks of the sun step's cost).</summary>
        private static float StartSpin()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-sunspin", System.StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s))
                    return Mathf.Max(1f, s);
            return 0f;
        }

        private readonly float _spinSeconds = StartSpin();

        /// <summary>
        /// The terrain's baked sun (LiveGroundLighting.SetSun), set by the LiveGround presenter: called with the sun's
        /// (x, y, up) each time it moves a step (<see cref="SunStepHours"/>), then <see cref="SunStepped"/> asks for the
        /// chunks on screen to be rebuilt with it. Null: no baked sun (the classic sprites).
        /// </summary>
        public static System.Action<float, float, float> TerrainSun;

        /// <summary>The terrain's sun moved a step: rebuild what is on screen (its masks and ceilings were baked for the last).</summary>
        public static System.Action SunStepped;

        /// <summary>The baked sun moves in steps of this many UO hours (2.5 real minutes); the outline shadows move smoothly.</summary>
        public const float SunStepHours = 0.5f;

        private (float, float, float) _bakedSun = (float.NaN, 0f, 0f);

        /// <summary>The sun now (for the HUD and checks).</summary>
        public SunState Sun { get; private set; } = SunClock.At(14f);

        private static readonly Color GoldenTint = new Color(1.10f, 0.92f, 0.74f);

        private static readonly TimeOfDay[] Times =
        {
            new TimeOfDay("Day", new Color(1.00f, 1.00f, 1.00f), Color.black, 0.10f),
            new TimeOfDay("Golden hour", new Color(1.08f, 0.94f, 0.78f), new Color(0.12f, 0.08f, 0.02f), 0.22f),
            new TimeOfDay("Dusk", new Color(0.62f, 0.56f, 0.76f), new Color(0.55f, 0.38f, 0.18f), 0.38f),
            new TimeOfDay("Night", new Color(0.30f, 0.34f, 0.54f), new Color(0.95f, 0.68f, 0.34f), 0.55f)
        };

        /// <summary>The next fixed time of day (back to the server's light after the last, online); the message to log.</summary>
        public string CycleOverride(bool online)
        {
            _timeOverride = _timeOverride + 1 >= Times.Length ? (online ? -1 : 0) : _timeOverride + 1;
            return _timeOverride < 0 ? "Light follows the server's time of day." : $"Light set to {Times[_timeOverride].Name.ToLowerInvariant()}.";
        }

        /// <summary>Eases toward the target light and puts the light at the player (<paramref name="playerUnity"/>: the avatar's anchor in Unity units).</summary>
        public void Update(GameSession session, Vector2 playerUnity, int artScale, DemoHud hud)
        {
            TimeOfDay target;
            if (_timeOverride >= 0)
            {
                target = Times[_timeOverride];
            }
            else if (session != null)
            {
                // Server light: 0 = full day, ~26 and above = deep night. The personal light (0x4E, Night Sight) lifts it as
                // TazUO's IsometricLevel does (LightSources.EffectiveLevel); 0 changes nothing.
                (int overall, int personal) = ServerLevels(session);
                int level = LightSources.EffectiveLevel(overall, personal);
                float night = Mathf.Clamp01((level - 2) / 22f);
                target = night < 0.5f
                    ? TimeOfDay.Lerp(Times[0], Times[2], night * 2f)
                    : TimeOfDay.Lerp(Times[2], Times[3], night * 2f - 1f);
                target = new TimeOfDay(night < 0.15f ? "Day" : night < 0.6f ? "Dusk" : "Night", target.Ambient, target.Light, target.Vignette);
            }
            else
            {
                target = Times[0];
            }

            // The server's season (0xBC) tints the ambient light; summer leaves it as it is. We have no seasonal art
            // sets (bare winter trees and the like), so the tint is all a season changes.
            // The options' seasons off (FoliageWind.ShowSeasons): no tint.
            Season shownSeason = session != null ? FoliageWind.Shown(session.Season) : Season.Summer;
            if (shownSeason != Season.Summer)
                target = new TimeOfDay(target.Name, target.Ambient * SeasonTint(shownSeason), target.Light, target.Vignette);

            // The sun: the shard's clock where the player stands (or the T key's time, or -hour). Golden light low in the sky.
            double hour = _spinSeconds > 0f ? 8.0 + Mathf.Floor(Time.realtimeSinceStartup / _spinSeconds) % 18 * SunStepHours
                : !float.IsNaN(_fixedHour) ? _fixedHour
                : _timeOverride >= 0 ? SunClock.HourFor(Times[_timeOverride].Name)
                : session != null ? SunClock.Hour(System.DateTime.UtcNow, session.World.Map, session.World.Player?.X ?? 0)
                : 14.0;
            Sun = SunClock.At(hour);
            if ((_timeOverride < 0 || !float.IsNaN(_fixedHour)) && Sun.Warmth > 0f)
                target = new TimeOfDay(target.Name, target.Ambient * Color.Lerp(Color.white, GoldenTint, Sun.Warmth), target.Light, target.Vignette);
            // Weather (WeatherView): an overcast sky greys and dims the light and fades the sun's shadows.
            float overcast = WeatherView.Overcast;
            if (overcast > 0f)
                target = new TimeOfDay(target.Name, target.Ambient * Color.Lerp(Color.white, OvercastTint, overcast), target.Light,
                                       Mathf.Lerp(target.Vignette, Mathf.Max(target.Vignette, 0.3f), overcast));
            ApplySun(Sun, overcast);
            StepTerrainSun(hour);

            _timeCurrent = TimeOfDay.Lerp(_timeCurrent, target, 1f - Mathf.Exp(-Time.deltaTime * 2f));
            ApplyLighting(_timeCurrent);
            // Item and static lights (ItemLights) as strong as the player's light is for this darkness (none by day), and
            // only while the personal light is below the overall level, as TazUO's UseLights.
            bool lightsOn = _timeOverride >= 0 || session == null
                            || LightSources.LightsOn(ServerLevels(session).Overall, ServerLevels(session).Personal);
            Color l = _timeCurrent.Light;
            LocalLightScale = lightsOn ? Mathf.Clamp01(Mathf.Max(l.r, Mathf.Max(l.g, l.b)) / NightLightPeak) : 0f;
            // The soft glow round the player is their personal light (0x4E: Night Sight, a staff light), as in the classic
            // client, where a character gives no light of its own: without one only a carried torch or lamp lights them.
            // (The item lights' strength above still follows the time of day.)
            Shader.SetGlobalVector("_UOLightColor", session == null || ServerLevels(session).Personal > 0 ? l : Color.clear);
            int hh = (int)Sun.Hour, mm = (int)((Sun.Hour - hh) * 60f);
            hud.TimeName = target.Name + $" {hh:00}:{mm:00}{(Sun.Moon ? " moon" : "")}"
                           + (_timeOverride < 0 && session != null ? $" (server light {session.World.GlobalLight}{(CustomLightLevel ? $", custom {ServerLevels(session).Overall}" : "")})" : "")
                           + (session != null && session.Season != Season.Summer ? $", {session.Season.ToString().ToLowerInvariant()}" : "");
            hud.Vignette = _timeCurrent.Vignette;

            // A ghost sees the world drained of color; it fades out again on resurrection.
            // TazUO's EnableBlackWhiteEffect off (GhostGrey): the world keeps its colour.
            _dead = Mathf.MoveTowards(_dead, GhostGrey && session != null && session.PlayerDead ? 1f : 0f, Time.deltaTime * 1.5f);
            Shader.SetGlobalFloat("_UODead", _dead);
            hud.Ghost = session != null && session.PlayerDead;

            Vector2 light = playerUnity + new Vector2(0, 30 * artScale);
            Shader.SetGlobalVector("_UOLightPos", new Vector4(light.x, light.y, 170f * artScale, 0));
        }

        // ---- the options' lighting (GameOptions: useCustomLightLevel, lightLevel, lightLevelType, blackWhiteWhenDead) ----

        /// <summary>TazUO's UseCustomLightLevel: the world is lit at <see cref="CustomLevel"/> instead of the server's light.</summary>
        public static bool CustomLightLevel;

        /// <summary>TazUO's LightLevel (0 full day .. 0x1E darkest).</summary>
        public static int CustomLevel;

        /// <summary>TazUO's LightLevelType (GameOptions.LightLevelAbsolute / LightLevelMinimum).</summary>
        public static int CustomLevelType;

        /// <summary>TazUO's EnableBlackWhiteEffect: a ghost sees the world in grey. On by default, as always.</summary>
        public static bool GhostGrey = true;

        /// <summary>The overall and personal light levels the world is lit at: the server's, or the custom level (GameOptions.LightLevels).</summary>
        private static (int Overall, int Personal) ServerLevels(GameSession session) =>
            GameOptions.LightLevels(session.World.GlobalLight, session.World.PersonalLight, CustomLightLevel, CustomLevel, CustomLevelType);

        /// <summary>How strongly item and static lights show, 0 (day, or the personal light at the overall level) to 1 (night).</summary>
        public static float LocalLightScale { get; private set; }

        private const float NightLightPeak = 0.95f; // Times' "Night" player light, the brightest

        /// <summary>The outline shadows and the terrain's shading follow the sun every frame.</summary>
        private static readonly Color OvercastTint = new Color(0.74f, 0.77f, 0.83f);

        private static void ApplySun(SunState sun, float overcast = 0f)
        {
            CharacterShadow.SetSun(new Vector2(sun.TowardX, sun.TowardY), sun.Elevation, sun.Strength * (1f - overcast * 0.85f));
            (float x, float y, float up) = SunClock.Vector(sun);
            Shader.SetGlobalVector("_LGSunDir", new Vector4(x, up, -y, 0f)); // terrain space: x, up, -tile y
            // The water's glitter (WorldSprite UOWaterShimmer): the sun's or the moon's, as strong as its shadows, golden when low.
            Shader.SetGlobalVector("_UOWaterSun", new Vector4(sun.Moon ? 0f : 1f, sun.Strength * (1f - overcast), sun.Warmth, 0f));
        }

        /// <summary>Moves the terrain's baked sun when the hour crosses a step, and asks for the screen to be rebuilt.</summary>
        private void StepTerrainSun(double hour)
        {
            if (TerrainSun == null)
                return;
            float stepped = Mathf.Floor((float)hour / SunStepHours) * SunStepHours + SunStepHours * 0.5f;
            (float, float, float) v = SunClock.Vector(SunClock.At(stepped));
            if (v == _bakedSun)
                return; // the same sun (the moon stands still all night)
            bool first = float.IsNaN(_bakedSun.Item1);
            _bakedSun = v;
            TerrainSun(v.Item1, v.Item2, v.Item3);
            Debug.Log($"UOClient sun: baked terrain sun for {stepped:0.00} h ({v.Item1:0.00}, {v.Item2:0.00}, {v.Item3:0.00}){(first ? "" : ", rebuilding the screen")}");
            SunStepped?.Invoke();
        }

        /// <summary>Daylight, before there is a world to light.</summary>
        public static void ApplyDefault()
        {
            LocalLightScale = 0f;
            ApplyLighting(Times[0]);
            ApplySun(SunClock.At(14f));
            Shader.SetGlobalFloat("_UODead", 0f);
        }

        /// <summary>Multiplied into the ambient light: fresh green in spring, warm in fall, cold in winter, grey and dim in desolation.</summary>
        private static Color SeasonTint(Season season) => season switch
        {
            Season.Spring => new Color(0.97f, 1.03f, 0.95f),
            Season.Fall => new Color(1.04f, 0.95f, 0.84f),
            Season.Winter => new Color(0.90f, 0.95f, 1.08f),
            Season.Desolation => new Color(0.78f, 0.74f, 0.74f),
            _ => Color.white
        };

        private static void ApplyLighting(TimeOfDay t)
        {
            Shader.SetGlobalVector("_UOAmbient", t.Ambient);
            Shader.SetGlobalVector("_UOLightColor", t.Light);
        }

        private readonly struct TimeOfDay
        {
            public readonly string Name;
            public readonly Color Ambient, Light;
            public readonly float Vignette;

            public TimeOfDay(string name, Color ambient, Color light, float vignette)
            {
                Name = name;
                Ambient = ambient;
                Light = light;
                Vignette = vignette;
            }

            public static TimeOfDay Lerp(TimeOfDay a, TimeOfDay b, float t) =>
                new TimeOfDay(b.Name, Color.LerpUnclamped(a.Ambient, b.Ambient, t), Color.LerpUnclamped(a.Light, b.Light, t),
                              Mathf.Lerp(a.Vignette, b.Vignette, t));
        }
    }
}
