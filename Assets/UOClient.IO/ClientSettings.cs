using System;
using System.IO;

namespace UOClient.IO
{
    /// <summary>
    /// Where the game connects and which client files it reads. One place for every tool (game, editor
    /// windows, CLI, tests): <c>UserSettings/unity-uo-settings.json</c> in the project, overridden by launch
    /// arguments, falling back to the UO Smallz client install on this PC.
    /// <list type="bullet">
    /// <item><c>-server host:port</c> (or <c>--server</c>): the shard. Default uosmallz.duckdns.org:2593, the live shard; the local test server is -server 127.0.0.1.</item>
    /// <item><c>-uodata dir</c>: the UO data files (art, gumps, maps, tiledata, hues, multis).</item>
    /// <item><c>-overrides dir</c>: the UO Smallz client folder whose ExternalImages, TileData and EquipConv
    /// folders carry the shard's custom art and item data; <c>none</c> turns them off.</item>
    /// </list>
    /// Switching to the live shard is only <c>-server uosmallz.duckdns.org:2593</c> (or the same in the file).
    /// </summary>
    [Serializable]
    public sealed class ClientSettings
    {
        public const string FileName = "unity-uo-settings.json";
        public const string DefaultHost = "uosmallz.duckdns.org";
        public const int DefaultPort = 2593;

        // Public fields, so Unity's JsonUtility and System.Text.Json (IncludeFields) both read the file.
        public string host = DefaultHost;
        public int port = DefaultPort;
        public string uoDataPath = "";
        public string overridesPath = "";

        /// <summary>Use the HDArt pack's 2x art where the renderer draws at 2x (ExternalImages always applies).</summary>
        public bool hdArt = true;

        /// <summary>
        /// The overhaul's HD art folders (HDArt layout: art/, land/, texmaps/, gumps/), laid over the HD pack for this
        /// client only: first listed wins; the shard's own ExternalImages still win over them. Empty: the pack alone.
        /// </summary>
        public string[] overhaulArt = new string[0];

        /// <summary>The UO Smallz launcher's install, the live client release.</summary>
        public static string UoSmallzRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UOSmallz");

        public const string EaClassicPath = @"C:\Program Files (x86)\Electronic Arts\Ultima Online Classic";

        /// <summary>The data folder to read: the setting, else the UO Smallz client's own UOData, else the EA install.</summary>
        public string ResolvedDataPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(uoDataPath))
                    return uoDataPath;
                string smallz = Path.Combine(UoSmallzRoot, "UOData", "7.0.23.1", "7.0.23.1");
                return Directory.Exists(smallz) ? smallz : EaClassicPath;
            }
        }

        /// <summary>The client folder with ExternalImages/TileData/EquipConv, or null when there is none (or "none").</summary>
        public string ResolvedOverridesPath
        {
            get
            {
                if (string.Equals(overridesPath?.Trim(), "none", StringComparison.OrdinalIgnoreCase))
                    return null;
                string dir = !string.IsNullOrWhiteSpace(overridesPath) ? overridesPath : Path.Combine(UoSmallzRoot, "Client");
                return Directory.Exists(dir) ? dir : null;
            }
        }

        /// <summary>Applies launch arguments over what the file said. Unknown arguments are left alone.</summary>
        public ClientSettings ApplyArguments(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                string name = args[i].TrimStart('-').ToLowerInvariant(), value = args[i + 1];
                switch (name)
                {
                    case "server":
                        if (TryParseServer(value, out string h, out int p))
                        {
                            host = h;
                            port = p;
                        }
                        break;
                    case "uodata":
                        uoDataPath = value;
                        break;
                    case "overrides":
                        overridesPath = value;
                        break;
                }
            }
            return this;
        }

        /// <summary>"host:port" or just "host" (port 2593).</summary>
        public static bool TryParseServer(string text, out string host, out int port)
        {
            host = null;
            port = DefaultPort;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int p) && p > 0 && p < 65536)
            {
                host = text.Substring(0, colon);
                port = p;
            }
            else
            {
                host = text;
            }
            return host.Length > 0;
        }

        public string Server => $"{host}:{port}";

        public bool IsLocal => host == "127.0.0.1" || host == "localhost" || host == "::1";
    }
}
