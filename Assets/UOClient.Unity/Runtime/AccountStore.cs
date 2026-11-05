using System;
using System.IO;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.Net;

namespace UOClient.Unity
{
    /// <summary>
    /// All account-file I/O (account, password, character, start city, client version), one file per server
    /// (see <see cref="PathFor(string, string, string)"/>).
    /// </summary>
    public sealed partial class AccountStore
    {
        [Serializable]
        private sealed class AccountFile
        {
            // The server is not here: it is a setting (unity-uo-settings.json / -server), so switching shards
            // never means editing credentials.
            public string account = "", password = "", character = "", startCity = "", clientVersion = "";
        }

        private readonly string _accountFile;

        /// <param name="accountFile">The local test account file; relative to the project root.</param>
        public AccountStore(string accountFile)
        {
            _accountFile = accountFile;
        }

        public string PathFor(string host) => PathFor(_accountFile, ClientPaths.ProjectRoot, host);

        private static AccountFile Read(string path) => JsonUtility.FromJson<AccountFile>(File.ReadAllText(path));

        /// <summary>The account and password stored for a server as they are in the file, or null if there is no file.</summary>
        public (string account, string password)? Stored(string host)
        {
            string path = PathFor(host);
            if (!File.Exists(path))
                return null;
            AccountFile a = Read(path);
            return (a.account, a.password);
        }

        /// <summary>The login remembered for a server, empty when there is none.</summary>
        public (string account, string password) Remembered(string host)
        {
            string path = PathFor(host);
            if (host.Length == 0 || !File.Exists(path))
                return ("", "");
            AccountFile a = Read(path);
            return (a.account ?? "", a.password ?? "");
        }

        /// <summary>Keeps what was typed for next time (the password only when asked; the file is git-ignored).</summary>
        public void Save(string host, string account, string password, bool rememberPassword)
        {
            try
            {
                string path = PathFor(host); // the server being logged in to, which may have just been typed
                AccountFile a = File.Exists(path) ? Read(path) : new AccountFile();
                a.account = account;
                a.password = rememberPassword ? password : "";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(a, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not save the account file ({e.Message}).");
            }
        }

        public ClientVersion ReadVersion(string host)
        {
            string path = PathFor(host);
            if (!File.Exists(path))
                return ClientVersion.Default;
            AccountFile a = Read(path);
            return string.IsNullOrEmpty(a.clientVersion) ? ClientVersion.Default : ClientVersion.Parse(a.clientVersion);
        }

        /// <summary>The unattended login for the configured server, or null (with a warning) if no account is remembered for it.</summary>
        public LoginSettings ReadLogin(ClientSettings settings)
        {
            // Unattended runs only log in with an account remembered for this server (never the test account elsewhere).
            string path = PathFor(settings.host);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"UOClient: no account file for {settings.Server} at {path}; running the offline demo.");
                return null;
            }
            AccountFile a = Read(path);
            return new LoginSettings
            {
                Host = settings.host,
                Port = settings.port,
                Account = a.account,
                Password = a.password,
                CharacterName = string.IsNullOrEmpty(a.character) ? "Adventurer" : a.character,
                StartCity = string.IsNullOrEmpty(a.startCity) ? "Britain" : a.startCity,
                Version = string.IsNullOrEmpty(a.clientVersion) ? ClientVersion.Default : ClientVersion.Parse(a.clientVersion)
            };
        }
    }
}
