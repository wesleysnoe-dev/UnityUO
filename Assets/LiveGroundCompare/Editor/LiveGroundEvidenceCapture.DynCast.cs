using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace UOClient.LiveGroundCompare.Editor
{
    /// <summary>
    /// Dynamic cast, cross-chunk margin and water multi-frame evidence (TEST only). Explicit invocation only (menu or
    /// -executeMethod); nothing runs on load, enters Play or touches the open scene; every global and the keyword are
    /// put back in finally.
    ///
    /// <see cref="CaptureDynCast"/>: Evidence~/dynCastChunk.json (DotNet LiveGroundDynCastFixtureTests: ceilings made by
    /// the shipped ShadowCeilingCore on the real Britain chunk of shadowRecvChunk.json, a mobile column at tile A, moved
    /// to B, removed, and the chunk with its real west/north/north-west neighbours loaded). The ground is drawn in
    /// terrain space through the presenter's terrain→Unity rows (as TerrainIsoPresenter), so TerrainLit reads the
    /// world ceiling at its own tile point; sprites as in the receive capture. Also times the ceiling texture upload
    /// (full vs per-chunk copy) on this GPU's CPU side.
    ///
    /// <see cref="CaptureWaterRealtimeBatch"/>: the shipped water shader rendered on separate Editor update ticks
    /// (separate frames, wall time apart) with a probe that writes Unity's own _Time.y to a float target, so the report
    /// shows the _Time each frame really got, not an override.
    /// </summary>
    public static partial class LiveGroundEvidenceCapture
    {
        /// <summary>DotNet LiveGroundDynCastFixtureTests.DynFixture (same field names).</summary>
        [Serializable]
        public sealed class DynFixture
        {
            public string source, baseFixture;
            public int map, x0, y0, tiles, mobileHeight, marginNeighbours, marginTexels, castATexels, castBTexels;
            public int[] casterA, casterB, receiver;
            public float[] ceilStatic, ceilCastA, ceilCastB, ceilRemoved, ceilMargin, ceilMarginCastA;
        }

        [MenuItem("UOClient/LiveGroundCompare/Capture dynamic cast + margin stills (TEST)")]
        public static void CaptureDynCastMenu()
        {
            string report = CaptureDynCast(Path.Combine(EvidenceDir, "dynCastChunk.json"), Path.Combine(EvidenceDir, "stills"));
            Debug.Log("UOClient LiveGround dynamic cast evidence: wrote " + report);
        }

        /// <summary>-executeMethod entry: optional -lgDynFixture &lt;json&gt; and -lgOut &lt;dir&gt;; exits the Editor (1 on failure).</summary>
        public static void CaptureDynCastBatch()
        {
            int code = 0;
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lgGamma") >= 0 && PlayerSettings.colorSpace != ColorSpace.Gamma)
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                string report = CaptureDynCast(Arg("-lgDynFixture") ?? Path.Combine(EvidenceDir, "dynCastChunk.json"),
                                               Arg("-lgOut") ?? Path.Combine(EvidenceDir, "stills"));
                Debug.Log("UOClient LiveGround dynamic cast evidence: wrote " + report);
            }
            catch (Exception e)
            {
                Debug.LogError("UOClient LiveGround dynamic cast evidence FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        public static string CaptureDynCast(string dynPath, string outDir)
        {
            DynFixture d = JsonUtility.FromJson<DynFixture>(File.ReadAllText(dynPath));
            string dir = Path.GetDirectoryName(dynPath);
            string basePath = Path.Combine(dir, d.baseFixture);
            ShadowFixture f = JsonUtility.FromJson<ShadowFixture>(File.ReadAllText(basePath));
            if (f.x0 != d.x0 || f.y0 != d.y0)
                throw new InvalidOperationException($"dynamic fixture chunk ({d.x0}, {d.y0}) is not the receive fixture's ({f.x0}, {f.y0})");
            int[] origTiles = f.mobTiles, origFoot = f.mobFootProbe, origHead = f.mobHeadProbe;
            f.mobTiles = d.casterA.Concat(d.casterB).Concat(d.receiver).ToArray(); // placements 0 = A, 1 = B, 2 = receiver
            Directory.CreateDirectory(outDir);
            var made = new List<Object>();
            var saved = new SavedGlobals();
            bool keywordWas = Shader.IsKeywordEnabled(SpriteKeyword);
            Texture savedCeil = Shader.GetGlobalTexture("_LGShadowCeil");
            Vector4 savedMap = Shader.GetGlobalVector("_LGSpriteShadowMap");
            float savedOn = Shader.GetGlobalFloat("_LGCeilOn");
            RenderTexture rt = null;
            try
            {
                Shader terrain = Need(TerrainShader), sprite = Need(SpriteShader);
                Texture2D ground = Track(made, Png(Path.Combine(dir, f.groundPng), FilterMode.Point));
                Texture2D atlas = Track(made, Png(Path.Combine(dir, f.atlasPng), FilterMode.Point));
                Texture2D mask = Track(made, MaskTexture(f));
                Texture2D Ceil(float[] c) => Track(made, CeilingTexture(f, c));
                Texture2D cStatic = Ceil(d.ceilStatic), cA = Ceil(d.ceilCastA), cB = Ceil(d.ceilCastB), cRemoved = Ceil(d.ceilRemoved),
                    cMargin = Ceil(d.ceilMargin), cMarginA = Ceil(d.ceilMarginCastA);

                Material terrainMat = Track(made, new Material(terrain));
                terrainMat.SetTexture("_MainTex", ground);
                terrainMat.SetTexture("_LGShadowTex", mask);
                Material spriteMat = Track(made, new Material(sprite) { mainTexture = atlas });

                GameObject groundGo = Track(made, Node("Dyn ground (terrain space)", Track(made, TerrainSpaceGround(f)), terrainMat, GroundLayer));
                GameObject planeGo = Track(made, Node("Dyn ground (screen plane)", Track(made, RecvGround(f)), terrainMat, GroundLayer));
                planeGo.SetActive(false);
                GameObject staticsGo = Track(made, Node("Dyn statics", Track(made, RecvSprites(f, -1, true)), spriteMat, SpriteLayer));
                GameObject bodyA = Track(made, Node("Dyn caster A", Track(made, RecvSprites(f, 0, true)), spriteMat, SpriteLayer));
                GameObject bodyB = Track(made, Node("Dyn caster B", Track(made, RecvSprites(f, 1, true)), spriteMat, SpriteLayer));
                GameObject bodyR = Track(made, Node("Dyn receiver", Track(made, RecvSprites(f, 2, true)), spriteMat, SpriteLayer));

                int cx = 0, cy = f.originY - ((f.x0 + f.y0 + f.tiles) * 22 + 22) + 60;
                var camGo = Track(made, new GameObject("LiveGround dyncast camera") { hideFlags = HideFlags.HideAndDontSave });
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
                Matrix4x4 toUnity = TerrainToUnity(f);

                void Globals(Vector4 ambient, Vector4 color, Vector4 pos, Texture ceiling)
                {
                    RecvGlobals(f, ambient, color, pos, ceiling);
                    Shader.SetGlobalMatrix("_LGTerrainToUnity", toUnity); // the ground is in terrain space here
                }
                Texture2D Render(bool keyword, bool ceilOn, Texture ceiling)
                {
                    Shader.SetGlobalTexture("_LGShadowCeil", ceiling);
                    Shader.SetGlobalFloat("_LGCeilOn", ceilOn ? 1f : 0f);
                    if (keyword) Shader.EnableKeyword(SpriteKeyword); else Shader.DisableKeyword(SpriteKeyword);
                    Shader.SetGlobalMatrix("_LGTerrainToUnity", planeGo.activeSelf ? Matrix4x4.identity : toUnity);
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.cullingMask = 1 << GroundLayer;
                    Matrix4x4 view = cam.worldToCameraMatrix;
                    if (!planeGo.activeSelf)
                    {
                        cam.cullingMatrix = cam.projectionMatrix * view; // the ground's bounds are unbounded; cull as the plain camera
                        cam.worldToCameraMatrix = view * toUnity;        // the presenter's terrain camera: terrain space through the UO projection
                    }
                    cam.Render();
                    cam.ResetWorldToCameraMatrix();
                    cam.ResetCullingMatrix();
                    cam.clearFlags = CameraClearFlags.Depth;
                    cam.cullingMask = 1 << SpriteLayer;
                    cam.Render();
                    Shader.DisableKeyword(SpriteKeyword);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var t = new Texture2D(RecvSize, RecvSize, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    t.ReadPixels(new Rect(0, 0, RecvSize, RecvSize), 0, 0);
                    t.Apply();
                    RenderTexture.active = prev;
                    return Track(made, t);
                }
                var shots = new List<(string File, string What)>();
                Texture2D Save(Texture2D t, string file, string what)
                {
                    File.WriteAllBytes(Path.Combine(outDir, file), t.EncodeToPNG());
                    shots.Add((file, what));
                    return t;
                }
                void Show(bool a, bool b, bool r) { bodyA.SetActive(a); bodyB.SetActive(b); bodyR.SetActive(r); }

                Vector4 day = Vector4.one, none = Vector4.zero;
                Globals(day, none, Vector4.zero, cStatic);

                // ---- 0. The ground path: terrain space through the presenter's rows vs the receive capture's screen plane ----
                Show(false, false, false);
                Texture2D tsOff = Render(true, false, cStatic);
                groundGo.SetActive(false); planeGo.SetActive(true);
                Texture2D planeOff = Render(true, false, cStatic);
                planeGo.SetActive(false); groundGo.SetActive(true);
                Diff eqGround = Compare(tsOff, planeOff);
                // The terrain now also reads the world ceiling: with the chunk's own (static) ceiling only it agrees with its mask.
                Texture2D tsOn = Render(true, true, cStatic);
                Diff ownCeil = Compare(tsOff, tsOn);

                // ---- 1. Dynamic cast: a real body at A casting, moved to B, removed; a second body (receiver) east of it ----
                Show(true, false, true);
                Texture2D sStatic = Save(Render(true, true, cStatic), "dynacast_static.png", $"caster body at A {T3(d.casterA)} + receiver at {T3(d.receiver)}; ceiling WITHOUT the caster (static only)");
                Texture2D sA = Save(Render(true, true, cA), "dynacast_castA.png", $"same, ceiling with the mobile column at A (dynamic moving approximation, {d.mobileHeight} Z)");
                Diff castA = Compare(sStatic, sA);
                int darkerA = CountDarker(sStatic, sA);
                Save(Track(made, DiffImage(sStatic, sA, 4)), "dynacast_castA_diff_x4.png", "|castA - static| x4");
                Save(Track(made, Zoom(f, 0, sStatic, sA)), "dynacast_castA_zoom.png", "x4 crop around A: left static ceiling, right with the caster column");
                (string Foot, string Head, float FootRatio, float HeadRatio) probeA = ReceiverProbes(f, origTiles, origFoot, origHead, sStatic, sA, cx, cy, atlas);
                Show(false, true, true);
                Texture2D sBStatic = Render(true, true, cStatic);
                Texture2D sB = Save(Render(true, true, cB), "dynacast_castB.png", $"caster moved to B {T3(d.casterB)}; ceiling recomposed (A's column gone)");
                Diff castB = Compare(sBStatic, sB);
                (string Foot, string Head, float FootRatio, float HeadRatio) probeB = ReceiverProbes(f, origTiles, origFoot, origHead, sBStatic, sB, cx, cy, atlas);
                Show(false, false, true);
                Texture2D sRemovedRef = Render(true, true, cStatic);
                Texture2D sRemoved = Save(Render(true, true, cRemoved), "dynacast_removed.png", "caster gone (not drawn for a frame: swept); receiver only");
                Diff removed = Compare(sRemovedRef, sRemoved);

                // ---- 2. Light coherence: night + the player's light at the receiver; the sun shadow dims only the ambient ----
                Show(true, false, true);
                Vector2 lp = FootUnity(f, 2);
                Globals(new Vector4(0.32f, 0.34f, 0.48f, 1f), new Vector4(0.9f, 0.75f, 0.5f, 1f), new Vector4(lp.x, lp.y, 140f, 0f), cStatic);
                Texture2D nStatic = Render(true, true, cStatic);
                Texture2D nA = Save(Render(true, true, cA), "dynacast_night_castA.png", "night ambient (0.32, 0.34, 0.48) + player light r=140 px at the receiver, caster at A");
                Diff nightCast = Compare(nStatic, nA);
                (string Foot, string Head, float FootRatio, float HeadRatio) probeNight = ReceiverProbes(f, origTiles, origFoot, origHead, nStatic, nA, cx, cy, atlas);
                Globals(day, none, Vector4.zero, cStatic);

                // ---- 3. Cross-chunk margin: the real west/north/north-west neighbours' heights and columns ----
                Show(false, false, false);
                Texture2D mOff = Save(Render(true, true, cStatic), "margin_off.png", "chunk alone (neighbours not loaded: their rays lit, the old seam)");
                Texture2D mOn = Save(Render(true, true, cMargin), "margin_on.png", $"same chunk with its {d.marginNeighbours} real neighbours loaded: their shadow continues across the west/north seams");
                Diff margin = Compare(mOff, mOn);
                int marginDarker = CountDarker(mOff, mOn);
                Save(Track(made, DiffImage(mOff, mOn, 4)), "margin_diff_x4.png", "|margin on - off| x4");
                Show(true, false, true);
                Save(Render(true, true, cMarginA), "margin_castA.png", "neighbours loaded + caster at A + receiver");

                // ---- 4. Keyword off: still the production sprite path (the receive capture's 4aa51a6 check covers the text) ----
                Texture2D kOff = Render(false, false, cMarginA), kOffNoCeil = Render(false, false, cStatic);
                Diff keyOff = Compare(kOff, kOffNoCeil);

                // ---- 5. Upload cost of the ceiling texture on this machine (CPU side; the GPU copy itself is not timed) ----
                string upload = UploadTiming(made);

                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
                var md = new StringBuilder();
                md.AppendLine("# LiveGroundCompare dynamic cast + cross-chunk margin evidence (isolated fixture, TEST only, not logged in)");
                md.AppendLine();
                md.AppendLine($"- Captured: {now}; Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}; color space {QualitySettings.activeColorSpace}; batch mode {Application.isBatchMode}");
                md.AppendLine($"- Fixture: `{dynPath}` on `{basePath}`: map {d.map} chunk ({d.x0}, {d.y0}); {d.source}; {f.source}");
                md.AppendLine($"- Caster A {T3(d.casterA)} -> B {T3(d.casterB)}; receiver {T3(d.receiver)}; mobile column {d.mobileHeight} Z (TazUO DEFAULT_CHARACTER_HEIGHT); texels raised by A {d.castATexels}, by B {d.castBTexels}; margin texels raised {d.marginTexels} by {d.marginNeighbours} real neighbours");
                md.AppendLine("- Ground drawn in terrain space through LiveGroundLighting.TerrainToUnityRows (the presenter's camera rows), so TerrainLit samples the world ceiling at its own tile point; sprites as the receive capture (one world pixel per output pixel). Bodies are a real body-400 stand frame.");
                md.AppendLine();
                md.AppendLine("## Ground path");
                md.AppendLine($"- Terrain-space ground vs the receive capture's screen-plane ground (ceiling off): {eqGround}");
                md.AppendLine($"- Terrain reading the chunk's own (static) world ceiling vs its mask only: {ownCeil}");
                md.AppendLine();
                md.AppendLine("## Dynamic cast (mobile column, labelled approximation, not a shadow map)");
                md.AppendLine($"- Static vs caster at A: {castA}; {darkerA} px darker");
                md.AppendLine($"- Receiver at A: foot {probeA.Foot}; head {probeA.Head}");
                md.AppendLine($"- Static vs caster at B: {castB}; receiver foot {probeB.Foot}; head {probeB.Head}");
                md.AppendLine($"- Removed (swept) vs static, receiver only: {removed} (0 = no stale shadow)");
                md.AppendLine();
                md.AppendLine("## Light coherence");
                md.AppendLine($"- Night + player light at the receiver, static vs caster at A: {nightCast}; receiver foot {probeNight.Foot}; head {probeNight.Head}");
                md.AppendLine($"  (day foot ratio {probeA.FootRatio:0.000} vs night-under-the-lamp {probeNight.FootRatio:0.000}: the sun shadow dims only the ambient, the player's light is not shadowed by it)");
                md.AppendLine();
                md.AppendLine("## Cross-chunk margin");
                md.AppendLine($"- Chunk alone vs with its loaded neighbours: {margin}; {marginDarker} px darker");
                md.AppendLine();
                md.AppendLine("## Keyword off");
                md.AppendLine($"- Keyword off, _LGCeilOn 0: margin+cast ceiling vs static ceiling bound: {keyOff} (0 = the production path ignores the ceiling)");
                md.AppendLine();
                md.AppendLine("## Upload (SpriteShadowCeiling, 1024^2 RHalf)");
                md.AppendLine(upload);
                md.AppendLine();
                md.AppendLine("## Stills");
                foreach (var s in shots)
                    md.AppendLine($"- `{s.File}`: {s.What} (sha256 {Sha(Path.Combine(outDir, s.File))})");
                md.AppendLine();
                md.AppendLine("Limits: one chunk drawn (the neighbours' geometry is not drawn, only their CPU heights/columns cast into it); a mobile is a 16 Z column on its foot tile (tile-snapped, no silhouette, no self-shadow on its own tile); items cast by tiledata height like map statics; still a CPU ceiling, not a Unity shadow map; no FPS figures (CPU per-frame cost is measured in DotNet: see dynacast.md / LiveGroundDynCastFixtureTests).");
                string report = Path.Combine(outDir, "dynacast_capture.md");
                File.WriteAllText(report, md.ToString().Replace("\r\n", "\n"));
                Debug.Log($"LGDYNCAST eqGround {eqGround}; ownCeil {ownCeil}; castA {castA} darker {darkerA}; probeA foot {probeA.FootRatio:0.000} head {probeA.HeadRatio:0.000}; castB {castB}; removed {removed}; night {nightCast} foot {probeNight.FootRatio:0.000}; margin {margin} darker {marginDarker}; keyOff {keyOff}; {upload.Replace("\n", " ")}");
                return report;
            }
            finally
            {
                if (keywordWas) Shader.EnableKeyword(SpriteKeyword); else Shader.DisableKeyword(SpriteKeyword);
                Shader.SetGlobalTexture("_LGShadowCeil", savedCeil);
                Shader.SetGlobalVector("_LGSpriteShadowMap", savedMap);
                Shader.SetGlobalFloat("_LGCeilOn", savedOn);
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

        private static string T3(int[] t) => $"({t[0]}, {t[1]}, {t[2]})";

        /// <summary>As SpriteShadowCeiling: RHalf, Repeat, bilinear, the chunk's block at its wrapped slot.</summary>
        private static Texture2D CeilingTexture(ShadowFixture f, float[] block)
        {
            int size = f.wrapTiles * f.shadowTexels, n = f.tiles * f.shadowTexels;
            var texels = new ushort[size * size];
            ushort empty = Mathf.FloatToHalf(f.noCeiling);
            for (int i = 0; i < texels.Length; i++)
                texels[i] = empty;
            int c0 = f.x0 % f.wrapTiles * f.shadowTexels, r0 = f.y0 % f.wrapTiles * f.shadowTexels;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    texels[(r0 + r) * size + c0 + c] = Mathf.FloatToHalf(block[r * n + c]);
            var t = new Texture2D(size, size, TextureFormat.RHalf, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            t.SetPixelData(texels, 0);
            t.Apply(false, false);
            return t;
        }

        /// <summary>LiveGroundLighting.TerrainToUnityRows(1, ToUnity(0, 0)) with the receive capture's ToUnity, depth fixed at 500.</summary>
        private static Matrix4x4 TerrainToUnity(ShadowFixture f)
        {
            const float h = 22f, zs = 4f;
            float hz = 1f / f.heightUnitPerZ;
            var m = Matrix4x4.zero;
            m.SetRow(0, new Vector4(h, 0f, h, -f.originX));
            m.SetRow(1, new Vector4(-h, zs * hz, h, f.originY));
            m.SetRow(2, new Vector4(0f, 0f, 0f, 500f));
            m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            return m;
        }

        /// <summary>The chunk's corner grid in terrain space (x, Z·HeightUnitPerZ, -y), as the Terrain backend's surface.</summary>
        private static Mesh TerrainSpaceGround(ShadowFixture f)
        {
            int n = f.tiles, c = n + 1;
            var v = new Vector3[c * c];
            var uv = new Vector2[c * c];
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    v[j * c + i] = new Vector3(f.x0 + i, f.cornerZ[j * c + i] * f.heightUnitPerZ, -(f.y0 + j));
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
            var m = new Mesh { name = "LiveGround dyncast ground", hideFlags = HideFlags.HideAndDontSave, vertices = v, uv = uv, triangles = tri.ToArray() };
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // drawn through a custom view matrix: never culled
            return m;
        }

        /// <summary>The receiver's foot and head probe pixels (the receive fixture's probe offsets moved to placement 2): off/on and the GPU ratio.</summary>
        private static (string Foot, string Head, float FootRatio, float HeadRatio) ReceiverProbes(ShadowFixture f, int[] origTiles, int[] origFoot, int[] origHead,
            Texture2D off, Texture2D on, int cx, int cy, Texture2D atlas)
        {
            int Left(int[] tiles, int p) => (tiles[p * 3] - tiles[p * 3 + 1]) * 22 - f.mobAnchorX;
            int Top(int[] tiles, int p) => (tiles[p * 3] + tiles[p * 3 + 1]) * 22 + 22 - tiles[p * 3 + 2] * 4 - f.mobAnchorY;
            int dl = Left(f.mobTiles, 2) - Left(origTiles, 0), dt = Top(f.mobTiles, 2) - Top(origTiles, 0);
            string One(int[] probe, out float ratio)
            {
                int px = probe[0] + dl, py = probe[1] + dt;
                Color32 a = Pixel(f, off, px, py, cx, cy), b = Pixel(f, on, px, py, cx, cy), art = AtlasPixel(f, atlas, 2, px, py);
                ratio = Ratio(a, b);
                return $"px ({px}, {py}) off {C(a)} on {C(b)} (body art {C(art)}): ratio {ratio:0.000}";
            }
            string foot = One(origFoot, out float fr), head = One(origHead, out float hr);
            return (foot, head, fr, hr);
        }

        /// <summary>CPU time of SpriteShadowCeiling's two upload paths after warmup; managed allocation as Unity's Mono reports it.</summary>
        private static string UploadTiming(List<Object> made)
        {
            const int size = 1024, block = 64, runs = 60, blocks = 4;
            var tex = Track(made, new Texture2D(size, size, TextureFormat.RHalf, false, true) { hideFlags = HideFlags.HideAndDontSave });
            var texels = new ushort[size * size];
            var staging = new Texture2D[blocks];
            for (int k = 0; k < blocks; k++)
                staging[k] = Track(made, new Texture2D(block, block, TextureFormat.RHalf, false, true) { hideFlags = HideFlags.HideAndDontSave });
            var small = new ushort[block * block];
            bool partial = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
            void Full() { tex.SetPixelData(texels, 0); tex.Apply(false, false); }
            void Partial()
            {
                for (int k = 0; k < blocks; k++)
                {
                    for (int r = 0; r < block; r++)
                        Array.Copy(texels, r * size + k * block, small, r * block, block);
                    staging[k].SetPixelData(small, 0);
                    staging[k].Apply(false, false);
                    Graphics.CopyTexture(staging[k], 0, 0, 0, 0, block, block, tex, 0, 0, k * block, 0);
                }
            }
            (double Ms, long Alloc) Time(Action a)
            {
                for (int i = 0; i < 5; i++) a();
                GL.Flush();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var w = Stopwatch.StartNew();
                for (int i = 0; i < runs; i++) a();
                w.Stop();
                return (w.Elapsed.TotalMilliseconds / runs, GC.GetAllocatedBytesForCurrentThread() - a0);
            }
            (double fullMs, long fullAlloc) = Time(Full);
            string p = "per-chunk copy unsupported here (full upload only)";
            if (partial)
            {
                (double partMs, long partAlloc) = Time(Partial);
                p = $"{blocks} changed chunks via 64^2 staging + Graphics.CopyTexture: {partMs:0.000} ms CPU per frame, {partAlloc} B allocated over {runs}";
            }
            return $"- Full SetPixelData + Apply (2 MB): {fullMs:0.000} ms CPU per upload, {fullAlloc} B allocated over {runs}\n- {p}\n- (after 5 warmup calls; CPU submit time on the main thread, not GPU time)";
        }

        // ------------------------------------------------------------------------------------------------------------
        // Water: genuine _Time across separate frames
        // ------------------------------------------------------------------------------------------------------------

        private const int WaterFrames = 5, ProbeLayer = 29;
        private const double WaterGapSeconds = 0.5, WaterTimeoutSeconds = 60;

        private const string ProbeShaderSource = @"Shader ""Hidden/LiveGroundCompare/TimeProbe""
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return float4(_Time.y, _Time.x, unity_DeltaTime.x, 1.0); }
            ENDCG
        }
    }
}";

        private sealed class WaterRun
        {
            public Fixture F;
            public string OutDir;
            public List<Object> Made = new List<Object>();
            public SavedGlobals Saved;
            public Camera Cam;
            public RenderTexture Rt, ProbeRt;
            public GameObject Sea, Probe, Ground;
            public double Start, Next;
            public readonly List<(int Frame, double Wall, float Time, float TimeX, float Delta, float Rt, Texture2D Tex, Diff Same)> Frames =
                new List<(int, double, float, float, float, float, Texture2D, Diff)>();
        }

        private static WaterRun _water;

        /// <summary>
        /// -executeMethod entry (no -quit): sets the scene up, then renders on later Editor update ticks (separate frames,
        /// at least <see cref="WaterGapSeconds"/> wall apart), each frame also probing the _Time.y Unity gave it; writes
        /// water_realtime_*.png and water_realtime.md and exits (1 on failure or timeout).
        /// </summary>
        public static void CaptureWaterRealtimeBatch()
        {
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lgGamma") >= 0 && PlayerSettings.colorSpace != ColorSpace.Gamma)
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                _water = WaterSetup(Arg("-lgFixture") ?? Path.Combine(EvidenceDir, "realWaterChunk.json"), Arg("-lgOut") ?? Path.Combine(EvidenceDir, "stills"));
                EditorApplication.update += WaterTick;
                Debug.Log("UOClient LiveGround water realtime: armed; frames render on later Editor update ticks");
            }
            catch (Exception e)
            {
                Debug.LogError("UOClient LiveGround water realtime evidence FAILED: " + e);
                WaterCleanup();
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        private static WaterRun WaterSetup(string fixturePath, string outDir)
        {
            var w = new WaterRun { F = JsonUtility.FromJson<Fixture>(File.ReadAllText(fixturePath)), OutDir = outDir, Saved = new SavedGlobals() };
            Directory.CreateDirectory(outDir);
            Fixture f = w.F;
            Shader water = Need(WaterShader), terrain = Need(TerrainShader);
            Shader probe = ShaderUtil.CreateShaderAsset(ProbeShaderSource);
            if (probe == null || ShaderUtil.ShaderHasError(probe) || !probe.isSupported)
                throw new InvalidOperationException("time probe shader did not compile");
            probe.hideFlags = HideFlags.HideAndDontSave;
            w.Made.Add(probe);
            Mesh ground = Track(w.Made, GroundMesh(f)), sea = Track(w.Made, WaterMesh(f));
            Material terrainMat = Track(w.Made, new Material(terrain));
            terrainMat.SetTexture("_MainTex", Track(w.Made, ArtTexture(f)));
            terrainMat.SetTexture("_LGShadowTex", Track(w.Made, SunTexture(f)));
            Material waterMat = Track(w.Made, new Material(water)); // the shipped shader: Unity's own _Time
            waterMat.SetVector("_EyeDir", new Vector4(EyeDir.x, EyeDir.y, EyeDir.z, 0f));
            w.Ground = Track(w.Made, Node("Water RT ground", ground, terrainMat, GroundLayer));
            w.Sea = Track(w.Made, Node("Water RT sea", sea, waterMat, GroundLayer));

            Vector3 center = ground.bounds.center;
            var camGo = Track(w.Made, new GameObject("LiveGround water realtime camera") { hideFlags = HideFlags.HideAndDontSave });
            w.Cam = camGo.AddComponent<Camera>();
            w.Cam.enabled = false;
            w.Cam.orthographic = true;
            w.Cam.orthographicSize = OrthoSize;
            w.Cam.nearClipPlane = 1f;
            w.Cam.farClipPlane = 200f;
            w.Cam.allowHDR = false;
            w.Cam.allowMSAA = false;
            w.Cam.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 1f);
            camGo.transform.position = center + EyeDir * 60f;
            camGo.transform.rotation = Quaternion.LookRotation(-EyeDir, Vector3.up);

            // The probe: a quad filling the view, drawn by the same camera into a float target.
            var quad = new Mesh
            {
                name = "LiveGround time probe", hideFlags = HideFlags.HideAndDontSave,
                vertices = new[] { new Vector3(-50, -50, 0), new Vector3(-50, 50, 0), new Vector3(50, 50, 0), new Vector3(50, -50, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            Track(w.Made, quad);
            w.Probe = Track(w.Made, Node("Water RT probe", quad, Track(w.Made, new Material(probe)), ProbeLayer));
            w.Probe.transform.position = center + EyeDir * 30f;
            w.Probe.transform.rotation = camGo.transform.rotation;

            w.Rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { antiAliasing = 1 };
            w.Rt.Create();
            w.ProbeRt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            w.ProbeRt.Create();
            float cx = f.x0 + f.tiles / 2f, cy = f.y0 + f.tiles / 2f;
            SetGlobals(f, Vector4.one, Vector4.zero, new Vector4(cx, cy, 1f, 0f));
            w.Start = EditorApplication.timeSinceStartup;
            w.Next = w.Start; // the first tick after arming renders frame 0
            return w;
        }

        private static void WaterTick()
        {
            WaterRun w = _water;
            if (w == null)
                return;
            double now = EditorApplication.timeSinceStartup;
            try
            {
                if (now - w.Start > WaterTimeoutSeconds)
                    throw new TimeoutException($"only {w.Frames.Count} of {WaterFrames} frames in {WaterTimeoutSeconds} s");
                if (now < w.Next)
                    return;
                w.Next = now + WaterGapSeconds;

                // The probe first: _Time.y as this frame's camera render sees it.
                w.Cam.targetTexture = w.ProbeRt;
                w.Cam.clearFlags = CameraClearFlags.SolidColor;
                w.Cam.cullingMask = 1 << ProbeLayer;
                w.Cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = w.ProbeRt;
                var pt = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };
                pt.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
                pt.Apply();
                Color pc = pt.GetPixel(1, 1);
                Object.DestroyImmediate(pt);
                RenderTexture.active = prev;

                w.Cam.targetTexture = w.Rt;
                Texture2D t = Track(w.Made, RenderPass(w.Cam, false));
                Texture2D again = RenderPass(w.Cam, false); // same frame, rendered again
                Diff same = Compare(t, again);
                Object.DestroyImmediate(again);
                w.Frames.Add((Time.frameCount, now - w.Start, pc.r, pc.g, pc.b, Time.realtimeSinceStartup, t, same));
                if (w.Frames.Count < WaterFrames)
                    return;

                EditorApplication.update -= WaterTick;
                string report = WaterReport(w);
                Debug.Log("UOClient LiveGround water realtime evidence: wrote " + report);
                WaterCleanup();
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                EditorApplication.update -= WaterTick;
                Debug.LogError("UOClient LiveGround water realtime evidence FAILED: " + e);
                WaterCleanup();
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        private static string WaterReport(WaterRun w)
        {
            var md = new StringBuilder();
            md.AppendLine("# LiveGroundCompare water: Unity _Time across separate frames (isolated fixture, TEST only, not logged in)");
            md.AppendLine();
            md.AppendLine($"- Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}; Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}; batch mode {Application.isBatchMode}; process {Process.GetCurrentProcess().Id}");
            md.AppendLine($"- Shipped `LiveGroundCompare/Water` (no override, no in-memory copy) on map {w.F.map} chunk ({w.F.x0}, {w.F.y0}); a probe material drawn by the same camera writes the _Time.y / _Time.x / unity_DeltaTime.x that frame's render got into a float target.");
            md.AppendLine($"- Each frame is a separate Editor update tick (EditorApplication.update), >= {WaterGapSeconds:0.0} s wall apart; within a frame the water is rendered twice (must be 0 px apart).");
            md.AppendLine();
            md.AppendLine("| frame | Time.frameCount | wall since arm (s) | probe _Time.y | probe _Time.x | unity_DeltaTime.x | Time.realtimeSinceStartup | same-frame repeat | vs previous frame |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int moved = 0;
            bool timeAdvanced = true;
            for (int k = 0; k < w.Frames.Count; k++)
            {
                var fr = w.Frames[k];
                string file = $"water_realtime_{k}.png";
                File.WriteAllBytes(Path.Combine(w.OutDir, file), fr.Tex.EncodeToPNG());
                string vs = "-";
                if (k > 0)
                {
                    Diff dd = Compare(w.Frames[k - 1].Tex, fr.Tex);
                    vs = dd.ToString();
                    if (dd.Changed > 0) moved++;
                    timeAdvanced &= fr.Time > w.Frames[k - 1].Time;
                }
                md.AppendLine($"| {k} (`{file}`, sha256 {Sha(Path.Combine(w.OutDir, file))}) | {fr.Frame} | {fr.Wall:0.000} | {fr.Time:0.0000} | {fr.TimeX:0.0000} | {fr.Delta:0.0000} | {fr.Rt:0.000} | {fr.Same.Changed} px | {vs} |");
            }
            File.WriteAllBytes(Path.Combine(w.OutDir, "water_realtime_first_last_diff_x8.png"), DiffImage(w.Frames[0].Tex, w.Frames[w.Frames.Count - 1].Tex, 8).EncodeToPNG());
            md.AppendLine();
            string verdict = timeAdvanced && moved == w.Frames.Count - 1
                ? $"_Time.y advanced on every frame ({w.Frames[0].Time:0.000} -> {w.Frames[w.Frames.Count - 1].Time:0.000} s) and the shipped water's pixels changed on all {moved} frame steps"
                : $"NOT shown: _Time.y advanced on every frame = {timeAdvanced}; frame steps with changed pixels {moved}/{w.Frames.Count - 1}";
            md.AppendLine($"- Verdict: {verdict}.");
            md.AppendLine("- `water_realtime_first_last_diff_x8.png`: |last - first| x8.");
            md.AppendLine("- Limits: Editor batch frames (EditorApplication.update), not the player loop of a live session; one chunk; the probe proves the value Unity bound for _Time on each render, the pixel deltas that the shipped shader responds to it.");
            string report = Path.Combine(w.OutDir, "water_realtime.md");
            File.WriteAllText(report, md.ToString().Replace("\r\n", "\n"));
            Debug.Log($"LGWATERTIME frames {w.Frames.Count}; times {string.Join(", ", w.Frames.Select(x => x.Time.ToString("0.0000")))}; frameCounts {string.Join(", ", w.Frames.Select(x => x.Frame))}; moved {moved}; same-frame {string.Join(", ", w.Frames.Select(x => x.Same.Changed))}; {verdict}");
            return report;
        }

        private static void WaterCleanup()
        {
            WaterRun w = _water;
            _water = null;
            if (w == null)
                return;
            w.Saved?.Restore();
            RenderTexture.active = null;
            foreach (RenderTexture r in new[] { w.Rt, w.ProbeRt })
                if (r != null)
                {
                    r.Release();
                    Object.DestroyImmediate(r);
                }
            for (int i = w.Made.Count - 1; i >= 0; i--)
                if (w.Made[i] != null)
                    Object.DestroyImmediate(w.Made[i]);
        }
    }
}
