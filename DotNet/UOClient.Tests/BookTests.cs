using System.Text;
using UOClient.Core;
using UOClient.Net;

namespace UOClient.Tests;

public sealed class BookTests
{
    private const uint Book = 0x40001234;

    // ModernUO's SendBookCover: 0xD4, length, serial, 1, writable, page count, title and author (length with null, UTF-8, null).
    private static byte[] Cover(bool writable, int pages, string title, string author)
    {
        byte[] t = Encoding.UTF8.GetBytes(title), a = Encoding.UTF8.GetBytes(author);
        return new PacketWriter(0xD4).UInt32(Book).Byte(1).Bool(writable).UInt16(pages)
            .UInt16(t.Length + 1).Utf8Z(title).UInt16(a.Length + 1).Utf8Z(author).ToArray();
    }

    // ModernUO's SendBookContent: 0x66, length, serial, page count, then per page number (1-based), line count, UTF-8 lines.
    private static byte[] Content(uint serial, params string[][] pages)
    {
        PacketWriter w = new PacketWriter(0x66).UInt32(serial).UInt16(pages.Length);
        for (int i = 0; i < pages.Length; i++)
        {
            w.UInt16(i + 1).UInt16(pages[i].Length);
            foreach (string line in pages[i])
                w.Utf8Z(line);
        }
        return w.ToArray();
    }

    private static GameSession NewSession(List<BookInfo> opened)
    {
        var session = new GameSession(new LoginSettings());
        session.BookOpened += opened.Add;
        return session;
    }

    [Fact]
    public void CoverIsLaidOutAsModernUoWritesIt()
    {
        // length = 17 + title bytes + author bytes (each null counted in the 17).
        Assert.Equal(17 + 6 + 3, Cover(true, 20, "a book", "Wes").Length);
        // SendBookContent: 9 + per page 4 + per line its UTF-8 bytes and a null.
        Assert.Equal(9 + 4 + 3 + 1 + 4,Content(Book, new[] { "hi", "" }, new string[0]).Length);
        var lengths = PacketLengths.For(ClientVersion.Default);
        Assert.Equal(-1, lengths[0xD4]);
        Assert.Equal(-1, lengths[0x66]);
        Assert.Equal(99, lengths[0x93]);
    }

    [Fact]
    public void CoverThenPagesOpenTheBook()
    {
        var opened = new List<BookInfo>();
        GameSession session = NewSession(opened);
        int changed = 0;
        session.BookChanged += _ => changed++;

        session.Inject(Cover(false, 3, "Tale of Ünder", "Wes"), true);
        BookInfo book = Assert.Single(opened);
        Assert.Equal(Book, book.Serial);
        Assert.Equal("Tale of Ünder", book.Title);
        Assert.Equal("Wes", book.Author);
        Assert.False(book.Writable);
        Assert.Equal(3, book.PageCount);
        Assert.All(book.Pages, Assert.Null);

        session.Inject(Content(Book, new[] { "Once upon", "a time" }, new string[0], new[] { "The end" }), true);
        Assert.Equal(1, changed);
        Assert.Equal(new[] { "Once upon", "a time" }, book.Pages[0]);
        Assert.Empty(book.Pages[1]);
        Assert.Equal(new[] { "The end" }, book.Pages[2]);

        // Opened again: the same book, refreshed (and the window refreshed with it).
        session.Inject(Cover(true, 3, "Renamed", "Ann"), true);
        Assert.Equal(2, opened.Count);
        Assert.Same(book, opened[1]);
        Assert.True(book.Writable);
        Assert.Equal("Renamed", book.Title);
        Assert.Equal(new[] { "The end" }, book.Pages[2]);

        session.CloseBook(book);
        Assert.Empty(session.Books);
    }

    [Fact]
    public void PagesForAnUnopenedBookAreDropped()
    {
        var opened = new List<BookInfo>();
        GameSession session = NewSession(opened);
        session.Inject(Content(Book, new[] { "x" }), true);
        Assert.Empty(session.Books);
    }

