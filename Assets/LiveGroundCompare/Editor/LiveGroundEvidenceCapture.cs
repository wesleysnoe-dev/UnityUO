using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UOClient.LiveGroundCompare.Editor
{
    /// <summary>
    /// LiveGroundCompare (TEST only) Phase C evidence: renders one real coastal chunk (Evidence~/realWaterChunk.json,
    /// written by DotNet LiveGroundEvidenceFixtureTests from map 0 + tiledata) with the shipped LiveGroundCompare
    /// shaders and UOClient/WorldSprite, off-screen, and writes PNG stills plus evidence.md with exact shader times and
    /// measured pixel deltas. Explicit invocation only (menu or -executeMethod); nothing here runs on load, enters Play,
    /// touches the open scene (objects are HideAndDontSave on layers 30/31 and destroyed) or the streaming path.
    ///
    /// Water time: the shipped water shader reads _Time.y exactly once. For a controlled pair the capture compiles an
    /// in-memory copy of that source with _Time.y replaced by a uniform (_LGEvidenceTime) and LiveGroundLight.cginc
    /// inlined; everything else is the shipped text. A second pair renders the shipped shader itself at two measured
    /// wall-clock moments (whatever _Time Unity supplies in the Editor; the report says if the pixels changed).
    /// </summary>
    public static partial class LiveGroundEvidenceCapture
    {
        public const float T0 = 10f, T1 = 10.5f;
        private const int Width = 768, Height = 480, GroundLayer = 31, SpriteLayer = 30;
        private const float OrthoSize = 10f;
        private const string WaterShader = "LiveGroundCompare/Water", TerrainShader = "LiveGroundCompare/TerrainLit",
            DepthShader = "LiveGroundCompare/DepthKey", SpriteShader = "UOClient/WorldSprite";
        private static readonly Vector3 EyeDir = new Vector3(0.5f, 0.70710678f, -0.5f); // TerrainWaterSupport.EyeDir

        /// <summary>The DotNet fixture (field names as LiveGroundEvidenceFixtureTests.Fixture).</summary>
        [Serializable]
        public sealed class Fixture
        {
            public string source;
            public int map, x0, y0, tiles, waterTiles, landTiles, occluderColumns, shadowTexels;
            public float heightUnitPerZ;
            public float[] sun;
            public int[] waterGraphics, graphics, tileArgb, cornerZ, sunMask;
            public float[] waterX, waterY, waterZ, waterShore;
            public int[] waterTint, waterIndices;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        private static string EvidenceDir => Path.Combine(ProjectRoot, "Assets", "LiveGroundCompare", "Evidence~");

        [MenuItem("UOClient/LiveGroundCompare/Capture evidence stills (TEST)")]
        public static void CaptureMenu()
        {
            string report = Capture(Path.Combine(EvidenceDir, "realWaterChunk.json"), Path.Combine(EvidenceDir, "stills"));
            Debug.Log("UOClient LiveGround evidence: wrote " + report);
        }

        /// <summary>-executeMethod entry: optional -lgFixture &lt;json&gt; and -lgOut &lt;dir&gt;; exits the Editor (1 on failure).</summary>
        public static void CaptureBatch()
        {
            int code = 0;
            try
            {
                // Scratch-project runs only (run-evidence-scratch.ps1): match the main project's Gamma colour space.
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lgGamma") >= 0 && PlayerSettings.colorSpace != ColorSpace.Gamma)
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                string report = Capture(Arg("-lgFixture") ?? Path.Combine(EvidenceDir, "realWaterChunk.json"),
                                        Arg("-lgOut") ?? Path.Combine(EvidenceDir, "stills"));
                Debug.Log("UOClient LiveGround evidence: wrote " + report);
            }
            catch (Exception e)
            {
                Debug.LogError("UOClient LiveGround evidence FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name)
                    return args[i + 1];
            return null;
        }

        public static string Capture(string fixturePath, string outDir)
        {
            Fixture f = JsonUtility.FromJson<Fixture>(File.ReadAllText(fixturePath));
            Directory.CreateDirectory(outDir);
            var made = new List<Object>();
            var saved = new SavedGlobals();
            var md = new StringBuilder();
            RenderTexture rt = null;
            try
            {
                Shader waterShipped = Need(WaterShader), terrain = Need(TerrainShader), depth = Need(DepthShader), sprite = Need(SpriteShader);
                Shader waterTimed = TimedWater(waterShipped);
                made.Add(waterTimed);

                Mesh ground = Track(made, GroundMesh(f));
                Mesh sea = Track(made, WaterMesh(f));
                Texture2D art = Track(made, ArtTexture(f));
                Texture2D sunMask = Track(made, SunTexture(f));

                Material terrainMat = Track(made, new Material(terrain));
                terrainMat.SetTexture("_MainTex", art);
                terrainMat.SetTexture("_LGShadowTex", sunMask);
                Material timedMat = Track(made, new Material(waterTimed));
                Material shippedMat = Track(made, new Material(waterShipped));
                foreach (Material m in new[] { timedMat, shippedMat })
                    m.SetVector("_EyeDir", new Vector4(EyeDir.x, EyeDir.y, EyeDir.z, 0f));
                Material depthMat = Track(made, new Material(depth));
                Material spriteMat = Track(made, new Material(sprite));

                GameObject groundGo = Track(made, Node("Ground", ground, terrainMat, GroundLayer));
                GameObject seaGo = Track(made, Node("Water", sea, timedMat, GroundLayer));
                GameObject proxyGo = Track(made, Node("DepthProxy", ground, depthMat, SpriteLayer));

                Vector3 center = ground.bounds.center;
                var camGo = Track(made, new GameObject("LiveGround evidence camera") { hideFlags = HideFlags.HideAndDontSave });
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = OrthoSize;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 200f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
                camGo.transform.position = center + EyeDir * 60f;
                camGo.transform.rotation = Quaternion.LookRotation(-EyeDir, Vector3.up);

                // Sorting markers: WorldSprite quads standing on the highest land tile (red, 0.8 below the ground) and
                // on another land tile (green, wholly above it), facing the camera.
                (Vector3 hill, Vector3 flat) = MarkerSites(f);
                GameObject red = Track(made, Node("Marker red", Track(made, Billboard(hill, camGo.transform.right, -0.8f, new Color32(255, 0, 0, 255))), spriteMat, SpriteLayer));
                GameObject green = Track(made, Node("Marker green", Track(made, Billboard(flat, camGo.transform.right, 0.05f, new Color32(0, 255, 0, 255))), spriteMat, SpriteLayer));

                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;

                float cx = f.x0 + f.tiles / 2f, cy = f.y0 + f.tiles / 2f;
                Vector4 day = new Vector4(1f, 1f, 1f, 1f), none = Vector4.zero, dayPos = new Vector4(cx, cy, 1f, 0f);
                SetGlobals(f, day, none, dayPos);

                var shots = new List<(string File, string What, Texture2D Tex)>();
                Texture2D Shot(string file, string what, bool sprites)
                {
                    Texture2D t = Track(made, RenderPass(cam, sprites));
                    File.WriteAllBytes(Path.Combine(outDir, file), t.EncodeToPNG());
                    shots.Add((file, what, t));
                    return t;
                }

                // ---- 1. Water motion, controlled shader time ----
                proxyGo.SetActive(false);
                red.SetActive(false);
                green.SetActive(false);
                timedMat.SetFloat("_LGEvidenceTime", T0);
                Texture2D t0 = Shot("water_t0.png", $"water, _LGEvidenceTime = {T0:0.000} s (day, sun mask on)", false);
                Texture2D t0Again = Track(made, RenderPass(cam, false));
                timedMat.SetFloat("_LGEvidenceTime", T1);
                Texture2D t1 = Shot("water_t1.png", $"water, _LGEvidenceTime = {T1:0.000} s (day, sun mask on)", false);
                Diff motion = Compare(t0, t1), repeat = Compare(t0, t0Again);
                File.WriteAllBytes(Path.Combine(outDir, "water_t0_t1_diff_x8.png"), Track(made, DiffImage(t0, t1, 8)).EncodeToPNG());
                Diff waterMask = Compare(t0, Track(made, WithoutWater(cam, seaGo)));

                // ---- 2. Shipped shader, Unity's own _Time, two measured wall-clock moments ----
                seaGo.GetComponent<MeshRenderer>().sharedMaterial = shippedMat;
                double wa = EditorApplication.timeSinceStartup;
                float ta = Time.time, ra = Time.realtimeSinceStartup;
                Texture2D a = Shot("realtime_a.png", $"shipped water shader, wall t = {wa:0.000} s (EditorApplication.timeSinceStartup)", false);
                Thread.Sleep(1000);
                double wb = EditorApplication.timeSinceStartup;
                float tb = Time.time, rb = Time.realtimeSinceStartup;
                Texture2D b = Shot("realtime_b.png", $"shipped water shader, wall t = {wb:0.000} s (EditorApplication.timeSinceStartup)", false);
                Diff realtime = Compare(a, b);
                seaGo.GetComponent<MeshRenderer>().sharedMaterial = timedMat;
                timedMat.SetFloat("_LGEvidenceTime", T0);

                // ---- 3. Lighting: sun mask off, night ambient + player light ----
                terrainMat.SetTexture("_LGShadowTex", Texture2D.whiteTexture);
                Texture2D noSun = Shot("light_day_nosun.png", $"day, sun mask replaced by white (no terrain shadow), t = {T0:0.000}", false);
                terrainMat.SetTexture("_LGShadowTex", sunMask);
                Diff shadow = Compare(t0, noSun);
                Vector4 night = new Vector4(0.32f, 0.34f, 0.48f, 1f), lamp = new Vector4(0.9f, 0.75f, 0.5f, 1f), lampPos = new Vector4(cx, cy, 7f, 0f);
                SetGlobals(f, night, lamp, lampPos);
                Texture2D nightTex = Shot("light_night_playerlight.png", $"fixture night ambient {V(night)} + player light {V(lamp)} r=7 at tile ({cx}, {cy}), t = {T0:0.000}", false);
                Diff nightDiff = Compare(t0, nightTex);
                SetGlobals(f, day, none, dayPos);

                // ---- 4. Sorting: terrain by its own pass, then sprites with / without the depth-only proxy ----
                red.SetActive(true);
                green.SetActive(true);
                Texture2D noProxy = Shot("sort_noproxy.png", "terrain pass + WorldSprite markers, no depth proxy", true);
                proxyGo.SetActive(true);
                Texture2D withProxy = Shot("sort_proxy.png", "terrain pass + DepthKey proxy (ColorMask 0) + WorldSprite markers", true);
                int redNo = Count(noProxy, true), redYes = Count(withProxy, true), greenNo = Count(noProxy, false), greenYes = Count(withProxy, false);

                // ---- report ----
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
                md.AppendLine("# LiveGroundCompare Phase C evidence (isolated fixture)");
                md.AppendLine();
                md.AppendLine($"- Captured: {now}; Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}; color space {QualitySettings.activeColorSpace}; batch mode {Application.isBatchMode}");
                md.AppendLine($"- Project: `{ProjectRoot}`");
                md.AppendLine($"- Fixture: `{fixturePath}`: map {f.map} chunk ({f.x0}, {f.y0}) {f.tiles}x{f.tiles}: {f.waterTiles} water tiles (graphics {string.Join(", ", Array.ConvertAll(f.waterGraphics, g => "0x" + g.ToString("X")))}), {f.landTiles} land, {f.occluderColumns} static occluder columns; {f.source}");
                md.AppendLine($"- Camera: orthographic along -_EyeDir {EyeDir} (the presenter's view kernel), size {OrthoSize}, {Width}x{Height}; NOT the presenter's exact oblique screen matrix.");
                md.AppendLine();
                md.AppendLine("## Stills");
                foreach (var s in shots)
                    md.AppendLine($"- `{s.File}`: {s.What} (sha256 {Sha(Path.Combine(outDir, s.File))})");
                md.AppendLine("- `water_t0_t1_diff_x8.png`: |t1 - t0| per channel x8");
                md.AppendLine();
                md.AppendLine("## Measured");
                md.AppendLine($"- Water motion (controlled): t0 = {T0:0.000} s, t1 = {T1:0.000} s, delta = {T1 - T0:0.000} s shader time: {motion}");
                md.AppendLine($"- Same t0 rendered twice: {repeat} (0 changed = deterministic; the t0/t1 delta is the time uniform)");
                md.AppendLine($"- Water coverage (t0 with vs without the water object): {waterMask.Changed} px");
                md.AppendLine($"- Shipped shader, Unity _Time: wall {wa:0.000} -> {wb:0.000} s (delta {wb - wa:0.000} s); Time.time {ta:0.000} -> {tb:0.000}; Time.realtimeSinceStartup {ra:0.000} -> {rb:0.000}: {realtime}");
                md.AppendLine($"- Sun mask on vs off (day): {shadow}");
                md.AppendLine($"- Day vs fixture night + player light: {nightDiff}");
                md.AppendLine($"- Sorting: red marker (0.8 below the ground on the highest land tile) {redNo} px without proxy -> {redYes} px with proxy; green marker (above ground) {greenNo} -> {greenYes} px");
                md.AppendLine();
                md.AppendLine("Limits: one chunk, offline fixture (not a live session); fixture camera and night/player-light values are illustrative; tile colour is the mean classic land art per tile (not the presenter's baked chunk art); no FPS measured.");
                string report = Path.Combine(outDir, "evidence.md");
                File.WriteAllText(report, md.ToString().Replace("\r\n", "\n"));
                Debug.Log($"LGEVIDENCE motion {motion}; repeat {repeat}; realtime {realtime}; shadow {shadow}; night {nightDiff}; sort red {redNo}->{redYes} green {greenNo}->{greenYes}");
                return report;
            }
            finally
            {
                saved.Restore();
                RenderTexture.active = null;
                if (rt != null)
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                }
                for (int i = made.Count - 1; i >= 0; i--)
                    if (made[i] != null)
                        Object.DestroyImmediate(made[i]);
            }
        }

        private static T Track<T>(List<Object> made, T o) where T : Object
        {
            made.Add(o);
            return o;
        }

        private static Shader Need(string name)
        {
            Shader s = Shader.Find(name);
            if (s == null || !s.isSupported)
                throw new InvalidOperationException($"shader '{name}' missing or not supported");
            return s;
        }

        /// <summary>The shipped water shader with _Time.y as a uniform and its include inlined (in memory, never saved).</summary>
        private static Shader TimedWater(Shader shipped)
        {
            string path = AssetDatabase.GetAssetPath(shipped);
            return TimedWater(File.ReadAllText(Path.Combine(ProjectRoot, path)), WaterInclude(shipped), "Hidden/LiveGroundCompare/WaterEvidenceTime");
        }

        /// <summary>LiveGroundLight.cginc next to the shipped water shader's Resources folder.</summary>
        private static string WaterInclude(Shader shipped) =>
            File.ReadAllText(Path.GetFullPath(Path.Combine(ProjectRoot, Path.GetDirectoryName(AssetDatabase.GetAssetPath(shipped)), "..", "LiveGroundLight.cginc")));

        /// <summary>Water shader text <paramref name="src"/> with _Time.y as a uniform, the include inlined, under <paramref name="hidden"/>.</summary>
        private static Shader TimedWater(string src, string include, string hidden)
        {
            const string time = "_Time.y", inc = "#include \"../LiveGroundLight.cginc\"", name = "Shader \"LiveGroundCompare/Water\"";
            int program = src.IndexOf("CGPROGRAM", StringComparison.Ordinal);
            string head = program < 0 ? src : src.Substring(0, program), code = program < 0 ? "" : src.Substring(program);
            int uses = (code.Length - code.Replace(time, "").Length) / time.Length; // the header comment may name it too
            if (uses != 1 || !code.Contains(inc) || !head.Contains(name))
                throw new InvalidOperationException($"water shader changed shape ({uses} uses of {time} in code); update the evidence patch");
            src = head.Replace(name, $"Shader \"{hidden}\"") +
                  code.Replace(inc, include + "\nfloat _LGEvidenceTime; // evidence fixture only: stands in for _Time.y\n")
                      .Replace(time, "_LGEvidenceTime");
            Shader s = ShaderUtil.CreateShaderAsset(src);
            if (s == null || ShaderUtil.ShaderHasError(s) || !s.isSupported)
                throw new InvalidOperationException("time-uniform copy of the water shader did not compile");
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static Vector3 Point(Fixture f, int i, int j) =>
            new Vector3(f.x0 + i, f.cornerZ[j * (f.tiles + 1) + i] * f.heightUnitPerZ, -(f.y0 + j));

        /// <summary>The chunk's corner grid, RIGHT–LEFT split as the compositor, UV = chunk texture (rows bottom-up).</summary>
        private static Mesh GroundMesh(Fixture f)
        {
            int n = f.tiles, c = n + 1;
            var v = new Vector3[c * c];
            var uv = new Vector2[c * c];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    v[j * c + i] = Point(f, i, j);
                    uv[j * c + i] = new Vector2((float)i / n, 1f - (float)j / n);
                }
            var tri = new List<int>(n * n * 6);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int top = j * c + i, right = top + 1, left = top + c, bottom = left + 1;
                    tri.Add(top); tri.Add(right); tri.Add(left);
                    tri.Add(right); tri.Add(bottom); tri.Add(left);
                }
            var m = new Mesh { name = "LiveGround evidence ground", hideFlags = HideFlags.HideAndDontSave, vertices = v, uv = uv, triangles = tri.ToArray() };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>As TerrainWaterSurface.Attach: terrain-space vertices, tint colour, uv.x = shore.</summary>
        private static Mesh WaterMesh(Fixture f)
        {
            int n = f.waterX.Length;
            var v = new Vector3[n];
            var col = new Color32[n];
            var uv = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(f.waterX[i], f.waterY[i], f.waterZ[i]);
                uint c = unchecked((uint)f.waterTint[i]);
                col[i] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
                uv[i] = new Vector2(f.waterShore[i], 0f);
            }
            var m = new Mesh { name = "LiveGround evidence water", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            m.vertices = v;
            m.colors32 = col;
            m.uv = uv;
            m.triangles = f.waterIndices;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D ArtTexture(Fixture f)
        {
            int n = f.tiles;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    uint c = unchecked((uint)f.tileArgb[j * n + i]);
                    px[(n - 1 - j) * n + i] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>LiveGroundLighting.ShadowMask bytes: already rows bottom-up, bilinear as the presenter's mask.</summary>
        private static Texture2D SunTexture(Fixture f)
        {
            int n = f.tiles * f.shadowTexels;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int k = 0; k < px.Length; k++)
            {
                byte b = (byte)f.sunMask[k];
                px[k] = new Color32(b, b, b, 255);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private static (Vector3 Hill, Vector3 Flat) MarkerSites(Fixture f)
        {
            int n = f.tiles;
            float best = float.MinValue, low = float.MaxValue;
            Vector3 hill = Vector3.zero, flat = Vector3.zero;
            for (int j = 1; j < n - 1; j++)
                for (int i = 1; i < n - 1; i++)
                {
                    if (Array.IndexOf(f.waterGraphics, f.graphics[j * n + i]) >= 0)
                        continue;
                    Vector3 p = (Point(f, i, j) + Point(f, i + 1, j) + Point(f, i + 1, j + 1) + Point(f, i, j + 1)) / 4f;
                    if (p.y > best) { best = p.y; hill = p; }
                    if (p.y < low) { low = p.y; flat = p; }
                }
            return (hill, flat);
        }

        /// <summary>A camera-facing upright quad from <paramref name="baseOffset"/> under <paramref name="at"/> to 1.6 above it.</summary>
        private static Mesh Billboard(Vector3 at, Vector3 right, float baseOffset, Color32 color)
        {
            Vector3 r = right.normalized * 0.35f, lo = at + Vector3.up * baseOffset, hi = at + Vector3.up * 1.6f;
            return new Mesh
            {
                name = "LiveGround evidence marker",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { lo - r, hi - r, hi + r, lo + r },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                uv2 = new Vector2[4], // height 0: never cut
                colors32 = new[] { color, color, color, color },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
        }

        private static GameObject Node(string name, Mesh mesh, Material material, int layer)
        {
            var go = new GameObject("LiveGround evidence " + name) { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        /// <summary>The terrain camera's pass (ground + water); then, as the main camera over it, the sprite pass (depth cleared).</summary>
        private static Texture2D RenderPass(Camera cam, bool sprites)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 1 << GroundLayer;
            cam.Render();
            if (sprites)
            {
                cam.clearFlags = CameraClearFlags.Depth;
                cam.cullingMask = 1 << SpriteLayer;
                cam.Render();
            }
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = cam.targetTexture;
            var t = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        private static Texture2D WithoutWater(Camera cam, GameObject sea)
        {
            sea.SetActive(false);
            Texture2D t = RenderPass(cam, false);
            sea.SetActive(true);
            return t;
        }

        private readonly struct Diff
        {
            public readonly int Changed, Over8, Total;
            public readonly double MeanAbs;
            public Diff(int changed, int over8, int total, double meanAbs) { Changed = changed; Over8 = over8; Total = total; MeanAbs = meanAbs; }
            public override string ToString() => $"{Changed}/{Total} px changed ({Over8} by > 8/255), mean |delta| {MeanAbs:0.000}/255 per channel";
        }

        private static Diff Compare(Texture2D a, Texture2D b)
        {
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int changed = 0, over = 0;
            long sum = 0;
            for (int k = 0; k < pa.Length; k++)
            {
                int dr = Math.Abs(pa[k].r - pb[k].r), dg = Math.Abs(pa[k].g - pb[k].g), db = Math.Abs(pa[k].b - pb[k].b);
                int max = Math.Max(dr, Math.Max(dg, db));
                if (max > 0) changed++;
                if (max > 8) over++;
                sum += dr + dg + db;
            }
            return new Diff(changed, over, pa.Length, sum / (3.0 * pa.Length));
        }

        private static Texture2D DiffImage(Texture2D a, Texture2D b, int gain)
        {
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            var d = new Color32[pa.Length];
            for (int k = 0; k < pa.Length; k++)
                d[k] = new Color32((byte)Math.Min(255, Math.Abs(pa[k].r - pb[k].r) * gain), (byte)Math.Min(255, Math.Abs(pa[k].g - pb[k].g) * gain),
                                   (byte)Math.Min(255, Math.Abs(pa[k].b - pb[k].b) * gain), 255);
            var t = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(d);
            t.Apply();
            return t;
        }

        /// <summary>Pixels of the pure red (or green) marker colour.</summary>
        private static int Count(Texture2D t, bool red)
        {
            int n = 0;
            foreach (Color32 c in t.GetPixels32())
                if (red ? c.r > 200 && c.g < 60 && c.b < 60 : c.g > 200 && c.r < 60 && c.b < 60)
                    n++;
            return n;
        }

        private static string Sha(string path)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)), 0, 8).Replace("-", "").ToLowerInvariant();
        }

        private static string V(Vector4 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        private static void SetGlobals(Fixture f, Vector4 ambient, Vector4 lightColor, Vector4 lightPos)
        {
            Shader.SetGlobalVector("_UOAmbient", ambient);
            Shader.SetGlobalVector("_UOLightColor", lightColor);
            Shader.SetGlobalVector("_UOLightPos", lightPos);
            Shader.SetGlobalFloat("_UODead", 0f);
            Shader.SetGlobalFloat("_UOCutZ", 0f);
            Shader.SetGlobalVector("_LGSunDir", new Vector4(f.sun[0], f.sun[1], f.sun[2], 0f));
            // Fixture convention: "Unity xy" = (tile x, tile y), so _UOLightPos is in tiles.
            var m = Matrix4x4.zero;
            m.SetRow(0, new Vector4(1f, 0f, 0f, 0f));
            m.SetRow(1, new Vector4(0f, 0f, -1f, 0f));
            Shader.SetGlobalMatrix("_LGTerrainToUnity", m);
        }

        /// <summary>The globals the capture sets, put back afterwards (the open Editor's Lighting owns them in Play).</summary>
        private sealed class SavedGlobals
        {
            private readonly Vector4 _ambient = Shader.GetGlobalVector("_UOAmbient"), _color = Shader.GetGlobalVector("_UOLightColor"),
                _pos = Shader.GetGlobalVector("_UOLightPos"), _sun = Shader.GetGlobalVector("_LGSunDir");
            private readonly float _dead = Shader.GetGlobalFloat("_UODead"), _cut = Shader.GetGlobalFloat("_UOCutZ");
            private readonly Matrix4x4 _toUnity = Shader.GetGlobalMatrix("_LGTerrainToUnity");

            public void Restore()
            {
                Shader.SetGlobalVector("_UOAmbient", _ambient);
                Shader.SetGlobalVector("_UOLightColor", _color);
                Shader.SetGlobalVector("_UOLightPos", _pos);
                Shader.SetGlobalVector("_LGSunDir", _sun);
                Shader.SetGlobalFloat("_UODead", _dead);
                Shader.SetGlobalFloat("_UOCutZ", _cut);
                Shader.SetGlobalMatrix("_LGTerrainToUnity", _toUnity);
            }
        }
    }
}
