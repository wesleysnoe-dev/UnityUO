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
    /// so the camera, player, mobiles, items, statics and roofs are never touched. Ctrl+Shift+G toggles Original ⇄ Mesh.
    /// </summary>
    [Preserve]
    public sealed class LiveGroundController : ILandBackendSwitch
    {
        private readonly LiveGroundSwitch _switch;
        private readonly MeshLandEmitter _mesh;
        private readonly string _parseWarning;
        private WorldStreamer _world;

        [Preserve]
        public LiveGroundController(bool optIn, string groundBackend, int scale)
        {
            bool parsed = GroundBackend.TryParse(groundBackend, out GroundBackendKind requested);
            if (!parsed && groundBackend != null)
                _parseWarning = $"-groundbackend '{groundBackend}' not understood; using Original";
            _mesh = new MeshLandEmitter(scale);
            _switch = new LiveGroundSwitch(optIn, requested, Apply);
        }

        public GroundBackendKind Active => _switch.Active;

        public string Attach(WorldStreamer world, string host)
        {
            _world = world;
            string status = _switch.Attach(host);
            return _parseWarning == null ? status : $"{status}; {_parseWarning}";
        }

        public void Detach()
        {
            _switch.Detach();
            _world = null;
        }

        public string Tick()
        {
            if (_world == null || !Input.GetKeyDown(KeyCode.G))
                return null;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return ctrl && shift ? _switch.Toggle() : null;
        }

        private void Apply(GroundBackendKind kind)
        {
            if (_world != null)
                _world.LandEmitter = kind == GroundBackendKind.Mesh ? _mesh : null;
        }

        public void Dispose()
        {
            _switch.Dispose(); // puts the original land back on a still-attached world
            _world = null;
        }
    }

    /// <summary>Worker-side: turns a chunk's diverted land into <see cref="LandMeshData"/> (no Unity API).</summary>
    internal sealed class MeshLandEmitter : ILandEmitter
    {
        private readonly int _scale;

        public MeshLandEmitter(int scale) => _scale = scale;

        public bool Diverts(in DrawItem item, bool baked) => GroundBackend.Diverts(GroundBackendKind.Mesh, item, baked);

        public ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land)
        {
            var builder = new MeshLandBuilder(_scale);
            foreach (LandPlacement p in land)
                builder.AddTile(p.Item, p.Art, p.Texmap, p.Depth);
            LandMeshData data = builder.Finish();
            return data.VertexCount == 0 ? null : new MeshLandChunk(chunkX, chunkY, data);
        }
    }

    /// <summary>Main-thread upload of one chunk's land mesh; owns its mesh, texture, material and object.</summary>
    internal sealed class MeshLandChunk : ILandChunk
    {
        private readonly int _cx, _cy;
        private LandMeshData _data;
        private GameObject _go;
        private Mesh _mesh;
        private Texture2D _texture;
        private Material _material;

        public MeshLandChunk(int cx, int cy, LandMeshData data)
        {
            _cx = cx;
            _cy = cy;
            _data = data;
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
        }

        public void Dispose()
        {
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
