using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UOClient.LiveGroundCompare.Editor
{
    /// <summary>
    /// Water sun-ceiling receive evidence (TEST only). Explicit invocation only (menu or -executeMethod); nothing runs on
    /// load, enters Play or touches the open scene; every global is put back in finally.
    ///
    /// Evidence~/waterRecvChunk.json (DotNet LiveGroundWaterRecvFixtureTests: the coastal chunk of realWaterChunk.json, its
    /// own CPU ceiling and the same with a mobile-height column in open water) is bound as the presenter binds
    /// SpriteShadowCeiling's texture (_LGShadowCeil, _LGSpriteShadowMap.w, _LGCeilOn); ground and water are drawn in terrain
    /// space as in the Phase C capture, so both shaders read the ceiling at their own tile point.
    ///
    /// Equivalence: the 83c0a85 water text (Evidence~/LiveGroundCompareWater.83c0a85.shader.txt, the shader before the
    /// receive) is compiled in memory and must render the same pixels as the shipped water with the ceiling off or empty.
    /// </summary>
    public static partial class LiveGroundEvidenceCapture
    {
        /// <summary>DotNet LiveGroundWaterRecvFixtureTests.WaterRecvFixture (same field names).</summary>
        [Serializable]
        public sealed class WaterRecvFixture
        {
            public string source, baseFixture;
            public int map, x0, y0, tiles, shadowTexels, wrapTiles, casterHeight, waterTexels, shadowedStatic, shadowedCast;
            public float heightUnitPerZ, noCeiling, softZ, biasZ;
            public int[] caster;
            public float[] probe;
            public float[] ceilStatic, ceilCast;
        }

        /// <summary>The ceiling globals SpriteShadowCeiling owns, saved and put back.</summary>
        internal sealed class CeilingGlobals
        {
            private readonly Texture _ceil = Shader.GetGlobalTexture("_LGShadowCeil");
            private readonly Vector4 _map = Shader.GetGlobalVector("_LGSpriteShadowMap");
            private readonly float _on = Shader.GetGlobalFloat("_LGCeilOn");

            public void Restore()
            {
                Shader.SetGlobalTexture("_LGShadowCeil", _ceil);
                Shader.SetGlobalVector("_LGSpriteShadowMap", _map);
                Shader.SetGlobalFloat("_LGCeilOn", _on);
            }
        }

        [MenuItem("UOClient/LiveGroundCompare/Capture water sun-ceiling receive stills (TEST)")]
        public static void CaptureWaterRecvMenu()
        {
            string report = CaptureWaterRecv(Path.Combine(EvidenceDir, "waterRecvChunk.json"), Path.Combine(EvidenceDir, "stills"));
            Debug.Log("UOClient LiveGround water receive evidence: wrote " + report);
        }

        /// <summary>-executeMethod entry: optional -lgWaterRecv &lt;json&gt; and -lgOut &lt;dir&gt;; exits the Editor (1 on failure).</summary>
        public static void CaptureWaterRecvBatch()
        {
            int code = 0;
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lgGamma") >= 0 && PlayerSettings.colorSpace != ColorSpace.Gamma)
                    PlayerSettings.colorSpace = ColorSpace.Gamma;
                string report = CaptureWaterRecv(Arg("-lgWaterRecv") ?? Path.Combine(EvidenceDir, "waterRecvChunk.json"),
                                                 Arg("-lgOut") ?? Path.Combine(EvidenceDir, "stills"));
                Debug.Log("UOClient LiveGround water receive evidence: wrote " + report);
            }
            catch (Exception e)
            {
                Debug.LogError("UOClient LiveGround water receive evidence FAILED: " + e);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        /// <summary>As SpriteShadowCeiling: RHalf, Repeat, bilinear, the chunk's block at its wrapped slot; null = empty.</summary>
        private static Texture2D WaterCeilingTexture(WaterRecvFixture r, float[] block)
        {
            int size = r.wrapTiles * r.shadowTexels, n = r.tiles * r.shadowTexels;
            var texels = new ushort[size * size];
            ushort empty = Mathf.FloatToHalf(r.noCeiling);
            for (int i = 0; i < texels.Length; i++)
                texels[i] = empty;
            if (block != null)
            {
                int c0 = r.x0 % r.wrapTiles * r.shadowTexels, r0 = r.y0 % r.wrapTiles * r.shadowTexels;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        texels[(r0 + y) * size + c0 + x] = Mathf.FloatToHalf(block[y * n + x]);
            }
            var t = new Texture2D(size, size, TextureFormat.RHalf, false, true) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            t.SetPixelData(texels, 0);
            t.Apply(false, false);
            return t;
        }

        private static void BindCeiling(WaterRecvFixture r, Texture ceiling, bool on)
        {
            Shader.SetGlobalTexture("_LGShadowCeil", ceiling);
            Shader.SetGlobalVector("_LGSpriteShadowMap", new Vector4(0f, 0f, 1f, 1f / r.wrapTiles));
            Shader.SetGlobalFloat("_LGCeilOn", on ? 1f : 0f);
        }

        /// <summary>CaptureWaterRealtimeBatch with -lgWaterRecv: the cast ceiling live for every frame, waterrecv_realtime_* outputs.</summary>
        private static void WaterRealtimeReceive(WaterRun w, string recvPath)
        {
            WaterRecvFixture r = JsonUtility.FromJson<WaterRecvFixture>(File.ReadAllText(recvPath));
            if (r.x0 != w.F.x0 || r.y0 != w.F.y0)
                throw new InvalidOperationException($"water receive fixture chunk ({r.x0}, {r.y0}) is not the water fixture's ({w.F.x0}, {w.F.y0})");
            w.SavedCeiling = new CeilingGlobals();
            BindCeiling(r, Track(w.Made, WaterCeilingTexture(r, r.ceilCast)), true);
            w.Prefix = "waterrecv_realtime";
            w.Receive = $"_LGCeilOn 1, cast ceiling of `{Path.GetFileName(recvPath)}` (column {r.casterHeight} Z at ({r.caster[0]}, {r.caster[1]}, {r.caster[2]}))";
        }

        /// <summary>Mean of rgb at a terrain-space point as the camera sees it (3x3 box), 0..255.</summary>
        private static float Probe(Camera cam, Texture2D t, Vector3 world, out Vector2Int px)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            px = new Vector2Int(Mathf.RoundToInt(s.x), Mathf.RoundToInt(s.y));
            float sum = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    Color32 c = t.GetPixel(px.x + dx, px.y + dy);
                    sum += (c.r + c.g + c.b) / 3f;
                }
            return sum / 9f;
        }

        private static int CountDarkerPx(Texture2D off, Texture2D on)
        {
            Color32[] a = off.GetPixels32(), b = on.GetPixels32();
            int n = 0;
            for (int k = 0; k < a.Length; k++)
                if (b[k].r + b[k].g + b[k].b + 6 < a[k].r + a[k].g + a[k].b)
                    n++;
            return n;
        }

        public static string CaptureWaterRecv(string recvPath, string outDir)
        {
            WaterRecvFixture r = JsonUtility.FromJson<WaterRecvFixture>(File.ReadAllText(recvPath));
            string dir = Path.GetDirectoryName(recvPath);
            string basePath = Path.Combine(dir, r.baseFixture);
            Fixture f = JsonUtility.FromJson<Fixture>(File.ReadAllText(basePath));
            if (f.x0 != r.x0 || f.y0 != r.y0)
                throw new InvalidOperationException($"water receive fixture chunk ({r.x0}, {r.y0}) is not the water fixture's ({f.x0}, {f.y0})");
            Directory.CreateDirectory(outDir);
            var made = new List<Object>();
            var saved = new SavedGlobals();
            var savedCeil = new CeilingGlobals();
            RenderTexture rt = null;
            try
            {
                Shader waterShipped = Need(WaterShader), terrain = Need(TerrainShader);
                string include = WaterInclude(waterShipped);
                string oldSrc = File.ReadAllText(Path.Combine(dir, "LiveGroundCompareWater.83c0a85.shader.txt"));
                if (!oldSrc.Contains("float3 light = LGSpriteLight(i.world);") || oldSrc.Contains("LGCeilVisibility"))
                    throw new InvalidOperationException("reference water text is not the 83c0a85 (pre-receive) shader");
                Shader timedNow = Track(made, TimedWater(waterShipped)), timedOld = Track(made, TimedWater(oldSrc, include, "Hidden/LiveGroundCompare/Water83c0a85Time"));
                // The 83c0a85 text as is (Unity's own _Time, include inlined), for the untimed comparison.
                Shader rawOld = ShaderUtil.CreateShaderAsset(oldSrc.Replace("Shader \"LiveGroundCompare/Water\"", "Shader \"Hidden/LiveGroundCompare/Water83c0a85\"")
                                                                  .Replace("#include \"../LiveGroundLight.cginc\"", include));
                if (rawOld == null || ShaderUtil.ShaderHasError(rawOld) || !rawOld.isSupported)
                    throw new InvalidOperationException("83c0a85 water copy did not compile");
                rawOld.hideFlags = HideFlags.HideAndDontSave;
                Track(made, rawOld);

                Mesh ground = Track(made, GroundMesh(f)), sea = Track(made, WaterMesh(f));
                Material terrainMat = Track(made, new Material(terrain));
                terrainMat.SetTexture("_MainTex", Track(made, ArtTexture(f)));
                terrainMat.SetTexture("_LGShadowTex", Track(made, SunTexture(f)));
                Material Water(Shader s)
                {
                    Material m = Track(made, new Material(s));
                    m.SetVector("_EyeDir", new Vector4(EyeDir.x, EyeDir.y, EyeDir.z, 0f));
                    m.SetFloat("_LGEvidenceTime", T0);
                    return m;
                }
                Material mNow = Water(timedNow), mOld = Water(timedOld), mShipped = Water(waterShipped), mRawOld = Water(rawOld);
                GameObject groundGo = Track(made, Node("WaterRecv ground", ground, terrainMat, GroundLayer));
                GameObject seaGo = Track(made, Node("WaterRecv water", sea, mNow, GroundLayer));
                MeshRenderer seaR = seaGo.GetComponent<MeshRenderer>();

                Vector3 center = ground.bounds.center;
                var camGo = Track(made, new GameObject("LiveGround waterrecv camera") { hideFlags = HideFlags.HideAndDontSave });
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
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { antiAliasing = 1 };
                rt.Create();
                cam.targetTexture = rt;

                Texture2D cEmpty = Track(made, WaterCeilingTexture(r, null)), cStatic = Track(made, WaterCeilingTexture(r, r.ceilStatic)),
                    cCast = Track(made, WaterCeilingTexture(r, r.ceilCast));
                float cx = f.x0 + f.tiles / 2f, cy = f.y0 + f.tiles / 2f;
                Vector4 day = Vector4.one, none = Vector4.zero, dayPos = new Vector4(cx, cy, 1f, 0f);
                Vector4 night = new Vector4(0.32f, 0.34f, 0.48f, 1f), lamp = new Vector4(0.9f, 0.75f, 0.5f, 1f), lampPos = new Vector4(r.probe[0], r.probe[1], 4f, 0f);
                Vector3 probeWorld = new Vector3(r.probe[0], r.probe[2] * r.heightUnitPerZ, -r.probe[1]);

                Texture2D Render(Material water, Texture ceiling, bool on)
                {
                    seaR.sharedMaterial = water;
                    BindCeiling(r, ceiling, on);
                    return Track(made, RenderPass(cam, false));
                }
                var shots = new List<(string File, string What)>();
                Texture2D Save(Texture2D t, string file, string what)
                {
                    File.WriteAllBytes(Path.Combine(outDir, file), t.EncodeToPNG());
                    shots.Add((file, what));
                    return t;
                }

                // ---- 1. Equivalence: off / empty ceiling must be the 83c0a85 water ----
                SetGlobals(f, day, none, dayPos);
                Texture2D oldDay = Render(mOld, cCast, false), nowOffDay = Render(mNow, cCast, false), nowEmptyDay = Render(mNow, cEmpty, true);
                Diff eqOff = Compare(oldDay, nowOffDay), eqEmpty = Compare(oldDay, nowEmptyDay);
                Diff eqRaw = Compare(Render(mRawOld, cCast, false), Render(mShipped, cCast, false));
                SetGlobals(f, night, lamp, lampPos);
                Diff eqNight = Compare(Render(mOld, cCast, false), Render(mNow, cCast, false));
                SetGlobals(f, day, none, dayPos);

                // ---- 2. Receive on: static (terrain) ceiling, then the cast column in open water ----
                Texture2D off = Save(nowOffDay, "waterrecv_off.png", $"day, _LGCeilOn 0 (cast ceiling bound but off): the water as before, t = {T0:0.000}");
                Texture2D onStatic = Save(Render(mNow, cStatic, true), "waterrecv_static_on.png", "day, _LGCeilOn 1, the chunk's own terrain ceiling (no casting statics in this chunk)");
                Texture2D on = Save(Render(mNow, cCast, true), "waterrecv_on.png", $"day, _LGCeilOn 1, ceiling + a {r.casterHeight} Z column in open water at ({r.caster[0]}, {r.caster[1]}, {r.caster[2]})");
                Diff dStatic = Compare(off, onStatic), dCast = Compare(onStatic, on);
                int darker = CountDarkerPx(onStatic, on);
                Save(Track(made, DiffImage(onStatic, on, 4)), "waterrecv_diff_x4.png", "|cast on - static on| x4 (the column's shadow on the water)");
                groundGo.SetActive(false);
                Texture2D wOn = Render(mNow, cCast, true), wStatic = Render(mNow, cStatic, true);
                groundGo.SetActive(true);
                Diff waterOnly = Compare(wStatic, wOn);
                int waterDarker = CountDarkerPx(wStatic, wOn);
                float pOff = Probe(cam, off, probeWorld, out Vector2Int ppx), pOn = Probe(cam, on, probeWorld, out _);

                // ---- 3. Light: night ambient + the player's light at the shaded water; the shadow dims only the ambient ----
                SetGlobals(f, night, none, dayPos);
                Texture2D nOffDark = Render(mNow, cCast, false), nOnDark = Render(mNow, cCast, true);
                SetGlobals(f, night, lamp, lampPos);
                Texture2D nOff = Save(Render(mNow, cCast, false), "waterrecv_night_off.png", $"night ambient {V(night)} + player light {V(lamp)} r=4 tiles at the probe, _LGCeilOn 0");
                Texture2D nOn = Save(Render(mNow, cCast, true), "waterrecv_night_playerlight_on.png", "same, _LGCeilOn 1 with the cast ceiling");
                float pnOffDark = Probe(cam, nOffDark, probeWorld, out _), pnOnDark = Probe(cam, nOnDark, probeWorld, out _);
                float pnOff = Probe(cam, nOff, probeWorld, out _), pnOn = Probe(cam, nOn, probeWorld, out _);
                SetGlobals(f, day, none, dayPos);

                // ---- 4. Motion with the receive live: controlled shader time, then the same frame rendered again ----
                mNow.SetFloat("_LGEvidenceTime", T0);
                Texture2D t0 = Save(Render(mNow, cCast, true), "waterrecv_t0_on.png", $"receive on, _LGEvidenceTime = {T0:0.000} s");
                Texture2D t0Again = Render(mNow, cCast, true);
                mNow.SetFloat("_LGEvidenceTime", T1);
                Texture2D t1 = Save(Render(mNow, cCast, true), "waterrecv_t1_on.png", $"receive on, _LGEvidenceTime = {T1:0.000} s");
                Save(Track(made, DiffImage(t0, t1, 8)), "waterrecv_t0_t1_diff_x8.png", "|t1 - t0| x8, receive on");
                Diff motion = Compare(t0, t1), repeat = Compare(t0, t0Again);
                float pt1Off;
                {
                    Texture2D t1Off = Render(mNow, cCast, false);
                    pt1Off = Probe(cam, t1Off, probeWorld, out _);
                }
                float pt1On = Probe(cam, t1, probeWorld, out _);
                mNow.SetFloat("_LGEvidenceTime", T0);

                string Ratio(float a, float b) => a <= 0 ? "n/a" : (b / a).ToString("0.000");
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
                var md = new StringBuilder();
                md.AppendLine("# LiveGroundCompare water sun-ceiling receive evidence (isolated fixture, TEST only, not logged in)");
                md.AppendLine();
                md.AppendLine($"- Captured: {now}; Unity {Application.unityVersion}; {SystemInfo.graphicsDeviceType} / {SystemInfo.graphicsDeviceName}; color space {QualitySettings.activeColorSpace}; batch mode {Application.isBatchMode}");
                md.AppendLine($"- Fixture: `{recvPath}` on `{basePath}`: map {r.map} chunk ({r.x0}, {r.y0}); {f.waterTiles} water tiles ({r.waterTexels} ceiling texels); {r.source}");
                md.AppendLine($"- CPU (DotNet): water texels in shadow (visibility < 0.5): own ceiling {r.shadowedStatic}, with the column {r.shadowedCast}; probe water point ({r.probe[0]}, {r.probe[1]}) Z {r.probe[2]} -> pixel {ppx}");
                md.AppendLine($"- Ceiling bound as SpriteShadowCeiling binds it: {r.wrapTiles * r.shadowTexels}^2 RHalf, Repeat, bilinear, block at the chunk's wrapped slot, _LGSpriteShadowMap.w = 1/{r.wrapTiles}; camera as the Phase C capture (orthographic along -_EyeDir, NOT the presenter's oblique matrix).");
                md.AppendLine();
                md.AppendLine("## Equivalence (feature off / missing must be the 83c0a85 water)");
                md.AppendLine($"- 83c0a85 water (in-memory copy of Evidence~/LiveGroundCompareWater.83c0a85.shader.txt) vs shipped, _LGCeilOn 0 with the cast ceiling bound, day: {eqOff}");
                md.AppendLine($"- 83c0a85 vs shipped, _LGCeilOn 1 with an EMPTY ceiling (no loaded chunk), day: {eqEmpty}");
                md.AppendLine($"- 83c0a85 vs shipped, _LGCeilOn 0, night + player light: {eqNight}");
                md.AppendLine($"- Untimed (Unity's own _Time, no time patch): 83c0a85 text vs the shipped asset, _LGCeilOn 0: {eqRaw}");
                md.AppendLine();
                md.AppendLine("## Receive");
                md.AppendLine($"- Off vs on with the chunk's own terrain ceiling: {dStatic}");
                md.AppendLine($"- Own ceiling vs + the column in open water: {dCast}; {darker} px darker");
                md.AppendLine($"- Water alone (ground hidden), own ceiling vs + column: {waterOnly}; {waterDarker} px darker (the shadow is on the water itself)");
                md.AppendLine($"- Probe (3x3 mean rgb), day: off {pOff:0.0} -> on {pOn:0.0}: ratio {Ratio(pOff, pOn)} (ambient term to the 0.7 floor, sun glint removed)");
                md.AppendLine();
                md.AppendLine("## Light coherence (the sun shadow dims only the ambient; the player's light is not shadowed)");
                md.AppendLine($"- Night, no player light, probe: off {pnOffDark:0.0} -> on {pnOnDark:0.0}: ratio {Ratio(pnOffDark, pnOnDark)}");
                md.AppendLine($"- Night + player light at the probe: off {pnOff:0.0} -> on {pnOn:0.0}: ratio {Ratio(pnOff, pnOn)} (closer to 1: the lamp's share is not dimmed)");
                md.AppendLine();
                md.AppendLine("## Motion with the receive live");
                md.AppendLine($"- t0 = {T0:0.000} s vs t1 = {T1:0.000} s, _LGCeilOn 1: {motion}; same t0 twice: {repeat}");
                md.AppendLine($"- Probe at t1: off {pt1Off:0.0} -> on {pt1On:0.0}: ratio {Ratio(pt1Off, pt1On)} (the shadow stays while the waves move)");
                md.AppendLine("- Unity's own _Time across separate frames with the receive live: `waterrecv_realtime.md` (CaptureWaterRealtimeBatch -lgWaterRecv).");
                md.AppendLine();
                md.AppendLine("## Stills");
                foreach (var s in shots)
                    md.AppendLine($"- `{s.File}`: {s.What} (sha256 {Sha(Path.Combine(outDir, s.File))})");
                md.AppendLine();
                md.AppendLine("Limits: one chunk; a fixture column stands in for a mobile/boat (in game: whatever SpriteShadowCeiling holds); the water is shaded at its own surface point, not refracted or depth-aware; still a CPU ceiling, not a Unity shadow map; no FPS figures.");
                string report = Path.Combine(outDir, "waterrecv.md");
                File.WriteAllText(report, md.ToString().Replace("\r\n", "\n"));
                Debug.Log($"LGWATERRECV eqOff {eqOff}; eqEmpty {eqEmpty}; eqNight {eqNight}; eqRaw {eqRaw}; static {dStatic}; cast {dCast} darker {darker}; waterOnly {waterOnly} darker {waterDarker}; " +
                          $"probe day {pOff:0.0}->{pOn:0.0}; night {pnOffDark:0.0}->{pnOnDark:0.0}; lamp {pnOff:0.0}->{pnOn:0.0}; motion {motion}; repeat {repeat}");
                return report;
            }
            finally
            {
                savedCeil.Restore();
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
    }
}
