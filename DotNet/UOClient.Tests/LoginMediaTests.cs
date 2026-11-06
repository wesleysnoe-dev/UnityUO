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
        Assert.Contains("Nothing is saved", OptionsWindow.ButtonHints[2]);
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
        Assert.Contains("MusicRequested += _ => EndLoginScreen();", demo);
        Assert.Contains("_loginMedia.MusicVolume = _musicVolume;", demo);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UOClient.Unity")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
