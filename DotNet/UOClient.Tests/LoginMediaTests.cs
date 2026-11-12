using System.Security.Cryptography;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// The login screen's video and music (<see cref="LoginMediaRules"/>, <see cref="LoginMediaState"/>): the file paths,
/// the whole-frame fit, the volume clamp, the login track lookup, when they play, and source checks on the Unity side
/// (one looping player with no audio, music never touched by the video's loop, stopped on entering the world).
/// </summary>
public sealed class LoginMediaTests
{
    [Fact]
    public void VideoPath_IsUnderStreamingAssetsLogin()
    {
        string root = Path.Combine(Path.GetTempPath(), "sa");
        Assert.Equal(Path.Combine(root, "Login", "login-bg-loop-0.5s-xfade.mp4"), LoginMediaRules.VideoPath(root));
        string url = LoginMediaRules.VideoUrl(root);
        Assert.StartsWith("file:///", url);
        Assert.EndsWith("/Login/login-bg-loop-0.5s-xfade.mp4", url);
        // A StreamingAssets path that is already a URL (Android) is appended to, not turned into a file URI.
        Assert.Equal("jar:file:///base.apk!/assets/Login/login-bg-loop-0.5s-xfade.mp4",
            LoginMediaRules.VideoUrl("jar:file:///base.apk!/assets/"));
    }

    [Fact]
    public void VideoFile_IsInTheProject()
    {
        string file = LoginMediaRules.VideoPath(Path.Combine(RepoRoot(), "Assets", "StreamingAssets"));
        Assert.True(File.Exists(file), file);
        Assert.True(new FileInfo(file).Length > 1_000_000);
    }

    [Theory]
    [InlineData(1920, 1080)] // wider than the video: pillarbox
    [InlineData(1104, 816)]  // exact
    [InlineData(800, 1200)]  // taller: letterbox
    [InlineData(3440, 1440)]
    public void Contain_ShowsTheWholeFrame_Centred_AtItsAspect(float sw, float sh)
    {
        var (x, y, w, h) = LoginMediaRules.Contain(sw, sh, 1104, 816);
        Assert.True(x >= -0.01f && y >= -0.01f, "never off screen");
        Assert.True(x + w <= sw + 0.01f && y + h <= sh + 0.01f, "never cropped");
        Assert.Equal(1104f / 816f, w / h, 3);
        Assert.True(Math.Abs(w - sw) < 0.01f || Math.Abs(h - sh) < 0.01f, "as large as fits");
        Assert.Equal(sw - w, 2 * x, 2);
        Assert.Equal(sh - h, 2 * y, 2);
    }

    [Fact]
    public void Contain_DegenerateSizes_DrawNothing()
    {
        Assert.Equal((0f, 0f, 0f, 0f), LoginMediaRules.Contain(0, 1080, 1104, 816));
        Assert.Equal((0f, 0f, 0f, 0f), LoginMediaRules.Contain(1920, 1080, 0, 0));
    }

    [Theory]
    [InlineData(0.45f, 0.45f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 1f)]
    public void Volume_IsClamped(float given, float expected) => Assert.Equal(expected, LoginMediaRules.Volume(given));

    [Fact]
    public void MusicConfig_ReadsBareAndExtensionNames()
    {
        var tracks = LoginMediaRules.ParseMusicConfig(new[]
        {
            "0 turfin,loop", "64 SelimsBar.mp3          ", "78 LoginLoop.mp3,loop", "junk", "", "x y",
        });
        Assert.Equal(("turfin", true), tracks[0]);
        Assert.Equal(("SelimsBar.mp3", false), tracks[64]);
        Assert.Equal(("LoginLoop.mp3", true), tracks[78]);
        Assert.Equal(3, tracks.Count);
        Assert.Equal(Path.Combine("d", "turfin.mp3"), LoginMediaRules.MusicFile("d", "turfin"));
        Assert.Equal(Path.Combine("d", "LoginLoop.mp3"), LoginMediaRules.MusicFile("d", "LoginLoop.mp3"));
    }

