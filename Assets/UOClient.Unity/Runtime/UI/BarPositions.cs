using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// Where the counter bar, the info bar and the cooldown bars were left: UserSettings/unity-uo-bars.txt, a "key=x,y"
    /// line each (HudBars.ParseBarPositions). Read once; written when a bar has been moved and the mouse is up again.
    /// </summary>
    public static class BarPositions
    {
        public const string FileName = "unity-uo-bars.txt";

        private static Dictionary<string, (int X, int Y)> _at;

        private static string FilePath => Path.Combine(ClientPaths.ProjectRoot, "UserSettings", FileName);

        private static Dictionary<string, (int X, int Y)> At
        {
            get
            {
                if (_at != null)
                    return _at;
                try
                {
                    _at = HudBars.ParseBarPositions(File.Exists(FilePath) ? File.ReadAllText(FilePath) : "");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"UOClient: {FilePath} is unreadable ({e.Message}); the bars start in their usual places.");
                    _at = new Dictionary<string, (int, int)>();
                }
                return _at;
            }
        }

        /// <summary>Where the bar starts: where it was left, or <paramref name="fallback"/>.</summary>
        public static Vector2 Get(string key, Vector2 fallback) =>
            At.TryGetValue(key, out (int X, int Y) p) ? new Vector2(p.X, p.Y) : fallback;

        /// <summary>Call each frame with the bar's place: a new place is written once the mouse button is up.</summary>
        public static void Track(string key, Vector2 position)
        {
            var now = (Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
            if (CounterBarWindow.SelfTest || Input.GetMouseButton(0) || (At.TryGetValue(key, out (int X, int Y) was) && was == now))
                return;
            At[key] = now;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, HudBars.FormatBarPositions(At));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {FilePath} ({e.Message}).");
            }
        }
    }
}
