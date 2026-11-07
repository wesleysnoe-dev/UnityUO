using System;
using System.Linq;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own.
    /// <summary>The world demo's command-line switches (-server, -uodata and -overrides are read by <c>ClientSettings</c>).</summary>
    public readonly struct LaunchOptions
    {
        /// <summary>-uoclient-capture: run the capture tour (and quit when done in a player).</summary>
        public readonly bool CaptureFromCommandLine;

        /// <summary>-uoclient-autologin: log straight in with the account file instead of showing the login screen.</summary>
        public readonly bool AutoLogin;

        /// <summary>-server chose the server for this run, so a server typed on the login screen is not saved.</summary>
        public readonly bool ServerFromArguments;

        /// <summary>-offline &lt;map&gt; &lt;x&gt; &lt;y&gt;: the offline demo at an exact spot (indoors included), e.g. to check a building.</summary>
        public readonly (int map, int x, int y)? OfflineAt;

        /// <summary>-liveground: opt in to the LiveGroundCompare land backends (TEST host only; see LiveGroundHostGuard).</summary>
        public readonly bool LiveGround;

        /// <summary>-groundbackend &lt;original|mesh|terrain&gt; as typed (null when absent, "" without a value); parsed by LiveGroundCompare.</summary>
        public readonly string GroundBackend;

        /// <summary>
        /// -liveground-anyhost: the normal client's explicit promotion of the land backends to any host, the live shard
        /// included. Does nothing without -liveground; without it the guard stays loopback-only.
        /// </summary>
        public readonly bool LiveGroundAnyHost;

        private LaunchOptions(bool captureFromCommandLine, bool autoLogin, bool serverFromArguments, (int map, int x, int y)? offlineAt,
                              bool liveGround, string groundBackend, bool liveGroundAnyHost)
        {
            CaptureFromCommandLine = captureFromCommandLine;
            AutoLogin = autoLogin;
            ServerFromArguments = serverFromArguments;
            OfflineAt = offlineAt;
            LiveGround = liveGround;
            GroundBackend = groundBackend;
            LiveGroundAnyHost = liveGroundAnyHost;
        }

        public static LaunchOptions FromCommandLine() => Parse(Environment.GetCommandLineArgs());

        public static LaunchOptions Parse(string[] cl)
        {
            (int map, int x, int y)? offlineAt = null;
            int at = Array.IndexOf(cl, "-offline");
            if (at >= 0 && at + 3 < cl.Length && int.TryParse(cl[at + 1], out int om) && int.TryParse(cl[at + 2], out int ox) && int.TryParse(cl[at + 3], out int oy))
                offlineAt = (om, ox, oy);
            string groundBackend = null;
            int gb = Array.FindIndex(cl, a => a.Equals("-groundbackend", StringComparison.OrdinalIgnoreCase));
            if (gb >= 0)
                groundBackend = gb + 1 < cl.Length && !cl[gb + 1].StartsWith("-") ? cl[gb + 1] : "";
            return new LaunchOptions(
                cl.Contains("-uoclient-capture"),
                cl.Contains("-uoclient-autologin"),
                cl.Any(a => a.TrimStart('-').Equals("server", StringComparison.OrdinalIgnoreCase)),
                offlineAt,
                cl.Any(a => a.Equals("-liveground", StringComparison.OrdinalIgnoreCase)),
                groundBackend,
                cl.Any(a => a.Equals("-liveground-anyhost", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
