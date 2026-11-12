using UOClient.Core;
using UOClient.Net;
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
        // TazUO's PAGE order (ModernOptionsGump.cs :6146-6164), the pages without a system here left out; Display (the
        // window: TazUO's Video > Game window) split off Video, whose world graphics fill a page of their own.
        Assert.Equal(new[] { "General", "Sound", "Video", "Display", "Macros", "Tooltip", "Speech", "Combat & Spells", "Containers", "Nameplates", "Counters", "Info Bar", "Cooldowns", "Ignore List" },
            OptionsWindow.Pages.Select(p => p.Title));
        Assert.Equal(Enum.GetValues<OptionsWindow.Page>().Length, OptionsWindow.Pages.Length);
        Assert.Equal(new[] { "Experimental", "TazUO" }, OptionsWindow.UnavailablePages);
        Assert.Empty(OptionsWindow.UnavailablePages.Intersect(OptionsWindow.Pages.Select(p => p.Title)));
        // Every left-out option says why, and sits on a page that is offered.
        Assert.All(OptionsWindow.NotOffered, n => Assert.Contains(':', n.Text));
        Assert.All(OptionsWindow.NotOffered, n => Assert.Contains(n.Page, OptionsWindow.Pages.Select(p => p.Page)));
    }

    // ---- batch 2: Speech, Containers, Tooltip, Combat & Spells, General, Macros ----

    [Fact]
    public void NewOptions_Defaults_FollowTazUO_OrKeepThisClientsBehaviour()
    {
        var d = GameOptions.Defaults();
        // As TazUO's Profile.cs.
        Assert.True(d.scaleSpeechDelay);
        Assert.Equal(100, d.speechDelay);
        Assert.False(d.saveJournalToFile);
        Assert.Equal(100, d.containersScale);
        Assert.False(d.doubleClickToLootInsideContainers);
        Assert.True(d.useTooltip);
        Assert.False(d.castSpellsByOneClick);
        Assert.True(d.alwaysRunUnlessHidden);
        // Kept as this client always behaved (TazUO's own defaults differ; LIVE_PARITY says so).
        Assert.False(d.alwaysRun);
        Assert.False(d.holdShiftToSplitStack); // as TazUO: lifting a stack asks how many
        Assert.True(d.buffBarTime);
    }

    [Fact]
    public void NewOptions_RoundTrip_AndParseAsTheirTypes()
    {
        var o = new GameOptions
        {
            alwaysRun = true, alwaysRunUnlessHidden = false, scaleSpeechDelay = false, speechDelay = 250, saveJournalToFile = true,
            containersScale = 150, doubleClickToLootInsideContainers = true, holdShiftToSplitStack = true, useTooltip = false,
            buffBarTime = false, castSpellsByOneClick = true,
        };
        string json = o.ToJson();
        Assert.Contains("\"speechDelay\": 250,", json);
        Assert.Contains("\"containersScale\": 150,", json);
        GameOptions again = GameOptions.Parse(json, out var unknown);
        Assert.Empty(unknown);
        Assert.True(again.SameAs(o));
        Assert.False(again.SameAs(GameOptions.Defaults()));

        // A fraction rounds; text and booleans for numbers are ignored; each key alone changes SameAs.
        Assert.Equal(121, GameOptions.Parse("{\"containersScale\": 120.6}").containersScale);
        Assert.Equal(100, GameOptions.Parse("{\"speechDelay\": \"lots\"}").speechDelay);
        Assert.Equal(100, GameOptions.Parse("{\"speechDelay\": true}").speechDelay);
        foreach (string key in new[] { "alwaysRun", "saveJournalToFile", "doubleClickToLootInsideContainers", "castSpellsByOneClick", "holdShiftToSplitStack" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": true}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "alwaysRunUnlessHidden", "scaleSpeechDelay", "useTooltip", "buffBarTime" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": false}").SameAs(GameOptions.Defaults()), key);
    }

    [Fact]
    public void ContainersScale_ClampsToTazUOsRange_AndGameBoardsKeepTheirSize()
    {
        Assert.Equal((50, 200), (GameOptions.MinContainersScale, GameOptions.MaxContainersScale)); // TazUO Constants.cs :87-88
        Assert.Equal(GameOptions.MinContainersScale, new GameOptions { containersScale = 10 }.Clamp().containersScale);
        Assert.Equal(GameOptions.MaxContainersScale, new GameOptions { containersScale = 900 }.Clamp().containersScale);
        Assert.Equal(GameOptions.MinContainersScale, GameOptions.Parse("{\"containersScale\": -5}").containersScale);
        Assert.Equal(100, GameOptions.Parse("{\"containersScale\": 1e12}").containersScale); // out of int's range: ignored

        Assert.Equal(1.5f, GameOptions.ContainerScale(150, 0x003C)); // a backpack
        Assert.Equal(2f, GameOptions.ContainerScale(400, 0x003C));
        Assert.Equal(0.5f, GameOptions.ContainerScale(0, 0x003C));
        Assert.Equal(1f, GameOptions.ContainerScale(150, 0x091A)); // chessboard
        Assert.Equal(1f, GameOptions.ContainerScale(50, 0x092E)); // backgammon
    }

    [Fact]
    public void SpeechDelay_ClampsAndTimesAsTazUO()
    {
        Assert.Equal(GameOptions.MaxSpeechDelay, new GameOptions { speechDelay = 5000 }.Clamp().speechDelay);
        Assert.Equal(GameOptions.MinSpeechDelay, new GameOptions { speechDelay = -3 }.Clamp().speechDelay);
        // Scaled (MessageManager.CalculateTimeToLive): 4 s a "line" at delay 100; lines by length 66 / 99; delay at least 10.
        Assert.Equal(4f, GameOptions.SpeechSeconds(10, true, 100));
        Assert.Equal(4f, GameOptions.SpeechSeconds(66, true, 100));
        Assert.Equal(8f, GameOptions.SpeechSeconds(67, true, 100));
        Assert.Equal(12f, GameOptions.SpeechSeconds(100, true, 100));
        Assert.Equal(10f, GameOptions.SpeechSeconds(10, true, 250));
        Assert.Equal(0.4f, GameOptions.SpeechSeconds(10, true, 0));
        // Not scaled: (5497558140000 * delay) >> 32 >> 5 ms, delay x 40 ms whatever the length.
        Assert.Equal(4f, GameOptions.SpeechSeconds(500, false, 100));
        Assert.Equal(40f, GameOptions.SpeechSeconds(5, false, 1000));
        Assert.Equal(0f, GameOptions.SpeechSeconds(5, false, 0));
    }

    [Fact]
    public void HoldShiftToSplitStack_IsTazUOsEquality()
    {
        // GameActions.PickUp: HoldShiftToSplitStack == the split key (Shift) pressed.
        Assert.True(GameOptions.OpensSplitMenu(holdShiftToSplitStack: true, shiftHeld: true));
        Assert.False(GameOptions.OpensSplitMenu(true, false));
        Assert.True(GameOptions.OpensSplitMenu(false, false));
        Assert.False(GameOptions.OpensSplitMenu(false, true));
    }

    [Fact]
    public void UseTooltip_GatesTextTooltipsOnly()
    {
        Assert.True(GameOptions.ShowsTooltip(useTooltip: true, isTextTooltip: true));
        Assert.False(GameOptions.ShowsTooltip(false, true)); // a buff, a gump element, a spell icon
        Assert.True(GameOptions.ShowsTooltip(false, false)); // item / mobile properties (TazUO gates SetText only)
    }

    [Fact]
    public void BuffBarTime_HidesOnlyTheIconTimer()
    {
        Assert.Equal("30s", BuffBarWindow.ShownTimer(true, 30));
        Assert.Equal("", BuffBarWindow.ShownTimer(false, 30));
        Assert.Equal("", BuffBarWindow.ShownTimer(true, 0)); // untimed stays blank
        Assert.Contains("Time left", BuffBarWindow.TooltipText("Bless", "", 30)); // the tooltip keeps its time line
    }

    [Fact]
    public void CastSpellsByOneClick_CastsOnAStillClickOnly()
    {
        Assert.True(SpellbookWindow.SingleClickCasts(true, 0, 0));
        Assert.True(SpellbookWindow.SingleClickCasts(true, 4, -4));
        Assert.False(SpellbookWindow.SingleClickCasts(true, 5, 0)); // a drag moves the icon, never casts
        Assert.False(SpellbookWindow.SingleClickCasts(false, 0, 0));
    }

    [Fact]
    public void DoubleClickToLoot_FollowsTazUOsItemGumpRule()
    {
        Assert.True(GameOptions.LootsOnDoubleClick(true, false, false, false, outermostIsWorldItem: true, haveBackpack: true));
        Assert.False(GameOptions.LootsOnDoubleClick(false, false, false, false, true, true)); // option off
        Assert.False(GameOptions.LootsOnDoubleClick(true, true, false, false, true, true)); // Ctrl uses it
        Assert.False(GameOptions.LootsOnDoubleClick(true, false, true, false, true, true)); // a bag opens
        Assert.False(GameOptions.LootsOnDoubleClick(true, false, false, true, true, true)); // holds things
        Assert.False(GameOptions.LootsOnDoubleClick(true, false, false, false, false, true)); // in the backpack or bank
        Assert.False(GameOptions.LootsOnDoubleClick(true, false, false, false, true, false)); // no backpack to put it in
    }

    [Fact]
    public void JournalFile_NameAndLines_AsTazUO()
    {
        var t = new DateTime(2026, 11, 5, 21, 4, 9);
        Assert.Equal("2026_11_05_21_04_09_Lord Smallz_journal.txt", GameOptions.JournalFileName(t, "Lord Smallz"));
        Assert.Equal("2026_11_05_21_04_09_a_b_c__journal.txt", GameOptions.JournalFileName(t, "a/b:c?"));
        Assert.Equal("2026_11_05_21_04_09_Unknown_journal.txt", GameOptions.JournalFileName(t, " "));
        Assert.EndsWith("]  Bob: hail", GameOptions.JournalLine(t, "Bob", "hail"));
        Assert.EndsWith("]  You see nothing.", GameOptions.JournalLine(t, null, "You see nothing."));
        Assert.StartsWith("[", GameOptions.JournalLine(t, "", "x"));
    }

    // ---- macros ----

    private sealed class FakeHotkeys
    {
        public List<MacroDraft.Row> Live = new() { new() { Key = "F2", Action = "bandageself" }, new() { Key = "Ctrl+1", Action = "say Hail!" } };
        public int Applies, Saves;
        public bool SaveWorks = true;

        public MacroDraft Draft() => new MacroDraft(
            () => Live.Select(r => r.Clone()).ToList(),
            rows => { Applies++; Live = rows.Select(r => r.Clone()).ToList(); },
            rows => { Saves++; Live = rows.Select(r => r.Clone()).ToList(); return SaveWorks; });
    }

    [Fact]
    public void Macros_EditsWaitForApply_ApplyNeverSaves_OkaySavesOnce()
    {
        var hk = new FakeHotkeys();
        MacroDraft m = hk.Draft();
        m.Rows[0].Action = "bandageself ";
        Assert.False(m.Changed); // trimmed, the same
        m.Add();
        Assert.False(m.Changed); // a blank row is nothing
        m.Rows[2].Key = "F9";
        m.Rows[2].Action = "skill Meditation";
        Assert.True(m.Changed);
        Assert.Equal(2, hk.Live.Count); // nothing bound yet

        m.Apply();
        Assert.Equal(3, hk.Live.Count);
        Assert.Equal((1, 0), (hk.Applies, hk.Saves));
        Assert.False(m.Changed);

        m.Okay(); // nothing new since Apply, but the file still lacks it: saved once
        Assert.Equal(1, hk.Saves);
        m.Okay(); // now nothing to write
        Assert.Equal(1, hk.Saves);
    }

    [Fact]
    public void Macros_RemoveAndCancel_AndOkayWithoutEditsWritesNothing()
    {
        var hk = new FakeHotkeys();
        MacroDraft m = hk.Draft();
        m.Okay();
        Assert.Equal((0, 0), (hk.Applies, hk.Saves)); // the file is not rewritten for nothing

        m.Remove(0);
        m.Remove(9); // out of range: ignored
        Assert.Single(m.Rows);
        m.Cancel();
        Assert.Equal(2, m.Rows.Count);
        Assert.Equal((0, 0), (hk.Applies, hk.Saves));

        m.Remove(1);
        m.Okay();
        Assert.Equal(1, hk.Saves);
        Assert.Equal("F2", Assert.Single(hk.Live).Key);
    }

    [Fact]
    public void Macros_AFailedSaveIsReported_AndTriedAgain()
    {
        var hk = new FakeHotkeys { SaveWorks = false };
        MacroDraft m = hk.Draft();
        m.Rows[1].Action = "say Bye";
        m.Okay();
        Assert.True(m.SaveFailed);
        hk.SaveWorks = true;
        m.Okay(); // bound already, not yet written: tried again
        Assert.False(m.SaveFailed);
        Assert.Equal(2, hk.Saves);
    }

    [Fact]
    public void Macros_ProblemsNameWhatIsWrong()
    {
        Assert.Null(MacroDraft.Problem(new MacroDraft.Row { Key = "F5", Action = "targetself" }));
        Assert.Null(MacroDraft.Problem(new MacroDraft.Row { Key = "Ctrl+Shift+Q", Action = "cast Greater Heal" }));
        Assert.Null(MacroDraft.Problem(new MacroDraft.Row())); // blank: dropped, not wrong
        Assert.Contains("No key", MacroDraft.Problem(new MacroDraft.Row { Action = "war" }));
        Assert.Contains("No action", MacroDraft.Problem(new MacroDraft.Row { Key = "F1" }));
        Assert.Contains("Meta", MacroDraft.Problem(new MacroDraft.Row { Key = "Meta+F1", Action = "war" }));
        Assert.Contains("after the +", MacroDraft.Problem(new MacroDraft.Row { Key = "Ctrl+", Action = "war" }));
        Assert.Contains("Unknown action", MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = "dance now" }));
        Assert.Contains("needs something", MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = "cast" }));
    }

    /// <summary>The editor offers exactly the actions Hotkeys.Run carries out: none invented, none missing.</summary>
    [Fact]
    public void Macros_VerbsAreHotkeysRunsCases()
    {
        string hotkeys = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime", "Hotkeys.cs"));
        int run = hotkeys.IndexOf("public void Run(", StringComparison.Ordinal);
        int end = hotkeys.IndexOf("private void ToggleRecording", run, StringComparison.Ordinal);
        var cases = System.Text.RegularExpressions.Regex.Matches(hotkeys.Substring(run, end - run), "case \"([a-z]+)\":")
            .Select(m => m.Groups[1].Value).OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(cases, MacroDraft.Verbs.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Subset(MacroDraft.Verbs.ToHashSet(), MacroDraft.VerbsWithArgument.ToHashSet());
    }

    /// <summary>Only the options window's Apply and Okay write the hotkeys file through the editor (SaveBindings), via MacroDraft.Okay.</summary>
    [Fact]
    public void OnlyApplyAndOkaySaveMacros()
    {
        string runtime = Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime");
        var files = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories);
        Assert.Equal(new[] { "OptionsWindow.cs" }, files.Where(f => File.ReadAllText(f).Contains(".SaveBindings(")).Select(Path.GetFileName));
        string window = File.ReadAllText(Path.Combine(runtime, "UI", "OptionsWindow.cs"));
        // Handed to the draft as its save; the window calls the draft's Okay from its Apply and Okay buttons.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(window, @"_macros\?\.Okay\(\)").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(window, @"hk\.SaveBindings\("));
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

    // ---- batch 3: health lines, speech hues, chat filters, container highlight, macro verbs ----

    [Fact]
    public void Batch3_Defaults_FollowTazUO_OrKeepThisClientsBehaviour()
    {
        var d = GameOptions.Defaults();
        // As TazUO's Profile.cs.
        Assert.Equal(1, d.healthLineSizeMultiplier);
        Assert.Equal((0x33, 0x21, 0x21, 0x44), (d.whisperHue, d.emoteHue, d.yellHue, d.partyMessageHue));
        Assert.False(d.ignoreGuildMessages);
        Assert.False(d.ignoreAllianceMessages);
        Assert.False(d.highlightContainerWhenSelected);
        // Kept as this client always behaved: lines drawn when hurt, the classic speech hue (TazUO 0x2B2), server lines shown.
        Assert.True(d.showMobilesHP);
        Assert.Equal(GameOptions.MobileHPWhenDamaged, d.mobileHPShowWhen);
        Assert.Equal(0x3B2, d.speechHue);
        Assert.True(d.showSystemMessagesInJournal);
        // The session's own defaults agree, so an unsaved game sends what the options say.
        var s = new GameSession(new LoginSettings()).SpeechOptions;
        Assert.Equal((d.speechHue, d.whisperHue, d.emoteHue, d.yellHue, d.partyMessageHue),
            ((int)s.Speech, (int)s.Whisper, (int)s.Emote, (int)s.Yell, (int)s.Party));
        Assert.Equal((d.ignoreGuildMessages, d.ignoreAllianceMessages), (s.IgnoreGuildMessages, s.IgnoreAllianceMessages));
        Assert.Equal(GameSession.SpeechHue, s.Speech);
        Assert.Equal(GameSession.PartyHue, s.Party);
    }

    [Fact]
    public void Batch3_RoundTrip_ParseAndClamp()
    {
        var o = new GameOptions
        {
            showMobilesHP = false, mobileHPShowWhen = GameOptions.MobileHPAlways, healthLineSizeMultiplier = 3,
            speechHue = 0x2B2, whisperHue = 100, emoteHue = 200, yellHue = 300, partyMessageHue = 400,
            ignoreGuildMessages = true, ignoreAllianceMessages = true, showSystemMessagesInJournal = false,
            highlightContainerWhenSelected = true,
        };
        string json = o.ToJson();
        Assert.Contains("\"speechHue\": 690,", json);
        GameOptions again = GameOptions.Parse(json, out var unknown);
        Assert.Empty(unknown);
        Assert.True(again.SameAs(o));

        foreach (string key in new[] { "ignoreGuildMessages", "ignoreAllianceMessages", "highlightContainerWhenSelected" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": true}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "showMobilesHP", "showSystemMessagesInJournal" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": false}").SameAs(GameOptions.Defaults()), key);
        foreach (string key in new[] { "mobileHPShowWhen", "speechHue", "whisperHue", "emoteHue", "yellHue", "partyMessageHue" })
            Assert.False(GameOptions.Parse("{\"" + key + "\": 0}").SameAs(GameOptions.Defaults()), key);
        Assert.False(GameOptions.Parse("{\"healthLineSizeMultiplier\": 2}").SameAs(GameOptions.Defaults()));

        var c = new GameOptions { speechHue = 5000, whisperHue = -4, mobileHPShowWhen = 3, healthLineSizeMultiplier = 9 }.Clamp();
        Assert.Equal(GameOptions.MaxHue, c.speechHue);
        Assert.Equal(GameOptions.MinHue, c.whisperHue);
        Assert.Equal(GameOptions.MobileHPSmart, c.mobileHPShowWhen); // batch 4: TazUO's "smart" (2) is the top value now
        Assert.Equal(GameOptions.MaxHealthLineSize, c.healthLineSizeMultiplier);
        Assert.Equal(GameOptions.MinHealthLineSize, GameOptions.Parse("{\"healthLineSizeMultiplier\": 0}").healthLineSizeMultiplier);
        Assert.Equal(GameOptions.MobileHPAlways, GameOptions.Parse("{\"mobileHPShowWhen\": -1}").mobileHPShowWhen);
    }

    [Fact]
    public void HealthLines_FollowShowMobilesHPAndShowWhen()
    {
        const int Always = GameOptions.MobileHPAlways, Damaged = GameOptions.MobileHPWhenDamaged;
        Assert.False(GameOptions.ShowsHealthLine(false, Always, 10, 20, true)); // off hides every line
        Assert.False(GameOptions.ShowsHealthLine(true, Always, 0, 0, true));    // no known maximum, no line
        Assert.True(GameOptions.ShowsHealthLine(true, Always, 20, 20, false));  // always: full lines too
        Assert.True(GameOptions.ShowsHealthLine(true, Damaged, 19, 20, false));
        Assert.False(GameOptions.ShowsHealthLine(true, Damaged, 20, 20, false)); // TazUO: showWhen 1 && current == max
        Assert.True(GameOptions.ShowsHealthLine(true, Damaged, 20, 20, true));   // whoever we fight / ourselves in war
        Assert.Equal(44f, GameOptions.HealthLineWidth(1));
        Assert.Equal(132f, GameOptions.HealthLineWidth(3));
        Assert.Equal(220f, GameOptions.HealthLineWidth(99));
        Assert.Equal(44f, GameOptions.HealthLineWidth(0));
    }

    [Fact]
    public void Journal_ServerLinesOnlyHiddenWhenAsked()
    {
        Assert.True(GameOptions.JournalShowsLine(true, 0xFFFFFFFF));
        Assert.False(GameOptions.JournalShowsLine(false, 0xFFFFFFFF));
        Assert.False(GameOptions.JournalShowsLine(false, 0));
        Assert.True(GameOptions.JournalShowsLine(false, 0x1234)); // someone said it (or a party member)
    }

    [Fact]
    public void ContainerHighlight_SelectsTheHoveredContainer_OnlyOverTheUi()
    {
        Assert.Equal(0u, ObjectHighlight.WorldSelection(true, 0x55, 0));          // option off: nothing over a window
        Assert.Equal(0x40000010u, ObjectHighlight.WorldSelection(true, 0x55, 0x40000010));
        Assert.Equal(0x55u, ObjectHighlight.WorldSelection(false, 0x55, 0x40000010)); // over the world the pick stands
        Assert.Equal(ObjectHighlight.WorldSelection(true, 7), ObjectHighlight.WorldSelection(true, 7, 0));
    }

    [Fact]
    public void SpeechHues_TypedLinesUseTheOptions()
    {
        var hues = new SpeechSettings { Speech = 0x2B2, Whisper = 0x10, Emote = 0x20, Yell = 0x30 };
        Assert.Equal((0, "hi", (ushort)0x2B2), GameSession.SpeechMode("hi", hues));
        Assert.Equal((8, "psst", (ushort)0x10), GameSession.SpeechMode("; psst", hues));
        Assert.Equal((2, "*waves*", (ushort)0x20), GameSession.SpeechMode(": waves", hues));
        Assert.Equal((9, "Guards!", (ushort)0x30), GameSession.SpeechMode("! Guards!", hues));
        Assert.Equal(GameSession.SpeechMode(": waves"), GameSession.SpeechMode(": waves", new SpeechSettings()));
    }

    private static byte[] UnicodeSpeech(uint serial, byte type, ushort hue, string name, string text) =>
        new PacketWriter(0xAE).UInt32(serial).UInt16(0x190).Byte(type).UInt16(hue).UInt16(3).Ascii("ENU", 4).Ascii(name, 30)
            .UnicodeZ(text).ToArray();

    [Fact]
    public void IgnoredGuildAndAllianceLines_NeverReachTheJournal_AndPartyTakesItsHue()
    {
        var session = new GameSession(new LoginSettings());
        session.SpeechOptions = new SpeechSettings { IgnoreGuildMessages = true, Party = 0x99 };
        var seen = new List<JournalEntry>();
        session.World.JournalAdded += seen.Add;

        session.Inject(UnicodeSpeech(0x10, 13, 0x44, "[Guild][Bob]", "guild line"), true);
        session.Inject(UnicodeSpeech(0x10, 14, 0x57, "[Alliance][Bob]", "ally line"), true);
        session.Inject(UnicodeSpeech(0x10, 2, 0, "Bob", "*nods*"), true);
        Assert.Equal(new[] { "ally line", "*nods*" }, seen.Select(j => j.Text));
        Assert.Equal(session.SpeechOptions.Emote, seen[1].Hue); // an emote without a hue takes the emote hue

        session.SpeechOptions.IgnoreAllianceMessages = true; // a change in place reaches the session at once
        session.Inject(UnicodeSpeech(0x10, 14, 0x57, "[Alliance][Bob]", "ally again"), true);
        Assert.Equal(2, seen.Count);

        session.Inject(new PacketWriter(0xBF).UInt16(0x06).Byte(0x04).UInt32(0x10).UnicodeZ("party line").ToArray(), true);
        Assert.Equal((ushort)0x99, seen[^1].Hue);
    }

    [Fact]
    public void Batch3_MacroVerbs_HaveTheirArguments()
    {
        foreach (string verb in new[] { "emote", "whisper", "yell" })
        {
            Assert.Contains(verb, MacroDraft.VerbsWithArgument);
            Assert.NotNull(MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = verb }));
            Assert.Null(MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = verb + " hello" }));
        }
        foreach (string verb in new[] { "spellbook", "logout", "party", "worldmap", "minimap", "worldchat" })
            Assert.Null(MacroDraft.Problem(new MacroDraft.Row { Key = "F1", Action = verb }));
        // Quests and guild menus are left out: ModernUO answers neither on an AoS shard.
        Assert.DoesNotContain("quests", MacroDraft.Verbs);
        Assert.DoesNotContain("guild", MacroDraft.Verbs);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UOClient.Unity")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
