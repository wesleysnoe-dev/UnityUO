using UOClient.Core;
using UOClient.Unity;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// <see cref="GameOptions"/> and the options window's <see cref="OptionsDraft"/>: controls edit the draft only, Apply
/// puts it into the game without writing, Okay applies and saves, Cancel and Defaults touch nothing but the draft; the
/// file keeps keys it doesn't know and every value is clamped.
/// </summary>
public sealed class GameOptionsTests
{
    /// <summary>A stand-in game: what Apply put in, and how many times Okay saved.</summary>
    private sealed class FakeGame
    {
        public GameOptions Live = GameOptions.Defaults();
        public int Applies, Saves;
        public GameOptions Saved;

        public OptionsDraft Draft() => new OptionsDraft(
            () => Live.Clone(),
            o => { Applies++; Live = o.Clone(); },
            o => { Saves++; Saved = o.Clone(); });
    }

    [Fact]
    public void Defaults_KeepTheProductDefaults()
    {
        var d = GameOptions.Defaults();
        Assert.True(d.highlightGameObjects); // outline-only hover on for everything interactable
        Assert.True(d.criminalActionQuery);
        Assert.False(d.beneficialCriminalActionQuery);
        Assert.Equal(GameOptions.AutoUiScale, d.uiScale);
        Assert.Equal(0.4f, d.tooltipDelay);
        // The session's own defaults agree, so an unsaved game behaves as the options say.
        var session = new GameSession(new LoginSettings());
        Assert.Equal(session.CriminalActionQuery, d.criminalActionQuery);
        Assert.Equal(session.BeneficialCriminalActionQuery, d.beneficialCriminalActionQuery);
        Assert.Equal(ObjectHighlight.HighlightGameObjects, d.highlightGameObjects);
    }

    [Fact]
    public void Draft_EditsWaitForApply_AndApplyNeverSaves()
    {
        var game = new FakeGame();
        OptionsDraft draft = game.Draft();
        draft.Draft.showNames = false;
        draft.Draft.criminalActionQuery = false;
        Assert.True(draft.Changed);
        Assert.True(game.Live.showNames); // nothing reached the game yet

        draft.Apply();
        Assert.False(game.Live.showNames);
        Assert.False(game.Live.criminalActionQuery);
        Assert.Equal(1, game.Applies);
        Assert.Equal(0, game.Saves);
        Assert.False(draft.Changed);
    }

    [Fact]
    public void Okay_AppliesAndSavesOnce()
    {
        var game = new FakeGame();
        OptionsDraft draft = game.Draft();
        draft.Draft.beneficialCriminalActionQuery = true;
        draft.Draft.tooltipDelay = 1.25f;
        draft.Okay();
        Assert.True(game.Live.beneficialCriminalActionQuery);
        Assert.Equal(1, game.Saves);
        Assert.True(game.Saved.beneficialCriminalActionQuery);
        Assert.Equal(1.25f, game.Saved.tooltipDelay);
    }

    [Fact]
    public void Cancel_DropsTheDraft_WithoutApplyingOrSaving()
    {
        var game = new FakeGame();
        OptionsDraft draft = game.Draft();
        draft.Draft.showHud = false;
        draft.Draft.zoom = 3f;
        draft.Cancel();
        Assert.True(draft.Draft.showHud);
        Assert.Equal(1f, draft.Draft.zoom);
        Assert.Equal(0, game.Applies);
        Assert.Equal(0, game.Saves);
    }

    [Fact]
    public void Defaults_ResetsOnlyTheDraft()
    {
        var game = new FakeGame();
        game.Live.highlightGameObjects = false;
        game.Live.criminalActionQuery = false;
        game.Live.musicVolume = 0.1f;
        OptionsDraft draft = game.Draft();
        draft.Defaults();
        Assert.True(draft.Draft.highlightGameObjects);
        Assert.True(draft.Draft.criminalActionQuery);
        Assert.Equal(0.45f, draft.Draft.musicVolume);
        Assert.False(game.Live.highlightGameObjects); // the game keeps its options until Apply / Okay
        Assert.Equal(0, game.Applies);
        Assert.Equal(0, game.Saves);
    }

    [Fact]
    public void Draft_StartsFromTheGame_NotTheDefaults()
    {
        var game = new FakeGame();
        game.Live.zoom = 2f; // the mouse wheel changed it
        Assert.Equal(2f, game.Draft().Draft.zoom);
    }

