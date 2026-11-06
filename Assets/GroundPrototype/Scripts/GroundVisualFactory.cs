using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// P2 runtime visuals shared by both sides (SAMPLE TEXTURES, NOT MAP ART). Everything is created in
    /// memory and owned here; Dispose() destroys every tracked Texture2D / Material / TerrainLayer /
    /// TerrainData / Mesh. Nothing is written to disk.
    ///
    /// Shared between Terrain and mesh:
    ///  - four procedural stylized albedo textures (grass, dirt, sand, stone) tinted from the Mat_* palette;
    ///  - one soft blend mask (Alphamap / Control), sampled corner-aligned exactly like Unity Terrain's
    ///    splat control (texel 0 on the map edge, texel res-1 on the far edge);
    ///  - texture UV = (worldXZ - sideOrigin.xz) / TextureWorldSize, i.e. local map coordinates, so the
    ///    Terrain side at x=0 and the mesh side at x=+200 repeat textures identically;
    ///  - metallic 0 and one SharedSmoothness value.
    /// Not shared: the shader itself (Nature/Terrain/Standard vs MeshGroundBlend) and the normals.
    /// </summary>
    public sealed class GroundVisualFactory : IDisposable
    {
        public const int LayerCount = 4;
        public const int TextureSize = 128;
        /// <summary>Corner-aligned blend mask resolution (power of two, ~8 texels per tile on the 32-tile sample).</summary>
        public const int ControlResolution = 256;
        /// <summary>World units covered by one repeat of an albedo texture.</summary>
        public const float TextureWorldSize = 4f;
        public const float SharedSmoothness = 0.08f;
        /// <summary>Half-width (tiles) of the box filter that softens per-tile materials into blend weights.</summary>
        public const float BlendRadiusTiles = 0.35f;
        /// <summary>Deterministic jitter (tiles) applied to the blend sample position so borders are not straight.</summary>
        public const float BlendJitterTiles = 0.18f;

        public const string MeshBlendShaderName = "UOClient/GroundPrototype/MeshGroundBlend";
        public const string WaterShaderName = "UOClient/GroundPrototype/StylizedWater";
        public const string LavaShaderName = "UOClient/GroundPrototype/StylizedLava";

        private static readonly int ControlId = Shader.PropertyToID("_Control");
        private static readonly int MapOriginId = Shader.PropertyToID("_MapOrigin");
        private static readonly int MapSizeId = Shader.PropertyToID("_MapSize");
        private static readonly int TexWorldSizeId = Shader.PropertyToID("_TexWorldSize");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int[] SplatIds =
        {
            Shader.PropertyToID("_Splat0"), Shader.PropertyToID("_Splat1"),
            Shader.PropertyToID("_Splat2"), Shader.PropertyToID("_Splat3"),
        };

        private readonly List<Object> _owned = new List<Object>();
        private readonly SampleElevationGrid _grid;
        private readonly List<TerrainLayer[]> _terrainLayerSets = new List<TerrainLayer[]>();
        private readonly List<TerrainData> _terrains = new List<TerrainData>();

        public Texture2D[] Albedo { get; }
        public Texture2D[] Solid { get; }
        public Texture2D Control { get; }
        /// <summary>Blend weights [y, x, layer] at ControlResolution, corner-aligned (same data as Control).</summary>
        public float[,,] Alphamap { get; }

        /// <summary>Null when the custom shader failed to load; the mesh side then falls back to P1 per-tile materials.</summary>
        public Material MeshBlendMaterial { get; }
        public Material WaterMaterial { get; }
        public Material LavaMaterial { get; }
        public Material ProbeMaterial { get; }
        public bool TexturesOn { get; private set; } = true;
        public string ShaderStatus { get; }

        public GroundVisualFactory(SampleElevationGrid grid, Material[] palette)
        {
            _grid = grid;
            Albedo = new Texture2D[LayerCount];
            Solid = new Texture2D[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                var c = palette[Mathf.Min(i, palette.Length - 1)].color;
                Albedo[i] = Track(StylizedTexture(i, c));
                Solid[i] = Track(SolidTexture(c, "Solid_" + i));
            }

            Alphamap = BuildAlphamap(grid);
            Control = Track(ControlTexture(Alphamap));

            var missing = new List<string>();
            MeshBlendMaterial = CreateMaterial(MeshBlendShaderName, "GroundPrototype_MeshBlend (runtime)", missing);
            WaterMaterial = CreateMaterial(WaterShaderName, "GroundPrototype_Water (runtime)", missing);
            LavaMaterial = CreateMaterial(LavaShaderName, "GroundPrototype_Lava (runtime)", missing);

            if (MeshBlendMaterial != null)
            {
                MeshBlendMaterial.SetTexture(ControlId, Control);
                MeshBlendMaterial.SetVector(MapSizeId, new Vector4(grid.TilesX * grid.tileSize, grid.TilesY * grid.tileSize, 0f, 0f));
                MeshBlendMaterial.SetFloat(TexWorldSizeId, TextureWorldSize);
                MeshBlendMaterial.SetFloat(SmoothnessId, SharedSmoothness);
                for (int i = 0; i < LayerCount; i++) MeshBlendMaterial.SetTexture(SplatIds[i], Albedo[i]);
            }

            var standard = Shader.Find("Standard");
            if (standard != null)
            {
                ProbeMaterial = Track(new Material(standard) { name = "GroundPrototype_MatteProbe (runtime)", color = new Color(0.62f, 0.62f, 0.62f) });
                ProbeMaterial.SetFloat("_Glossiness", 0f);
                ProbeMaterial.SetFloat("_Metallic", 0f);
            }

            ShaderStatus = missing.Count == 0
                ? "shaders OK (MeshGroundBlend, StylizedWater, StylizedLava)"
                : "MISSING SHADERS: " + string.Join(", ", missing);
        }

        public T Track<T>(T obj) where T : Object
        {
            if (obj != null) _owned.Add(obj);
            return obj;
        }

        /// <summary>Terrain layers using the shared albedo, tile size = TextureWorldSize, offset 0 (terrain origin = side origin).</summary>
        public TerrainLayer[] CreateTerrainLayers(string[] names)
        {
            var layers = new TerrainLayer[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                layers[i] = Track(new TerrainLayer
                {
                    name = "Layer_" + (names != null && i < names.Length ? names[i] : i.ToString()),
                    diffuseTexture = TexturesOn ? Albedo[i] : Solid[i],
                    tileSize = new Vector2(TextureWorldSize, TextureWorldSize),
                    tileOffset = Vector2.zero,
                    metallic = 0f,
                    smoothness = SharedSmoothness,
                });
                ForceConstantSmoothness(layers[i]);
            }
            _terrainLayerSets.Add(layers);
            return layers;
        }

        /// <summary>Registers a TerrainData so SetTextures can refresh its layers + alphamaps.</summary>
        public void RegisterTerrain(TerrainData data)
        {
            _terrains.Add(Track(data));
        }

        /// <summary>Textures on: procedural albedo. Off: flat palette colours (same soft blend mask, P1-like look).</summary>
        public void SetTextures(bool on)
        {
            TexturesOn = on;
            var set = on ? Albedo : Solid;
            foreach (var layers in _terrainLayerSets)
                for (int i = 0; i < layers.Length; i++)
                    layers[i].diffuseTexture = set[i];
            foreach (var data in _terrains)
            {
                if (data == null) continue;
                data.terrainLayers = data.terrainLayers; // re-push layer textures to the terrain material
                data.SetAlphamaps(0, 0, Alphamap);       // and keep the shared mask exactly as built
            }
            if (MeshBlendMaterial != null)
                for (int i = 0; i < LayerCount; i++) MeshBlendMaterial.SetTexture(SplatIds[i], set[i]);
        }

        /// <summary>Per-renderer side origin so fluid/ground patterns are computed in local map coordinates.</summary>
        public static void SetMapOrigin(Renderer r, Vector3 origin)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetVector(MapOriginId, new Vector4(origin.x, origin.y, origin.z, 0f));
            r.SetPropertyBlock(mpb);
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                var o = _owned[i];
                if (o == null) continue;
                if (Application.isPlaying) Object.Destroy(o);
                else Object.DestroyImmediate(o);
            }
            _owned.Clear();
            _terrainLayerSets.Clear();
            _terrains.Clear();
        }

        // ---------------------------------------------------------------- blend mask

        /// <summary>
        /// Soft blend weights at fractional grid position (gx, gy): a box filter (half-width BlendRadiusTiles)
        /// over the per-tile material map, sampled at a jittered position. Both sides use this one function
        /// (via Alphamap/Control), so mask differences between them come only from how each shader samples it.
        /// </summary>
        public static Vector4 BlendWeights(SampleElevationGrid grid, float gx, float gy)
        {
            gx += BlendJitterTiles * (ValueNoise(gx * 1.7f, gy * 1.7f, 11) * 2f - 1f);
            gy += BlendJitterTiles * (ValueNoise(gx * 1.7f, gy * 1.7f, 23) * 2f - 1f);

            float r = BlendRadiusTiles;
            var w = Vector4.zero;
            int x0 = Mathf.FloorToInt(gx - r), x1 = Mathf.FloorToInt(gx + r);
            int y0 = Mathf.FloorToInt(gy - r), y1 = Mathf.FloorToInt(gy + r);
            for (int ty = y0; ty <= y1; ty++)
            {
                float oy = Mathf.Max(0f, Mathf.Min(gy + r, ty + 1) - Mathf.Max(gy - r, ty));
                if (oy <= 0f) continue;
                for (int tx = x0; tx <= x1; tx++)
                {
                    float ox = Mathf.Max(0f, Mathf.Min(gx + r, tx + 1) - Mathf.Max(gx - r, tx));
                    if (ox <= 0f) continue;
                    int m = Mathf.Clamp(grid.TileMaterial(Mathf.Clamp(tx, 0, grid.TilesX - 1), Mathf.Clamp(ty, 0, grid.TilesY - 1)), 0, LayerCount - 1);
                    w[m] += ox * oy;
                }
            }

            // Mild contrast so borders read as soft edges rather than long linear ramps.
            for (int i = 0; i < 4; i++) w[i] = w[i] * w[i];
            float sum = w.x + w.y + w.z + w.w;
            return sum > 0f ? w / sum : new Vector4(1f, 0f, 0f, 0f);
        }

        private static float[,,] BuildAlphamap(SampleElevationGrid grid)
        {
            int res = ControlResolution;
            var a = new float[res, res, LayerCount];
            for (int ay = 0; ay < res; ay++)
            for (int ax = 0; ax < res; ax++)
            {
                // Corner-aligned: texel 0 sits on the map's near edge, texel res-1 on the far edge (Unity Terrain convention).
                float gx = ax * grid.TilesX / (float)(res - 1);
                float gy = ay * grid.TilesY / (float)(res - 1);
                var w = BlendWeights(grid, gx, gy);
                for (int i = 0; i < LayerCount; i++) a[ay, ax, i] = w[i];
            }
            return a;
        }

        private static Texture2D ControlTexture(float[,,] a)
        {
            int res = a.GetLength(0);
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
            {
                name = "GroundPrototype_BlendControl (runtime)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[res * res];
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
                px[y * res + x] = new Color(a[y, x, 0], a[y, x, 1], a[y, x, 2], a[y, x, 3]);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------------------------------------------------------- procedural albedo

        private static Texture2D StylizedTexture(int layer, Color tint)
        {
            int n = TextureSize;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                Color c;
                switch (layer)
                {
                    case 0: c = Grass(u, v, x, y, tint); break;
                    case 1: c = Dirt(u, v, x, y, tint); break;
                    case 2: c = Sand(u, v, tint); break;
                    default: c = Stone(u, v, tint); break;
                }
                c.a = 1f; // smoothness comes from SharedSmoothness on both sides, not from albedo alpha
                px[y * n + x] = c;
            }

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, false)
            {
                name = "GroundPrototype_SampleAlbedo_" + layer + " (runtime)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Color Grass(float u, float v, int x, int y, Color tint)
        {
            float f = Fbm(u, v, 4, 4, 1);
            float clump = Fbm(u, v, 8, 3, 7);
            var c = Color.Lerp(tint * 0.82f, tint * 1.15f, f);
            c = Color.Lerp(c, new Color(tint.r * 1.25f, tint.g * 1.12f, tint.b * 0.7f), Mathf.SmoothStep(0.55f, 0.8f, clump) * 0.5f);
            float blade = Hash(x, y, 3);
            if (blade > 0.9f) c *= 1.18f;
            else if (blade < 0.08f) c *= 0.8f;
            return c;
        }

        private static Color Dirt(float u, float v, int x, int y, Color tint)
        {
            float f = Fbm(u, v, 4, 4, 2);
            var c = Color.Lerp(tint * 0.8f, tint * 1.12f, f);
            // Pebbles: one jittered disc per lattice cell (period 16 -> seamless).
            const int cells = 16;
            float cu = u * cells, cv = v * cells;
            int ix = Mathf.FloorToInt(cu), iy = Mathf.FloorToInt(cv);
            float px = Hash(ix % cells, iy % cells, 5), py = Hash(ix % cells, iy % cells, 9);
            float d = Mathf.Sqrt((cu - ix - px) * (cu - ix - px) + (cv - iy - py) * (cv - iy - py));
            if (Hash(ix % cells, iy % cells, 13) > 0.45f && d < 0.22f) c = Color.Lerp(c, tint * (d < 0.12f ? 1.3f : 0.7f), 0.7f);
            if (Hash(x, y, 4) > 0.94f) c *= 0.85f;
            return c;
        }

        private static Color Sand(float u, float v, Color tint)
        {
            float f = Fbm(u, v, 4, 3, 3);
            float warp = Fbm(u, v, 2, 2, 17);
            float ripple = Mathf.Sin((v + warp * 0.25f) * Mathf.PI * 2f * 7f) * 0.5f + 0.5f; // integer frequency -> seamless
            var c = Color.Lerp(tint * 0.9f, tint * 1.08f, f);
            return c * Mathf.Lerp(0.94f, 1.04f, ripple);
        }

        private static Color Stone(float u, float v, Color tint)
        {
            float f = Fbm(u, v, 4, 4, 4);
            float crack = Mathf.Abs(Fbm(u, v, 8, 3, 29) - 0.5f);
            var c = Color.Lerp(tint * 0.72f, tint * 1.18f, f);
            if (crack < 0.025f) c *= 0.62f;
            else if (crack < 0.05f) c *= 0.85f;
            return c;
        }

        private static Texture2D SolidTexture(Color c, string name)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, false)
            {
                name = "GroundPrototype_" + name + " (runtime)",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(c.r, c.g, c.b, 1f);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------------------------------------------------------- noise helpers (deterministic, tileable)

        /// <summary>Tileable fbm on [0,1)^2: base lattice period doubles per octave so every octave wraps.</summary>
        private static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * PeriodicNoise(u * period, v * period, period, seed + o * 31);
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        private static float PeriodicNoise(float x, float y, int period, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x0 = Mod(ix, period), x1 = Mod(ix + 1, period), y0 = Mod(iy, period), y1 = Mod(iy + 1, period);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static float ValueNoise(float x, float y, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(ix, iy, seed), Hash(ix + 1, iy, seed), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1, seed), Hash(ix + 1, iy + 1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // ---------------------------------------------------------------- misc

        private Material CreateMaterial(string shaderName, string name, List<string> missing)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null || !shader.isSupported)
            {
                missing.Add(shaderName);
                Debug.LogError("[GroundPrototype] shader missing or unsupported: " + shaderName);
                return null;
            }
            return Track(new Material(shader) { name = name });
        }

        /// <summary>Unity 6 TerrainLayer may take smoothness from albedo alpha; force the constant so both sides use SharedSmoothness.</summary>
        private static void ForceConstantSmoothness(TerrainLayer layer)
        {
            var prop = typeof(TerrainLayer).GetProperty("smoothnessSource", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite || !prop.PropertyType.IsEnum) return;
            if (!Enum.IsDefined(prop.PropertyType, "Constant")) return;
            prop.SetValue(layer, Enum.Parse(prop.PropertyType, "Constant"));
        }
    }
}
