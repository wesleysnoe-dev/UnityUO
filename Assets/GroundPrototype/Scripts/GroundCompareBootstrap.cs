using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Entry point for Scenes/GroundCompare.unity (isolated prototype; not part of the client).
    /// Builds both sides from the same SampleElevationGrid, wires the shared light, the fixed iso
    /// camera and the overlay, then runs HeightParityCheck (offline model + Play readback).
    /// P2: adds shared sample textures + soft blend, water/lava rects and VisualCompareToggles, all
    /// added at runtime from Start (no scene YAML changes, no RuntimeInitializeOnLoad).
    /// </summary>
    public sealed class GroundCompareBootstrap : MonoBehaviour
    {
        public const string DataAssetPath = "GroundPrototype/Data/SampleElevation_32x32.json";

        public TextAsset elevationJson;
        public Material grass;
        public Material dirt;
        public Material sand;
        public Material stone;
        public Camera compareCamera;
        public Transform lightRoot;
        public Vector3 meshSideOffset = new Vector3(200f, 0f, 0f);
        public CompareMode initialMode = CompareMode.SideBySide;

        public SampleElevationGrid Grid { get; private set; }
        public Terrain TerrainSide { get; private set; }
        public GameObject MeshSide { get; private set; }

        /// <summary>Play-mode readback report (TerrainData vs mesh planar tris). Valid after Start.</summary>
        public HeightParityCheck.ParityReport LastParityReport { get; private set; }

        /// <summary>Offline-model report (same formulas as README numbers). Valid after Start.</summary>
        public HeightParityCheck.ParityReport OfflineParityReport { get; private set; }

        /// <summary>P2 runtime textures/materials/layers; disposed in OnDestroy.</summary>
        public GroundVisualFactory Visuals { get; private set; }

        private readonly List<Material> _fallbacks = new List<Material>();
        private GameObject _root;

        private void Start()
        {
            Grid = SampleElevationGrid.FromJson(elevationJson != null
                ? elevationJson.text
                : File.ReadAllText(Path.Combine(Application.dataPath, DataAssetPath)));

            var palette = new[]
            {
                grass != null ? grass : Fallback("Mat_Grass", new Color(0.33f, 0.52f, 0.22f)),
                dirt != null ? dirt : Fallback("Mat_Dirt", new Color(0.50f, 0.37f, 0.23f)),
                sand != null ? sand : Fallback("Mat_Sand", new Color(0.80f, 0.72f, 0.50f)),
                stone != null ? stone : Fallback("Mat_Stone", new Color(0.50f, 0.50f, 0.52f)),
            };

            var rigRoot = lightRoot != null ? lightRoot : new GameObject("Shared Light Rig").transform;
            var rig = rigRoot.GetComponent<SharedLightRig>();
            if (rig == null) rig = rigRoot.gameObject.AddComponent<SharedLightRig>();
            rig.Apply();

            // P2: shared sample textures / blend mask / fluid materials, owned and destroyed by Visuals.
            Visuals = new GroundVisualFactory(Grid, palette);
            foreach (var m in _fallbacks) Visuals.Track(m);
            _fallbacks.Clear();

            _root = new GameObject("GroundPrototype Sides (runtime)");
            var root = _root.transform;
            TerrainSide = TerrainSideBuilder.Build(Grid, palette, Vector3.zero, root, Visuals);
            MeshSide = MeshSideBuilder.Build(Grid, palette, meshSideOffset, root, Visuals.MeshBlendMaterial, Visuals);

            var terrainFluids = FluidSurfaceBuilder.Build(Grid, Vector3.zero, root, Visuals, "UnityTerrain");
            var meshFluids = FluidSurfaceBuilder.Build(Grid, meshSideOffset, root, Visuals, "CustomMesh");

            var toggles = GetComponent<VisualCompareToggles>();
            if (toggles == null) toggles = gameObject.AddComponent<VisualCompareToggles>();
            toggles.visuals = Visuals;
            toggles.lightRig = rig;
            toggles.fluidRoots.Add(terrainFluids.Root);
            toggles.fluidRoots.Add(meshFluids.Root);
            toggles.lavaLights.AddRange(terrainFluids.LavaLights);
            toggles.lavaLights.AddRange(meshFluids.LavaLights);

            float minY = Grid.MinZ() * Grid.zScale, maxY = Grid.MaxZ() * Grid.zScale;
            var size = new Vector3(Grid.TilesX * Grid.tileSize, maxY - minY, Grid.TilesY * Grid.tileSize);
            var localCenter = new Vector3(size.x * 0.5f, (minY + maxY) * 0.5f, size.z * 0.5f);
            var terrainBounds = new Bounds(localCenter, size);
            var meshBounds = new Bounds(localCenter + meshSideOffset, size);

            var cam = compareCamera != null ? compareCamera : Camera.main;
            if (cam == null) cam = new GameObject("Iso Compare Camera").AddComponent<Camera>();
            var iso = cam.GetComponent<IsoCompareCamera>();
            if (iso == null) iso = cam.gameObject.AddComponent<IsoCompareCamera>();
            iso.Configure(terrainBounds, meshBounds, initialMode);

            OfflineParityReport = HeightParityCheck.ComputeOffline(Grid);
            LastParityReport = HeightParityCheck.Measure(Grid, TerrainSide);
            Debug.Log("[GroundPrototype] " + OfflineParityReport);
            Debug.Log("[GroundPrototype] " + LastParityReport);

            var labels = GetComponent<CompareOverlayLabels>();
            if (labels == null) labels = gameObject.AddComponent<CompareOverlayLabels>();
            labels.compareCamera = iso;
            labels.toggles = toggles;
            labels.conventionLine =
                $"Sample {Grid.TilesX}x{Grid.TilesY} tiles ({Grid.cornersX}x{Grid.cornersY} corners) · 1 tile = {Grid.tileSize:0.##} u · 1 Z = {Grid.zScale:0.###} u · Z range {Grid.MinZ()}..{Grid.MaxZ()} · same light/palette/textures both sides";
            labels.parityLine =
                $"Parity (play readback, world u): corner max {LastParityReport.cornerMaxAbs:0.#####} · interior max {LastParityReport.interiorMaxAbs:0.#####} (SampleHeight {LastParityReport.interiorMaxAbsSampleHeight:0.#####})" +
                $" · offline model predicted {OfflineParityReport.interiorMaxAbs:0.###} (prediction, not observed)";
            labels.noteLines = BuildNoteLines();
            Debug.Log("[GroundPrototype] P2 visuals: " + Visuals.ShaderStatus + " · fluids: " + string.Join("; ", FluidSummaries()));
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
            Visuals?.Dispose();
            Visuals = null;
            foreach (var m in _fallbacks) if (m != null) Destroy(m);
            _fallbacks.Clear();
        }

        private string[] BuildNoteLines()
        {
            var lines = new List<string>
            {
                $"Blend (shared): same {GroundVisualFactory.ControlResolution}² corner-aligned mask, same 4 sample textures, UV = (world - side origin) / {GroundVisualFactory.TextureWorldSize:0.#} u, metallic 0, smoothness {GroundVisualFactory.SharedSmoothness:0.##}",
                "Blend (NOT shared): Terrain = Nature/Terrain/Standard + Unity terrain normals/LOD; Mesh = MeshGroundBlend + corner central-diff normals -> slope/cliff shading differs",
                "Lava: emission only brightens the lava itself (no bloom, lights nothing); orange on ground/grey probes = real point light (L)",
                "Water: depth fade + shore foam from camera depth (ortho-corrected), shader ripples; no reflection/refraction/SSR/simulation; ignores point lights",
                "Fluids (bounded rects, heights unchanged): " + string.Join("; ", FluidSummaries()),
            };
            if (Visuals.MeshBlendMaterial == null || Visuals.WaterMaterial == null || Visuals.LavaMaterial == null)
                lines.Add(Visuals.ShaderStatus + " -> mesh side uses P1 per-tile materials / fluids skipped");
            return lines.ToArray();
        }

        private IEnumerable<string> FluidSummaries()
        {
            foreach (var f in Grid.fluids)
                yield return $"{f} submerged corners {Grid.SubmergedCorners(f)}/{(f.w + 1) * (f.h + 1)}";
        }

        private Material Fallback(string name, Color c)
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name + " (runtime fallback)", color = c };
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            _fallbacks.Add(m);
            return m;
        }
    }
}