    [Fact]
    public void Clamp_BringsEveryValueIntoRange()
    {
        var o = new GameOptions
        {
            zoom = 99f, uiScale = 9f, tooltipDelay = -1f, soundVolume = 2f, musicVolume = float.NaN,
        }.Clamp();
        Assert.Equal(GameOptions.MaxZoom, o.zoom);
        Assert.Equal(GameOptions.MaxUiScale, o.uiScale);
        Assert.Equal(0f, o.tooltipDelay);
        Assert.Equal(1f, o.soundVolume);
        Assert.Equal(0.45f, o.musicVolume);

        Assert.Equal(GameOptions.MinZoom, new GameOptions { zoom = 0.01f }.Clamp().zoom);
        Assert.Equal(GameOptions.MinUiScale, new GameOptions { uiScale = 0.3f }.Clamp().uiScale);
        Assert.Equal(GameOptions.AutoUiScale, new GameOptions { uiScale = -2f }.Clamp().uiScale);
        Assert.Equal(GameOptions.MaxTooltipDelay, new GameOptions { tooltipDelay = 10f }.Clamp().tooltipDelay);
    }

    [Fact]
    public void UiScale_AutoFollowsTheScreen_FixedStaysFixed()
    {
        Assert.Equal(1.08f, GameOptions.ResolveUiScale(1080, GameOptions.AutoUiScale), 3);
        Assert.Equal(GameOptions.MinUiScale, GameOptions.ResolveUiScale(480, GameOptions.AutoUiScale));
        Assert.Equal(1.5f, GameOptions.ResolveUiScale(1080, 1.5f));
    }

    [Fact]
    public void Parse_ClampsBadValues_AndKeepsDefaultsForMissingOrBadKeys()
    {
        var o = GameOptions.Parse("{ \"zoom\": 50, \"showNames\": \"yes\", \"tooltipDelay\": 3 }");
        Assert.Equal(GameOptions.MaxZoom, o.zoom);
        Assert.True(o.showNames);
        Assert.Equal(GameOptions.MaxTooltipDelay, o.tooltipDelay);
        Assert.True(o.criminalActionQuery);
        Assert.True(GameOptions.Parse("not json").SameAs(GameOptions.Defaults()));
        Assert.True(GameOptions.Parse("").SameAs(GameOptions.Defaults()));
    }

    [Fact]
    public void RoundTrip_KeepsEveryOption_AndUnknownKeysAsTheyWere()
    {
        const string file = "{\n  \"showNames\": false,\n  \"futureThing\": {\"a\": [1, 2, \"x,}\"]},\n  \"criminalActionQuery\": false,\n"
                          + "  \"beneficialCriminalActionQuery\": true,\n  \"note\": \"keep \\\"me\\\"\",\n  \"uiScale\": 1.25\n}";
        GameOptions o = GameOptions.Parse(file, out var unknown);
        Assert.False(o.showNames);
        Assert.False(o.criminalActionQuery);
        Assert.True(o.beneficialCriminalActionQuery);
        Assert.Equal(1.25f, o.uiScale);
        Assert.Equal(new[] { "futureThing", "note" }, unknown.Select(kv => kv.Key));

        string written = o.ToJson(unknown);
        Assert.Contains("\"futureThing\": {\"a\": [1, 2, \"x,}\"]}", written);
        Assert.Contains("\"note\": \"keep \\\"me\\\"\"", written);
        GameOptions again = GameOptions.Parse(written, out var unknownAgain);
        Assert.True(again.SameAs(o));
        Assert.Equal(unknown, unknownAgain);
    }

    [Fact]
    public void Pages_FollowTazUO_AndUnavailableOnesHaveNoControls()
    {
        Assert.Equal(new[] { "General", "Sound", "Video", "Macros", "Tooltip", "Combat & Spells" }, OptionsWindow.Pages.Select(p => p.Title));
        Assert.Equal(Enum.GetValues<OptionsWindow.Page>().Length, OptionsWindow.Pages.Length);
        Assert.Empty(OptionsWindow.UnavailablePages.Intersect(OptionsWindow.Pages.Select(p => p.Title)));
    }

    /// <summary>Only the options window's Okay writes the file: one Save call site, reached only through SaveOptions.</summary>
    [Fact]
    public void OnlyOkaySaves()
    {
        string runtime = Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime");
        var files = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories);
        Assert.Equal(new[] { "WorldDemo.cs" }, files.Where(f => File.ReadAllText(f).Contains("GameOptionsStore.Save(")).Select(Path.GetFileName));
        Assert.Equal(new[] { "IGameOptions.cs", "OptionsWindow.cs", "WorldDemo.cs" },
            files.Where(f => File.ReadAllText(f).Contains("SaveOptions")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        // The window hands SaveOptions to the draft, whose Okay alone calls it (Draft_EditsWaitForApply_AndApplyNeverSaves).
        string window = File.ReadAllText(Path.Combine(runtime, "UI", "OptionsWindow.cs"));
        Assert.Contains("new OptionsDraft(game.CurrentOptions, game.ApplyOptions, game.SaveOptions)", window);
        Assert.DoesNotContain("SaveOptions(", window);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UOClient.Unity")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
