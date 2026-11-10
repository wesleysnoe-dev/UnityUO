using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UOClient.Core;
using static UOClient.Unity.CoroutineUtil;

namespace UOClient.Unity
{
    /// <summary>
    /// Screenshots of every page before the world: the login form, the character list, and each step of character
    /// creation (appearance, profession, Advanced's skills, the starting town). Nothing is created or saved; then back out.
    /// It logs in with the account file for the server (AccountStore.ReadLogin), as unattended runs do.
    /// </summary>
    internal sealed class LoginTour
    {
        private readonly GameContext _ctx;
        private readonly LoginFlow _login;
        private readonly Func<LoginSettings> _account;

        public LoginTour(GameContext ctx, LoginFlow login, Func<LoginSettings> account)
        {
            _ctx = ctx;
            _login = login;
            _account = account;
        }

        public IEnumerator Run()
        {
            string dir = Path.Combine(ClientPaths.ProjectRoot, "Library", "UOClient");
            yield return WaitFrames(90);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_1_form.png"));
            yield return WaitFrames(10);
            LoginSettings account = _account();
            string report;
            if (account == null)
            {
                report = "no account file for this server";
                File.WriteAllText(Path.Combine(dir, "login_capture_done.txt"), report);
                yield break;
            }
            _login.StartTour(account);
            float until = Time.realtimeSinceStartup + 15f;
            while (_login.Screen.Current != UI.LoginScreen.Page.Characters && Time.realtimeSinceStartup < until)
                yield return null;
            yield return WaitFrames(20);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_2_characters.png"));
            yield return WaitFrames(10);
            report = $"page after login: {_login.Screen.Current}; status: {_login.Screen.Status}";
            if (_login.Screen.Current == UI.LoginScreen.Page.Characters)
            {
                (UI.LoginScreen.Page Page, int Profession, string Shot)[] steps =
                {
                    (UI.LoginScreen.Page.Create, -1, "login_3_appearance.png"),
                    (UI.LoginScreen.Page.Profession, 0, "login_4_profession.png"),
                    (UI.LoginScreen.Page.Skills, 5, "login_5_skills.png"),
                    (UI.LoginScreen.Page.City, 0, "login_6_city.png"),
                };
                foreach (var step in steps)
                {
                    _login.Screen.TourShow(step.Page, "Aldric", step.Profession);
                    yield return WaitFrames(30);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, step.Shot));
                    yield return WaitFrames(10);
                }
                report += $"; characters [{string.Join(", ", _ctx.Session.Characters)}]; cities [{string.Join(", ", _ctx.Session.Cities.ConvertAll(c => $"{c.Name} {c.X},{c.Y} map {c.Map} #{c.Cliloc}"))}]";
            }
            _ctx.Session?.Dispose();
            _ctx.Session = null;
            _login.Failed("");
            File.WriteAllText(Path.Combine(dir, "login_capture_done.txt"), report);
        }
    }
}
