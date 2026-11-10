using System;
using System.IO;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;

namespace UOClient.Unity
{
    /// <summary>
    /// The login screen: filled from the settings and the remembered account, it saves the account and a newly
    /// typed server, then starts the session and follows it through the character pages.
    /// </summary>
    public sealed class LoginFlow
    {
        private readonly GameContext _ctx;
        private readonly UI.GumpArt _gumpArt;
        private readonly Func<AccountStore> _accounts;
        private readonly ClientSettings _settings;
        private readonly Action<LoginSettings> _startOnline;
        private readonly Action _quit;

        /// <summary><paramref name="accounts"/> is called on every use, so it always sees the current account file.</summary>
        public LoginFlow(GameContext ctx, UI.GumpArt gumpArt, Func<AccountStore> accounts, ClientSettings settings,
                         Action<LoginSettings> startOnline, Action quit)
        {
            _ctx = ctx;
            _gumpArt = gumpArt;
            _accounts = accounts;
            _settings = settings;
            _startOnline = startOnline;
            _quit = quit;
        }

        public UI.LoginScreen Screen { get; private set; }

        /// <summary>The login video's current frame (null: the old backdrop); set before <see cref="Show"/>.</summary>
        public Func<Texture> VideoFrame { get; set; }

        /// <summary>The login screen's Options button; set before <see cref="Show"/>.</summary>
        public Action OptionsPressed { get; set; }

        public void Show()
        {
            Screen = new UI.LoginScreen(_gumpArt, Path.Combine(GameContext.ProjectRoot, "Tools", "ComfyRestyle", "style", "mockup_scene.png"));
            Screen.VideoFrame = VideoFrame;
            Screen.OptionsPressed = OptionsPressed;
            Screen.Host = _settings.host;
            Screen.Port = _settings.port;
            if (_accounts().Stored(_settings.host) is (var account, var password))
            {
                Screen.Account = account;
                Screen.Password = password;
                Screen.RememberPassword = !string.IsNullOrEmpty(password);
            }
            Screen.QuitPressed = _quit;
            Screen.RememberedFor = host => _accounts().Remembered(host);
            Screen.LoginPressed = screen =>
            {
                _accounts().Save(screen.Host, screen.Account, screen.Password, screen.RememberPassword);
                // A server typed on the login screen becomes the setting (unless -server chose it for this run).
                if ((screen.Host != _settings.host || screen.Port != _settings.port) && !ServerFromArguments)
                {
                    _settings.host = screen.Host;
                    _settings.port = screen.Port;
                    ClientSettings onDisk = File.Exists(ClientSettingsStore.SettingsPath)
                        ? JsonUtility.FromJson<ClientSettings>(File.ReadAllText(ClientSettingsStore.SettingsPath)) : _settings;
                    onDisk.host = screen.Host;
                    onDisk.port = screen.Port;
                    ClientSettingsStore.Save(onDisk);
                }
                var login = new LoginSettings
                {
                    Host = screen.Host, Port = screen.Port, Account = screen.Account, Password = screen.Password,
                    Interactive = true,
                    // The server being logged in to (under -server the settings' host is not it).
                    Version = _accounts().ReadVersion(screen.Host)
                };
                _startOnline(login);
                if (_ctx.Session != null)
                    Attach(_ctx.Session);
            };
        }

        private static bool ServerFromArguments => LaunchOptions.FromCommandLine().ServerFromArguments;

        public void Attach(GameSession session) => Screen.Attach(session);

        /// <summary>
        /// The login tour's login: straight to the character pages with <paramref name="login"/>, saving nothing (Log in
        /// would remember the account for the server in place of the player's own).
        /// </summary>
        internal void StartTour(LoginSettings login)
        {
            login.Interactive = true;
            _startOnline(login);
            if (_ctx.Session != null)
                Attach(_ctx.Session);
        }

        public void Failed(string reason) => Screen.Failed(reason);

        public void Draw() => Screen.Draw();
    }
}
