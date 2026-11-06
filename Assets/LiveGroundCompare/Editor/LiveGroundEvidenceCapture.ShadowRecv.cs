using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UOClient.LiveGroundCompare.Editor
{
    /// <summary>
    /// Sprite sun-shadow receive evidence (TEST only): renders Evidence~/shadowRecvChunk.json (a real Britain chunk from
    /// DotNet LiveGroundSpriteShadowFixtureTests: the terrain bake, the map statics as IsoCompositor placed them, a real
    /// human body frame, the sun mask and the shadow ceiling) at one world pixel per output pixel, with the shipped
    /// UOClient/WorldSprite and LiveGroundCompare/TerrainLit, off-screen. Writes stills/shadowrecv_*.png and
    /// shadowrecv.md. Explicit invocation only (menu or -executeMethod CaptureShadowRecvBatch); it enables the
    /// LG_SPRITE_SHADOW keyword only between renders and puts it and every global back in finally.
    ///
    /// Equivalence: the 4aa51a6 WorldSprite text (Evidence~/WorldSprite.4aa51a6.shader.txt) is compiled in memory and
    /// rendered against the shipped shader with the keyword off; the report gives the exact pixel difference.
    /// </summary>
    public static partial class LiveGroundEvidenceCapture
    {
        private const int RecvSize = 1024, RecvHalf = RecvSize / 2;
        private const string SpriteKeyword = "LG_SPRITE_SHADOW";

        /// <summary>DotNet LiveGroundSpriteShadowFixtureTests.Fixture (same field names).</summary>
        [Serializable]
        public sealed class ShadowFixture
        {
            public string source, groundPng, atlasPng, groundSha, atlasSha;
            public int map, x0, y0, tiles, scale, originX, originY, shadowTexels, wrapTiles, occluderColumns, groundTexels;
            public float heightUnitPerZ, noCeiling, minShade, softZ, biasZ;
            public float[] sun, ceiling, qFoot, mobFootShade, mobHeadShade;
            public int[] cornerZ, sunMask, qRect, qAtlas, qTile, mobAtlas, mobTiles, mobFootProbe, mobHeadProbe;
            public int atlasWidth, atlasHeight, quadCount, uprightQuads, groundQuads, mobAnchorX, mobAnchorY, mobBody, mobAction, mobDir;
        }

        [MenuItem("UOClient/LiveGroundCompare/Capture sprite shadow receive stills (TEST)")]
        public static void CaptureShadowRecvMenu()
        {
            string report = CaptureShadowRecv(Path.Combine(EvidenceDir, "shadowRecvChunk.json"), Path.Combine(EvidenceDir, "stills"));
            Debug.Log("UOClient LiveGround shadow receive evidence: wrote " + report);
        }

        /// <summary>-executeMethod entry: optional -lgShadowFixture &lt;json&gt; and -lgOut &lt;dir&gt;; exits the Editor (1 on failure).</summary>
        public static void CaptureShadowRecvBatch()
        {
            int code = 0;
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lgGamma") >= 0 && PlayerSettings.colorSpace != ColorSpace.Gamma)
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                string report = CaptureShadowRecv(Arg("-lgShadowFixture") ?? Path.Combine(EvidenceDir, "shadowRecvChunk.json"),
                                                  Arg("-lgOut") ?? Path.Combine(EvidenceDir, "stills"));
                Debug.Log("UOClient LiveGround shadow receive evidence: wrote " + report);
            }
            catch (Exception e)
            {
                Debug.LogError("UOClient LiveGround shadow receive evidence FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        public static string CaptureShadowRecv(string fixturePath, string outDir)
        {
            ShadowFixture f = JsonUtility.FromJson<ShadowFixture>(File.ReadAllText(fixturePath));
            string fixtureDir = Path.GetDirectoryName(fixturePath);
            Directory.CreateDirectory(outDir);
            var made = new List<Object>();
            var saved = new SavedGlobals();
            bool keywordWas = Shader.IsKeywordEnabled(SpriteKeyword);
            Texture savedCeil = Shader.GetGlobalTexture("_LGShadowCeil");
            Vector4 savedMap = Shader.GetGlobalVector("_LGSpriteShadowMap");
            RenderTexture rt = null;
            try
            {
                Shader terrain = Need(TerrainShader), sprite = Need(SpriteShader);
                Shader head = HeadSprite(Path.Combine(fixtureDir, "WorldSprite.4aa51a6.shader.txt"));
                made.Add(head);

                Texture2D ground = Track(made, Png(Path.Combine(fixtureDir, f.groundPng), FilterMode.Point));
                Texture2D atlas = Track(made, Png(Path.Combine(fixtureDir, f.atlasPng), FilterMode.Point));
                Texture2D mask = Track(made, MaskTexture(f));
                Texture2D ceilEmpty = Track(made, CeilingTexture(f, false));
                Texture2D ceil = Track(made, CeilingTexture(f, true));

                Material terrainMat = Track(made, new Material(terrain));
                terrainMat.SetTexture("_MainTex", ground);
                terrainMat.SetTexture("_LGShadowTex", mask);
                Material spriteMat = Track(made, new Material(sprite) { mainTexture = atlas });
                Material headMat = Track(made, new Material(head) { mainTexture = atlas });

                Mesh groundMesh = Track(made, RecvGround(f));
                GameObject groundGo = Track(made, Node("Recv ground", groundMesh, terrainMat, GroundLayer));
                Mesh statics = Track(made, RecvSprites(f, -1, true)), staticsNoFoot = Track(made, RecvSprites(f, -1, false));
                GameObject spriteGo = Track(made, Node("Recv sprites", statics, spriteMat, SpriteLayer));
                var mobMeshes = new Mesh[3];
                for (int p = 0; p < 3; p++)
                    mobMeshes[p] = Track(made, RecvSprites(f, p, true));

                // The camera looks along +Z at Unity XY (the main camera's frame), 1 world pixel per output pixel.
                int cx = 0, cy = f.originY - ((f.x0 + f.y0 + f.tiles) * 22 + 22) + 60;
                var camGo = Track(made, new GameObject("LiveGround shadow receive camera") { hideFlags = HideFlags.HideAndDontSave });
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = RecvHalf;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 2000f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
                camGo.transform.position = new Vector3(cx, cy, -1000f);
                camGo.transform.rotation = Quaternion.identity;
                rt = new RenderTexture(RecvSize, RecvSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;

                Vector4 day = Vector4.one, none = Vector4.zero;
                RecvGlobals(f, day, none, Vector4.zero, ceil);
                var shots = new List<(string File, string What)>();
                Texture2D Shot(string file, string what, bool on)
                {
                    Texture2D t = Track(made, RecvRender(cam, on));
                    if (file != null)
                    {
                        File.WriteAllBytes(Path.Combine(outDir, file), t.EncodeToPNG());
                        shots.Add((file, what));
                    }
                    return t;
                }

                // ---- 1. Equivalence: keyword off must be the 4aa51a6 shader exactly ----
                spriteGo.GetComponent<MeshRenderer>().sharedMaterial = headMat;
                Texture2D headTex = Shot(null, null, false);
                spriteGo.GetComponent<MeshRenderer>().sharedMaterial = spriteMat;
                Texture2D off = Shot("shadowrecv_off.png", "keyword off (production path): terrain CPU mask only, sprites unshadowed", false);
                Diff eqHead = Compare(headTex, off);
                spriteGo.GetComponent<MeshFilter>().sharedMesh = staticsNoFoot;
                Diff eqNoFoot = Compare(off, Shot(null, null, false));
                spriteGo.GetComponent<MeshFilter>().sharedMesh = statics;
                Shader.SetGlobalTexture("_LGShadowCeil", ceilEmpty);
                Diff eqEmpty = Compare(off, Shot(null, null, true));
                Shader.SetGlobalTexture("_LGShadowCeil", ceil);

                // ---- 2. Receive on: statics ----
                Texture2D on = Shot("shadowrecv_on.png", "keyword on: statics receive the CPU shadow ceiling (terrain + map static columns)", true);
                Diff recv = Compare(off, on);
                File.WriteAllBytes(Path.Combine(outDir, "shadowrecv_diff_x4.png"), Track(made, DiffImage(off, on, 4)).EncodeToPNG());
                shots.Add(("shadowrecv_diff_x4.png", "|on - off| x4"));
                File.WriteAllBytes(Path.Combine(outDir, "shadowrecv_ceiling.png"), Track(made, CeilingImage(f)).EncodeToPNG());
                shots.Add(("shadowrecv_ceiling.png", "the chunk's shadow ceiling above its ground (black: none, white: >= 40 Z above), 4 px per tile, tile x right, tile y down"));

                // ---- 3. A real body frame (dynamic receiver) at three tiles; probes vs the CPU mirror ----
                string[] names = { "partial", "shadow", "lit" };
                var probe = new StringBuilder();
                bool probesOk = true;
                GameObject mobGo = Track(made, Node("Recv mobile", mobMeshes[0], spriteMat, SpriteLayer));
                for (int p = 0; p < 3; p++)
                {
                    mobGo.GetComponent<MeshFilter>().sharedMesh = mobMeshes[p];
                    Texture2D mOff = Shot(null, null, false);
                    Texture2D mOn = Shot($"shadowrecv_mobile_{names[p]}.png", $"body {f.mobBody} action {f.mobAction} dir {f.mobDir} at tile ({f.mobTiles[p * 3]}, {f.mobTiles[p * 3 + 1]}, {f.mobTiles[p * 3 + 2]}), keyword on", true);
                    File.WriteAllBytes(Path.Combine(outDir, $"shadowrecv_mobile_{names[p]}_zoom.png"), Track(made, Zoom(f, p, mOff, mOn)).EncodeToPNG());
                    shots.Add(($"shadowrecv_mobile_{names[p]}_zoom.png", "x4 crop around the body: left keyword off, right keyword on"));
                    foreach ((string part, int[] at, float[] exp) in new[] { ("foot", f.mobFootProbe, f.mobFootShade), ("head", f.mobHeadProbe, f.mobHeadShade) })
                    {
                        (int px, int py) = (at[p * 2], at[p * 2 + 1]);
                        Color32 a = Pixel(f, mOff, px, py, cx, cy), b = Pixel(f, mOn, px, py, cx, cy);
                        Color32 art = AtlasPixel(f, atlas, p, px, py);
                        bool visible = a.r == art.r && a.g == art.g && a.b == art.b;
                        float ratio = Ratio(a, b);
                        bool ok = visible && Math.Abs(ratio - exp[p]) <= 0.02f;
                        probesOk &= ok;
                        probe.AppendLine($"- {names[p]} {part} probe world px ({px}, {py}): off {C(a)} (body art {C(art)}, visible {visible}), on {C(b)}: GPU ratio {ratio:0.000}, CPU mirror (SpriteShadow) {exp[p]:0.000} -> {(ok ? "MATCH" : "MISMATCH")}");
                    }
                }
                mobGo.GetComponent<MeshFilter>().sharedMesh = mobMeshes[0];

                // ---- 4. Light change: night ambient + the player light on the partial body, receive on ----
                Vector2 lp = FootUnity(f, 0);
                RecvGlobals(f, new Vector4(0.32f, 0.34f, 0.48f, 1f), new Vector4(0.9f, 0.75f, 0.5f, 1f), new Vector4(lp.x, lp.y, 140f, 0f), ceil);
                Texture2D nightOn = Shot("shadowrecv_night_on.png", "night ambient (0.32, 0.34, 0.48) + player light r=140 px at the partial body, keyword on", true);
                Texture2D nightOff = Shot("shadowrecv_night_off.png", "same night, keyword off", false);
                Diff night = Compare(nightOff, nightOn);
                RecvGlobals(f, day, none, Vector4.zero, ceil);

                int darker = CountDarker(off, on);
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
                var md = new StringBuilder();
                md.AppendLine("# LiveGroundCompare sprite shadow receive evidence (isolated fixture, TEST only)");
                md.AppendLine();
                md.AppendLine($"- Captured: {now}; Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}; color space {QualitySettings.activeColorSpace}; batch mode {Application.isBatchMode}");
                md.AppendLine($"- Fixture: `{fixturePath}`: map {f.map} chunk ({f.x0}, {f.y0}): {f.occluderColumns} static columns, {f.quadCount} static quads ({f.uprightQuads} upright, {f.groundQuads} ground), atlas sha256 {f.atlasSha.Substring(0, 16)}, ground sha256 {f.groundSha.Substring(0, 16)}; {f.source}");
                md.AppendLine($"- Camera: orthographic along +Z over Unity XY, 1 world pixel per output pixel ({RecvSize}x{RecvSize}), centre ({cx}, {cy}). Terrain = TerrainLit on the chunk's corner grid in the sprites' plane (same normals, art, mask); drawn first, sprites over it with depth cleared (no hill occlusion here).");
                md.AppendLine();
                md.AppendLine("## Equivalence (keyword off)");
                md.AppendLine($"- 4aa51a6 WorldSprite (compiled in memory from WorldSprite.4aa51a6.shader.txt) vs shipped WorldSprite, keyword off, same mesh (feet in UV3): {eqHead}");
                md.AppendLine($"- Shipped, keyword off: mesh with UV3 feet vs the same quads without UV3: {eqNoFoot}");
                md.AppendLine($"- Shipped: keyword off vs keyword on with an empty ceiling (every texel NoCeiling): {eqEmpty}");
                md.AppendLine();
                md.AppendLine("## Receive");
                md.AppendLine($"- Statics, keyword off vs on: {recv}; {darker} px darker with on");
                md.Append(probe);
                md.AppendLine($"- Probes: {(probesOk ? "all match" : "MISMATCH present")} (tolerance 0.02; 8-bit output, RHalf ceiling)");
                md.AppendLine($"- Night + player light, keyword off vs on: {night}");
                md.AppendLine();
                md.AppendLine("## Stills");
                foreach (var s in shots)
                    md.AppendLine($"- `{s.File}`: {s.What} (sha256 {Sha(Path.Combine(outDir, s.File))})");
                md.AppendLine();
                md.AppendLine("Limits: one chunk (rays leaving it count as lit, as the terrain mask); billboard reconstruction (a sprite is a vertical plane through its foot tile centre; overhangs and roofs are not modelled separately); mobiles/items do not cast; terrain sorting against sprites is not exercised here (see Phase C sort stills). Not a Unity shadow map.");
                string report = Path.Combine(outDir, "shadowrecv.md");
                File.WriteAllText(report, md.ToString().Replace("\r\n", "\n"));
                Debug.Log($"LGSHADOWRECV eqHead {eqHead}; eqNoFoot {eqNoFoot}; eqEmpty {eqEmpty}; recv {recv}; darker {darker}; probes {(probesOk ? "ok" : "MISMATCH")}; night {night}");
                return report;
            }
            finally
            {
                if (keywordWas) Shader.EnableKeyword(SpriteKeyword); else Shader.DisableKeyword(SpriteKeyword);
                Shader.SetGlobalTexture("_LGShadowCeil", savedCeil);
                Shader.SetGlobalVector("_LGSpriteShadowMap", savedMap);
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

        /// <summary>The 4aa51a6 WorldSprite text under a hidden name, compiled in memory (never saved).</summary>
        private static Shader HeadSprite(string path)
        {
            const string name = "Shader \"UOClient/WorldSprite\"";
            string src = File.ReadAllText(path);
            if (!src.Contains(name) || src.Contains("LG_SPRITE_SHADOW"))
                throw new InvalidOperationException("reference WorldSprite text is not the 4aa51a6 shader");
            Shader s = ShaderUtil.CreateShaderAsset(src.Replace(name, "Shader \"Hidden/LiveGroundCompare/WorldSprite4aa51a6\""));
            if (s == null || ShaderUtil.ShaderHasError(s) || !s.isSupported)
                throw new InvalidOperationException("4aa51a6 WorldSprite copy did not compile");
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static Texture2D Png(string path, FilterMode filter)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (!t.LoadImage(File.ReadAllBytes(path), false))
                throw new InvalidOperationException("could not load " + path);
            t.filterMode = filter;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        private static Texture2D MaskTexture(ShadowFixture f)
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

        /// <summary>As SpriteShadowCeiling: RHalf, Repeat, bilinear, the chunk's block at its wrapped slot (empty: NoCeiling only).</summary>
        private static Texture2D CeilingTexture(ShadowFixture f, bool withChunk)
        {
            int size = f.wrapTiles * f.shadowTexels, block = f.tiles * f.shadowTexels;
            var texels = new ushort[size * size];
            ushort empty = Mathf.FloatToHalf(f.noCeiling);
            for (int i = 0; i < texels.Length; i++)
                texels[i] = empty;
            if (withChunk)
            {
                int c0 = f.x0 % f.wrapTiles * f.shadowTexels, r0 = f.y0 % f.wrapTiles * f.shadowTexels;
                for (int r = 0; r < block; r++)
                    for (int c = 0; c < block; c++)
                        texels[(r0 + r) * size + c0 + c] = Mathf.FloatToHalf(f.ceiling[r * block + c]);
            }
            var t = new Texture2D(size, size, TextureFormat.RHalf, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            t.SetPixelData(texels, 0);
            t.Apply(false, false);
            return t;
        }

        private static Vector2 ToUnity(ShadowFixture f, float wx, float wy) => new Vector2(wx - f.originX, f.originY - wy);

        /// <summary>The chunk's corner grid in the sprites' plane, terrain-space normals, the bake's UVs, behind every sprite.</summary>
        private static Mesh RecvGround(ShadowFixture f)
        {
            int n = f.tiles, c = n + 1;
            var screen = new Vector3[c * c];
            var space = new Vector3[c * c];
            var uv = new Vector2[c * c];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    int x = f.x0 + i, y = f.y0 + j, z = f.cornerZ[j * c + i];
                    Vector2 u = ToUnity(f, (x - y) * 22f, (x + y) * 22f - z * 4f);
                    screen[j * c + i] = new Vector3(u.x, u.y, 500f);
                    space[j * c + i] = new Vector3(x, z * f.heightUnitPerZ, -y);
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
            var normals = new Mesh { vertices = space, triangles = tri.ToArray() };
            normals.RecalculateNormals();
            var m = new Mesh { name = "LiveGround shadow receive ground", hideFlags = HideFlags.HideAndDontSave, vertices = screen, uv = uv, triangles = tri.ToArray(), normals = normals.normals };
            Object.DestroyImmediate(normals);
            m.RecalculateBounds();
            return m;
        }

        private static Vector2 FootUnity(ShadowFixture f, int placement)
        {
            int x = f.mobTiles[placement * 3], y = f.mobTiles[placement * 3 + 1], z = f.mobTiles[placement * 3 + 2];
            return ToUnity(f, (x - y) * 22f, (x + y) * 22f + 22f - z * 4f);
        }

        /// <summary>
        /// The statics as the streamer quads them (draw order = depth), or (placement >= 0) only the body frame there,
        /// at the depth its tile diagonal sorts among them. <paramref name="feet"/>: UV3 foot anchors as MeshBuilder writes them.
        /// </summary>
        private static Mesh RecvSprites(ShadowFixture f, int placement, bool feet)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var cut = new List<Vector2>(); var foot = new List<Vector4>(); var col = new List<Color32>(); var tri = new List<int>();
            void Quad(int left, int top, int w, int h, int au, int av, int aw, int ah, float depth, Vector4 ft)
            {
                int i = v.Count;
                Vector2 tl = ToUnity(f, left, top);
                var r = new Rect(tl.x, tl.y - h, w, h);
                v.Add(new Vector3(r.xMin, r.yMin, depth)); v.Add(new Vector3(r.xMin, r.yMax, depth));
                v.Add(new Vector3(r.xMax, r.yMax, depth)); v.Add(new Vector3(r.xMax, r.yMin, depth));
                float u0 = (float)au / f.atlasWidth, u1 = (float)(au + aw) / f.atlasWidth;
                float v0 = 1f - (float)(av + ah) / f.atlasHeight, v1 = 1f - (float)av / f.atlasHeight;
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u1, v0));
                for (int k = 0; k < 4; k++) { cut.Add(Vector2.zero); foot.Add(ft); col.Add(new Color32(255, 255, 255, 255)); }
                tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Vector4 Foot(float wx, float wy, float z, float kind)
            {
                Vector2 u = ToUnity(f, wx, wy);
                return new Vector4(u.x, u.y, z, kind);
            }
            const float step = 0.01f;
            if (placement < 0)
                for (int q = 0; q < f.quadCount; q++)
                    Quad(f.qRect[q * 4], f.qRect[q * 4 + 1], f.qRect[q * 4 + 2], f.qRect[q * 4 + 3],
                         f.qAtlas[q * 4], f.qAtlas[q * 4 + 1], f.qAtlas[q * 4 + 2], f.qAtlas[q * 4 + 3],
                         -q * step, Foot(f.qFoot[q * 4], f.qFoot[q * 4 + 1], f.qFoot[q * 4 + 2], f.qFoot[q * 4 + 3]));
            else
            {
                int x = f.mobTiles[placement * 3], y = f.mobTiles[placement * 3 + 1], z = f.mobTiles[placement * 3 + 2];
                int order = 0; // after every static on an earlier diagonal, or on its own diagonal at or below its Z
                for (int q = 0; q < f.quadCount; q++)
                {
                    int d = f.qTile[q * 3] + f.qTile[q * 3 + 1];
                    if (d < x + y || (d == x + y && f.qTile[q * 3 + 2] <= z))
                        order = q + 1;
                }
                float ax = (x - y) * 22f, ay = (x + y) * 22f + 22f - z * 4f;
                Quad((int)ax - f.mobAnchorX, (int)ay - f.mobAnchorY, f.mobAtlas[2], f.mobAtlas[3], f.mobAtlas[0], f.mobAtlas[1], f.mobAtlas[2], f.mobAtlas[3],
                     -(order - 0.5f) * step, Foot(ax, ay, z, 1f));
            }
            var m = new Mesh { name = "LiveGround shadow receive sprites", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetUVs(1, cut);
            if (feet)
                m.SetUVs(2, foot);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        private static void RecvGlobals(ShadowFixture f, Vector4 ambient, Vector4 lightColor, Vector4 lightPos, Texture ceiling)
        {
            Shader.SetGlobalVector("_UOAmbient", ambient);
            Shader.SetGlobalVector("_UOLightColor", lightColor);
            Shader.SetGlobalVector("_UOLightPos", lightPos);
            Shader.SetGlobalFloat("_UODead", 0f);
            Shader.SetGlobalFloat("_UOCutZ", 0f);
            Shader.SetGlobalVector("_LGSunDir", new Vector4(f.sun[0], f.sun[1], f.sun[2], 0f));
            var m = Matrix4x4.zero; // the ground mesh is already in Unity XY
            m.SetRow(0, new Vector4(1f, 0f, 0f, 0f));
            m.SetRow(1, new Vector4(0f, 1f, 0f, 0f));
            Shader.SetGlobalMatrix("_LGTerrainToUnity", m);
            Shader.SetGlobalTexture("_LGShadowCeil", ceiling);
            Shader.SetGlobalVector("_LGSpriteShadowMap", new Vector4(f.originX, f.originY, f.scale, 1f / f.wrapTiles));
        }

        /// <summary>Terrain pass, then the sprite pass over it with depth cleared (the presenter's two cameras).</summary>
        private static Texture2D RecvRender(Camera cam, bool keyword)
        {
            if (keyword) Shader.EnableKeyword(SpriteKeyword); else Shader.DisableKeyword(SpriteKeyword);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = 1 << GroundLayer;
            cam.Render();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.cullingMask = 1 << SpriteLayer;
            cam.Render();
            Shader.DisableKeyword(SpriteKeyword);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = cam.targetTexture;
            var t = new Texture2D(RecvSize, RecvSize, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.ReadPixels(new Rect(0, 0, RecvSize, RecvSize), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        /// <summary>The output pixel covering world-screen pixel (px, py).</summary>
        private static (int I, int J) OutPixel(ShadowFixture f, int px, int py, int cx, int cy) =>
            (px - f.originX - cx + RecvHalf, f.originY - py - 1 - cy + RecvHalf);

        private static Color32 Pixel(ShadowFixture f, Texture2D t, int px, int py, int cx, int cy)
        {
            (int i, int j) = OutPixel(f, px, py, cx, cy);
            return t.GetPixels32()[j * RecvSize + i];
        }

        /// <summary>The body frame's own art at world-screen pixel (px, py) for a placement.</summary>
        private static Color32 AtlasPixel(ShadowFixture f, Texture2D atlas, int placement, int px, int py)
        {
            int x = f.mobTiles[placement * 3], y = f.mobTiles[placement * 3 + 1], z = f.mobTiles[placement * 3 + 2];
            int left = (x - y) * 22 - f.mobAnchorX, top = (x + y) * 22 + 22 - z * 4 - f.mobAnchorY;
            int au = f.mobAtlas[0] + (px - left), av = f.mobAtlas[1] + (py - top);
            return atlas.GetPixels32()[(f.atlasHeight - 1 - av) * f.atlasWidth + au];
        }

        private static float Ratio(Color32 off, Color32 on)
        {
            int c = off.r >= off.g && off.r >= off.b ? 0 : off.g >= off.b ? 1 : 2;
            float a = c == 0 ? off.r : c == 1 ? off.g : off.b, b = c == 0 ? on.r : c == 1 ? on.g : on.b;
            return a <= 0f ? 1f : b / a;
        }

        private static string C(Color32 c) => $"({c.r},{c.g},{c.b})";

        private static int CountDarker(Texture2D a, Texture2D b)
        {
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int n = 0;
            for (int k = 0; k < pa.Length; k++)
                if (pb[k].r + pb[k].g + pb[k].b < pa[k].r + pa[k].g + pa[k].b)
                    n++;
            return n;
        }

        /// <summary>x4 nearest crop (64x96 output pixels around the body) of off | on, side by side.</summary>
        private static Texture2D Zoom(ShadowFixture f, int placement, Texture2D off, Texture2D on)
        {
            const int w = 64, h = 96, k = 4;
            int x = f.mobTiles[placement * 3], y = f.mobTiles[placement * 3 + 1], z = f.mobTiles[placement * 3 + 2];
            int cx = 0, cy = f.originY - ((f.x0 + f.y0 + f.tiles) * 22 + 22) + 60;
            (int i0, int j0) = OutPixel(f, (x - y) * 22 - w / 2, (x + y) * 22 + 22 - z * 4 + 12, cx, cy);
            Color32[] a = off.GetPixels32(), b = on.GetPixels32();
            var o = new Color32[2 * w * k * h * k];
            for (int j = 0; j < h * k; j++)
                for (int i = 0; i < w * k; i++)
                {
                    int si = Mathf.Clamp(i0 + i / k, 0, RecvSize - 1), sj = Mathf.Clamp(j0 + j / k, 0, RecvSize - 1);
                    o[j * 2 * w * k + i] = a[sj * RecvSize + si];
                    o[j * 2 * w * k + w * k + i] = b[sj * RecvSize + si];
                }
            var t = new Texture2D(2 * w * k, h * k, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(o);
            t.Apply();
            return t;
        }

        private static Texture2D CeilingImage(ShadowFixture f)
        {
            int n = f.tiles * f.shadowTexels, c = f.tiles + 1;
            var px = new Color32[n * n];
            for (int r = 0; r < n; r++)
                for (int col = 0; col < n; col++)
                {
                    int i = Mathf.Min(f.tiles - 1, col / f.shadowTexels), j = Mathf.Min(f.tiles - 1, r / f.shadowTexels);
                    float groundZ = (f.cornerZ[j * c + i] + f.cornerZ[j * c + i + 1] + f.cornerZ[(j + 1) * c + i] + f.cornerZ[(j + 1) * c + i + 1]) / 4f;
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt((f.ceiling[r * n + col] - groundZ) / 40f * 255f), 0, 255);
                    px[(n - 1 - r) * n + col] = new Color32(v, v, v, 255); // image top = tile y min
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
