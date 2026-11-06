using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// P2 fluid visuals for one side: a flat subdivided surface per SampleElevationGrid.FluidRect at
    /// surfaceZ * zScale (corner heights are never modified; the ground simply pokes through where it is
    /// higher than the surface). Lava rects also get one real point light and a few matte grey probes so
    /// emission (which lights nothing) can be compared with actual light falloff.
    /// Built identically on both sides relative to the side origin.
    /// </summary>
    public static class FluidSurfaceBuilder
    {
        public const int SubdivisionsPerTile = 2;
        public const float LavaLightHeight = 0.9f;
        public const float LavaLightRange = 6f;
        public const float LavaLightIntensity = 2.6f;
        public static readonly Color LavaLightColor = new Color(1f, 0.46f, 0.16f, 1f);

        public sealed class SideFluids
        {
            public GameObject Root;
            public readonly List<Light> LavaLights = new List<Light>();
        }

        public static SideFluids Build(SampleElevationGrid grid, Vector3 origin, Transform parent, GroundVisualFactory visuals, string sideName)
        {
            var side = new SideFluids { Root = new GameObject("Fluids_" + sideName + " (SAMPLE)") };
            side.Root.transform.SetParent(parent, false);
            side.Root.transform.position = origin;

            foreach (var f in grid.fluids)
            {
                var mat = f.IsLava ? visuals.LavaMaterial : visuals.WaterMaterial;
                if (mat == null) continue; // shader missing: overlay reports it, no pink surfaces

                var go = new GameObject($"{(f.IsLava ? "Lava" : "Water")}_{f.tx}_{f.ty}");
                go.transform.SetParent(side.Root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = visuals.Track(SurfaceMesh(grid, f));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = f.IsLava;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GroundVisualFactory.SetMapOrigin(mr, origin);

                if (f.IsLava)
                {
                    side.LavaLights.Add(LavaLight(grid, f, go.transform));
                    MatteProbes(grid, f, side.Root.transform, visuals);
                }
            }
            return side;
        }

        public static float SurfaceY(SampleElevationGrid grid, SampleElevationGrid.FluidRect f) => f.surfaceZ * grid.zScale;

        private static Mesh SurfaceMesh(SampleElevationGrid grid, SampleElevationGrid.FluidRect f)
        {
            int nx = f.w * SubdivisionsPerTile, ny = f.h * SubdivisionsPerTile;
            float y = SurfaceY(grid, f);
            var verts = new Vector3[(nx + 1) * (ny + 1)];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float lx = (f.tx + i / (float)SubdivisionsPerTile) * grid.tileSize;
                float lz = (f.ty + j / (float)SubdivisionsPerTile) * grid.tileSize;
                int k = j * (nx + 1) + i;
                verts[k] = new Vector3(lx, y, lz);
                uvs[k] = new Vector2(i / (float)nx, j / (float)ny); // rect-relative: used for the bounded-rect edge fade
                normals[k] = Vector3.up;
            }

            var tris = new int[nx * ny * 6];
            int t = 0;
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = d;
                tris[t++] = a; tris[t++] = d; tris[t++] = b;
            }

            var mesh = new Mesh { name = "GroundPrototype_Fluid_" + f.type + " (in-memory)" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Light LavaLight(SampleElevationGrid grid, SampleElevationGrid.FluidRect f, Transform parent)
        {
            var go = new GameObject("LavaPointLight (real light, toggle L)");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3((f.tx + f.w * 0.5f) * grid.tileSize, SurfaceY(grid, f) + LavaLightHeight, (f.ty + f.h * 0.5f) * grid.tileSize);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LavaLightColor;
            light.intensity = LavaLightIntensity;
            light.range = LavaLightRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel; // per-light only; no quality/pixel-light-count change
            return light;
        }

        /// <summary>Matte grey cubes beside the lava: lit only by the real point light (and sun/ambient), never by emission.</summary>
        private static void MatteProbes(SampleElevationGrid grid, SampleElevationGrid.FluidRect f, Transform parent, GroundVisualFactory visuals)
        {
            if (visuals.ProbeMaterial == null) return;
            // West of the rect on the twisted field, spread along the trench.
            float gx = Mathf.Max(0.5f, f.tx - 1.5f);
            for (int i = 0; i < 3; i++)
            {
                float gy = f.ty + f.h * (i + 0.5f) / 3f;
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                probe.name = "MatteProbe_" + i;
                Object.Destroy(probe.GetComponent<Collider>());
                probe.transform.SetParent(parent, false);
                const float size = 0.45f;
                probe.transform.localScale = Vector3.one * size;
                float ground = (float)grid.MeshPlanarHeight(gx, gy);
                probe.transform.localPosition = new Vector3(gx * grid.tileSize, ground + size * 0.5f, gy * grid.tileSize);
                var mr = probe.GetComponent<MeshRenderer>();
                mr.sharedMaterial = visuals.ProbeMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
        }
    }
}
