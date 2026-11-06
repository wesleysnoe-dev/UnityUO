using System;
using UnityEngine;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// Shows the Terrain backend. A Unity Terrain is Y-up and ignores rotation, so it cannot lie in the main camera's
    /// XY sprite plane; it lives on its own layer and a second camera draws it first, with a view matrix that maps
    /// terrain space through the exact UO projection (<see cref="TerrainLandBuilder.ToWorldScreen"/>) into the main
    /// camera's view. The main camera then clears depth only and draws statics, mobiles and roofs on top.
    ///
    /// Caveats (measured by design, not hidden): all diverted land sorts BEHIND every sprite (the original sorts land
    /// per diagonal against statics, so a static behind a hill now shows through it); the terrain is lit by its own
    /// directional light (the compositor's shade direction and flat = 1) and not by the time-of-day ambient or the
    /// player's light; Unity's heightmap triangulation/LOD decides the surface between samples.
    /// Owns everything it changes and puts the main camera back on <see cref="Dispose"/>.
    /// </summary>
    internal sealed class TerrainIsoPresenter : IDisposable
    {
        /// <summary>Unused by the project; the terrain camera sees only this layer and the main camera stops seeing it.</summary>
        public const int Layer = 31;

        /// <summary>A built-in shader the player always includes (GraphicsSettings), so the TEST build can draw it.</summary>
        public const string ShaderName = "Legacy Shaders/Diffuse";

        private const float DepthCenter = 10000f; // main camera: z -10000, near 0.1, far 20000
        private const float DepthPerPixel = 0.5f;

        private readonly WorldStreamer _world;
        private readonly int _scale;
        private readonly Camera _main;
        private readonly int _savedMask;
        private readonly CameraClearFlags _savedClear;
        private Camera _cam;
        private GameObject _light;
        private GameObject _root;
        private volatile string _failure;

        private TerrainIsoPresenter(WorldStreamer world, int scale, Camera main, Shader shader)
        {
            _world = world;
            _scale = scale;
            _main = main;
            Shader = shader;
            _savedMask = main.cullingMask;
            _savedClear = main.clearFlags;

            _root = new GameObject("LiveGround Terrain (TEST)");

            var camGo = new GameObject("LiveGround terrain camera");
            camGo.transform.SetParent(_root.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = main.backgroundColor;
            _cam.depth = main.depth - 1;
            _cam.targetTexture = main.targetTexture;
            _cam.rect = main.rect;
            _cam.allowHDR = main.allowHDR;
            _cam.allowMSAA = main.allowMSAA;
            _cam.useOcclusionCulling = false;
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane = main.farClipPlane;

            // The compositor's light (from the screen's upper left) in terrain space: u = +X, v = tile y = -Z, up = +Y.
            Vector3 toLight = new Vector3(-0.6f, 0.77f, 0.2f).normalized;
            _light = new GameObject("LiveGround terrain light");
            _light.transform.SetParent(_root.transform, false);
            _light.transform.rotation = Quaternion.LookRotation(-toLight);
            var light = _light.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.shadows = LightShadows.None;
            light.color = Color.white;
            light.intensity = Mathf.Max(0.1f, (1f - AmbientUp()) / toLight.y); // flat ground ≈ 1, as the compositor

            main.cullingMask &= ~(1 << Layer);
            main.clearFlags = CameraClearFlags.Depth;
            Camera.onPreCull += OnPreCull;
        }

        public Shader Shader { get; }

        public Transform Root => _root != null ? _root.transform : null;

        public bool IsDisposed => _root == null;

        /// <summary>Terrain chunks currently attached (shown by the TEST indicator).</summary>
        public int ChunkCount { get; set; }

        /// <summary>Set from any thread when a chunk could not be built; the controller falls back to Original.</summary>
        public string Failure => _failure;

        public void ReportFailure(string why)
        {
            if (_failure == null)
                _failure = why;
        }

        /// <summary>Null (and why) when the terrain cannot be shown here: no main camera or no usable shader.</summary>
        public static TerrainIsoPresenter TryCreate(WorldStreamer world, int scale, out string error)
        {
            error = null;
            try
            {
                Camera main = Camera.main;
                if (world == null || main == null)
                {
                    error = "no world or main camera";
                    return null;
                }
                Shader shader = Shader.Find(ShaderName);
                if (shader == null || !shader.isSupported)
                {
                    error = $"shader '{ShaderName}' is not in this build or not supported";
                    return null;
                }
                return new TerrainIsoPresenter(world, scale, main, shader);
            }
            catch (Exception e)
            {
                error = $"presenter: {e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        private static float AmbientUp()
        {
            var result = new Color[1];
            RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up }, result);
            return Mathf.Clamp(result[0].grayscale, 0f, 0.9f);
        }

        /// <summary>Every frame, after the main camera moved: the terrain camera's view follows it.</summary>
        private void OnPreCull(Camera c)
        {
            if (c != _cam || _main == null)
                return;
            float s = _scale, hz = 1f / TerrainLandBuilder.HeightUnitPerZ, k = DepthPerPixel;
            const float h = LiveGroundProjection.TileHalf, zs = LiveGroundProjection.ZStep;
            Vector3 camPos = _main.transform.position;
            Vector2 t = _world.ToUnity(Vector2.zero);
            Vector2 center = _world.FromUnity(new Vector2(camPos.x, camPos.y));
            float nearCenter = center.y / s; // Nearness of the ground (Z 0) under the screen center

            // Terrain space → main camera's world: x, y = ToUnity(ToWorldScreen(p)); z = a depth in front of the main
            // camera that grows away from the viewer (TerrainLandBuilder.Nearness), so hills hide what is behind them.
            var a = Matrix4x4.zero;
            a.SetRow(0, new Vector4(h * s, 0f, h * s, t.x));
            a.SetRow(1, new Vector4(-h * s, zs * hz * s, h * s, t.y));
            a.SetRow(2, new Vector4(-h * k, -zs * hz * k, h * k, camPos.z + DepthCenter + k * nearCenter));
            a.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            _cam.worldToCameraMatrix = _main.worldToCameraMatrix * a;
            _cam.projectionMatrix = _main.projectionMatrix;

            // Terrain LOD and basemap distances use the transform: keep it above the ground under the screen center.
            float d = center.x / (h * s), sum = center.y / (h * s);
            _cam.transform.position = new Vector3((sum + d) / 2f, 40f, -(sum - d) / 2f);
        }

        public void Dispose()
        {
            if (_root == null)
                return;
            Camera.onPreCull -= OnPreCull;
            if (_main != null)
            {
                _main.cullingMask = _savedMask;
                _main.clearFlags = _savedClear;
            }
            UnityEngine.Object.Destroy(_root); // camera, light and any terrain chunk not yet disposed
            _root = null;
            _cam = null;
            _light = null;
        }
    }
}
