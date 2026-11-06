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

        /// <summary>Ornamental login art under StreamingAssets (logo, account/password panels, login button).</summary>
        public const string LogoRelativePath = "Login/logo.png";
        public const string AccountPanelRelativePath = "Login/account_panel.png";
        public const string PasswordPanelRelativePath = "Login/password_panel.png";
        public const string LoginButtonRelativePath = "Login/login_button.png";

        /// <summary>
        /// Inner editable slot inside <see cref="AccountPanelRelativePath"/>, as fractions of the panel (provisional
        /// from edge detection on the shipped PNG; replace if ornament-manifest.json gains official rects).
        /// </summary>
        public static readonly (float X, float Y, float W, float H) AccountInputFrac = (0.0453f, 0.5556f, 0.9081f, 0.1884f);

        /// <summary>Inner editable slot inside <see cref="PasswordPanelRelativePath"/>.</summary>
        public static readonly (float X, float Y, float W, float H) PasswordInputFrac = (0.0454f, 0.5473f, 0.9067f, 0.1891f);

        /// <summary>Native pixel size of the logo PNG (for aspect when the texture is not loaded yet).</summary>
        public const float LogoNativeW = 1521f, LogoNativeH = 985f;
        public const float AccountNativeW = 816f, AccountNativeH = 207f;
        public const float PasswordNativeW = 815f, PasswordNativeH = 201f;
        public const float LoginNativeW = 617f, LoginNativeH = 146f;


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

        /// <summary>StreamingAssets-relative path joined the same way as the video.</summary>
        public static string OrnamentPath(string streamingAssetsPath, string relative) =>
            Path.Combine(streamingAssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// Places the ornamental logo above the account / password / login stack with no overlap. Coordinates are in
        /// the login screen's scaled GUI space (Screen / UiScale). Aspects are width/height. The baked labels stay in
        /// the art; <see cref="LoginOrnamentLayout.AccountInput"/> / PasswordInput are where the real fields go.
        /// </summary>
        public static LoginOrnamentLayout LayoutOrnament(
            float screenW, float screenH,
            float logoAspect, float accountAspect, float passwordAspect, float loginAspect)
        {
            if (screenW <= 1f || screenH <= 1f || logoAspect <= 0f || accountAspect <= 0f || passwordAspect <= 0f || loginAspect <= 0f)
                return default;

            float bottomMargin = Math.Max(14f, screenH * 0.035f);
            float topMargin = Math.Max(10f, screenH * 0.025f);
            float gapControls = Math.Max(8f, screenH * 0.012f);
            float gapLogo = Math.Max(14f, screenH * 0.02f);
            float secondaryH = Math.Max(52f, screenH * 0.08f); // host/port + remember/quit under the art

            float width = Math.Min(screenW * 0.46f, 780f);
            LoginOrnamentLayout best = default;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float accountH = width / accountAspect;
                float passwordH = width / passwordAspect;
                float loginW = Math.Min(width * 0.78f, width);
                float loginH = loginW / loginAspect;
                float stackH = accountH + passwordH + loginH + 2f * gapControls;
                float usedBottom = bottomMargin + secondaryH + stackH;
                if (usedBottom > screenH * 0.72f && width > screenW * 0.28f)
                {
                    width *= 0.92f;
                    continue;
                }

                float loginY = screenH - bottomMargin - secondaryH - loginH;
                float passwordY = loginY - gapControls - passwordH;
                float accountY = passwordY - gapControls - accountH;
                float cx = screenW * 0.5f;
                var account = new LoginRect(cx - width * 0.5f, accountY, width, accountH);
                var password = new LoginRect(cx - width * 0.5f, passwordY, width, passwordH);
                var login = new LoginRect(cx - loginW * 0.5f, loginY, loginW, loginH);

                float availTop = accountY - gapLogo - topMargin;
                if (availTop < 24f && width > screenW * 0.28f)
                {
                    width *= 0.9f;
                    continue;
                }

                float logoW = Math.Min(Math.Min(screenW * 0.36f, Math.Max(8f, availTop) * logoAspect), width * 1.05f);
                float logoH = logoW / logoAspect;
                if (logoH > availTop && availTop > 0f)
                {
                    logoH = availTop;
                    logoW = logoH * logoAspect;
                }
                float logoY = topMargin + Math.Max(0f, (availTop - logoH) * 0.15f);
                var logo = new LoginRect(cx - logoW * 0.5f, logoY, logoW, logoH);

                // No overlap: logo bottom must stay above account top.
                if (logo.Y + logo.H > account.Y - gapLogo * 0.5f && width > screenW * 0.28f)
                {
                    width *= 0.9f;
                    continue;
                }

                best = new LoginOrnamentLayout(
                    logo, account, password, login,
                    SubRect(account, AccountInputFrac),
                    SubRect(password, PasswordInputFrac));
                break;
            }
            return best;
        }

        /// <summary>A slot rectangle inside a panel, from fractions of that panel.</summary>
        public static LoginRect SubRect(LoginRect panel, (float X, float Y, float W, float H) frac) =>
            new LoginRect(
                panel.X + panel.W * frac.X,
                panel.Y + panel.H * frac.Y,
                panel.W * frac.W,
                panel.H * frac.H);
    }

    /// <summary>Axis-aligned rect in login GUI space (no UnityEngine dependency for the test project).</summary>
    public readonly struct LoginRect
    {
        public readonly float X, Y, W, H;
        public LoginRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
        public float XMax => X + W;
        public float YMax => Y + H;
        public bool Overlaps(LoginRect o) => X < o.XMax && XMax > o.X && Y < o.YMax && YMax > o.Y;
        public bool InBounds(float screenW, float screenH) =>
            X >= -0.5f && Y >= -0.5f && XMax <= screenW + 0.5f && YMax <= screenH + 0.5f;
    }

    /// <summary>Where the ornamental logo and controls draw; inputs sit in the panel slots (baked labels stay in art).</summary>
    public readonly struct LoginOrnamentLayout
    {
        public readonly LoginRect Logo, Account, Password, Login, AccountInput, PasswordInput;
        public readonly bool Valid;
        public LoginOrnamentLayout(
            LoginRect logo, LoginRect account, LoginRect password, LoginRect login,
            LoginRect accountInput, LoginRect passwordInput)
        {
            Logo = logo; Account = account; Password = password; Login = login;
            AccountInput = accountInput; PasswordInput = passwordInput;
            Valid = logo.W > 1f && account.W > 1f && login.W > 1f;
        }
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
