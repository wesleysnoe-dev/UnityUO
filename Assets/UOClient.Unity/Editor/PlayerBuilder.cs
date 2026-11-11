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

        /// <summary>Survives domain reload so the refresh-then-build handshake cannot drop the request.</summary>
        public const string BuildRefreshedFile = "Library/UOClient/build-refreshed";

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [MenuItem("UOClient/Build Windows Player (Unity UO)")]
        public static void BuildFromMenu()
        {
            string result = Build();
            EditorUtility.DisplayDialog(ProductName, result, "OK");
        }

        public static string Build() => Build(ProductName, OutputFolder);

        // ---- LiveGroundCompare TEST build: its own folder and product name; Builds/Windows is never touched ----

        public const string GroundCompareProductName = "Unity UO GroundCompare";
        public const string GroundCompareOutputFolder = "Builds/Windows-GroundCompare";

        /// <summary>Touch this file to have the editor build the GroundCompare TEST player; the result goes to build-groundcompare-result.txt.</summary>
        public const string GroundCompareRequestFile = "Library/UOClient/build-groundcompare";

        [MenuItem("UOClient/Build Windows Player (GroundCompare TEST)")]
        public static void BuildGroundCompareFromMenu()
        {
            string result = BuildGroundCompare();
            EditorUtility.DisplayDialog(GroundCompareProductName, result, "OK");
        }

        /// <summary>
        /// Builds the same scene as "Unity UO.exe" under a distinct product name and folder, then puts the project's product
        /// name back, in memory and on disk (BuildPlayer saves ProjectSettings.asset with the temporary name).
        /// </summary>
        public static string BuildGroundCompare()
        {
            string productName = PlayerSettings.productName;
            string settingsFile = Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset");
            byte[] settingsBefore = File.Exists(settingsFile) ? File.ReadAllBytes(settingsFile) : null;
            try
            {
                return Build(GroundCompareProductName, GroundCompareOutputFolder);
            }
            finally
            {
                PlayerSettings.productName = productName;
                if (settingsBefore != null)
                    File.WriteAllBytes(settingsFile, settingsBefore);
            }
        }

        // Called from BuildIfRequested's poll. Unlike the live build it never leaves Play mode: it waits for edit mode.
        private static void BuildGroundCompareIfRequested()
        {
            string request = Path.Combine(ProjectRoot, GroundCompareRequestFile);
            string refreshed = request + "-refreshed";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (!File.Exists(refreshed))
            {
                File.WriteAllText(refreshed, System.DateTime.UtcNow.ToString("o")); // survives the domain reload
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            try
            {
                File.WriteAllText(Path.Combine(ProjectRoot, "Library", "UOClient", "build-groundcompare-result.txt"), BuildGroundCompare());
                File.Delete(request);
            }
            finally
            {
                File.Delete(refreshed);
            }
        }

        private static string Build(string productName, string outputFolder)
        {
            if (!File.Exists(WorldDemoLauncher.ScenePath))
                WorldDemoLauncher.CreateScene();

            PlayerSettings.productName = productName;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // keep the connection and the frame loop alive when unfocused
            ApplyIcon();

            string outDir = Path.Combine(ProjectRoot, outputFolder);
            Directory.CreateDirectory(outDir);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { WorldDemoLauncher.ScenePath },
                locationPathName = Path.Combine(outDir, productName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(outDir, ClientPaths.PathsFileName),
                    JsonUtility.ToJson(new PathsFile { projectRoot = ProjectRoot.Replace('\\', '/') }, true));
            }

            string text = $"{s.result}: {s.outputPath}, {s.totalSize / (1024 * 1024)} MB, {s.totalTime.TotalSeconds:F0} s, {s.totalErrors} errors, {s.totalWarnings} warnings";
            Debug.Log("UOClient: build " + text);
            return text;
        }

        // ---- the package: the player as it goes to other PCs ----

        public const string PackageFolder = "Builds/Package/Unity UO";

        /// <summary>What the player reads from the project folder, copied beside the executable (project-relative).</summary>
        public static readonly string[] PackagedContent =
        {
            "Theme",                                          // the HD art, the gump kit, the world map's markers
            "Tools/ComfyRestyle/style/mockup_scene.png",      // the login screen's backdrop while the video loads
        };

        [MenuItem("UOClient/Package Windows Player (for other PCs)")]
        public static void PackageFromMenu() => EditorUtility.DisplayDialog(ProductName, Package(), "OK");

        /// <summary>
        /// Builds the player, then makes Builds/Package/Unity UO: the build without the paths file that points back at this
        /// project (so the game takes its own folder for the project's: ClientPaths.ProjectRoot), with
        /// <see cref="PackagedContent"/> copied in. Settings, the account file, hotkeys and logs are made there on first
        /// run; nothing of this PC's UserSettings is copied. The folder is made anew each time.
        /// </summary>
        public static string Package()
        {
            string built = Build();
            if (!built.StartsWith(BuildResult.Succeeded.ToString()))
                return built;
            string from = Path.Combine(ProjectRoot, OutputFolder), to = Path.Combine(ProjectRoot, PackageFolder);
            if (Directory.Exists(to))
                Directory.Delete(to, true);
            int files = CopyTree(from, to, f => Path.GetFileName(f) != ClientPaths.PathsFileName
                                               && !f.Replace('\\', '/').Contains("_DoNotShip/"));
            foreach (string item in PackagedContent)
            {
                string source = Path.Combine(ProjectRoot, item), target = Path.Combine(to, item);
                if (Directory.Exists(source))
                    files += CopyTree(source, target, f => !f.EndsWith(".meta"));
                else if (File.Exists(source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, true);
                    files++;
                }
                else
                    Debug.LogWarning("UOClient: package: " + item + " is missing from the project");
            }
            long bytes = 0;
            foreach (string f in Directory.GetFiles(to, "*", SearchOption.AllDirectories))
                bytes += new FileInfo(f).Length;
            string text = $"Packaged: {to.Replace('\\', '/')}, {files} files, {bytes / (1024 * 1024)} MB";
            Debug.Log("UOClient: " + text);
            return text;
        }

        private static int CopyTree(string from, string to, System.Func<string, bool> take)
        {
            int n = 0;
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                if (!take(file))
                    continue;
                string target = Path.Combine(to, file.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                n++;
            }
            return n;
        }

        /// <summary>The game's own emblem (the 256 px frame of the UO install's uoico.ico), stamped into the exe so shortcuts show it.</summary>
        public const string IconPath = "Assets/UOClient.Unity/Editor/Icon/uo_icon.png";

        private static void ApplyIcon()
        {
            if (AssetImporter.GetAtPath(IconPath) is TextureImporter importer &&
                (importer.textureCompression != TextureImporterCompression.Uncompressed || !importer.alphaIsTransparency || importer.mipmapEnabled))
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning("UOClient: no icon at " + IconPath + "; the exe keeps Unity's default icon");
                return;
            }
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        [System.Serializable]
        private sealed class PathsFile
        {
            public string projectRoot;
        }

        private static double _nextPoll;

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
            BuildGroundCompareIfRequested();

            string request = Path.Combine(ProjectRoot, BuildRequestFile);
            string refreshed = Path.Combine(ProjectRoot, BuildRefreshedFile);
            string resultPath = Path.Combine(ProjectRoot, "Library", "UOClient", "build-result.txt");
            if (!File.Exists(request) || EditorApplication.isCompiling)
                return;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false; // builds need edit mode; handled on a later poll
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // File flag survives domain reload (static fields do not). Write it BEFORE Refresh.
            if (!File.Exists(refreshed))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(refreshed));
                File.WriteAllText(refreshed, System.DateTime.UtcNow.ToString("o"));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }

            // Keep the request until the result is written so a failed/interrupted build can be retried.
            try
            {
                string result = Build();
                File.WriteAllText(resultPath, result);
                if (File.Exists(request))
                    File.Delete(request);
            }
            finally
            {
                if (File.Exists(refreshed))
                    File.Delete(refreshed);
            }
        }
    }
}