    [Fact]
    public void LoginMusicPath_FollowsConfigId78_ElseLoginLoop_ElseNull()
    {
        string data = Directory.CreateTempSubdirectory("uoclient-music-").FullName;
        try
        {
            string folder = Path.Combine(data, "Music", "Digital");
            Directory.CreateDirectory(folder);
            Assert.Null(LoginMediaRules.LoginMusicPath(data)); // nothing there
            Assert.Null(LoginMediaRules.LoginMusicPath(null));

            File.WriteAllBytes(Path.Combine(folder, "LoginLoop.mp3"), new byte[] { 1 });
            Assert.Equal(Path.Combine(folder, "LoginLoop.mp3"), LoginMediaRules.LoginMusicPath(data)); // no Config.txt

            File.WriteAllLines(Path.Combine(folder, "Config.txt"), new[] { "77 HumanLevel.mp3", "78 Other,loop" });
            File.WriteAllBytes(Path.Combine(folder, "Other.mp3"), new byte[] { 1 });
            Assert.Equal(Path.Combine(folder, "Other.mp3"), LoginMediaRules.LoginMusicPath(data));
        }
        finally
        {
            Directory.Delete(data, true);
        }
    }

    /// <summary>The 7.0.23.1 data on this machine, when present: id 78 is the known LoginLoop.mp3.</summary>
    [Fact]
    public void LoginMusicPath_RealData_IsTheKnownLoginLoop()
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UOSmallz", "UOData", "7.0.23.1", "7.0.23.1");
        if (!Directory.Exists(Path.Combine(data, "Music", "Digital")))
            return; // not on this machine
        string path = LoginMediaRules.LoginMusicPath(data);
        Assert.NotNull(path);
        Assert.Equal("LoginLoop.mp3", Path.GetFileName(path));
        using var md5 = MD5.Create();
        using var s = File.OpenRead(path);
        Assert.Equal("6aa2f398a001302aa47d43d457704df0", Convert.ToHexString(md5.ComputeHash(s)).ToLowerInvariant());
    }

    [Fact]
    public void State_StartsAndStopsOnce_AndComesBackAfterAFailedLogin()
    {
        var m = new LoginMediaState();
        Assert.True(m.Start());   // login screen shown
        Assert.False(m.Start());  // shown again / a failed login while still playing: no second player, no restart
        Assert.True(m.Playing);
        Assert.True(m.Stop());    // the server's music or entering the world
        Assert.False(m.Stop());   // entering the world after the music already stopped it; quitting
        Assert.False(m.Playing);
        Assert.True(m.Start());   // dropped back to the form before the world
        Assert.Equal(2, m.Starts);
        Assert.Equal(1, m.Stops);
    }

    [Fact]
    public void State_AFailedPartStaysOff_ReportedOnce_TheOtherPlays()
    {
        var m = new LoginMediaState();
        m.Start();
        Assert.True(m.FailVideo());
        Assert.False(m.FailVideo());
        Assert.False(m.WantsVideo);
        Assert.True(m.WantsMusic);
        m.Stop();
        Assert.False(m.WantsMusic);
        m.Start();
        Assert.False(m.WantsVideo); // not retried for the run
        Assert.True(m.WantsMusic);
    }

    [Fact]
    public void OptionsButtonHints_SayWhatApplyAndOkayDo()
    {
        Assert.Equal(4, OptionsWindow.ButtonHints.Length);
        Assert.StartsWith("Defaults", OptionsWindow.ButtonHints[0]);
        Assert.Contains("Nothing is saved", OptionsWindow.ButtonHints[1]);
        Assert.Contains("saves", OptionsWindow.ButtonHints[2]); // Apply saves too: closing the game after it keeps them
        Assert.StartsWith("Apply", OptionsWindow.ButtonHints[2]);
        Assert.Contains("unity-uo-options.json", OptionsWindow.ButtonHints[3]);
    }

    /// <summary>The Unity side as the brief asks: one looping player, no video audio, contain drawing, music on its own.</summary>
    [Fact]
    public void UnitySide_LoopsOnePlayer_NoVideoAudio_MusicApart()
    {
        string runtime = Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime");
        string media = File.ReadAllText(Path.Combine(runtime, "LoginMedia.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(media, @"AddComponent<VideoPlayer>"));
        Assert.Contains("_video.isLooping = true;", media);
        Assert.Contains("_video.audioOutputMode = VideoAudioOutputMode.None;", media);
        Assert.Contains("_music.loop = true;", media);
        Assert.DoesNotContain("loopPointReached", media); // nothing happens to the music when the video loops

        string screen = File.ReadAllText(Path.Combine(runtime, "UI", "LoginScreen.cs"));
        Assert.Contains("LoginMediaRules.Contain(", screen);
        Assert.DoesNotContain("video, ScaleMode.ScaleAndCrop", screen);

        string demo = File.ReadAllText(Path.Combine(runtime, "WorldDemo.cs"));
        int entered = demo.IndexOf("private void OnEnteredWorld()", StringComparison.Ordinal);
        Assert.True(entered > 0);
        Assert.Contains("EndLoginScreen();", demo.Substring(entered, 200));
        Assert.Contains("MusicRequested += _life.Guard<int>(_ => EndLoginScreen());", demo);
        Assert.Contains("_loginMedia.MusicVolume = _musicVolume;", demo);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]  // 4:3
    [InlineData(800, 600)]
    public void LayoutOrnament_NoOverlap_InBounds_Centered(float w, float h)
    {
        float logoA = LoginMediaRules.LogoNativeW / LoginMediaRules.LogoNativeH;
        float accountA = LoginMediaRules.AccountNativeW / LoginMediaRules.AccountNativeH;
        float passwordA = LoginMediaRules.PasswordNativeW / LoginMediaRules.PasswordNativeH;
        float loginA = LoginMediaRules.LoginNativeW / LoginMediaRules.LoginNativeH;
        var L = LoginMediaRules.LayoutOrnament(w, h, logoA, accountA, passwordA, loginA);
        Assert.True(L.Valid);
        Assert.True(L.Logo.InBounds(w, h));
        Assert.True(L.Account.InBounds(w, h));
        Assert.True(L.Password.InBounds(w, h));
        Assert.True(L.Login.InBounds(w, h));
        Assert.False(L.Logo.Overlaps(L.Account));
        Assert.False(L.Logo.Overlaps(L.Password));
        Assert.False(L.Logo.Overlaps(L.Login));
        Assert.False(L.Account.Overlaps(L.Password));
        Assert.False(L.Password.Overlaps(L.Login));
        Assert.True(L.Logo.YMax <= L.Account.Y);
        Assert.True(L.Account.YMax <= L.Password.Y);
        Assert.True(L.Password.YMax <= L.Login.Y);
        Assert.Equal(w / 2f, L.Logo.X + L.Logo.W / 2f, 1);
        Assert.Equal(w / 2f, L.Account.X + L.Account.W / 2f, 1);
        Assert.Equal(w / 2f, L.Login.X + L.Login.W / 2f, 1);
        // Inputs sit inside their panels.
        Assert.True(L.AccountInput.X >= L.Account.X && L.AccountInput.XMax <= L.Account.XMax);
        Assert.True(L.AccountInput.Y >= L.Account.Y && L.AccountInput.YMax <= L.Account.YMax);
        Assert.True(L.PasswordInput.X >= L.Password.X && L.PasswordInput.XMax <= L.Password.XMax);
        Assert.True(L.PasswordInput.Y >= L.Password.Y && L.PasswordInput.YMax <= L.Password.YMax);
        // Aspects preserved within a pixel.
        Assert.Equal(logoA, L.Logo.W / L.Logo.H, 2);
        Assert.Equal(accountA, L.Account.W / L.Account.H, 2);
        Assert.Equal(loginA, L.Login.W / L.Login.H, 2);
        // Stack order and gaps stay non-negative.
        Assert.True(L.Account.Y - L.Logo.YMax >= 0);
        Assert.True(L.Password.Y - L.Account.YMax >= 0);
        Assert.True(L.Login.Y - L.Password.YMax >= 0);
        // Secondary reserve under the Login button.
        Assert.True(L.Login.YMax <= h - h * LoginMediaRules.OrnamentSecondaryReserveFrac + 1f);
    }

    [Fact]
    public void LayoutOrnament_1920x1080_NearRecommendedPreview()
    {
        float logoA = LoginMediaRules.LogoNativeW / LoginMediaRules.LogoNativeH;
        float accountA = LoginMediaRules.AccountNativeW / LoginMediaRules.AccountNativeH;
        float passwordA = LoginMediaRules.PasswordNativeW / LoginMediaRules.PasswordNativeH;
        float loginA = LoginMediaRules.LoginNativeW / LoginMediaRules.LoginNativeH;
        var L = LoginMediaRules.LayoutOrnament(1920, 1080, logoA, accountA, passwordA, loginA);
        Assert.True(L.Valid);
        // Preview pack scales to ~0.36/0.32/0.24 when fitting; we allow the nominal 0.42/0.38/0.28 too.
        Assert.InRange(L.Logo.W / 1920f, 0.30f, 0.43f);
        Assert.InRange(L.Account.W / 1920f, 0.27f, 0.39f);
        Assert.InRange(L.Login.W / 1920f, 0.20f, 0.29f);
        Assert.True(L.Logo.Y < L.Account.Y);
        Assert.True(L.Login.YMax < 1080f);
    }

    [Fact]
    public void OrnamentArt_IsInStreamingAssetsLogin()
    {
        string root = Path.Combine(RepoRoot(), "Assets", "StreamingAssets");
        Assert.True(File.Exists(LoginMediaRules.OrnamentPath(root, LoginMediaRules.LogoRelativePath)));
        Assert.True(File.Exists(LoginMediaRules.OrnamentPath(root, LoginMediaRules.AccountPanelRelativePath)));
        Assert.True(File.Exists(LoginMediaRules.OrnamentPath(root, LoginMediaRules.PasswordPanelRelativePath)));
        Assert.True(File.Exists(LoginMediaRules.OrnamentPath(root, LoginMediaRules.LoginButtonRelativePath)));
    }

    [Fact]
    public void UnitySide_OrnamentLogin_UsesLayoutAndNoDuplicateLabels()
    {
        string screen = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime", "UI", "LoginScreen.cs"));
        Assert.Contains("LayoutOrnament(", screen);
        Assert.Contains("DrawOrnamentLogin", screen);
        Assert.Contains("HasOrnament", screen);
        // Ornament path must not redraw baked Account Name / Password / Login captions.
        int orn = screen.IndexOf("private void DrawOrnamentLogin", StringComparison.Ordinal);
        Assert.True(orn > 0);
        string body = screen.Substring(orn, screen.IndexOf("private static Rect ToRect", orn, StringComparison.Ordinal) - orn);
        Assert.DoesNotContain("\"Account Name\"", body);
        Assert.DoesNotContain("\"Password:\"", body);
        Assert.DoesNotContain("\"Login\"", body);
        Assert.Contains("GUI.PasswordField", body);
        Assert.Contains("login-account", body);
    }

        [Fact]
    public void InputFrac_IsRecessedFill_NotManifestTextSpan()
    {
        // Manifest input_text_xywh starts under the left ornament (~x=43) and is ~732px wide; the true dark
        // fill between end-caps is ~x=124 and ~567px. Using the manifest span is what made the fields "way off".
        var manifestText = LoginMediaRules.FracFromNativeXywh(LoginMediaRules.AccountNativeW, LoginMediaRules.AccountNativeH, (43, 116, 732, 38));
        Assert.True(LoginMediaRules.AccountInputFrac.X > manifestText.X + 0.05f);
        Assert.True(LoginMediaRules.AccountInputFrac.W < manifestText.W - 0.10f);
        var accountFrac = LoginMediaRules.FracFromNativeXywh(LoginMediaRules.AccountNativeW, LoginMediaRules.AccountNativeH, LoginMediaRules.AccountInputNativeXywh);
        var passwordFrac = LoginMediaRules.FracFromNativeXywh(LoginMediaRules.PasswordNativeW, LoginMediaRules.PasswordNativeH, LoginMediaRules.PasswordInputNativeXywh);
        Assert.Equal(accountFrac.X, LoginMediaRules.AccountInputFrac.X, 5);
        Assert.Equal(accountFrac.Y, LoginMediaRules.AccountInputFrac.Y, 5);
        Assert.Equal(accountFrac.W, LoginMediaRules.AccountInputFrac.W, 5);
        Assert.Equal(accountFrac.H, LoginMediaRules.AccountInputFrac.H, 5);
        Assert.Equal(passwordFrac.X, LoginMediaRules.PasswordInputFrac.X, 5);
        Assert.Equal(passwordFrac.Y, LoginMediaRules.PasswordInputFrac.Y, 5);
        Assert.Equal(passwordFrac.W, LoginMediaRules.PasswordInputFrac.W, 5);
        Assert.Equal(passwordFrac.H, LoginMediaRules.PasswordInputFrac.H, 5);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]
    [InlineData(800, 600)]
    public void MapNativeSlotToPanel_AlignsWithLaidOutInputs(float sw, float sh)
    {
        float logoA = LoginMediaRules.LogoNativeW / LoginMediaRules.LogoNativeH;
        float accountA = LoginMediaRules.AccountNativeW / LoginMediaRules.AccountNativeH;
        float passwordA = LoginMediaRules.PasswordNativeW / LoginMediaRules.PasswordNativeH;
        float loginA = LoginMediaRules.LoginNativeW / LoginMediaRules.LoginNativeH;
        var L = LoginMediaRules.LayoutOrnament(sw, sh, logoA, accountA, passwordA, loginA);
        Assert.True(L.Valid);

        var accountMapped = LoginMediaRules.MapNativeSlotToPanel(
            L.Account, LoginMediaRules.AccountNativeW, LoginMediaRules.AccountNativeH, LoginMediaRules.AccountInputNativeXywh);
        var passwordMapped = LoginMediaRules.MapNativeSlotToPanel(
            L.Password, LoginMediaRules.PasswordNativeW, LoginMediaRules.PasswordNativeH, LoginMediaRules.PasswordInputNativeXywh);

        Assert.Equal(L.AccountInput.X, accountMapped.X, 2);
        Assert.Equal(L.AccountInput.Y, accountMapped.Y, 2);
        Assert.Equal(L.AccountInput.W, accountMapped.W, 2);
        Assert.Equal(L.AccountInput.H, accountMapped.H, 2);
        Assert.Equal(L.PasswordInput.X, passwordMapped.X, 2);
        Assert.Equal(L.PasswordInput.Y, passwordMapped.Y, 2);
        Assert.Equal(L.PasswordInput.W, passwordMapped.W, 2);
        Assert.Equal(L.PasswordInput.H, passwordMapped.H, 2);

        // Hit/text rect stays inside the drawn panel (and therefore over the recessed art, not the side ornaments).
        Assert.True(L.AccountInput.X > L.Account.X);
        Assert.True(L.AccountInput.XMax < L.Account.XMax);
        Assert.True(L.AccountInput.Y > L.Account.Y);
        Assert.True(L.AccountInput.YMax < L.Account.YMax);
        Assert.True(L.PasswordInput.X > L.Password.X);
        Assert.True(L.PasswordInput.XMax < L.Password.XMax);

        // Nested scale: laying out in a pre-scaled logical space (UiScale divides Screen by s) still maps linearly.
        float s = 1.25f;
        var nested = LoginMediaRules.LayoutOrnament(sw / s, sh / s, logoA, accountA, passwordA, loginA);
        Assert.True(nested.Valid);
        var nestedMap = LoginMediaRules.MapNativeSlotToPanel(
            nested.Account, LoginMediaRules.AccountNativeW, LoginMediaRules.AccountNativeH, LoginMediaRules.AccountInputNativeXywh);
        Assert.Equal(nested.AccountInput.X, nestedMap.X, 2);
        Assert.Equal(nested.AccountInput.Y, nestedMap.Y, 2);
        Assert.Equal(nested.AccountInput.W, nestedMap.W, 2);
        Assert.Equal(nested.AccountInput.H, nestedMap.H, 2);
    }

        private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UOClient.Unity")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
