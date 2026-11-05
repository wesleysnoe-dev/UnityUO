using System.Collections;
using System.IO;
using UnityEngine;
using static UOClient.Unity.CoroutineUtil;

namespace UOClient.Unity
{
    /// <summary>Screenshots of the login form, character list and creation page (nothing is created), then back out.</summary>
    internal sealed class LoginTour
    {
        private readonly GameContext _ctx;
        private readonly LoginFlow _login;

        public LoginTour(GameContext ctx, LoginFlow login)
        {
            _ctx = ctx;
            _login = login;
        }

        public IEnumerator Run()
        {
            string dir = Path.Combine(ClientPaths.ProjectRoot, "Library", "UOClient");
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_1_form.png"));
            yield return WaitFrames(10);
            _login.Screen.LoginPressed(_login.Screen);
            float until = Time.realtimeSinceStartup + 10f;
            while (_login.Screen.Current != UI.LoginScreen.Page.Characters && Time.realtimeSinceStartup < until)
                yield return null;
            yield return WaitFrames(10);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_2_characters.png"));
            yield return WaitFrames(10);
            string report = $"page after login: {_login.Screen.Current}; status: {_login.Screen.Status}";
            if (_login.Screen.Current == UI.LoginScreen.Page.Characters)
            {
                _login.Screen.Current = UI.LoginScreen.Page.Create;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_3_create.png"));
                yield return WaitFrames(10);
                report += $"; characters [{string.Join(", ", _ctx.Session.Characters)}]";
            }
            _ctx.Session?.Dispose();
            _ctx.Session = null;
            _login.Failed("");
            File.WriteAllText(Path.Combine(dir, "login_capture_done.txt"), report);
        }
    }
}
