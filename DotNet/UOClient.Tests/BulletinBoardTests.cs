using System.Text;
using UOClient.Core;
using UOClient.Net;
using UOClient.Unity.UI;

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
    public void SummariesListInArrivalOrderAsTazUo()
    {
        var session = new GameSession(new LoginSettings());
        session.Inject(Display("Board"), true);
        BulletinBoardInfo board = session.BulletinBoards[Board];
        session.Inject(Messages(0x41, 0x42, 0x43, 0x44), true);
        session.Inject(Summary(0x41, 0, "Ann", "First", "Day 1 @ 10:00"), true);
        session.Inject(Summary(0x43, 0, "Bob", "Second", "Day 2 @ 11:00"), true);
        session.Inject(Summary(0x42, 0x41, "Wes", "RE:First", "Day 3 @ 12:00"), true);

        Assert.Equal("Ann", board.Posts[0x41].Poster);
        Assert.Equal("Day 1 @ 10:00", board.Posts[0x41].Time);
        Assert.Null(board.Posts[0x41].Lines);
        // BulletinBoardGump.AddBulletinObject: one flat list, replies not nested, rows "poster - subject - time".
        Assert.Equal(new uint[] { 0x41, 0x43, 0x42 }, session.BulletinSummaries(board).Select(p => p.Serial));
        Assert.Equal("Ann - First - Day 1 @ 10:00", board.Posts[0x41].Summary);
        // 0x44 is on the board but has no summary yet: not listed.
        Assert.DoesNotContain(session.BulletinSummaries(board), p => p.Serial == 0x44);

        // A summary sent again replaces its row at the end of the list.
        session.Inject(Summary(0x41, 0, "Ann", "First (edited)", "Day 1 @ 10:00"), true);
        Assert.Equal(new uint[] { 0x43, 0x42, 0x41 }, session.BulletinSummaries(board).Select(p => p.Serial));

        // 0x41 deleted (0x1D): it leaves the list; its reply stays where it was.
        session.Inject(new PacketWriter(0x1D, 5).UInt32(0x41).ToArray(), false);
        Assert.Equal(new uint[] { 0x43, 0x42 }, session.BulletinSummaries(board).Select(p => p.Serial));

        // Opened again: the list starts over.
        session.Inject(Display("Board"), true);
        Assert.Empty(session.BulletinSummaries(board));
    }

    [Fact]
    public void RepliesAndOwnMessagesAsTazUo()
    {
        // BulletinBoardItem: Reply starts the subject with TazLang "re" ("RE:"), even on a reply.
        Assert.Equal("RE:First", GameSession.BulletinReplySubject("First"));
        Assert.Equal("RE:RE:First", GameSession.BulletinReplySubject("RE:First"));

        // BulletinBoardData: variant 2 (Remove) when the poster is the player's name, else 1 (Reply).
        var session = new GameSession(new LoginSettings());
        Assert.False(session.IsOwnBulletinPost(new BulletinPost { Poster = "Ann" })); // no player yet
        Assert.False(session.IsOwnBulletinPost(null));
    }

    // TazUO's BulletinBoardGump and BulletinBoardItem, as built.
    [Fact]
    public void LayoutIsTazUosBoardGump()
    {
        Assert.Equal(0x087A, BulletinBoardWindow.Background);
        Assert.Equal((159, 36, 170), (BulletinBoardWindow.NameX, BulletinBoardWindow.NameY, BulletinBoardWindow.NameWidth));
        Assert.Equal((15, 170, 80), (BulletinBoardWindow.PostX, BulletinBoardWindow.PostY, BulletinBoardWindow.PostSize));
        Assert.Equal((127, 159, 241), (BulletinBoardWindow.ListX, BulletinBoardWindow.ListY, BulletinBoardWindow.ListWidth));
        Assert.Equal(18 * 9, BulletinBoardWindow.RowHeight * BulletinBoardWindow.ListRows);
        Assert.Equal((0x1523, 230, 23, 1), (BulletinBoardWindow.RowIcon, BulletinBoardWindow.RowWidth, BulletinBoardWindow.RowTextX, BulletinBoardWindow.RowTextY));
        Assert.Equal((1280 / 2 - 245f, 720 / 2 - 205f), BulletinBoardWindow.OpenAt(1280, 720));

        Assert.Equal((0x0820, 408, 0x082E), (BulletinItemLayout.Scroll, BulletinItemLayout.ScrollHeight, BulletinItemLayout.Expander));
        Assert.Equal((0x0883, 0x0886, 0x0884, 0x0885),
            (BulletinItemLayout.PostHeader, BulletinItemLayout.PostButton, BulletinItemLayout.ReplyButton, BulletinItemLayout.RemoveButton));
        Assert.Equal((97, 12), BulletinItemLayout.PostHeaderAt);
        Assert.Equal((37, 235, 50), (BulletinItemLayout.LeftButtonX, BulletinItemLayout.RemoveButtonX, BulletinItemLayout.ButtonUp));
        Assert.Equal((30, 40, 58, 77), (BulletinItemLayout.LabelX, BulletinItemLayout.AuthorY, BulletinItemLayout.DateY, BulletinItemLayout.TitleY));
        Assert.Equal((0x0835, 30, 106, 235, 4), (BulletinItemLayout.Divider, BulletinItemLayout.DividerX, BulletinItemLayout.DividerY,
            BulletinItemLayout.DividerWidth, BulletinItemLayout.DividerHeight));
        Assert.Equal((40, 120, 220, 272, 224), (BulletinItemLayout.TextX, BulletinItemLayout.TextY, BulletinItemLayout.TextWidth,
            BulletinItemLayout.TextAreaWidth, BulletinItemLayout.TextAreaHeight));
        Assert.Equal(((40, 40), (400, 335)), (BulletinItemLayout.ReadAt, BulletinItemLayout.ComposeAt));
    }

    [Fact]
    public void ScrollPiecesStackAsTazUosExpandableScroll()
    {
        // Made-up part sizes: top 300x60, right edge 250x30, middle 240x30, bottom 280x40, expander 20x10.
        var p = BulletinItemLayout.ScrollPieces(408, (300, 60), (250, 30), (240, 30), (280, 40), (20, 10));
        Assert.Equal(300, p.Width); // the widest part
        Assert.Equal((0, 0), (p.Top.X, p.Top.Y));
        Assert.Equal(((300 - 250) / 2, 60, 408 - 60 - 40 - 10), (p.Middle.X, p.Middle.Y, p.Middle.Height));
        Assert.Equal((20 / 2 + 20 / 4, 408 - 40 - 10), (p.Bottom.X, p.Bottom.Y)); // off = top - bottom widths
        Assert.Equal(((300 - 20) >> 1, 408 - 10 - 2), (p.Expander.X, p.Expander.Y));
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
