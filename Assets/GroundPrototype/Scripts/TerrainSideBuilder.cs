using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Builds the Unity Terrain side entirely in memory (no TerrainData / TerrainLayer assets are saved).
    /// Heightmap resolution = corner count (33), so every heightmap sample sits exactly on a grid corner.
    /// P1 splat (visuals == null): one solid-colour TerrainLayer per palette material, hard per-tile alphamap.
    /// P2 splat (visuals != null): shared procedural layers + the shared soft mask from GroundVisualFactory,
    /// rendered by the built-in Nature/Terrain/Standard shader (not the mesh side's shader).
    /// Built-in render pipeline.
    /// </summary>
    public static class TerrainSideBuilder
    {
        public const int AlphamapResolution = 128; // P1: 4 texels per tile; Unity blends between texels at tile borders.

        public static Terrain Build(SampleElevationGrid grid, Material[] palette, Vector3 origin, Transform parent,
            GroundVisualFactory visuals = null)
        {
            int minZ = grid.MinZ(), maxZ = grid.MaxZ();
            float baseY = minZ * grid.zScale;
            float rangeY = Mathf.Max(1, maxZ - minZ) * grid.zScale;

            var data = new TerrainData { name = "GroundPrototype_TerrainData (in-memory)" };
            // Resolution must be set before size (setting resolution rescales size).
            data.heightmapResolution = grid.cornersX; // 33 = 2^5 + 1
            data.size = new Vector3(grid.TilesX * grid.tileSize, rangeY, grid.TilesY * grid.tileSize);

            var heights = new float[grid.cornersY, grid.cornersX]; // [row = y, col = x]
            for (int cy = 0; cy < grid.cornersY; cy++)
            for (int cx = 0; cx < grid.cornersX; cx++)
                heights[cy, cx] = (float)((grid.CornerWorldY(cx, cy) - baseY) / rangeY);
            data.SetHeights(0, 0, heights);

            if (visuals != null)
            {
                visuals.RegisterTerrain(data);
                data.terrainLayers = visuals.CreateTerrainLayers(grid.materialNames);
                data.alphamapResolution = GroundVisualFactory.ControlResolution;
                data.SetAlphamaps(0, 0, visuals.Alphamap);
                return CreateTerrain(data, origin, baseY, parent, visuals);
            }

            var layers = new TerrainLayer[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                layers[i] = new TerrainLayer
                {
                    name = "Layer_" + palette[i].name,
                    diffuseTexture = SolidTexture(palette[i].color, palette[i].name),
                    tileSize = new Vector2(grid.tileSize, grid.tileSize),
                    metallic = 0f,
                    smoothness = 0f,
                };
            }
            data.terrainLayers = layers;

            data.alphamapResolution = AlphamapResolution;
            int res = data.alphamapResolution;
            var alpha = new float[res, res, layers.Length];
            for (int ay = 0; ay < res; ay++)
            for (int ax = 0; ax < res; ax++)
            {
                int tx = Mathf.Min(grid.TilesX - 1, ax * grid.TilesX / res);
                int ty = Mathf.Min(grid.TilesY - 1, ay * grid.TilesY / res);
                int m = Mathf.Clamp(grid.TileMaterial(tx, ty), 0, layers.Length - 1);
                alpha[ay, ax, m] = 1f;
            }
            data.SetAlphamaps(0, 0, alpha);
            return CreateTerrain(data, origin, baseY, parent, null);
        }

        private static Terrain CreateTerrain(TerrainData data, Vector3 origin, float baseY, Transform parent, GroundVisualFactory visuals)
        {
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Side_UnityTerrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin + new Vector3(0f, baseY, 0f);

            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 1f;   // keep near-full LOD so the comparison is not LOD-dominated
            terrain.basemapDistance = 1000f;   // avoid the low-res basemap at our camera distance
            terrain.drawInstanced = false;
            var shader = Shader.Find("Nature/Terrain/Standard");
            if (shader != null)
            {
                var mat = new Material(shader) { name = "GroundPrototype_TerrainMat (runtime)" };
                terrain.materialTemplate = visuals != null ? visuals.Track(mat) : mat;
            }
            terrain.Flush();
            return terrain;
        }

        private static Texture2D SolidTexture(Color c, string name)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false)
            {
                name = "Solid_" + name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(c.r, c.g, c.b, 1f);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
