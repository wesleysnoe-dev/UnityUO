using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// Main thread: one chunk's grass (GrassBuilder) as a mesh drawn by the MAIN camera among the sprites: a child of the
    /// streamer's chunk (its depth-origin offset, like the occlusion proxy), at each blade's projected pixel and depth key
    /// (<see cref="GrassBuilder.Project"/>), so blades in front of a character cover its feet and hills hide grass.
    /// The terrain position rides along (UV3) for the wind's phase and the terrain light.
    /// </summary>
    internal sealed class TerrainGrass : IDisposable
    {
        private GameObject _go;
        private Mesh _mesh;
        private Material _material;

        private static readonly int WindVecId = Shader.PropertyToID("_WindVec");
        private static readonly int UpVecId = Shader.PropertyToID("_UpVec");

        public static TerrainGrass Attach(Transform chunk, IWorldSpace world, Shader shader, Texture sunMask, GrassData d, string name)
        {
            if (chunk == null || world == null || shader == null || d == null || d.Count == 0 || d.Depth == null)
                return null;
            var g = new TerrainGrass();
            try
            {
                var vertices = new List<Vector3>(d.VertexCount);
                var colors = new List<Color32>(d.VertexCount);
                var uv = new List<Vector2>(d.VertexCount);
                var chunkUv = new List<Vector2>(d.VertexCount);
                var terrain = new List<Vector3>(d.VertexCount);
                for (int v = 0; v < d.VertexCount; v += 3)
                {
                    if (float.IsNaN(d.Depth[v]))
                        continue; // no land depth on that tile
                    for (int k = v; k < v + 3; k++)
                    {
                        Vector2 u = world.ToUnity(new Vector2(d.ScreenX[k], d.ScreenY[k]));
                        vertices.Add(new Vector3(u.x, u.y, d.Depth[k]));
                        colors.Add(new Color32(d.Colors[k * 4], d.Colors[k * 4 + 1], d.Colors[k * 4 + 2], d.Colors[k * 4 + 3]));
                        uv.Add(new Vector2(d.Uv[k * 2], d.Uv[k * 2 + 1]));
                        chunkUv.Add(new Vector2(d.ChunkUv[k * 2], d.ChunkUv[k * 2 + 1]));
                        terrain.Add(new Vector3(d.Positions[k * 3], d.Positions[k * 3 + 1], d.Positions[k * 3 + 2]));
                    }
                }
                if (vertices.Count == 0)
                    return null;
                var indices = new int[vertices.Count];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = i;
                g._mesh = new Mesh { name = $"UO grass {name}", indexFormat = IndexFormat.UInt32 };
                g._mesh.SetVertices(vertices);
                g._mesh.SetColors(colors);
                g._mesh.SetUVs(0, uv);
                g._mesh.SetUVs(1, chunkUv);
                g._mesh.SetUVs(2, terrain);
                g._mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                g._mesh.RecalculateBounds();
                Bounds bounds = g._mesh.bounds;
                bounds.Expand(new Vector3(40f, 40f, 0f)); // the wind moves tips a little outside the rest bounds
                g._mesh.bounds = bounds;

                // The wind and "up" as Unity vectors per terrain unit: wind along terrain (1, 0, 1), straight across the
                // screen; up is one terrain height unit (the projection is linear, so any point gives the same deltas).
                Vector2 p0 = world.ToUnity(Vector2.zero);
                (float wx, float wy) = TerrainLandBuilder.ToWorldScreen(0.70710678f, 0f, 0.70710678f, 1);
                (float ux, float uy) = TerrainLandBuilder.ToWorldScreen(0f, 1f, 0f, 1);
                float s = d.Scale;
                Vector2 wind = world.ToUnity(new Vector2(wx * s, wy * s)) - p0;
                Vector2 up = world.ToUnity(new Vector2(ux * s, uy * s)) - p0;

                g._material = new Material(shader) { name = $"UO grass {name}" };
                g._material.SetVector(WindVecId, new Vector4(wind.x, wind.y, 0f, 0f));
                g._material.SetVector(UpVecId, new Vector4(up.x, up.y, 0f, 0f));
                if (sunMask != null)
                    g._material.SetTexture(TerrainIsoPresenter.SunMaskProperty, sunMask);
                g._go = new GameObject("Grass (LiveGround terrain)") { layer = chunk.gameObject.layer };
                g._go.transform.SetParent(chunk, false);
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
