using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UOClient.Unity;

namespace UOClient.Editor
{
    /// <summary>Creates (once) and plays the world demo scene.</summary>
    public static class WorldDemoLauncher
    {
        public const string ScenePath = "Assets/Scenes/WorldDemo.unity";

        /// <summary>Touch this file (relative to the project) to have the editor open and play the demo after its next script reload.</summary>
        public const string PlayRequestFile = "Library/UOClient/play-world-demo";

        [MenuItem("UOClient/Play World Demo")]
        public static void Play()
        {
            if (EditorApplication.isPlaying)
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!File.Exists(ScenePath))
                CreateScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("UOClient/Recreate World Demo Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            cameraGo.transform.position = new Vector3(0, 0, -1000);

            new GameObject("UO World Demo").AddComponent<WorldDemo>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"UOClient: created {ScenePath}");
        }

        private static double _nextPoll;
        private static bool _refreshed;

        [InitializeOnLoadMethod]
        private static void WatchForRequests()
        {
            EditorApplication.update -= PlayIfRequested;
            EditorApplication.update += PlayIfRequested;
        }

        private static void PlayIfRequested()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;

            string request = Path.Combine(Path.GetDirectoryName(Application.dataPath), PlayRequestFile);
            if (!File.Exists(request) || EditorApplication.isCompiling)
                return;
            if (EditorApplication.isPlaying)
            {
                // Restart: leave Play mode (which also picks up script changes); the request is handled once back in edit mode.
                EditorApplication.isPlaying = false;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // Import changed scripts first: a request can arrive before the editor noticed new code,
            // and playing then would run the old build. A refresh that recompiles reloads this class,
            // so the next poll comes back here with nothing left to import and plays.
            if (!_refreshed)
            {
                _refreshed = true;
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            _refreshed = false;
            File.Delete(request);

            EditorApplication.delayCall += () =>
            {
                // A request should not be blocked by a save prompt; unsaved changes to other scenes are kept on disk untouched.
                if (!File.Exists(ScenePath))
                    CreateScene();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
                Debug.Log("UOClient: playing World Demo on request.");
            };
        }
    }
}
