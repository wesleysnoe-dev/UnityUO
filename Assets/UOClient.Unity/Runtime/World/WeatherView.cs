using System;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// The weather over the world: Resources/Weather.shader on one quad in front of the main camera (after the sprites,
    /// before the UI). Follows the server's weather (0x65: rain, fierce storm, snow, storm brewing; ModernUO counts 0..70)
    /// and adds morning mist (UO hours 4 to 8). Also: an overcast sky dims the light and the sun's shadows
    /// (<see cref="Overcast"/>, read by Lighting), storms bend the grass harder (_UOWindBoost) and flash with lightning.
    /// -weather rain|storm|snow|fog|clear fixes it (captures, checks); -noweather turns this off (the old HUD streaks).
    /// </summary>
    public sealed class WeatherView : IDisposable
    {
        public static readonly bool Enabled = !Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-noweather", StringComparison.OrdinalIgnoreCase));

        /// <summary>0 clear to 1 heavy cloud: Lighting dims the ambient and fades the sun's shadows by it.</summary>
        public static float Overcast { get; private set; }

        /// <summary>Whether this view draws the weather (the HUD's screen streaks stay off then).</summary>
        public static bool Active { get; private set; }

        private static readonly int RainId = Shader.PropertyToID("_WRain"), SnowId = Shader.PropertyToID("_WSnow"),
            FogId = Shader.PropertyToID("_WFog"), WindId = Shader.PropertyToID("_WWind"), FlashId = Shader.PropertyToID("_WFlash"),
            OriginId = Shader.PropertyToID("_WOrigin"), ScaleId = Shader.PropertyToID("_WScale"),
            WindBoostId = Shader.PropertyToID("_UOWindBoost");

        private readonly GameObject _go;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private readonly string _forced = Forced();

        private float _rain, _snow, _fog, _storm, _wind = 0.15f;
        private float _nextFlash = 6f, _flashAge = 99f;

        public string Status { get; private set; } = "clear";

        private static string Forced()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-weather", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1].ToLowerInvariant();
            return null;
        }

        public WeatherView(Camera camera)
        {
            Shader shader = Enabled ? Resources.Load<Shader>("Weather") : null;
            if (shader == null || !shader.isSupported || camera == null)
            {
                Debug.Log($"UOClient weather: off ({(!Enabled ? "-noweather" : shader == null ? "shader missing" : camera == null ? "no camera" : "shader unsupported")})");
                return;
            }
            _go = new GameObject("Weather");
            _go.transform.SetParent(camera.transform, false);
            _go.transform.localPosition = new Vector3(0f, 0f, camera.nearClipPlane + 1f);
            var mesh = new Mesh { name = "Weather quad" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f)); // never culled
            _go.AddComponent<MeshFilter>().sharedMesh = mesh;
            _material = new Material(shader) { name = "Weather" };
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
            Active = true;
            Debug.Log($"UOClient weather: on (server weather 0x65, morning mist{(_forced != null ? $", fixed: {_forced}" : "")})");
        }

        /// <summary>Every frame: eases toward the weather now and draws it over <paramref name="camera"/>'s view.</summary>
        public void Update(GameSession session, Camera camera, float uoHour)
        {
            if (_renderer == null || camera == null)
                return;
            float rain = 0f, snow = 0f, fog = 0f, storm = 0f;
            string kind = _forced;
            Weather w = session?.Weather;
            if (kind == null && w != null)
            {
                float amount = w.Count <= 0 ? 0f : Mathf.Clamp01(0.35f + w.Count / 70f * 0.65f);
                switch (w.Type)
                {
                    case WeatherType.Rain: rain = amount; break;
                    case WeatherType.FierceStorm: rain = Mathf.Max(0.85f, amount); storm = 1f; break;
                    case WeatherType.Snow: snow = amount; break;
                    case WeatherType.StormBrewing: fog = 0.25f; storm = 0.3f; break;
                }
            }
            else if (kind != null)
            {
                switch (kind)
                {
                    case "rain": rain = 0.7f; break;
                    case "storm": rain = 1f; storm = 1f; break;
                    case "snow": snow = 0.8f; break;
                    case "fog": fog = 0.8f; break;
                }
            }
            // Mist at dawn (the server's 4:00 to 8:00), and some with rain.
            if (kind == null)
            {
                float mist = uoHour < 4f || uoHour > 8f ? 0f : Mathf.Sin((uoHour - 4f) / 4f * Mathf.PI);
                fog = Mathf.Max(fog, mist * 0.45f);
            }
            fog = Mathf.Max(fog, rain * 0.3f);

            float k = 1f - Mathf.Exp(-Time.deltaTime * 0.4f); // weather comes and goes over a few seconds
            _rain = Mathf.Lerp(_rain, rain, k);
            _snow = Mathf.Lerp(_snow, snow, k);
            _fog = Mathf.Lerp(_fog, fog, k);
            _storm = Mathf.Lerp(_storm, storm, k);
            float wind = 0.15f + _storm * 0.35f + Mathf.Sin(Time.time * 0.13f) * 0.06f;
            _wind = Mathf.Lerp(_wind, wind, k);
            Overcast = Mathf.Clamp01(Mathf.Max(Mathf.Max(_rain * 0.75f, _snow * 0.55f), Mathf.Max(_storm * 0.9f, _fog * 0.4f)));
            Shader.SetGlobalFloat(WindBoostId, _storm * 1.6f + _rain * 0.5f);

            // Lightning in a storm: every 7 to 20 seconds, a double flash.
            float flash = 0f;
            if (_storm > 0.5f)
            {
                _nextFlash -= Time.deltaTime;
                if (_nextFlash <= 0f)
                {
                    _flashAge = 0f;
                    _nextFlash = UnityEngine.Random.Range(7f, 20f);
                }
            }
            _flashAge += Time.deltaTime;
            if (_flashAge < 0.6f)
                flash = Mathf.Max(Pulse(_flashAge, 0.0f, 0.09f), Pulse(_flashAge, 0.18f, 0.35f) * 0.8f);

            bool show = _rain > 0.01f || _snow > 0.01f || _fog > 0.01f || flash > 0f;
            _renderer.enabled = show;
            Status = !show ? "clear"
                : $"{(_rain > 0.01f ? $"rain {_rain:0.00} " : "")}{(_snow > 0.01f ? $"snow {_snow:0.00} " : "")}{(_fog > 0.01f ? $"fog {_fog:0.00} " : "")}{(_storm > 0.01f ? $"storm {_storm:0.00}" : "")}".Trim();
            if (!show)
                return;
            float h = camera.orthographicSize * 2f, wdt = h * camera.aspect;
            _go.transform.localScale = new Vector3(wdt * 1.02f, h * 1.02f, 1f);
            _material.SetFloat(RainId, _rain);
            _material.SetFloat(SnowId, _snow);
            _material.SetFloat(FogId, _fog);
            _material.SetFloat(WindId, _wind);
            _material.SetFloat(FlashId, flash);
            Vector3 p = camera.transform.position;
            _material.SetVector(OriginId, new Vector4(p.x - wdt / 2f, p.y + h / 2f, 0f, 0f)); // the view's top left, Unity units
            _material.SetFloat(ScaleId, Screen.height / Mathf.Max(1f, h));
        }

        private static float Pulse(float age, float start, float end) =>
            age < start || age > end ? 0f : Mathf.Sin((age - start) / (end - start) * Mathf.PI);

        public void Dispose()
        {
            Active = false;
            Overcast = 0f;
            Shader.SetGlobalFloat(WindBoostId, 0f);
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
