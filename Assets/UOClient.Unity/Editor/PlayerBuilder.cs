using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UOClient.Unity;

namespace UOClient.Editor
{
    /// <summary>
    /// Builds the Windows player ("Unity UO") from the world demo scene into Builds/Windows, and writes the
    /// paths file that points the player back at this project for baked art and the account file.
    /// </summary>
    public static class PlayerBuilder
    {
        public const string ProductName = "Unity UO";
        public const string OutputFolder = "Builds/Windows";

        /// <summary>Touch this file (relative to the project) to have the editor build the player; the result goes to build-result.txt.</summary>
        public const string BuildRequestFile = "Library/UOClient/build-player";

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [MenuItem("UOClient/Build Windows Player (Unity UO)")]
        public static void BuildFromMenu()
        {
            string result = Build();
            EditorUtility.DisplayDialog(ProductName, result, "OK");
        }

        public static string Build()
        {
            if (!File.Exists(WorldDemoLauncher.ScenePath))
                WorldDemoLauncher.CreateScene();

            PlayerSettings.productName = ProductName;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // keep the connection and the frame loop alive when unfocused

            string outDir = Path.Combine(ProjectRoot, OutputFolder);
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { WorldDemoLauncher.ScenePath },
                locationPathName = Path.Combine(outDir, ProductName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(outDir, WorldDemo.PathsFileName),
                    JsonUtility.ToJson(new PathsFile { projectRoot = ProjectRoot.Replace('\\', '/') }, true));
            }

            string text = $"{s.result}: {s.outputPath}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:F0} s, {s.totalErrors} errors, {s.totalWarnings} warnings";
            Debug.Log("UOClient: build " + text);
            return text;
        }

        [System.Serializable]
        private sealed class PathsFile
        {
            public string projectRoot;
        }

        private static double _nextPoll;
        private static bool _refreshed;

        [InitializeOnLoadMethod]
        private static void WatchForRequests()
        {
            EditorApplication.update -= BuildIfRequested;
            EditorApplication.update += BuildIfRequested;
        }

        private static void BuildIfRequested()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;

            string request = Path.Combine(ProjectRoot, BuildRequestFile);
            if (!File.Exists(request) || EditorApplication.isCompiling)
                return;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false; // builds need edit mode; handled on a later poll
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!_refreshed)
            {
                _refreshed = true; // import changed scripts before building (see WorldDemoLauncher)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            _refreshed = false;
            File.Delete(request);

            EditorApplication.delayCall += () =>
            {
                string result = Build();
                File.WriteAllText(Path.Combine(ProjectRoot, "Library", "UOClient", "build-result.txt"), result);
            };
        }
    }
}
