using System.IO;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Entry point for Scenes/GroundCompare.unity (isolated P1 prototype; not part of the client).
    /// Builds both sides from the same SampleElevationGrid, wires the shared light, the fixed iso
    /// camera and the overlay, then runs HeightParityCheck (offline model + Play readback).
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

            var root = new GameObject("GroundPrototype Sides (runtime)").transform;
            TerrainSide = TerrainSideBuilder.Build(Grid, palette, Vector3.zero, root);
            MeshSide = MeshSideBuilder.Build(Grid, palette, meshSideOffset, root);

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
            labels.conventionLine =
                $"Sample {Grid.TilesX}x{Grid.TilesY} tiles ({Grid.cornersX}x{Grid.cornersY} corners) · 1 tile = {Grid.tileSize:0.##} u · 1 Z = {Grid.zScale:0.###} u · Z range {Grid.MinZ()}..{Grid.MaxZ()} · same light/palette both sides";
            labels.parityLine =
                $"Parity (play readback, world u): corner max {LastParityReport.cornerMaxAbs:0.#####} · interior max {LastParityReport.interiorMaxAbs:0.#####} (SampleHeight {LastParityReport.interiorMaxAbsSampleHeight:0.#####})";
        }

        private static Material Fallback(string name, Color c)
        {
            var shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name + " (runtime fallback)", color = c };
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_Metallic", 0f);
            return m;
        }
    }
}
