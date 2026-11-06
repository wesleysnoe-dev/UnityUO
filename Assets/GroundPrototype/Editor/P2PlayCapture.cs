using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UOClient.GroundPrototype;

namespace UOClient.GroundPrototype.Editor
{
    /// <summary>
    /// EXCEPTION for P2 acceptance screenshots. File-flag driven; phase on disk.
    /// Uses synchronous Camera.Render -> PNG (not async ScreenCapture) so toggles are visible.
    /// </summary>
    public static class P2PlayCapture
    {
        public const string RequestFile = "Library/UOClient/p2-play-capture";
        public const string StopFile = "Library/UOClient/p2-play-stop";
        public const string StateFile = "Library/UOClient/p2-play-capture-state.txt";
        public const string ResultFile = "Library/UOClient/p2-play-capture-result.txt";
        public const string SetupFile = "Library/UOClient/p2-play-prior-setup.json";
        public const string ScenePath = "Assets/GroundPrototype/Scenes/GroundCompare.unity";
        public const string WorldDemoPath = "Assets/Scenes/WorldDemo.unity";
        public const string ShotDir = "C:/Users/Mr.Smallz/Desktop/grok-bot-screenshots";

        private static double _nextPoll;

        [InitializeOnLoadMethod]
        private static void Watch()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static string Root => Path.GetDirectoryName(Application.dataPath);

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.25;
            if (EditorApplication.isCompiling) return;

            string stop = Path.Combine(Root, StopFile);
            if (File.Exists(stop))
            {
                File.Delete(stop);
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    File.WriteAllText(Path.Combine(Root, StateFile), "phase=restore\nphaseAt=" + NowInv());
                    return;
                }
                RestorePriorOrWorldDemo();
                ClearState();
                WriteResult("STOP restored edit mode");
                return;
            }

            string statePath = Path.Combine(Root, StateFile);
            string request = Path.Combine(Root, RequestFile);

