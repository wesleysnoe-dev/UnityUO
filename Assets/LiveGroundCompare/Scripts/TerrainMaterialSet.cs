using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UOClient.IO;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// The natural terrain's materials on the GPU (<c>-terrainstyle natural</c>): the CC0 albedo (height in alpha) and
    /// normal maps from Resources/TerrainMaterials stacked into two Texture2DArrays, the land graphic → material table,
    /// and a per-material tint that pulls each photo texture toward the mean colour UO's own art has for that ground
    /// (sampled from the land tiles as chunks build), so the world keeps its palette. Shader globals:
    /// <c>_LGAlbedoArray</c>, <c>_LGNormalArray</c>, <c>_LGMatTint[8]</c>. Null (with a reason) if anything is missing.
    /// </summary>
    internal sealed class TerrainMaterialSet : IDisposable
    {
        public const string Folder = "TerrainMaterials";
        public const string ShaderResource = "LiveGroundCompareTerrainSplat";
        public const string ShaderName = "LiveGroundCompare/TerrainSplat";

        /// <summary>How far the tint moves a texture toward UO's colour (0 = photo as is, 1 = UO's mean exactly).</summary>
        public const float TintStrength = 0.7f;

        private const int MaxSamples = 4000;

        private static readonly int AlbedoId = Shader.PropertyToID("_LGAlbedoArray");
        private static readonly int NormalId = Shader.PropertyToID("_LGNormalArray");
        private static readonly int TintId = Shader.PropertyToID("_LGMatTint");

        private readonly Vector3[] _textureMean = new Vector3[TerrainMaterials.Count];
        private readonly double[,] _uoSum = new double[TerrainMaterials.Count, 3];
        private readonly int[] _uoCount = new int[TerrainMaterials.Count];
        private readonly Vector4[] _tint = new Vector4[TerrainMaterials.Count];
        private readonly object _lock = new object();
        private bool _tintDirty = true;

        private TerrainMaterialSet(byte[] table, Shader shader)
        {
            Table = table;
            Shader = shader;
        }

        public byte[] Table { get; }
        public Shader Shader { get; }
        public Texture2DArray Albedo { get; private set; }
        public Texture2DArray Normal { get; private set; }

        /// <summary>Whether the command line asks for the natural terrain (<c>-terrainstyle natural</c>).</summary>
        public static bool Requested()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], "-terrainstyle", StringComparison.OrdinalIgnoreCase))
                    return string.Equals(args[i + 1], "natural", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        public static TerrainMaterialSet TryCreate(UOData data, out string error)
        {
            error = null;
            try
            {
                if (!WaterTiles.TryLand(data, out var land, out error))
                    return null;
                Shader shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null)
                    shader = Shader.Find(ShaderName);
                if (shader == null || !shader.isSupported)
                {
                    error = $"shader '{ShaderName}' is not in this build or not supported";
                    return null;
                }
                var set = new TerrainMaterialSet(TerrainMaterials.Table(land), shader);
                if (!set.Load(out error))
                {
                    set.Dispose();
                    return null;
                }
                return set;
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        private bool Load(out string error)
        {
            error = null;
            var albedo = new Texture2D[TerrainMaterials.Count];
            var normal = new Texture2D[TerrainMaterials.Count];
            for (int i = 0; i < TerrainMaterials.Count; i++)
            {
                string name = $"{Folder}/m{i}_{TerrainMaterials.Names[i]}";
                albedo[i] = Resources.Load<Texture2D>(name + "_albedo");
                normal[i] = Resources.Load<Texture2D>(name + "_normal");
                if (albedo[i] == null || normal[i] == null)
                {
                    error = $"missing {name}_albedo/_normal in Resources";
                    return false;
                }
            }
            Albedo = Stack(albedo, "UO terrain albedo", out error);
            if (Albedo == null)
                return false;
            Normal = Stack(normal, "UO terrain normals", out error);
            if (Normal == null)
                return false;
            Shader.SetGlobalTexture(AlbedoId, Albedo);
            Shader.SetGlobalTexture(NormalId, Normal);

            // The textures' own mean colours (means.txt from the prep script), for the tint toward UO's colours.
            var means = Resources.Load<TextAsset>($"{Folder}/means");
            for (int i = 0; i < TerrainMaterials.Count; i++)
                _textureMean[i] = new Vector3(128, 128, 128);
            if (means != null)
                foreach (string line in means.text.Split('\n'))
                {
                    string[] p = line.Trim().Split(' ');
                    if (p.Length == 5 && int.TryParse(p[0], out int k) && k >= 0 && k < TerrainMaterials.Count)
                        _textureMean[k] = new Vector3(float.Parse(p[2], CultureInfo.InvariantCulture),
                            float.Parse(p[3], CultureInfo.InvariantCulture), float.Parse(p[4], CultureInfo.InvariantCulture));
                }
            PublishTint();
            return true;
        }

        private static Texture2DArray Stack(Texture2D[] layers, string name, out string error)
        {
            error = null;
            Texture2D first = layers[0];
            foreach (Texture2D t in layers)
                if (t.width != first.width || t.height != first.height || t.graphicsFormat != first.graphicsFormat || t.mipmapCount != first.mipmapCount)
                {
                    error = $"{t.name}: {t.width}x{t.height} {t.graphicsFormat} {t.mipmapCount} mips, not like {first.name} ({first.width}x{first.height} {first.graphicsFormat} {first.mipmapCount})";
                    return null;
                }
            var array = new Texture2DArray(first.width, first.height, layers.Length, first.graphicsFormat, TextureCreationFlags.MipChain, first.mipmapCount)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            for (int i = 0; i < layers.Length; i++)
                for (int mip = 0; mip < first.mipmapCount; mip++)
                    Graphics.CopyTexture(layers[i], 0, mip, array, i, mip);
            return array;
        }

        public int MaterialOf(int landGraphic) => TerrainMaterials.Lookup(Table, landGraphic);

        /// <summary>Worker threads: one UO land tile's mean colour (ARGB) for its material, until enough are known.</summary>
        public void AddUoSample(int material, uint argb)
        {
            if (material < 0 || material >= TerrainMaterials.Count)
                return;
            lock (_lock)
            {
                if (_uoCount[material] >= MaxSamples)
                    return;
                _uoSum[material, 0] += (argb >> 16) & 0xFF;
                _uoSum[material, 1] += (argb >> 8) & 0xFF;
                _uoSum[material, 2] += argb & 0xFF;
                _uoCount[material]++;
                _tintDirty = true;
            }
        }

        public bool NeedsSamples(int material)
        {
            lock (_lock)
                return material >= 0 && material < TerrainMaterials.Count && _uoCount[material] < MaxSamples;
        }

        /// <summary>Main thread: recomputes the tints when new samples came in and publishes them.</summary>
        public void PublishTint()
        {
            lock (_lock)
            {
                if (!_tintDirty)
                    return;
                _tintDirty = false;
                for (int k = 0; k < TerrainMaterials.Count; k++)
                {
                    Vector3 tex = _textureMean[k];
                    Vector4 t = Vector4.one;
                    if (_uoCount[k] > 0)
                        for (int c = 0; c < 3; c++)
                        {
                            float uo = (float)(_uoSum[k, c] / _uoCount[k]);
                            float ratio = Mathf.Clamp(uo / Mathf.Max(1f, tex[c]), 0.55f, 1.8f);
                            t[c] = Mathf.Lerp(1f, ratio, TintStrength);
                        }
                    _tint[k] = t;
                }
            }
            Shader.SetGlobalVectorArray(TintId, _tint);
        }

        public void Dispose()
        {
            if (Albedo != null)
                UnityEngine.Object.Destroy(Albedo);
            if (Normal != null)
                UnityEngine.Object.Destroy(Normal);
            Albedo = null;
            Normal = null;
        }
    }
}
