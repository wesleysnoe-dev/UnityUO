using System.Text;
using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class BulletinBoardTests
{
    private const uint Board = 0x40000100;

    // ModernUO's SendBBDisplayBoard: 0x71, 38, command 0, board, name in 30 bytes (UTF-8, null-padded).
    private static byte[] Display(string name)
    {
        byte[] n = Encoding.UTF8.GetBytes(name);
        return new PacketWriter(0x71).Byte(0).UInt32(Board).Utf8(n).Zero(30 - n.Length).ToArray();
    }

    // SendBBMessage's strings: a length byte (bytes + 1, or + 2 for a body line) and the UTF-8 with its null(s).
    private static PacketWriter Str(PacketWriter w, string s, bool pad = false)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        w.Byte(b.Length + (pad ? 2 : 1)).Utf8(b).Byte(0);
        return pad ? w.Byte(0) : w;
    }

    private static byte[] Summary(uint message, uint thread, string poster, string subject, string time)
    {
        PacketWriter w = new PacketWriter(0x71).Byte(1).UInt32(Board).UInt32(message).UInt32(thread);
        Str(Str(Str(w, poster), subject), time);
        return w.ToArray();
    }

    private static byte[] Content(uint message, string poster, string subject, string time, params string[] lines)
    {
        PacketWriter w = new PacketWriter(0x71).Byte(2).UInt32(Board).UInt32(message);
        Str(Str(Str(w, poster), subject), time);
        w.UInt16(0x190).UInt16(0x83EA).Byte(2).UInt16(0x1517).UInt16(0).UInt16(0x1539).UInt16(0x0021);
        w.Byte(lines.Length);
        foreach (string line in lines)
            Str(w, line, true);
        return w.ToArray();
    }

    // 0x3C as ModernUO's SendContainerContent writes it for the board's message items (graphic 0x0EB0).
    private static byte[] Messages(params uint[] serials)
    {
        PacketWriter w = new PacketWriter(0x3C).UInt16(serials.Length);
        foreach (uint s in serials)
            w.UInt32(s).UInt16(0x0EB0).Byte(0).UInt16(1).UInt16(0).UInt16(0).Byte(0).UInt32(Board).UInt16(0);
        return w.ToArray();
    }

    [Fact]
    public void BoardOpensWithItsName()
    {
        Assert.Equal(38, Display("Bulletin board").Length);
        Assert.Equal(-1, PacketLengths.For(ClientVersion.Default)[0x71]);

        var session = new GameSession(new LoginSettings());
        var opened = new List<BulletinBoardInfo>();
        session.BulletinBoardOpened += opened.Add;
        session.Inject(Display("Brit Board"), true);
        BulletinBoardInfo board = Assert.Single(opened);
        Assert.Equal(Board, board.Serial);
        Assert.Equal("Brit Board", board.Name);

        session.Inject(Display("Brit Board"), true);
        Assert.Same(board, opened[1]);
        session.CloseBulletinBoard(board);
        Assert.Empty(session.BulletinBoards);
    }

    [Fact]
    public void SummariesListAsThreads()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(Display("Board"), true);
        BulletinBoardInfo board = session.BulletinBoards[Board];
        session.Inject(Messages(0x41, 0x42, 0x43), true);
        session.Inject(Summary(0x41, 0, "Ann", "First", "Day 1 @ 10:00"), true);
        session.Inject(Summary(0x43, 0, "Bob", "Second", "Day 2 @ 11:00"), true);
        session.Inject(Summary(0x42, 0x41, "Wes", "RE: First", "Day 3 @ 12:00"), true);

        Assert.Equal("Ann", board.Posts[0x41].Poster);
        Assert.Equal("Day 1 @ 10:00", board.Posts[0x41].Time);
        Assert.Null(board.Posts[0x41].Lines);
        var threads = session.BulletinThreads(board);
        Assert.Equal(new uint[] { 0x41, 0x42, 0x43 }, threads.Select(t => t.Post.Serial));
        Assert.Equal(new[] { 0, 1, 0 }, threads.Select(t => t.Depth));

        // 0x41 deleted (0x1D): it leaves the list and its reply becomes a thread of its own.
        session.Inject(new PacketWriter(0x1D, 5).UInt32(0x41).ToArray(), false);
        threads = session.BulletinThreads(board);
        Assert.Equal(new uint[] { 0x43, 0x42 }, threads.Select(t => t.Post.Serial)); // summaries' arrival order
        Assert.All(threads, t => Assert.Equal(0, t.Depth));
    }

    [Fact]
    public void ContentOpensTheMessage()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(Display("Board"), true);
        var read = new List<BulletinPost>();
        session.BulletinPostOpened += (_, p) => read.Add(p);
        session.Inject(Summary(0x41, 0, "Ann", "Hi", "t"), true);
        session.Inject(Content(0x41, "Ann", "Hi", "t", "Hello there", "", "Ünder"), true);

        BulletinPost post = Assert.Single(read);
        Assert.Same(session.BulletinBoards[Board].Posts[0x41], post);
        Assert.Equal(new[] { "Hello there", "", "Ünder" }, post.Lines);
        Assert.Equal(0x190, post.Body);
        Assert.Equal(0x83EA, post.Hue);
        Assert.Equal(new (ushort, ushort)[] { (0x1517, 0), (0x1539, 0x21) }, post.Equipment);
    }

    [Fact]
    public void MessagesForAnUnopenedBoardAreDropped()
    {
        var session = new GameSession(new LoginSettings());
        int read = 0;
        session.BulletinPostOpened += (_, _) => read++;
        session.Inject(Summary(0x41, 0, "Ann", "Hi", "t"), true);
        session.Inject(Content(0x41, "Ann", "Hi", "t", "x"), true);
        Assert.Empty(session.BulletinBoards);
        Assert.Equal(0, read);
    }

    [Fact]
    public void OutgoingRequestsMatchModernUoReader()
    {
        // BBClientRequest: command, board, then the message for 3 (read), 4 (summary) and 6 (remove).
        Assert.Equal(new byte[] { 0x71, 0, 12, 4, 0x40, 0, 1, 0, 0, 0, 0, 0x41 }, GameSession.BulletinRequest(4, Board, 0x41));
        // BBPostMessage: board, thread, subject (length with null), line count, lines (length with null).
        Assert.Equal(new byte[] { 0x71, 0, 22, 5, 0x40, 0, 1, 0, 0, 0, 0, 0x41, 3, (byte)'H', (byte)'i', 0, 2, 2, (byte)'a', 0, 1, 0 },
            GameSession.BulletinPostRequest(Board, 0x41, "Hi", new[] { "a", "" }));
        Assert.Null(GameSession.BulletinPostRequest(Board, 0, "", new[] { "a" }));
        Assert.Null(GameSession.BulletinPostRequest(Board, 0, "s", new string[0]));
        Assert.Equal(new[] { "one", "", "two" }, GameSession.BulletinLines("one\r\n\r\ntwo\n\n"));
        Assert.Empty(GameSession.BulletinLines("\n"));
        Assert.Equal(255, GameSession.BulletinLines(new string('\n', 300) + "x").Length);
        byte[] longSubject = GameSession.BulletinPostRequest(Board, 0, new string('é', 200), new[] { "a" })!;
        Assert.Equal(253, longSubject[12]); // 253 bytes hold 126 two-byte characters (252), plus the null
    }
}
