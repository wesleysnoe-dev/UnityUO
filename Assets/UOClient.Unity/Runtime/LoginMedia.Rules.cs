using System;
using System.Collections.Generic;
using System.IO;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.

    /// <summary>
    /// The login screen's background video and music: where they come from, how the video fits the screen, and when
    /// they play (<see cref="LoginMediaState"/>). The video is a baked loop with its own 0.5 s crossfade, played with an
    /// ordinary looping VideoPlayer (no second player, no runtime crossfade); the music is the UO data's login track,
    /// on its own source, so the video looping never touches it.
    /// </summary>
    public static class LoginMediaRules
    {
        /// <summary>Under StreamingAssets.</summary>
        public const string VideoRelativePath = "Login/login-bg-loop-0.5s-xfade.mp4";

        /// <summary>Music/Digital/Config.txt id of LoginLoop.mp3 (TazUO's LoginMusicIndex, ModernUO's MusicName.LoginLoop).</summary>
        public const int LoginMusicId = 78;

        public const string LoginMusicFallbackName = "LoginLoop";

        /// <summary>How dark the scrim over the video is, so the login panel and its status line stay readable.</summary>
        public const float VideoScrim = 0.35f;

        /// <summary>The video file on disk (desktop and Editor: StreamingAssets is a plain folder).</summary>
        public static string VideoPath(string streamingAssetsPath) =>
            Path.Combine(streamingAssetsPath, VideoRelativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// The URL VideoPlayer takes: a file:// URI for a folder path; a StreamingAssets path that already is a URL
        /// (Android's jar:file://, WebGL's http) gets the relative path appended as is.
        /// </summary>
        public static string VideoUrl(string streamingAssetsPath)
        {
            if (streamingAssetsPath.Contains("://"))
                return streamingAssetsPath.TrimEnd('/') + "/" + VideoRelativePath;
            return new Uri(Path.GetFullPath(VideoPath(streamingAssetsPath))).AbsoluteUri;
        }

        /// <summary>
        /// The video's rect on a screen: the whole frame, as large as fits, centred (contain). What is left over is
        /// the dark pillarbox / letterbox; nothing of the picture (the bottom-right caption included) is cut.
        /// </summary>
        public static (float X, float Y, float W, float H) Contain(float screenW, float screenH, float videoW, float videoH)
        {
            if (screenW <= 0 || screenH <= 0 || videoW <= 0 || videoH <= 0)
                return (0, 0, 0, 0);
            float k = Math.Min(screenW / videoW, screenH / videoH);
            float w = videoW * k, h = videoH * k;
            return ((screenW - w) / 2f, (screenH - h) / 2f, w, h);
        }

        /// <summary>A music volume as the source takes it: 0..1, and a bad value (NaN) is silence rather than full.</summary>
        public static float Volume(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));

        /// <summary>Config.txt: "id name[,loop]" per line.</summary>
        public static Dictionary<int, (string File, bool Loop)> ParseMusicConfig(IEnumerable<string> lines)
        {
            var tracks = new Dictionary<int, (string, bool)>();
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], out int id))
                    continue;
                string[] nameAndFlag = parts[1].Split(',');
                tracks[id] = (nameAndFlag[0].Trim(), nameAndFlag.Length > 1 && nameAndFlag[1].Trim().Equals("loop", StringComparison.OrdinalIgnoreCase));
            }
            return tracks;
        }

        /// <summary>
        /// The login track's file under the UO data folder, or null if it isn't there. Config.txt's id 78 names it;
        /// without that entry, Music/Digital/LoginLoop.mp3. The login music always loops, whatever Config.txt says.
        /// </summary>
        public static string LoginMusicPath(string dataDirectory)
        {
            if (string.IsNullOrEmpty(dataDirectory))
                return null;
            string folder = Path.Combine(dataDirectory, "Music", "Digital");
            string config = Path.Combine(folder, "Config.txt");
            string name = LoginMusicFallbackName;
            if (File.Exists(config) && ParseMusicConfig(File.ReadAllLines(config)).TryGetValue(LoginMusicId, out var track))
                name = track.File;
            string path = MusicFile(folder, name);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// A Config.txt name as a file in Music/Digital. 7.0.23.1's Config.txt names ids 0-56 bare ("turfin") and
        /// 57-87 with the extension ("LoginLoop.mp3"); both mean the same .mp3.
        /// </summary>
        public static string MusicFile(string folder, string name) =>
            Path.Combine(folder, name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? name : name + ".mp3");
    }

    /// <summary>
    /// When the login video and music play: from the login screen showing until the world is entered (or the
    /// screen is torn down / the client quits); back again when a login fails to the form. Start and Stop say
    /// whether anything has to happen, so a repeated call never starts a second player or restarts the music.
    /// A part that failed (missing file, decoder error) stays off for the run and is reported once.
    /// </summary>
    public sealed class LoginMediaState
    {
        public bool Playing { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool VideoFailed { get; private set; }
        public bool MusicFailed { get; private set; }

        /// <summary>True when the media has to be started now (it was stopped).</summary>
        public bool Start()
        {
            if (Playing)
                return false;
            Playing = true;
            Starts++;
            return true;
        }

        /// <summary>True when the media has to be stopped and released now (it was playing).</summary>
        public bool Stop()
        {
            if (!Playing)
                return false;
            Playing = false;
            Stops++;
            return true;
        }

        /// <summary>True the first time only: the caller logs the failure once and falls back.</summary>
        public bool FailVideo()
        {
            if (VideoFailed)
                return false;
            VideoFailed = true;
            return true;
        }

        /// <summary>True the first time only.</summary>
        public bool FailMusic()
        {
            if (MusicFailed)
                return false;
            MusicFailed = true;
            return true;
        }

        public bool WantsVideo => Playing && !VideoFailed;
        public bool WantsMusic => Playing && !MusicFailed;
    }
}
