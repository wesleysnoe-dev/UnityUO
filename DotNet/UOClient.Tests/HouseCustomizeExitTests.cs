using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

/// <summary>
/// Customize mode's exit: ModernUO's BeginCustomize sends 0xBF 0x20 command 4 (HousePackets.cs:30-49); the client's Exit is
/// 0xD7 (player serial, 0x000C, 0x0A) as TazUO's Send_CustomHouseBuildingExit; ModernUO's Designer_Close ends the context
/// without committing and answers 0xBF 0x20 command 5 (HouseFoundation.cs:1731-1770).
/// </summary>
public sealed class HouseCustomizeExitTests
{
    private const uint Me = 0x00000042, House = 0x40001234, Other = 0x40005678;

    private static GameSession InWorld(List<byte[]> sent)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        sent.Clear();
        return session;
    }

    // Byte for byte ModernUO's SendBegin/EndHouseCustomization: BF 0011 0020 serial command 0000 FFFF FFFF FF.
    private static byte[] Customize(uint house, byte command) =>
        new byte[] { 0xBF, 0x00, 0x11, 0x00, 0x20, (byte)(house >> 24), (byte)(house >> 16), (byte)(house >> 8), (byte)house, command,
                     0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    private static int Exits(List<byte[]> sent) => sent.Count(p => p[0] == 0xD7);

    [Fact]
    public void TheExitIsTazUOsTenBytes()
    {
        Assert.Equal(new byte[] { 0xD7, 0x00, 0x0A, 0x00, 0x00, 0x00, 0x42, 0x00, 0x0C, 0x0A }, GameSession.HouseCustomizationExit(Me));
        Assert.Equal(17, Customize(House, 4).Length);
    }

    [Fact]
    public void BeginExitAndTheServersEnd()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        var changes = new List<(uint, bool)>();
        s.HouseCustomizationChanged += (h, b) => changes.Add((h, b));

        Assert.False(s.ExitHouseCustomization()); // not customizing: nothing sent
        Assert.Empty(sent);

        s.Inject(Customize(House, 4), true);
        Assert.Equal(House, s.CustomizingHouse);
        Assert.False(s.HouseExitPending);

        s.Update(10);
        Assert.True(s.ExitHouseCustomization());
        Assert.Equal(1, Exits(sent));
        Assert.Equal(GameSession.HouseCustomizationExit(Me), sent.Single(p => p[0] == 0xD7));
        Assert.True(s.HouseExitPending);
        Assert.Equal(House, s.CustomizingHouse); // still customizing until the server says so

        // A double click: no second request while the first is unanswered.
        Assert.False(s.ExitHouseCustomization());
        s.Update(12);
        Assert.False(s.ExitHouseCustomization());
        Assert.Equal(1, Exits(sent));

        s.Inject(Customize(House, 5), true);
        Assert.Equal(0u, s.CustomizingHouse);
        Assert.False(s.HouseExitPending);
        Assert.Equal(new[] { (House, true), (House, false) }, changes);
        Assert.False(s.ExitHouseCustomization());
        Assert.Equal(1, Exits(sent));
    }

    [Fact]
    public void NoAnswerAllowsAnotherTryLater()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.Inject(Customize(House, 4), true);
        s.Update(10);
        Assert.True(s.ExitHouseCustomization());
        s.Update(10 + GameSession.ExitRetrySeconds + 0.1);
        Assert.False(s.HouseExitPending);
        Assert.True(s.ExitHouseCustomization());
        Assert.Equal(2, Exits(sent));
    }

    [Fact]
    public void ANewBeginReplacesTheHouseAndClearsTheWait()
    {
        var sent = new List<byte[]>();
        GameSession s = InWorld(sent);
        s.Inject(Customize(House, 4), true);
        s.Update(10);
        s.ExitHouseCustomization();
        s.Inject(Customize(Other, 4), true);
        Assert.Equal(Other, s.CustomizingHouse);
        Assert.False(s.HouseExitPending);
        // An end for a house we are not customizing changes nothing.
        s.Inject(Customize(House, 5), true);
        Assert.Equal(Other, s.CustomizingHouse);
    }

    [Fact]
    public void NothingIsSentOutOfTheWorldAndNothingAutomatically()
    {
        var sent = new List<byte[]>();
        var s = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        s.StartOffline(sent.Add);
        s.Inject(Customize(House, 4), true); // before the world: kept, but no exit can be sent
        Assert.False(s.ExitHouseCustomization());
        Assert.DoesNotContain(sent, p => p[0] == 0xD7);

        var sent2 = new List<byte[]>();
        GameSession w = InWorld(sent2);
        w.Inject(Customize(House, 4), true);
        for (double t = 1; t < 30; t += 0.5)
            w.Update(t);
        Assert.Equal(0, Exits(sent2)); // the session never exits, commits or discards on its own
        Assert.DoesNotContain(sent2, p => p[0] == 0xD7);
    }

    [Fact]
    public void TheWindowPromisesNoCommit()
    {
        string text = UOClient.Unity.UI.HouseCustomizeWindow.Explanation;
        Assert.Contains("without committing", text);
        Assert.Contains("draft", text);
        Assert.DoesNotContain("Commit button", text);
    }
}
