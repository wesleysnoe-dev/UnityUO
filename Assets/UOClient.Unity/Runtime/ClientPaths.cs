using System;
using System.IO;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>Where the project lives on this machine, for resolving relative paths.</summary>
    public static class ClientPaths
    {
        /// <summary>Written next to a player build: where the project (baked art, account file) lives on this machine.</summary>
        public const string PathsFileName = "unity-uo-paths.json";

        [Serializable]
        private sealed class PathsFile
        {
            public string projectRoot = "";
        }

        private static string _projectRoot;

        /// <summary>
        /// Folder relative paths (baked art, account file, capture output) resolve against: the project in the
        /// editor; in a player, the project named by the paths file next to the executable, else the player's folder.
        /// </summary>
        public static string ProjectRoot
        {
            get
            {
                if (_projectRoot != null)
                    return _projectRoot;
                string appFolder = Path.GetDirectoryName(Application.dataPath);
                _projectRoot = appFolder;
                string paths = Path.Combine(appFolder, PathsFileName);
                if (!Application.isEditor && File.Exists(paths))
                {
                    string root = JsonUtility.FromJson<PathsFile>(File.ReadAllText(paths)).projectRoot;
                    if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                        _projectRoot = root;
                }
                return _projectRoot;
            }
        }
    }
}