    [Fact]
    public void PageNumbersOutsideTheBookAreIgnored()
    {
        var opened = new List<BookInfo>();
        GameSession session = NewSession(opened);
        session.Inject(Cover(false, 1, "t", "a"), true);
        // Page 2 of a one-page book, then page 1.
        byte[] packet = new PacketWriter(0x66).UInt32(Book).UInt16(2)
            .UInt16(2).UInt16(1).Utf8Z("nope")
            .UInt16(1).UInt16(1).Utf8Z("yes").ToArray();
        session.Inject(packet, true);
        Assert.Equal(new[] { "yes" }, session.Books[Book].Pages[0]);
    }

    [Fact]
    public void OldFixedCoverIsRead()
    {
        // 0x93 (99 bytes): serial, writable, a byte, page count, title (60), author (30).
        byte[] packet = new PacketWriter(0x93, 99).UInt32(Book).Byte(1).Byte(1).UInt16(16)
            .Ascii("Old title", 60).Ascii("Old author", 30).ToArray();
        var opened = new List<BookInfo>();
        GameSession session = NewSession(opened);
        session.Inject(packet, false);
        BookInfo book = Assert.Single(opened);
        Assert.True(book.Writable);
        Assert.Equal(16, book.PageCount);
        Assert.Equal("Old title", book.Title);
        Assert.Equal("Old author", book.Author);
    }

    [Fact]
    public void EditsChangeTheBookOnlyWhenWritable()
    {
        var opened = new List<BookInfo>();
        GameSession session = NewSession(opened);
        session.Inject(Cover(false, 2, "t", "a"), true);
        BookInfo book = opened[0];
        session.SetBookPage(book, 0, new[] { "scribble" });
        session.SetBookHeader(book, "mine", "me");
        Assert.Null(book.Pages[0]);
        Assert.Equal("t", book.Title);

        session.Inject(Cover(true, 2, "t", "a"), true);
        session.SetBookPage(book, 0, new[] { "line", "", "" });
        Assert.Equal(new[] { "line" }, book.Pages[0]);
        session.SetBookHeader(book, new string('x', 70), new string('é', 20));
        Assert.Equal(60, book.Title.Length);
        Assert.Equal(15, book.Author.Length); // 2 bytes each: 30 bytes
    }

    [Fact]
    public void PageLinesAreKeptWithinModernUoLimits()
    {
        string[] lines = GameSession.BookPageLines(new[] { "1", "2", "3", "4", "5", "6", "7", new string('z', 100), "9" });
        Assert.Equal(8, lines.Length);
        Assert.Equal(79, lines[7].Length);
        Assert.Equal(new[] { "a", "", "b" }, GameSession.BookPageLines(new[] { "a", "", "b", "", "" }));
        Assert.Empty(GameSession.BookPageLines(new[] { "", "" }));
        Assert.Equal("ab", GameSession.Utf8Prefix("ab😀", 5)); // the emoji is 4 bytes: not split
    }

    [Fact]
    public void OutgoingRequestsMatchModernUoReader()
    {
        // BookPackets.HeaderChange: serial, 4 skipped bytes, title length + UTF-8, author length + UTF-8.
        Assert.Equal(new byte[] { 0xD4, 0, 19, 0x40, 0x00, 0x12, 0x34, 0, 0, 0, 0, 0, 2, (byte)'H', (byte)'i', 0, 2, (byte)'M', (byte)'e' },
            GameSession.BookHeaderRequest(Book, "Hi", "Me"));
        // ContentChange: serial, page count, then page number, line count, null-terminated UTF-8 lines.
        Assert.Equal(new byte[] { 0x66, 0, 18, 0x40, 0x00, 0x12, 0x34, 0, 1, 0, 3, 0, 2, (byte)'a', 0, 0xC3, 0xA9, 0 },
            GameSession.BookPageContentRequest(Book, 3, new[] { "a", "é" }));
        Assert.Equal(new byte[] { 0x66, 0, 13, 0x40, 0x00, 0x12, 0x34, 0, 1, 0, 3, 0xFF, 0xFF },
            GameSession.BookPageRequest(Book, 3));
    }
}
