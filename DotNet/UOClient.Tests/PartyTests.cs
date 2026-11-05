using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class PartyTests
{
    private const uint Me = 0x00000042, Bob = 0x00000077, Ann = 0x00000088;

    // ModernUO's PartyPackets: 0xBF, length, 0x0006, command ...
    private static PacketWriter Party(int command) => new PacketWriter(0xBF).UInt16(0x06).Byte(command);

    // ... member list: count, serials (7 + 4n).
    private static byte[] MemberList(params uint[] members)
    {
        PacketWriter w = Party(0x01).Byte(members.Length);
        foreach (uint m in members)
            w.UInt32(m);
        return w.ToArray();
    }

    // ... removal: count of those left, the removed serial, those left (11 + 4n).
    private static byte[] Removed(uint removed, params uint[] left)
    {
        PacketWriter w = Party(0x02).Byte(left.Length).UInt32(removed);
        foreach (uint m in left)
            w.UInt32(m);
        return w.ToArray();
    }

    private static GameSession NewSession(out List<uint> invites, out int[] changes)
    {
        var session = new GameSession(new LoginSettings());
        session.World.Mobiles[Bob] = new Mobile { Serial = Bob, Name = "Bob" };
        session.World.Mobiles[Ann] = new Mobile { Serial = Ann, Name = "Ann" };
        var i = invites = new List<uint>();
        var c = changes = new int[1];
        session.PartyInvited += i.Add;
        session.PartyChanged += () => c[0]++;
        return session;
    }

    [Fact]
    public void PacketsAreLaidOutAsModernUoWritesThem()
    {
        Assert.Equal(7 + 3 * 4, MemberList(Bob, Me, Ann).Length);
        Assert.Equal(11 + 2 * 4, Removed(Ann, Bob, Me).Length);
        Assert.Equal(11, Removed(Me).Length);
        // GetPartyTextMessagePacketLength: 12 + 2 per char (the null included in the 12).
        Assert.Equal(12 + 2 * 2, Party(0x04).UInt32(Bob).UnicodeZ("hi").ToArray().Length);
        Assert.Equal(10, Party(0x07).UInt32(Bob).ToArray().Length);
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0xBF]);
    }

    [Fact]
    public void InvitationThenMemberListThenLeave()
    {
        GameSession session = NewSession(out var invites, out var changes);

        session.Inject(Party(0x07).UInt32(Bob).ToArray(), true);
        Assert.Equal(Bob, Assert.Single(invites));
        Assert.Equal(Bob, session.PartyInviter);
        Assert.False(session.InParty);

        // Accepted: ModernUO's OnAccept sends everyone the list, leader first.
        session.Inject(MemberList(Bob, Me), true);
        Assert.True(session.InParty);
        Assert.Equal(new[] { Bob, Me }, session.PartyMembers);
        Assert.Equal(Bob, session.PartyLeader);
        Assert.Equal(0u, session.PartyInviter);

        session.Inject(MemberList(Bob, Me, Ann), true);
        Assert.Equal(3, session.PartyMembers.Count);

        // Ann removed: those left follow the removed serial.
        session.Inject(Removed(Ann, Bob, Me), true);
        Assert.Equal(new[] { Bob, Me }, session.PartyMembers);

        // We left (or the party disbanded): an empty party.
        session.Inject(Removed(Me), true);
        Assert.False(session.InParty);
        Assert.Equal(0u, session.PartyLeader);
        Assert.Equal(5, changes[0]);
    }

    [Fact]
    public void DeclinedInvitationIsDropped()
    {
        GameSession session = NewSession(out _, out _);
        session.Inject(Party(0x07).UInt32(Bob).ToArray(), true);
        // Party.OnDecline (and the 30-second DeclineTimer) send the candidate an empty removal.
        session.Inject(Removed(Me), true);
        Assert.Equal(0u, session.PartyInviter);
        Assert.False(session.InParty);
    }

    [Fact]
    public void PartyOfOneIsNoParty()
    {
        GameSession session = NewSession(out _, out _);
        session.Inject(MemberList(Me), true);
        Assert.False(session.InParty);
    }

    [Fact]
    public void MessagesGoToTheJournalAsParty()
    {
        GameSession session = NewSession(out _, out _);
        session.Inject(MemberList(Bob, Me), true);

        session.Inject(Party(0x04).UInt32(Bob).UnicodeZ("heal me").ToArray(), true);
        session.Inject(Party(0x03).UInt32(Bob).UnicodeZ("just you").ToArray(), true);

        JournalEntry all = session.World.Journal[^2], whisper = session.World.Journal[^1];
        Assert.Equal("heal me", all.Text);
        Assert.Equal("[Party][Bob]", all.Speaker);
        Assert.Equal(GameSession.PartyMessageType, all.Type);
        Assert.Equal(GameSession.PartyHue, all.Hue);
        Assert.Equal(Bob, all.Serial);
        Assert.Equal("just you", whisper.Text);
        Assert.Equal("[Party][Bob] (private)", whisper.Speaker);

        // Bob walks out of range: his messages keep his name.
        session.World.Mobiles.Remove(Bob);
        session.Inject(Party(0x04).UInt32(Bob).UnicodeZ("where?").ToArray(), true);
        Assert.Equal("[Party][Bob]", session.World.Journal[^1].Speaker);
    }

    [Fact]
    public void OutgoingRequestsMatchModernUoReader()
    {
        // IncomingExtendedCommandPackets.PartyMessage: 0xBF, length, 0x0006, command, then per command.
        Assert.Equal(new byte[] { 0xBF, 0, 10, 0, 6, 0x01, 0, 0, 0, 0 }, GameSession.PartyAddRequest());
        Assert.Equal(new byte[] { 0xBF, 0, 10, 0, 6, 0x02, 0, 0, 0, 0x77 }, GameSession.PartyRemoveRequest(Bob));
        Assert.Equal(new byte[] { 0xBF, 0, 7, 0, 6, 0x06, 1 }, GameSession.PartyCanLootRequest(true));
        Assert.Equal(new byte[] { 0xBF, 0, 7, 0, 6, 0x06, 0 }, GameSession.PartyCanLootRequest(false));
        Assert.Equal(new byte[] { 0xBF, 0, 10, 0, 6, 0x08, 0, 0, 0, 0x77 }, GameSession.PartyAcceptRequest(Bob));
        Assert.Equal(new byte[] { 0xBF, 0, 10, 0, 6, 0x09, 0, 0, 0, 0x77 }, GameSession.PartyDeclineRequest(Bob));
        // Text is ReadBigUniSafe: big-endian UTF-16 up to a null.
        Assert.Equal(new byte[] { 0xBF, 0, 12, 0, 6, 0x04, 0, (byte)'h', 0, (byte)'i', 0, 0 }, GameSession.PartyPublicMessageRequest("hi"));
        Assert.Equal(new byte[] { 0xBF, 0, 16, 0, 6, 0x03, 0, 0, 0, 0x77, 0, (byte)'h', 0, (byte)'i', 0, 0 },
            GameSession.PartyPrivateMessageRequest(Bob, "hi"));
    }

    [Theory]
    [InlineData("/accept", GameSession.PartyLineCommand.Accept, 0, "")]
    [InlineData("/Decline", GameSession.PartyLineCommand.Decline, 0, "")]
    [InlineData("/add", GameSession.PartyLineCommand.Add, 0, "")]
    [InlineData("/quit", GameSession.PartyLineCommand.Quit, 0, "")]
    [InlineData("/leave", GameSession.PartyLineCommand.Quit, 0, "")]
    [InlineData("/hello all", GameSession.PartyLineCommand.Say, 0, "hello all")]
    [InlineData("/2 just you", GameSession.PartyLineCommand.Say, 2, "just you")]
    [InlineData("/10 last one", GameSession.PartyLineCommand.Say, 10, "last one")]
    [InlineData("/11 too many", GameSession.PartyLineCommand.Say, 0, "11 too many")]
    [InlineData("/2", GameSession.PartyLineCommand.Say, 0, "2")]
    [InlineData("/", GameSession.PartyLineCommand.None, 0, "")]
    [InlineData("hello", GameSession.PartyLineCommand.None, 0, "")]
    public void PartyLinesParseLikeClassicUo(string line, GameSession.PartyLineCommand command, int member, string text)
    {
        var parsed = GameSession.ParsePartyLine(line);
        Assert.Equal(command, parsed.Command);
        Assert.Equal(member, parsed.Member);
        Assert.Equal(text, parsed.Text);
    }
}
