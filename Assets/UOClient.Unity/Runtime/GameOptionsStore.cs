using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>
    /// Reads and writes UserSettings/unity-uo-options.json (<see cref="GameOptions"/>). Reading never writes: with no
    /// file the game keeps its code defaults and nothing is created. Only the options window's Okay calls <see cref="Save"/>.
    /// unity-uo-settings.json (server, data folders) is never touched here.
    /// </summary>
    public static class GameOptionsStore
    {
        public static string OptionsPath => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", GameOptions.FileName);

        /// <summary>The saved options (clamped), or null when none were saved or the file is unreadable.</summary>
        public static GameOptions TryLoad()
        {
            try
            {
                if (File.Exists(OptionsPath))
                    return GameOptions.Parse(File.ReadAllText(OptionsPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: {OptionsPath} is unreadable ({e.Message}); using the default options.");
            }
            return null;
        }

        /// <summary>Writes the options, keeping any keys in the file this version doesn't know.</summary>
        public static void Save(GameOptions options)
        {
            try
            {
                List<KeyValuePair<string, string>> unknown = null;
                if (File.Exists(OptionsPath))
                    GameOptions.Parse(File.ReadAllText(OptionsPath), out unknown);
                Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath));
                string temp = OptionsPath + ".tmp";
                File.WriteAllText(temp, options.Clone().Clamp().ToJson(unknown));
                if (File.Exists(OptionsPath))
                    File.Replace(temp, OptionsPath, null);
                else
                    File.Move(temp, OptionsPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {OptionsPath} ({e.Message}).");
            }
        }
    }
}
