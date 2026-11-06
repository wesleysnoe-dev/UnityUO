using System;
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
    /// EXCEPTION for P2 acceptance screenshots. File-flag driven; phase persisted to disk
    /// so domain reload on Play does not lose state. Aborts if any open scene is dirty.
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
            _nextPoll = EditorApplication.timeSinceStartup + 0.35;
            if (EditorApplication.isCompiling) return;

            string stop = Path.Combine(Root, StopFile);
            if (File.Exists(stop))
            {
                File.Delete(stop);
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    WriteResult("STOP requested; leaving Play. Restore on next idle tick.");
                    // leave a restore marker
                    File.WriteAllText(Path.Combine(Root, StateFile), "phase=restore\nphaseAt=" + Now());
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
                // Persist prior setup as scene path list (simple)
                File.WriteAllText(Path.Combine(Root, SetupFile), ActiveScenePaths());
                File.Delete(request);

                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string day = Path.Combine(ShotDir, "GroundCompare-P2-DAY-side-" + stamp + ".png").Replace('\\', '/');
                string night = Path.Combine(ShotDir, "GroundCompare-P2-NIGHT-side-" + stamp + ".png").Replace('\\', '/');
                string lavaOff = Path.Combine(ShotDir, "GroundCompare-P2-DAY-lavaLightOff-" + stamp + ".png").Replace('\\', '/');

                File.WriteAllText(statePath,
                    "phase=enter\nphaseAt=" + Now() +
                    "\nday=" + day + "\nnight=" + night + "\nlavaOff=" + lavaOff + "\n");

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
                Debug.Log("[GroundPrototype] P2PlayCapture: enter Play GroundCompare");
                return;
            }

            // Resume from persisted state (survives domain reload)
            var st = ReadState(statePath);
            if (!st.ContainsKey("phase")) { ClearState(); return; }
            string phase = st["phase"];
            double phaseAt = st.ContainsKey("phaseAt") ? double.Parse(st["phaseAt"], CultureInfo.InvariantCulture) : 0;
            double age = EditorApplication.timeSinceStartup - phaseAt;
            // timeSinceStartup resets on domain reload — use wall clock fallback via file mtime bump
            // Re-base phaseAt on first tick after reload if age negative or huge
            if (age < 0 || age > 600)
            {
                st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                WriteState(statePath, st);
                phaseAt = double.Parse(st["phaseAt"], CultureInfo.InvariantCulture);
                age = 0;
            }

            switch (phase)
            {
                case "enter":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 2.0) break;
                    var cam = FindCam();
                    if (cam != null) cam.SetMode(CompareMode.SideBySide);
                    var tog = FindTog();
                    if (tog != null)
                    {
                        tog.SetTextures(true);
                        tog.SetFluids(true);
                        tog.SetLavaLight(true);
                        tog.SetNight(false);
                    }
                    st["phase"] = "day";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                    WriteState(statePath, st);
                    break;

                case "day":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 0.8) break;
                    ScreenCapture.CaptureScreenshot(st["day"]);
                    Debug.Log("[GroundPrototype] P2PlayCapture DAY " + st["day"]);
                    st["phase"] = "tonight";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                    WriteState(statePath, st);
                    break;

                case "tonight":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 0.4) break;
                    var t2 = FindTog();
                    if (t2 != null) t2.SetNight(true);
                    st["phase"] = "night";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                    WriteState(statePath, st);
                    break;

                case "night":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 0.8) break;
                    ScreenCapture.CaptureScreenshot(st["night"]);
                    Debug.Log("[GroundPrototype] P2PlayCapture NIGHT " + st["night"]);
                    var t3 = FindTog();
                    if (t3 != null) { t3.SetNight(false); t3.SetLavaLight(false); }
                    st["phase"] = "lavaoff";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                    WriteState(statePath, st);
                    break;

                case "lavaoff":
                    if (!EditorApplication.isPlaying) break;
                    if (age < 0.8) break;
                    ScreenCapture.CaptureScreenshot(st["lavaOff"]);
                    Debug.Log("[GroundPrototype] P2PlayCapture LAVAOFF " + st["lavaOff"]);
                    st["phase"] = "stop";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
                    WriteState(statePath, st);
                    EditorApplication.isPlaying = false;
                    break;

                case "stop":
                    if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) break;
                    if (age < 0.6) break;
                    st["phase"] = "restore";
                    st["phaseAt"] = Now().ToString(CultureInfo.InvariantCulture);
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

        private static IsoCompareCamera FindCam() =>
            UnityEngine.Object.FindAnyObjectByType<IsoCompareCamera>();

        private static VisualCompareToggles FindTog() =>
            UnityEngine.Object.FindAnyObjectByType<VisualCompareToggles>();

        private static double Now() => EditorApplication.timeSinceStartup;

        private static void ClearState()
        {
            string p = Path.Combine(Root, StateFile);
            if (File.Exists(p)) File.Delete(p);
        }

        private static void WriteResult(string text)
        {
            File.WriteAllText(Path.Combine(Root, ResultFile), text);
        }

        private static string ActiveScenePaths()
        {
            var parts = new System.Collections.Generic.List<string>();
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
                var lines = File.ReadAllLines(setup);
                foreach (var line in lines)
                    if (!string.IsNullOrWhiteSpace(line)) { path = line.Trim(); break; }
            }
            if (File.Exists(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar))))
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            else
                EditorSceneManager.OpenScene(WorldDemoPath, OpenSceneMode.Single);
        }

        private static System.Collections.Generic.Dictionary<string, string> ReadState(string path)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            return d;
        }

        private static void WriteState(string path, System.Collections.Generic.Dictionary<string, string> d)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            File.WriteAllText(path, sb.ToString());
        }
    }
}