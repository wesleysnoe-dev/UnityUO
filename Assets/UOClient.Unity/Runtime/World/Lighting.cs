using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>Time-of-day lighting: the server's light level, or a fixed time chosen with T.</summary>
    public sealed class Lighting
    {
        // Time of day: index into Times; -1 follows the server's light level.
        private int _timeOverride = -1;
        private TimeOfDay _timeCurrent = Times[0];

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
                // Server light: 0 = full day, ~26 and above = deep night.
                float night = Mathf.Clamp01((session.World.GlobalLight - 2) / 22f);
                target = night < 0.5f
                    ? TimeOfDay.Lerp(Times[0], Times[2], night * 2f)
                    : TimeOfDay.Lerp(Times[2], Times[3], night * 2f - 1f);
                target = new TimeOfDay(night < 0.15f ? "Day" : night < 0.6f ? "Dusk" : "Night", target.Ambient, target.Light, target.Vignette);
            }
            else
            {
                target = Times[0];
            }

            _timeCurrent = TimeOfDay.Lerp(_timeCurrent, target, 1f - Mathf.Exp(-Time.deltaTime * 2f));
            ApplyLighting(_timeCurrent);
            hud.TimeName = target.Name + (_timeOverride < 0 && session != null ? $" (server light {session.World.GlobalLight})" : "");
            hud.Vignette = _timeCurrent.Vignette;

            Vector2 light = playerUnity + new Vector2(0, 30 * artScale);
            Shader.SetGlobalVector("_UOLightPos", new Vector4(light.x, light.y, 170f * artScale, 0));
        }

        /// <summary>Daylight, before there is a world to light.</summary>
        public static void ApplyDefault() => ApplyLighting(Times[0]);

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