            if (!File.Exists(statePath))
            {
                if (!File.Exists(request)) return;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.isDirty)
                    {
                        File.Delete(request);
                        WriteResult("ABORT dirty scene open: " + s.path);
                        return;
                    }
                }

                Directory.CreateDirectory(ShotDir);
                Directory.CreateDirectory(Path.Combine(Root, "Library/UOClient"));
                File.WriteAllText(Path.Combine(Root, SetupFile), ActiveScenePaths());
                File.Delete(request);

                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string day = Path.Combine(ShotDir, "GroundCompare-P2-DAY-side-" + stamp + ".png").Replace('\\', '/');
                string night = Path.Combine(ShotDir, "GroundCompare-P2-NIGHT-side-" + stamp + ".png").Replace('\\', '/');
                string lavaOff = Path.Combine(ShotDir, "GroundCompare-P2-DAY-lavaLightOff-" + stamp + ".png").Replace('\\', '/');

                File.WriteAllText(statePath,
                    "phase=enter\nphaseAt=" + NowInv() +
                    "\nday=" + day + "\nnight=" + night + "\nlavaOff=" + lavaOff + "\n");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
                Debug.Log("[GroundPrototype] P2PlayCapture: enter Play GroundCompare");
                return;
            }

            var st = ReadState(statePath);
            if (!st.ContainsKey("phase")) { ClearState(); return; }
            string phase = st["phase"];
            double phaseAt = ParseAt(st);
            double age = EditorApplication.timeSinceStartup - phaseAt;
            if (age < 0 || age > 600)
            {
                st["phaseAt"] = NowInv();
                WriteState(statePath, st);
                age = 0;
            }

            switch (phase)
            {
                case "enter":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 2.5) break;
                    ForceSideBySide();
                    ApplyVisuals(textures: true, fluids: true, lavaLight: true, night: false);
                    st["phase"] = "day";
                    st["phaseAt"] = NowInv();
                    WriteState(statePath, st);
                    break;

                case "day":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 1.0) break;
                    SyncCapture(st["day"], "DAY");
                    ApplyVisuals(textures: true, fluids: true, lavaLight: true, night: true);
                    st["phase"] = "night";
                    st["phaseAt"] = NowInv();
                    WriteState(statePath, st);
                    break;

                case "night":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 1.2) break;
                    SyncCapture(st["night"], "NIGHT");
                    ApplyVisuals(textures: true, fluids: true, lavaLight: false, night: false);
                    st["phase"] = "lavaoff";
                    st["phaseAt"] = NowInv();
                    WriteState(statePath, st);
                    break;

                case "lavaoff":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 1.0) break;
                    SyncCapture(st["lavaOff"], "LAVAOFF");
                    st["phase"] = "stop";
                    st["phaseAt"] = NowInv();
                    WriteState(statePath, st);
                    EditorApplication.isPlaying = false;
                    break;

                case "stop":
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) break;
                    if (age < 0.6) break;
                    st["phase"] = "restore";
                    st["phaseAt"] = NowInv();
                    WriteState(statePath, st);
                    break;

                case "restore":
                    if (EditorApplication.isPlayingOrWillChangePlaymode) break;
                    RestorePriorOrWorldDemo();
                    WriteResult("OK\nday=" + st["day"] + "\nnight=" + st["night"] + "\nlavaOff=" + st["lavaOff"]);
                    ClearState();
                    Debug.Log("[GroundPrototype] P2PlayCapture DONE");
                    break;
            }
        }

        private static void ForceSideBySide()
        {
            foreach (var cam in UnityEngine.Object.FindObjectsByType<IsoCompareCamera>(FindObjectsSortMode.None))
                cam.SetMode(CompareMode.SideBySide);
        }

        private static void ApplyVisuals(bool textures, bool fluids, bool lavaLight, bool night)
        {
            var togs = UnityEngine.Object.FindObjectsByType<VisualCompareToggles>(FindObjectsSortMode.None);
            Debug.Log($"[GroundPrototype] P2PlayCapture ApplyVisuals togCount={togs.Length} tex={textures} fluids={fluids} lava={lavaLight} night={night}");
            foreach (var t in togs)
            {
                t.SetTextures(textures);
                t.SetFluids(fluids);
                t.SetLavaLight(lavaLight);
                t.SetNight(night);
            }
            // Belt-and-suspenders: drive SharedLightRig directly
            foreach (var rig in UnityEngine.Object.FindObjectsByType<SharedLightRig>(FindObjectsSortMode.None))
                rig.SetNight(night);
        }

        private static void SyncCapture(string path, string tag)
        {
            // Composite left+right prototype cameras into one PNG (side-by-side).
            var cams = new List<Camera>();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (!c.enabled) continue;
                // Prototype compare cams only (skip unrelated)
                if (c.GetComponent<IsoCompareCamera>() != null || (c.name != null && c.name.Contains("Iso Compare")))
                    cams.Add(c);
            }
            cams.Sort((a, b) => a.rect.x.CompareTo(b.rect.x));

            int w = Mathf.Max(8, Screen.width);
            int h = Mathf.Max(8, Screen.height);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            // Clear
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0.06f, 0.06f, 0.07f, 1f));

            if (cams.Count == 0)
            {
                // Fallback: main camera full frame
                var main = Camera.main;
                if (main != null)
                {
                    var old = main.targetTexture;
                    var oldRect = main.rect;
                    main.rect = new Rect(0, 0, 1, 1);
                    main.targetTexture = rt;
                    main.Render();
                    main.targetTexture = old;
                    main.rect = oldRect;
                }
            }
            else
            {
                foreach (var cam in cams)
                {
                    var old = cam.targetTexture;
                    // Render each camera into its viewport region via full RT then... simpler: temporary full render with scissor via rect
                    // Unity Camera.rect with targetTexture uses normalized viewport of the RT.
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = old;
                }
            }

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            File.WriteAllBytes(path, png);
            Debug.Log($"[GroundPrototype] P2PlayCapture {tag} sync {path} bytes={png.Length} cams={cams.Count}");
        }

        private static string NowInv() => EditorApplication.timeSinceStartup.ToString(CultureInfo.InvariantCulture);

        private static double ParseAt(Dictionary<string, string> st)
        {
            if (!st.ContainsKey("phaseAt")) return 0;
            double.TryParse(st["phaseAt"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            return v;
        }

        private static void ClearState()
        {
            string p = Path.Combine(Root, StateFile);
            if (File.Exists(p)) File.Delete(p);
        }

        private static void WriteResult(string text) =>
            File.WriteAllText(Path.Combine(Root, ResultFile), text);

        private static string ActiveScenePaths()
        {
            var parts = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (!string.IsNullOrEmpty(s.path)) parts.Add(s.path);
            }
            return string.Join("\n", parts);
        }

        private static void RestorePriorOrWorldDemo()
        {
            string setup = Path.Combine(Root, SetupFile);
            string path = WorldDemoPath;
            if (File.Exists(setup))
            {
                foreach (var line in File.ReadAllLines(setup))
                    if (!string.IsNullOrWhiteSpace(line)) { path = line.Trim(); break; }
            }
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            EditorSceneManager.OpenScene(File.Exists(full) ? path : WorldDemoPath, OpenSceneMode.Single);
        }

        private static Dictionary<string, string> ReadState(string path)
        {
            var d = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            return d;
        }

        private static void WriteState(string path, Dictionary<string, string> d)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }
    }
}