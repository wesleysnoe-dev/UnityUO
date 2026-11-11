using System;
using System.IO;
using UnityEngine;
using UOClient.IO;

namespace UOClient.Unity
{
    /// <summary>Reads and writes UserSettings/unity-uo-settings.json (server, data folder, client overrides).</summary>
    public static class ClientSettingsStore
    {
        public static string SettingsPath => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", ClientSettings.FileName);

        /// <summary>The settings file as it is (no launch arguments), or null if there is none; for the editor windows too.</summary>
        public static ClientSettings ReadSettingsFile(bool orDefaults = true)
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonUtility.FromJson<ClientSettings>(File.ReadAllText(SettingsPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: {SettingsPath} is unreadable ({e.Message}); using defaults.");
            }
            return orDefaults ? new ClientSettings() : null;
        }

        /// <summary>The client overrides for a data set opened by an editor tool, when the settings name a client folder.</summary>
        public static void ApplyOverrides(UOData data, ClientSettings s)
        {
            string dir = s.ResolvedOverridesPath;
            if (dir != null)
                data.UseOverrides(new ClientOverrides(dir, s.hdArt, s.overhaulArt));
        }

        /// <summary>Reads the settings file (writing one with the detected folders on first run), then applies launch arguments.</summary>
        public static ClientSettings Load()
        {
            ClientSettings.OwnRoot = ClientPaths.ProjectRoot; // the install's own UOData and Shard folders, when the launcher made them
            ClientSettings s = ReadSettingsFile(orDefaults: false);
            if (s == null)
            {
                s = new ClientSettings();
                s.uoDataPath = s.ResolvedDataPath;
                s.overridesPath = s.ResolvedOverridesPath ?? "none";
                Save(s);
            }
            return s.ApplyArguments(Environment.GetCommandLineArgs());
        }

        public static void Save(ClientSettings s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(s, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {SettingsPath} ({e.Message}).");
            }
        }
    }
}
