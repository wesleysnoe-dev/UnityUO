using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.LiveGroundCompare
{
    /// <summary>Main thread: one chunk's grass (GrassBuilder) as a mesh under the presenter's root, on the terrain camera's layer.</summary>
    internal sealed class TerrainGrass : IDisposable
    {
        private GameObject _go;
        private Mesh _mesh;
        private Material _material;

        public static TerrainGrass Attach(Transform root, Shader shader, Texture sunMask, GrassData d, string name)
        {
            if (root == null || shader == null || d == null || d.Count == 0)
                return null;
            var g = new TerrainGrass();
            try
            {
                int n = d.VertexCount;
                var vertices = new Vector3[n];
                var colors = new Color32[n];
                var uv = new Vector2[n];
                var chunkUv = new Vector2[n];
                var indices = new int[n];
                for (int i = 0; i < n; i++)
                {
                    vertices[i] = new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                    colors[i] = new Color32(d.Colors[i * 4], d.Colors[i * 4 + 1], d.Colors[i * 4 + 2], d.Colors[i * 4 + 3]);
                    uv[i] = new Vector2(d.Uv[i * 2], d.Uv[i * 2 + 1]);
                    chunkUv[i] = new Vector2(d.ChunkUv[i * 2], d.ChunkUv[i * 2 + 1]);
                    indices[i] = i;
                }
                g._mesh = new Mesh { name = $"UO grass {name}", indexFormat = IndexFormat.UInt32 };
                g._mesh.vertices = vertices;
                g._mesh.colors32 = colors;
                g._mesh.uv = uv;
                g._mesh.uv2 = chunkUv;
                g._mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                g._mesh.RecalculateBounds();
                // Wind moves the tips a little outside the mesh's rest bounds.
                Bounds bounds = g._mesh.bounds;
                bounds.Expand(0.6f);
                g._mesh.bounds = bounds;

                g._material = new Material(shader) { name = $"UO grass {name}" };
                if (sunMask != null)
                    g._material.SetTexture(TerrainIsoPresenter.SunMaskProperty, sunMask);
                g._go = new GameObject("Grass (LiveGround terrain)") { layer = TerrainIsoPresenter.Layer };
                g._go.transform.SetParent(root, false);
                g._go.AddComponent<MeshFilter>().sharedMesh = g._mesh;
                var r = g._go.AddComponent<MeshRenderer>();
                r.sharedMaterial = g._material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return g;
            }
            catch
            {
                g.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            _go = null;
            _mesh = null;
            _material = null;
        }
    }
}
