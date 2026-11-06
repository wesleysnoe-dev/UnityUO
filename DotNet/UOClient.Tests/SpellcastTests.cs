using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

namespace UOClient.Tests;

/// <summary>
/// Casting a spell (TazUO's Send_CastSpell / Send_CastSpellFromBook, OutgoingPackets.cs:1056-1135) from an offline
/// session at our client version, checked byte for byte against ModernUO's readers: IncomingExtendedCommandPackets.CastSpell
/// (0xBF 0x1C) and IncomingPlayerPackets TextCommand 0x27 / 0x56. No server, no Unity.
/// </summary>
public sealed class SpellcastTests
{
    private const uint Me = 0x00000042, Book = 0x40001234;

    private static GameSession InWorld(List<byte[]> sent, ClientVersion version)
    {
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false, Version = version });
        session.StartOffline(sent.Add);
        session.Inject(new PacketWriter(0x1B, 37).UInt32(Me).UInt32(0).UInt16(0x190).UInt16(1495).UInt16(1629).UInt16(0)
            .Byte(0).Zero(9).UInt16(7168).UInt16(4096).Zero(6).ToArray(), false);
        session.Inject(new byte[] { 0x55 }, false);
        Assert.Equal(SessionState.InWorld, session.State);
        session.Update(1.0); // the first update pings; get it out of the way
        sent.Clear();
        return session;
    }

    private static List<byte[]> NoPings(List<byte[]> sent) => sent.Where(p => p[0] != 0x73).ToList();

    // 0xBF, length 9, sub 0x1C, 0x0002 (no book: ModernUO reads a serial only after 1), the 1-based spell number.
    private static byte[] Extended(int spell) =>
        new byte[] { 0xBF, 0x00, 0x09, 0x00, 0x1C, 0x00, 0x02, (byte)(spell >> 8), (byte)spell };

    [Theory]
    [InlineData(1)]   // Clumsy
    [InlineData(29)]  // Greater Heal
    [InlineData(101)] // Animate Dead, the first necromancy spell
    public void CastSpellSendsTheExtendedCastAtOurVersion(int spell)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);

        session.CastSpell(spell);

        Assert.Equal(Extended(spell), Assert.Single(NoPings(sent)));
        Assert.Equal(Extended(spell), GameSession.CastSpellRequest(spell, new ClientVersion(7, 0, 23, 1)));
    }

    [Fact]
    public void OlderClientsCastByTheMacroTextCommand()
    {
        // Below CV_60142 (ClientVersion.cs:36): 0x12, length, 0x56, ASCII spell number, null.
        byte[] old = { 0x12, 0x00, 0x07, 0x56, (byte)'2', (byte)'9', 0x00 };
        Assert.Equal(old, GameSession.CastSpellRequest(29, new ClientVersion(6, 0, 14, 1)));
        Assert.Equal(Extended(29), GameSession.CastSpellRequest(29, new ClientVersion(6, 0, 14, 2)));

        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, new ClientVersion(5, 0, 9, 1));
        session.CastSpell(29);
        Assert.Equal(old, Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void CastSpellFromBookSendsTheSpellAndTheBookSerial()
    {
        // ModernUO tokenizes "spell serial" and parses the serial with Utility.ToUInt32 (decimal, as TazUO writes it).
        byte[] p = GameSession.CastSpellFromBookRequest(4, Book);
        string text = $"4 {Book}";
        Assert.Equal(new byte[] { 0x12, 0x00, (byte)(4 + text.Length + 1), 0x27 }, p[..4]);
        Assert.Equal(text, System.Text.Encoding.ASCII.GetString(p, 4, text.Length));
        Assert.Equal(0, p[^1]);
    }

    [Fact]
    public void ClickingASpellInTheBookCastsFromThatBook()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        var opened = new List<(uint Book, int First, ulong Bits)>();
        session.SpellbookContent += (b, f, bits) => opened.Add((b, f, bits));

        // ModernUO's NewSpellbookContent (0xBF 0x1B): 1, book serial, graphic, first spell id, 64 bits low byte first.
        session.Inject(new PacketWriter(0xBF).UInt16(0x1B).UInt16(0x01).UInt32(Book).UInt16(0x2253).UInt16(101)
            .Byte(0x05).Zero(7).ToArray(), true);
        (uint book, int first, ulong bits) = Assert.Single(opened);
        Assert.Equal((Book, 101, 0x05UL), (book, first, bits));

        SpellbookWindow.CastFromBook(session, book, first, 2); // the window's third row: Corpse Skin, spell 103

        Assert.Equal(GameSession.CastSpellFromBookRequest(103, Book), Assert.Single(NoPings(sent)));
    }

    [Fact]
    public void NothingIsCastOutOfWorld()
    {
        var sent = new List<byte[]>();
        var session = new GameSession(new LoginSettings { AnnounceChatLinks = false });
        session.StartOffline(sent.Add);
        session.CastSpell(1);
        session.CastSpellFromBook(1, Book);
        SpellbookWindow.CastFromBook(null, Book, 1, 0);
        Assert.Empty(sent);
    }
}
