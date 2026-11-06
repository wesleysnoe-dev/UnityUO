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
    [InlineData(4)]   // Heal
    [InlineData(29)]  // Greater Heal
    [InlineData(101)] // Animate Dead, the first necromancy spell
    [InlineData(202)] // Close Wounds
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
    public void TheClassicBooksSpellCastsFromThatBook()
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        var opened = new List<(uint Book, int First, ulong Bits)>();
        session.SpellbookContent += (b, f, bits) => opened.Add((b, f, bits));

        // A paladin book (0x2252) holding Close Wounds (bit 1) and Dispel Evil (bit 3).
        session.Inject(new PacketWriter(0xBF).UInt16(0x1B).UInt16(0x01).UInt32(Book).UInt16(0x2252).UInt16(201)
            .Byte(0x0A).Zero(7).ToArray(), true);
        (uint book, int first, ulong bits) = Assert.Single(opened);
        Assert.True(SpellbookWindow.TryGetBookInfo(first, out SpellbookInfo info));

        // The first detail spread's right page is Dispel Evil; its icon or name click casts it from this book.
        SpellbookWindow.DetailEntry right = SpellbookWindow.DetailEntries(info, bits)[1];
        Assert.Equal((2, true, 3), (right.Page, right.Right, right.Spell));
        SpellbookWindow.CastFromBook(session, book, info.First, right.Spell);

        Assert.Equal(GameSession.CastSpellFromBookRequest(204, Book), Assert.Single(NoPings(sent)));
    }

    // ModernUO's SendNewSpellbookContent (OutgoingContainerPackets.cs:46-69) byte for byte: 0xBF, length 23, sub 0x1B,
    // command 1, book serial, graphic, offset (Spellbook.BookOffset + 1), the 64 content bits low byte first.
    private static byte[] NewSpellbookContent(uint book, ushort graphic, ushort offset, ulong content)
    {
        var p = new byte[23];
        p[0] = 0xBF;
        p[2] = 23;
        p[4] = 0x1B;
        p[6] = 0x01;
        p[7] = (byte)(book >> 24);
        p[8] = (byte)(book >> 16);
        p[9] = (byte)(book >> 8);
        p[10] = (byte)book;
        p[11] = (byte)(graphic >> 8);
        p[12] = (byte)graphic;
        p[13] = (byte)(offset >> 8);
        p[14] = (byte)offset;
        for (int i = 0; i < 8; i++)
            p[15 + i] = (byte)(content >> (i * 8));
        return p;
    }

    // "spell serial" in decimal after 0x12 len 0x27, null-terminated (IncomingPlayerPackets.cs:162, TextCommand 0x27).
    private static byte[] FromBook(string text)
    {
        var p = new List<byte> { 0x12, 0x00, (byte)(4 + text.Length + 1), 0x27 };
        p.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
        p.Add(0);
        return p.ToArray();
    }

    [Theory]
    [InlineData(1, 3, "4 1073746484")]     // Heal
    [InlineData(1, 0, "1 1073746484")]     // Clumsy
    [InlineData(101, 0, "101 1073746484")] // Animate Dead
    [InlineData(201, 1, "202 1073746484")] // Close Wounds
    public void CastFromBookBytes(int first, int index, string text)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);

        SpellbookWindow.CastFromBook(session, Book, first, index);

        Assert.Equal(FromBook(text), Assert.Single(NoPings(sent)));
    }

    [Theory]
    // A full magery book (0x0EFA); a necro book (0x2253) with Animate Dead, Corpse Skin, Wither and Exorcism; a paladin
    // book (0x2252) with Cleanse by Fire, Close Wounds and Sacred Journey, plus a stray bit past its ten spells.
    [InlineData(0x0EFA, 1, ulong.MaxValue, SpellbookKind.Magery, 64, 36)]
    [InlineData(0x2253, 101, 0x1_4005UL, SpellbookKind.Necromancy, 4, 4)]
    [InlineData(0x2252, 201, 0x1_0000_0203UL, SpellbookKind.Chivalry, 3, 3)]
    public void SpellbookContentFillsTheWindowModel(int graphic, int first, ulong bits, SpellbookKind kind, int held, int maxPage)
    {
        var sent = new List<byte[]>();
        GameSession session = InWorld(sent, ClientVersion.Default);
        SpellbookState state = null;
        session.SpellbookContent += (b, f, c) =>
        {
            if (state == null)
                state = new SpellbookState(b, f, c);
            else
                state.SetContent(f, c); // SessionUiBinder: the open window takes new content in place
        };

        session.Inject(NewSpellbookContent(Book, (ushort)graphic, (ushort)first, bits), true);

        Assert.NotNull(state);
        Assert.Equal((Book, first, bits, true, kind), (state.Book, state.First, state.Bits, state.Classic, state.Info.Kind));
        Assert.Equal(held, state.Known.Count);
        Assert.All(state.Known, i => Assert.True((bits & (1UL << i)) != 0));
        Assert.Equal((maxPage, 1, true), (state.MaxPage, state.Page, state.OnIndex));

        // An index name opens the spread describing it, and that spread's detail entry is the same spell.
        SpellbookWindow.IndexEntry last = state.Index[^1];
        state.GoTo(last.ToPage);
        Assert.False(state.OnIndex);
        Assert.Contains(state.Detail, d => d.Page == state.Page && d.Spell == last.Spell);

        // The page corners stop at the book's ends.
        state.GoTo(state.Page + 100);
        Assert.Equal(maxPage, state.Page);
        state.GoTo(-3);
        Assert.Equal(1, state.Page);

        // Content sent again with only the first held spell: the book shrinks and the open spread stays inside it.
        state.GoTo(maxPage);
        ulong one = 1UL << state.Known[0];
        session.Inject(NewSpellbookContent(Book, (ushort)graphic, (ushort)first, one), true);
        Assert.Equal(new[] { state.Known[0] }, state.Known);
        Assert.Equal(state.Info.IndexPages + 1, state.MaxPage);
        Assert.Equal(state.MaxPage, state.Page);
        Assert.Empty(NoPings(sent)); // opening and paging the book sends nothing
    }

    [Fact]
    public void OtherSchoolsKeepTheListModel()
    {
        var state = new SpellbookState(Book, 401, 0x3FUL); // Bushido
        Assert.False(state.Classic);
        Assert.Equal((401, 0x3FUL), (state.First, state.Bits));
        state.SetContent(1, 0x8UL); // the same serial now sends a magery book
        Assert.True(state.Classic);
        Assert.Equal(new[] { 3 }, state.Known);
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
