using UOClient.Core;
using UOClient.IO;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// What each paperdoll button and double-click puts on the wire, as TazUO's PaperDollGump sends it and ModernUO reads
/// it, and that every button command is bound in SessionUiBinder.RunCommand to the matching session call or window.
/// Offline sessions only. Quests and Guild send on any shard; a ModernUO shard without ML / SE answers them with
/// nothing (MLQuestSystem.QuestGumpRequest returns unless Enabled, Guild.GuildGumpRequest unless NewGuildSystem).
/// </summary>
public sealed class PaperdollButtonsTests
{
    private const uint Me = 0x00000042, Other = 0x00000099;

    private static (GameSession Session, List<byte[]> Sent) InWorld()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        sent.Clear();
        return (session, sent);
    }

    [Fact]
    public void HelpSendsTheEmpty0x9B()
    {
        var (session, sent) = InWorld();
        session.RequestHelp();
        byte[] p = Assert.Single(sent);
        Assert.Equal(258, p.Length); // ModernUO registers 0x9B at 258
        Assert.Equal(0x9B, p[0]);
        Assert.All(p.Skip(1), b => Assert.Equal(0, b));
    }

    [Fact]
    public void WarTogglesWithTazUOsChangeWarMode()
    {
        var (session, sent) = InWorld();
        Assert.False(session.WarMode);
        session.SetWarMode(!session.WarMode);
        Assert.Equal(new byte[] { 0x72, 1, 0x32, 0, 0 }, Assert.Single(sent));

        // The server's 0x72 echo is what flips the button's art; the next click asks for peace.
        session.Inject(new byte[] { 0x72, 1, 0, 0x32, 0 }, false);
        Assert.True(session.WarMode);
        PaperdollWindow.Button war = PaperdollWindow.Buttons(ClientVersion.Default, self: true, session.WarMode)[6];
        Assert.Equal((0x07E8, 0x07E9), (war.Normal, war.Pressed));
        sent.Clear();
        session.SetWarMode(!session.WarMode);
        Assert.Equal(new byte[] { 0x72, 0, 0x32, 0, 0 }, Assert.Single(sent));
    }

    [Fact]
    public void QuestsAndGuildAreTheEncodedCommandsAndNothingElse()
    {
        var (session, sent) = InWorld();
        session.RequestQuestMenu();
        Assert.Equal(GameSession.EncodedCommand(Me, 0x32, 0x00), Assert.Single(sent));
        sent.Clear();
        session.RequestGuildMenu();
        Assert.Equal(GameSession.EncodedCommand(Me, 0x28, 0x0A), Assert.Single(sent));
    }

    [Fact]
    public void VirtueGemRepliesToGump461WithTheBeheldSerial()
    {
        var (session, sent) = InWorld();
        session.RequestVirtueGump(Other);
        // TazUO's ReplyGump(player, 0x1CD, 1, [LocalSerial], no texts).
        Assert.Equal(new byte[] { 0xB1, 0, 27, 0, 0, 0, 0x42, 0, 0, 0x01, 0xCD, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0x99, 0, 0, 0, 0 },
            Assert.Single(sent));
    }

    [Fact]
    public void ProfileScrollAsksForTheProfile()
    {
        var (session, sent) = InWorld();
        session.RequestProfile(Other);
        Assert.Equal(new byte[] { 0xB8, 0, 8, 0, 0, 0, 0, 0x99 }, Assert.Single(sent));
    }

    [Fact]
    public void NothingIsSentBeforeTheWorld()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.RequestHelp();
        session.SetWarMode(true);
        session.RequestQuestMenu();
        session.RequestGuildMenu();
        session.RequestVirtueGump(Other);
        session.RequestProfile(Other);
        Assert.Empty(sent);
    }

    /// <summary>
    /// The button commands' bindings, read from SessionUiBinder.RunCommand (a UnityEngine file, so read rather than
    /// compiled): network commands call the session, local ones open their window and send nothing themselves.
    /// </summary>
    [Fact]
    public void EveryButtonCommandIsBound()
    {
        string binder = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "UOClient.Unity", "Runtime", "World", "SessionUiBinder.cs"));
        var expected = new Dictionary<string, string>
        {
            ["help"] = "_ctx.Session.RequestHelp();",
            ["options"] = "Ui.Open(new UI.OptionsWindow(",
            ["logout"] = "Ui.Open(new UI.ConfirmWindow(",
            ["journal"] = "_ctx.Hud.JournalVisible = !_ctx.Hud.JournalVisible;",
            ["quests"] = "_ctx.Session.RequestQuestMenu();",
            ["skills"] = "Ui.Open(new UI.SkillsWindow());",
            ["guild"] = "_ctx.Session.RequestGuildMenu();",
            ["war"] = "_ctx.Session.SetWarMode(!_ctx.Session.WarMode);",
            ["status"] = "Ui.Open(new UI.StatusWindow());",
        };
        var commands = new HashSet<string>();
        foreach (bool self in new[] { true, false })
            foreach (bool war in new[] { true, false })
            {
                foreach (PaperdollWindow.Button b in PaperdollWindow.Buttons(ClientVersion.Default, self, war))
                    commands.Add(b.Command);
                foreach (PaperdollWindow.Button b in PaperdollWindow.Buttons(new ClientVersion(4, 0, 11, 'c'), self, war))
                    commands.Add(b.Command);
            }
        Assert.Subset(expected.Keys.ToHashSet(), commands);
        Assert.Subset(commands, expected.Keys.ToHashSet());

        foreach (var (command, call) in expected)
        {
            int at = binder.IndexOf($"case \"{command}\":", StringComparison.Ordinal);
            Assert.True(at >= 0, $"RunCommand has no case \"{command}\"");
            int end = binder.IndexOf("break;", at, StringComparison.Ordinal);
            Assert.Contains(call, binder.Substring(at, end - at));
        }
        Assert.Contains("Ui.Command = RunCommand;", binder);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Assets", "UOClient.Unity")))
                return dir.FullName;
        throw new DirectoryNotFoundException("repo root");
    }
}
