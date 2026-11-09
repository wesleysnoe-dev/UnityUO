using System;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// What the Terrain backend needs for water: which land graphics are water (<see cref="WaterTiles"/>, from the
    /// tiledata the streamer itself composes with) and the water shader. Either missing turns water off with a reason;
    /// the terrain itself is unaffected.
    /// </summary>
    internal sealed class TerrainWaterSupport : IDisposable
    {
        /// <summary>Resources path of Shaders/Resources/LiveGroundCompareWater.shader (Resources ship in the player).</summary>
        public const string ShaderResource = "LiveGroundCompareWater";
        public const string ShaderName = "LiveGroundCompare/Water";

        /// <summary>Terrain-space direction to the viewer: the kernel of the presenter's screen projection, (1, √2, -1)/2.</summary>
        public static readonly Vector4 EyeDir = new Vector4(0.5f, 0.70710678f, -0.5f, 0f);

        private TerrainWaterSupport(bool[] table, bool[] statics, Material material)
        {
            Table = table;
            StaticTable = statics;
            Material = material;
        }

        /// <summary>Land graphic → water (index = graphic).</summary>
        public bool[] Table { get; }

        /// <summary>Static graphic → open-water static the surface takes over (<see cref="WaterTiles.IsOceanStatic"/>).</summary>
        public bool[] StaticTable { get; }

        /// <summary>One runtime material for every chunk (per-tile colour and shore distance are vertex data).</summary>
        public Material Material { get; private set; }

        public static TerrainWaterSupport TryCreate(WorldStreamer world, out string error)
        {
            error = null;
            try
            {
                if (Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-noterrain-water", StringComparison.OrdinalIgnoreCase)))
                {
                    error = "-noterrain-water"; // diagnosis
                    return null;
                }
                if (!WaterTiles.TryLand(world?.Data, out var land, out error))
                    return null;
                bool[] table = WaterTiles.Table(land);
                Shader shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null)
                    shader = Shader.Find(ShaderName);
                if (shader == null || !shader.isSupported)
                {
                    error = $"shader '{ShaderName}' is not in this build or not supported";
                    return null;
                }
                var material = new Material(shader) { name = "UO land water (LiveGround)" };
                material.SetVector("_EyeDir", EyeDir);
                return new TerrainWaterSupport(table, WaterTiles.StaticTable(world.Data.TileData.Statics), material);
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        public void Dispose()
        {
            if (Material != null)
                UnityEngine.Object.Destroy(Material);
            Material = null;
        }
    }

    /// <summary>Main-thread upload of one chunk's water mesh under the presenter's root (terrain space = its local space).</summary>
    internal sealed class TerrainWaterSurface : IDisposable
    {
        private Mesh _mesh;
        private GameObject _go;

        public static TerrainWaterSurface Attach(Transform root, Material material, WaterSurfaceData d, string name)
        {
            if (root == null || material == null || d == null || d.VertexCount == 0)
                return null;
            var s = new TerrainWaterSurface();
            try
            {
                int n = d.VertexCount;
                var vertices = new Vector3[n];
                var colors = new Color32[n];
                var uvs = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    vertices[i] = new Vector3(d.X[i], d.Y[i], d.Z[i]);
                    uint c = d.Tint[i];
                    colors[i] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
                    uvs[i] = new Vector2(d.Shore[i], 0f);
                }
                s._mesh = new Mesh { name = $"UO land water {name}", indexFormat = IndexFormat.UInt32 };
                s._mesh.vertices = vertices;
                s._mesh.colors32 = colors;
                s._mesh.uv = uvs;
                s._mesh.triangles = d.Indices;
                s._mesh.RecalculateNormals();
                s._mesh.RecalculateBounds();

                s._go = new GameObject("Water (LiveGround terrain)") { layer = TerrainIsoPresenter.Layer };
                s._go.transform.SetParent(root, false);
                s._go.AddComponent<MeshFilter>().sharedMesh = s._mesh;
                var r = s._go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _go = null;
            _mesh = null;
        }
    }
}
