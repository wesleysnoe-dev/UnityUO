using UOClient.Core;
using UOClient.IO;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// The paperdoll's button column as TazUO's PaperDollGump builds it for our 7.0.23.1 client (Quests and Guild from
/// CV_500A), the Quests / Guild requests as ModernUO's EncodedCommand reads them, and the paperdoll book decision.
/// </summary>
public sealed class PaperdollAosTests
{
    private const uint Me = 0x00000042;

    [Fact]
    public void OurPaperdollHasTheCv500AButtonColumn()
    {
        PaperdollWindow.Button[] b = PaperdollWindow.Buttons(ClientVersion.Default, self: true, war: false);
        Assert.Equal(new[] { "help", "options", "logout", "quests", "skills", "guild", "war", "status" }, b.Select(x => x.Command));
        Assert.All(b, x => Assert.Equal(185, x.X));
        Assert.Equal(new[] { 44, 71, 98, 125, 152, 179, 206, 233 }, b.Select(x => x.Y));
        Assert.Equal(new[] { 0x07EF, 2006, 2009, 22453, 2015, 22450, 0x07E5, 2027 }, b.Select(x => x.Normal));
        Assert.Equal(new[] { 0x07F0, 2007, 2010, 22455, 2016, 22452, 0x07E6, 2028 }, b.Select(x => x.Pressed));
        Assert.DoesNotContain(b, x => x.Command == "journal" || x.Command == "chat");

        PaperdollWindow.Button war = PaperdollWindow.Buttons(ClientVersion.Default, self: true, war: true)[6];
        Assert.Equal((0x07E8, 0x07E9, "war"), (war.Normal, war.Pressed, war.Command));
    }

    [Fact]
    public void BeforeCv500AItIsJournal()
    {
        Assert.True(new ClientVersion(5, 0, 0, 'a') >= PaperdollWindow.QuestsFrom);
        PaperdollWindow.Button[] old = PaperdollWindow.Buttons(new ClientVersion(4, 0, 11, 'c'), self: true, war: false);
        Assert.Equal((2012, 2013, "journal", 125), (old[3].Normal, old[3].Pressed, old[3].Command, old[3].Y));
        Assert.Equal("guild", old[5].Command); // TazUO adds Guild at every version
    }

    [Fact]
    public void SomeoneElsesPaperdollHasStatusAlone()
    {
        PaperdollWindow.Button s = Assert.Single(PaperdollWindow.Buttons(ClientVersion.Default, self: false, war: true));
        Assert.Equal((2027, 2028, 185, 233, "status"), (s.Normal, s.Pressed, s.X, s.Y, s.Command));
        Assert.Equal((135f, 195f), PaperdollWindow.OtherBarAt(200, 200, 130));
    }

    [Fact]
    public void ChromeFollowsPaperDollGumpSettings()
    {
        Assert.Equal((0x07D0, 0x07D1), (PaperdollWindow.BackgroundPlayer, PaperdollWindow.BackgroundOther));
        Assert.Equal((0x07D2, 0x0071), (PaperdollWindow.ScrollGump, PaperdollWindow.VirtueGem));
        Assert.Equal((25, 196, 80, 4), (PaperdollWindow.ProfileX, PaperdollWindow.ProfileY, PaperdollWindow.VirtueX, PaperdollWindow.VirtueY));
        Assert.Equal(25 + 30 - 4, PaperdollWindow.PartyX(30));
        Assert.Equal((8, 19, 39, 262, 185), (PaperdollWindow.BodyX, PaperdollWindow.BodyY, PaperdollWindow.TitleX, PaperdollWindow.TitleY, PaperdollWindow.TitleWidth));
    }

    [Fact]
    public void PaperdollBooksFollowCharacterListFlag0x20()
    {
        // ModernUO's ExpansionAOS character list flags: ContextMenus | AOS (0x08 | 0x20), which TazUO reads as PaperdollBooks.
        const uint aos = 0x08 | 0x20;
        Assert.True(PaperdollWindow.ShowsPaperdollBooks(self: true, aos));
        Assert.False(PaperdollWindow.ShowsPaperdollBooks(self: false, aos));
        Assert.False(PaperdollWindow.ShowsPaperdollBooks(self: true, 0x08));
        Assert.True(PaperdollWindow.ShowsRacialBook(self: true, aos, ClientVersion.Default)); // CV_7000 and later
        Assert.False(PaperdollWindow.ShowsRacialBook(self: true, aos, new ClientVersion(6, 0, 14, 2)));
    }

    [Fact]
    public void QuestsAndGuildSendTheEncodedCommandsModernUOReads()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.RequestQuestMenu(); // not in the world yet: nothing
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        sent.Clear();

        // ModernUO's EncodedCommand: serial, packet id (ushort), then the registered encoded handler (0x32 quests, 0x28 guild).
        session.RequestQuestMenu();
        session.RequestGuildMenu();
        Assert.Equal(new byte[] { 0xD7, 0, 10, 0, 0, 0, 0x42, 0, 0x32, 0x00 }, sent[0]);
        Assert.Equal(new byte[] { 0xD7, 0, 10, 0, 0, 0, 0x42, 0, 0x28, 0x0A }, sent[1]);
        Assert.Equal(2, sent.Count);
    }
}

/// <summary>The AoS status and paperdoll art in the install: what the windows size themselves to.</summary>
[Collection("UO install")]
public sealed class PaperdollAosInstallTests
{
    private readonly UOData _data;

    public PaperdollAosInstallTests(UOInstallFixture fixture) => _data = fixture.Data;

    [Fact]
    public void TheArtExists()
    {
        var status = _data.GetGump(StatusWindow.Background);
        Assert.NotNull(status);
        Assert.Equal((427, 184), (status!.Width, status.Height));
        // The corner the status turns into a health bar from sits inside the art (TazUO's minimize box is 16x16).
        Assert.True(StatusWindow.BarCornerX + 16 <= status.Width && StatusWindow.BarCornerY + 16 <= status.Height);

        foreach (bool self in new[] { true, false })
            foreach (bool war in new[] { true, false })
                foreach (PaperdollWindow.Button b in PaperdollWindow.Buttons(ClientVersion.Default, self, war))
                {
                    Assert.NotNull(_data.GetGump(b.Normal));
                    Assert.NotNull(_data.GetGump(b.Pressed));
                }
        foreach (int g in new[] { PaperdollWindow.BackgroundPlayer, PaperdollWindow.BackgroundOther, PaperdollWindow.ScrollGump,
                     PaperdollWindow.VirtueGem, StatusWindow.LockUp, StatusWindow.LockDown, StatusWindow.LockLocked })
            Assert.NotNull(_data.GetGump(g));
    }
}
