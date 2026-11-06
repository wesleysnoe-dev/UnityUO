using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting;
using UOClient.ArtPipeline;
using UOClient.Unity;

[assembly: AlwaysLinkAssembly]

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// TEST-only land backend toggle. WorldDemo creates it by name under -liveground; every change goes through
    /// <see cref="LiveGroundSwitch"/> (fail-closed host guard) and only swaps <see cref="WorldStreamer.LandEmitter"/>,
    /// so the camera, player, mobiles, items, statics and roofs are never touched. Ctrl+Shift+G cycles
    /// Original → Mesh → Terrain. A persistent overlay shows requested/active/fallback/host, and every attach, toggle
    /// and fallback is also written to Player.log.
    /// </summary>
    [Preserve]
    public sealed class LiveGroundController : ILandBackendSwitch
    {
        private readonly LiveGroundSwitch _switch;
        private readonly MeshLandEmitter _mesh;
        private readonly int _scale;
        private readonly string _parseWarning;
        private readonly LiveGroundOverlay _overlay;
        private WorldStreamer _world;
        private TerrainIsoPresenter _terrain;
        private bool _awaitingChunks; // an experimental backend was applied; log once when its first chunks attach

        [Preserve]
        public LiveGroundController(bool optIn, string groundBackend, int scale)
        {
            bool parsed = GroundBackend.TryParse(groundBackend, out GroundBackendKind requested);
            if (!parsed && groundBackend != null)
                _parseWarning = $"-groundbackend '{groundBackend}' not understood; using Original";
            _scale = scale;
            _mesh = new MeshLandEmitter(scale);
            _switch = new LiveGroundSwitch(optIn, requested, Apply);

            var go = new GameObject("LiveGround TEST indicator");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _overlay = go.AddComponent<LiveGroundOverlay>();
            _overlay.Controller = this;
            Report("created");
        }

        public GroundBackendKind Active => _switch.Active;

        /// <summary>Land chunks the active backend has attached right now (0 for Original).</summary>
        private int ActiveChunks => _switch.Active == GroundBackendKind.Mesh ? _mesh.Chunks.Value
            : _switch.Active == GroundBackendKind.Terrain && _terrain != null ? _terrain.ChunkCount : 0;

        /// <summary>The indicator line (also what Player.log gets).</summary>
        public string Indicator
        {
            get
            {
                string line = LiveGroundStatus.Indicator(_switch.Requested, _switch.Active, _switch.Reason, _switch.Host, _switch.IsAttached, ActiveChunks);
                if (_switch.Active == GroundBackendKind.Terrain && _terrain != null && !_terrain.IsDisposed)
                    line += " | " + WaterIndicator(_terrain);
                return _parseWarning == null ? line : $"{line} | {_parseWarning}";
            }
        }

        private static string WaterIndicator(TerrainIsoPresenter t)
        {
            if (t.Water == null)
                return $"water off: {t.WaterError}";
            string s = $"water tiles {t.WaterTileCount} in {t.WaterChunkCount} chunks";
            return t.WaterChunkError == null ? s : $"{s} (error: {t.WaterChunkError})";
        }

        public bool IsFallback => _switch.IsAttached && LiveGroundStatus.IsFallback(_switch.Requested, _switch.Active, _switch.Reason);

        public string Attach(WorldStreamer world, string host)
        {
            _world = world;
            string status = _switch.Attach(host);
            Report("attach");
            return _parseWarning == null ? status : $"{status}; {_parseWarning}";
        }

        public void Detach()
        {
            _switch.Detach();
            DisposeTerrain(); // the world (and its terrain chunks) is going away; the main camera gets its settings back
            _world = null;
        }

        public string Tick()
        {
            if (_world == null)
                return null;
            if (_terrain?.Failure is string failure)
            {
                string status = _switch.Fail(failure);
                Report("terrain failed");
                return status;
            }
            if (_awaitingChunks && ActiveChunks > 0)
            {
                _awaitingChunks = false; // the attach/toggle line was logged before the streamer rebuilt anything
                Report("chunks attached");
            }
            if (!Input.GetKeyDown(KeyCode.G))
                return null;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl || !shift)
                return null;
            string toggled = _switch.Toggle();
            Report("toggle");
            return toggled;
        }

        /// <summary>Returns null when applied, else why not (the switch then applies Original and shows the reason).</summary>
        private string Apply(GroundBackendKind kind)
        {
            _awaitingChunks = false;
            if (_world == null)
                return null;
            if (kind == GroundBackendKind.Terrain)
            {
                if (_terrain == null || _terrain.IsDisposed)
                {
                    _terrain = TerrainIsoPresenter.TryCreate(_world, _scale, out string error);
                    if (_terrain == null)
                        return error;
                }
                _world.LandEmitter = new TerrainLandEmitter(_terrain, _scale);
                _awaitingChunks = true;
                return null;
            }
            _world.LandEmitter = kind == GroundBackendKind.Mesh ? _mesh : null; // disposes any terrain chunks first
            DisposeTerrain();
            _awaitingChunks = kind == GroundBackendKind.Mesh;
            return null;
        }

        private void DisposeTerrain()
        {
            _terrain?.Dispose();
            _terrain = null;
        }

        private void Report(string what)
        {
            string line = $"UOClient LiveGround ({what}): {Indicator}";
            if (IsFallback)
                Debug.LogWarning(line);
            else
                Debug.Log(line);
        }

        public void Dispose()
        {
            _switch.Dispose(); // puts the original land back on a still-attached world
            DisposeTerrain();
            _world = null;
            if (_overlay != null)
                UnityEngine.Object.Destroy(_overlay.gameObject);
        }
    }

    /// <summary>The TEST indicator: one line at the top of the screen every frame while -liveground; red on fallback.</summary>
    internal sealed class LiveGroundOverlay : MonoBehaviour
    {
        public LiveGroundController Controller;
        private GUIStyle _style;
        private string _text;

        private void Update() => _text = Controller?.Indicator;

        private void OnGUI()
        {
            if (_text == null)
                return;
            _style ??= new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            bool warn = Controller.IsFallback;
            _style.normal.textColor = warn ? new Color(1f, 0.35f, 0.3f) : new Color(0.6f, 1f, 0.6f);
            GUI.depth = -1000;
            Vector2 size = _style.CalcSize(new GUIContent(_text));
            GUI.Box(new Rect((Screen.width - size.x) / 2f, 4f, size.x + 12f, size.y + 4f), _text, _style);
        }
    }

    /// <summary>Attached land chunks of one backend (main thread only), for the indicator.</summary>
    internal sealed class LandChunkCount
    {
        public int Value;
    }

    /// <summary>Worker-side: turns a chunk's diverted land into <see cref="LandMeshData"/> (no Unity API).</summary>
    internal sealed class MeshLandEmitter : ILandEmitter
    {
        private readonly int _scale;

        public MeshLandEmitter(int scale) => _scale = scale;

        public LandChunkCount Chunks { get; } = new LandChunkCount();

        public bool Diverts(in DrawItem item, bool baked) => GroundBackend.Diverts(GroundBackendKind.Mesh, item, baked);

        public ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land)
        {
            var builder = new MeshLandBuilder(_scale);
            foreach (LandPlacement p in land)
                builder.AddTile(p.Item, p.Art, p.Texmap, p.Depth);
            LandMeshData data = builder.Finish();
            return data.VertexCount == 0 ? null : new MeshLandChunk(chunkX, chunkY, data, Chunks);
        }
    }

    /// <summary>Main-thread upload of one chunk's land mesh; owns its mesh, texture, material and object.</summary>
    internal sealed class MeshLandChunk : ILandChunk
    {
        private readonly int _cx, _cy;
        private readonly LandChunkCount _count;
        private LandMeshData _data;
        private GameObject _go;
        private Mesh _mesh;
        private Texture2D _texture;
        private Material _material;
        private bool _counted;

        public MeshLandChunk(int cx, int cy, LandMeshData data, LandChunkCount count)
        {
            _cx = cx;
            _cy = cy;
            _data = data;
            _count = count;
        }

        public void Attach(Transform chunk, Shader shader, IWorldSpace world)
        {
            LandMeshData d = _data;
            _data = null; // the arrays are not needed once uploaded

            _texture = new Texture2D(d.AtlasWidth, d.AtlasHeight, TextureFormat.RGBA32, false)
            {
                name = $"UO land mesh {_cx},{_cy}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _texture.SetPixelData(d.AtlasRgba, 0);
            _texture.Apply(false, true);

            var vertices = new Vector3[d.VertexCount];
            var uvs = new Vector2[d.VertexCount];
            var cut = new Vector2[d.VertexCount]; // MeshBuilder.NeverCut: land always draws
            var colors = new Color[d.VertexCount];
            for (int i = 0; i < d.VertexCount; i++)
            {
                Vector2 p = world.ToUnity(new Vector2(d.X[i], d.Y[i]));
                vertices[i] = new Vector3(p.x, p.y, d.Depth[i]);
                uvs[i] = new Vector2(d.U[i], d.V[i]);
                colors[i] = new Color(d.Shade[i], d.Shade[i], d.Shade[i], 1f);
            }

            _mesh = new Mesh { name = $"Land mesh {_cx},{_cy}", indexFormat = IndexFormat.UInt32 };
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.uv2 = cut;
            _mesh.colors = colors;
            _mesh.triangles = d.Indices;
            _mesh.RecalculateBounds();

            _material = new Material(shader) { mainTexture = _texture, name = $"UO land mesh {_cx},{_cy}" };
            _go = new GameObject("Land (LiveGround mesh)");
            _go.transform.SetParent(chunk, false);
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _count.Value++;
            _counted = true;
        }

        public void Dispose()
        {
            if (_counted)
                _count.Value--;
            _counted = false;
            _data = null;
            if (_mesh != null) Object.Destroy(_mesh);
            if (_material != null) Object.Destroy(_material);
            if (_texture != null) Object.Destroy(_texture);
            if (_go != null) Object.Destroy(_go);
            _mesh = null;
            _material = null;
            _texture = null;
            _go = null;
        }
    }
}
