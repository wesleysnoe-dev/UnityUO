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
    /// per diagonal against statics, so a static behind a hill now shows through it); Unity's heightmap
    /// triangulation/LOD decides the surface between samples.
    /// Light: with the lit shader (<see cref="SharedLight"/>) terrain and water read the sprites' own globals
    /// (time-of-day ambient, player light, ghost) at each point's Unity x, y, times the compositor's shade and the
    /// chunk's sun shadow (<see cref="LiveGroundLighting"/>); no Unity light is used. Without it the terrain falls back to
    /// Legacy Diffuse and its own isolated directional light, and the indicator says so.
    /// Water (<see cref="TerrainWaterSupport"/>) is a transparent mesh per chunk on the same layer, drawn after the
    /// terrain by this camera, so it shares the terrain's depth and sorts behind every sprite as well.
    /// Owns everything it changes and puts the main camera back on <see cref="Dispose"/>.
    /// </summary>
    internal sealed class TerrainIsoPresenter : IDisposable
    {
        /// <summary>Unused by the project; the terrain camera sees only this layer and the main camera stops seeing it.</summary>
        public const int Layer = 31;

        /// <summary>The land material lit like the sprites (Shaders/Resources/LiveGroundCompareTerrain.shader; Resources ship in the player).</summary>
        public const string LitShaderResource = "LiveGroundCompareTerrain";
        public const string LitShaderName = "LiveGroundCompare/TerrainLit";

        /// <summary>Fallback: a built-in shader the player always includes (GraphicsSettings), lit by the presenter's own light.</summary>
        public const string ShaderName = "Legacy Shaders/Diffuse";

        /// <summary>The terrain material's sun-visibility mask (LiveGroundLighting.ShadowMask).</summary>
        public const string SunMaskProperty = "_LGShadowTex";

        private static readonly int TerrainToUnityId = Shader.PropertyToID("_LGTerrainToUnity");
        private static readonly int SunDirId = Shader.PropertyToID("_LGSunDir");

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

        private TerrainIsoPresenter(WorldStreamer world, int scale, Camera main, Shader shader, string isolatedWhy)
        {
            _world = world;
            _scale = scale;
            _main = main;
            Shader = shader;
            SharedLight = isolatedWhy == null;
            LightIsolatedWhy = isolatedWhy;
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
            (float lx, float ly, float lz) = LiveGroundLighting.SunTerrain;
            Shader.SetGlobalVector(SunDirId, new Vector4(lx, ly, lz, 0f));
            if (!SharedLight)
            {
                // Fallback (no lit shader): Legacy Diffuse needs a Unity light; it is isolated from the sprites' light.
                Vector3 toLight = new Vector3(lx, ly, lz).normalized;
                _light = new GameObject("LiveGround terrain light");
                _light.transform.SetParent(_root.transform, false);
                _light.transform.rotation = Quaternion.LookRotation(-toLight);
                var light = _light.AddComponent<Light>();
                light.type = LightType.Directional;
                light.cullingMask = 1 << Layer;
                light.shadows = LightShadows.None;
                light.color = Color.white;
                light.intensity = Mathf.Max(0.1f, (1f - AmbientUp()) / toLight.y); // flat ground ≈ 1, as the compositor
            }

            main.cullingMask &= ~(1 << Layer);
            main.clearFlags = CameraClearFlags.Depth;
            Camera.onPreCull += OnPreCull;
            Debug.Log(SharedLight
                ? "UOClient LiveGround light: shared (terrain and water read the sprites' time-of-day, player light and ghost globals; terrain sun shadow from map heights)"
                : $"UOClient LiveGround light: isolated ({isolatedWhy})");

            // Water is optional: without tiledata or its shader the terrain still draws, and the indicator says why.
            Water = TerrainWaterSupport.TryCreate(world, out string waterError);
            WaterError = waterError;
            Debug.Log(Water != null
                ? $"UOClient LiveGround water: on ({WaterTiles.WaterName} land rows with Wet: {CountWater(Water.Table)})"
                : $"UOClient LiveGround water: off ({waterError})");
        }

        public Shader Shader { get; }

        /// <summary>True when the land material is the lit shader that reads the sprites' light globals.</summary>
        public bool SharedLight { get; }

        /// <summary>Why the terrain fell back to its own isolated light (null when shared).</summary>
        public string LightIsolatedWhy { get; }

        /// <summary>Null when water is off (see <see cref="WaterError"/>).</summary>
        public TerrainWaterSupport Water { get; private set; }

        public string WaterError { get; }

        /// <summary>The first water chunk that could not be uploaded (its terrain stays), for the indicator.</summary>
        public string WaterChunkError { get; set; }

        /// <summary>Water tiles / chunks with water currently attached (main thread).</summary>
        public int WaterTileCount { get; set; }

        public int WaterChunkCount { get; set; }

        /// <summary>Whether the first water chunk was logged (one Player.log line per presenter, not per chunk).</summary>
        public bool LoggedWaterChunk { get; set; }

        private static int CountWater(bool[] table)
        {
            int n = 0;
            foreach (bool w in table)
                if (w)
                    n++;
            return n;
        }

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
                Shader lit = Resources.Load<Shader>(LitShaderResource);
                if (lit == null)
                    lit = Shader.Find(LitShaderName);
                if (lit != null && lit.isSupported)
                    return new TerrainIsoPresenter(world, scale, main, lit, null);

                Shader shader = Shader.Find(ShaderName);
                if (shader == null || !shader.isSupported)
                {
                    error = $"shader '{ShaderName}' is not in this build or not supported";
                    return null;
                }
                return new TerrainIsoPresenter(world, scale, main, shader, $"shader '{LitShaderName}' is not in this build or not supported");
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
            (float[] rx, float[] ry) = LiveGroundLighting.TerrainToUnityRows(_scale, t.x, t.y);
            var a = Matrix4x4.zero;
            a.SetRow(0, new Vector4(rx[0], rx[1], rx[2], rx[3]));
            a.SetRow(1, new Vector4(ry[0], ry[1], ry[2], ry[3]));
            a.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            // The terrain and water shaders light each point at its Unity x, y, as the sprites (the player light's distance).
            Shader.SetGlobalMatrix(TerrainToUnityId, a);
            a.SetRow(2, new Vector4(-h * k, -zs * hz * k, h * k, camPos.z + DepthCenter + k * nearCenter));
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
            Water?.Dispose();
            Water = null;
            _root = null;
            _cam = null;
            _light = null;
        }
    }
}
