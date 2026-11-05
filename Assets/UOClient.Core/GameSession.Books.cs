using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A book the server opened (0xD4, or 0x93 from older servers), with the pages it has sent (0x66).</summary>
    public sealed class BookInfo
    {
        public uint Serial;
        public string Title = "", Author = "";
        /// <summary>The server lets us change it (ModernUO: a writable book within one tile).</summary>
        public bool Writable;
        /// <summary>The lines of each page, 0-based; null for a page the server has not sent yet.</summary>
        public string[][] Pages = Array.Empty<string[]>();

        public int PageCount => Pages.Length;
    }

    /// <summary>Books: 0xD4/0x93 cover and 0x66 pages, as ModernUO's BookPackets write and read them.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The most bytes of title and author ModernUO's HeaderChange takes (it drops the whole change past them).</summary>
        public const int MaxBookTitleBytes = 60, MaxBookAuthorBytes = 30;
        /// <summary>ModernUO's ContentChange takes at most 8 lines a page, each shorter than 80 characters.</summary>
        public const int BookLinesPerPage = 8, MaxBookLineLength = 79;

        /// <summary>Open books by serial.</summary>
        public readonly Dictionary<uint, BookInfo> Books = new Dictionary<uint, BookInfo>();

        /// <summary>A book's cover arrived: open its window (a second cover for the same book updates it).</summary>
        public event Action<BookInfo> BookOpened;
        /// <summary>Pages arrived for an open book.</summary>
        public event Action<BookInfo> BookChanged;

        private void RegisterBookHandlers()
        {
            _handlers[0xD4] = r => BookHeader(r, false);
            _handlers[0x93] = r => BookHeader(r, true);
            _handlers[0x66] = BookPages;
        }

        /// <summary>
        /// 0xD4 as ModernUO's SendBookCover writes it: serial, 1, writable, page count, then title and author, each a
        /// length (null included) and UTF-8. The old fixed 0x93 (99 bytes; ModernUO only reads it) is serial, writable,
        /// a skipped byte, page count, title (60 bytes) and author (30). Read as ClassicUO's OpenBook reads both.
        /// </summary>
        private void BookHeader(PacketReader r, bool old)
        {
            uint serial = r.ReadUInt32();
            bool writable = r.ReadBool();
            if (old)
                r.Skip(1);
            else
                writable = r.ReadBool();
            int pages = r.ReadUInt16();
            string title = old ? r.ReadUtf8(60) : r.ReadUtf8(r.ReadUInt16());
            string author = old ? r.ReadUtf8(30) : r.ReadUtf8(r.ReadUInt16());

            if (!Books.TryGetValue(serial, out BookInfo book))
                Books[serial] = book = new BookInfo { Serial = serial };
            book.Title = title;
            book.Author = author;
            book.Writable = writable;
            if (book.Pages.Length != pages)
            {
                var resized = new string[pages][];
                Array.Copy(book.Pages, resized, Math.Min(pages, book.Pages.Length));
                book.Pages = resized;
            }
            BookOpened?.Invoke(book);
        }

        /// <summary>
        /// 0x66 as ModernUO's SendBookContent writes it: serial, page count, then per page its number (1-based), line
        /// count and the lines as null-terminated UTF-8. ModernUO sends every page right after the cover. Pages for a
        /// book with no cover are dropped, as ClassicUO does with no book gump open.
        /// </summary>
        private void BookPages(PacketReader r)
        {
            if (!Books.TryGetValue(r.ReadUInt32(), out BookInfo book))
                return;
            int count = r.ReadUInt16();
            for (int i = 0; i < count && r.Remaining > 0; i++)
            {
                int page = r.ReadUInt16() - 1;
                int lineCount = r.ReadUInt16();
                var lines = new string[lineCount];
                for (int j = 0; j < lineCount; j++)
                    lines[j] = r.ReadUtf8Z();
                if (page >= 0 && page < book.Pages.Length)
                    book.Pages[page] = lines;
            }
            BookChanged?.Invoke(book);
        }

        /// <summary>The window closed: forget the book (the server sends it whole again when it is next opened).</summary>
        public void CloseBook(BookInfo book)
        {
            if (book != null && Books.TryGetValue(book.Serial, out BookInfo open) && open == book)
                Books.Remove(book.Serial);
        }

        /// <summary>Asks for a page the server has not sent (0x66 with 0xFFFF lines, as ClassicUO asks).</summary>
        public void RequestBookPage(BookInfo book, int page)
        {
            if (book == null || page < 0 || page >= book.PageCount || book.Pages[page] != null || State != SessionState.InWorld)
                return;
            _conn.Send(BookPageRequest(book.Serial, page + 1));
        }

        /// <summary>Renames a writable book; sent only when something changed. The server does not answer.</summary>
        public void SetBookHeader(BookInfo book, string title, string author)
        {
            if (book == null || !book.Writable)
                return;
            title = Utf8Prefix((title ?? "").Replace("\n", " "), MaxBookTitleBytes);
            author = Utf8Prefix((author ?? "").Replace("\n", " "), MaxBookAuthorBytes);
            if (title == book.Title && author == book.Author)
                return;
            book.Title = title;
            book.Author = author;
            if (State == SessionState.InWorld)
                _conn.Send(BookHeaderRequest(book.Serial, title, author));
        }

        /// <summary>Rewrites one page of a writable book (0-based); sent only when something changed.</summary>
        public void SetBookPage(BookInfo book, int page, IReadOnlyList<string> lines)
        {
            if (book == null || !book.Writable || page < 0 || page >= book.PageCount)
                return;
            string[] fixedLines = BookPageLines(lines);
            string[] old = book.Pages[page];
            if (old != null && BookPageLines(old).SequenceEqual(fixedLines))
                return;
            book.Pages[page] = fixedLines;
            if (State == SessionState.InWorld)
                _conn.Send(BookPageContentRequest(book.Serial, page + 1, fixedLines));
        }

        /// <summary>
        /// A page as ModernUO takes it: at most 8 lines, each under 80 characters, trailing empty lines dropped (as
        /// ClassicUO sends only the lines in use).
        /// </summary>
        public static string[] BookPageLines(IReadOnlyList<string> lines)
        {
            var result = new List<string>();
            for (int i = 0; i < Math.Min(BookLinesPerPage, lines?.Count ?? 0); i++)
            {
                string line = (lines[i] ?? "").Replace("\r", "").Replace("\n", " ");
                result.Add(line.Length > MaxBookLineLength ? line.Substring(0, MaxBookLineLength) : line);
            }
            while (result.Count > 0 && result[result.Count - 1].Length == 0)
                result.RemoveAt(result.Count - 1);
            return result.ToArray();
        }

        /// <summary>The longest start of <paramref name="s"/> whose UTF-8 fits in <paramref name="maxBytes"/>, whole characters only.</summary>
        public static string Utf8Prefix(string s, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(s) <= maxBytes)
                return s;
            int bytes = 0, i = 0;
            while (i < s.Length)
            {
                int n = char.IsSurrogatePair(s, i) ? 2 : 1;
                int size = Encoding.UTF8.GetByteCount(s.Substring(i, n));
                if (bytes + size > maxBytes)
                    break;
                bytes += size;
                i += n;
            }
            return s.Substring(0, i);
        }

        /// <summary>
        /// 0xD4 as ModernUO's HeaderChange reads it: serial, 4 skipped bytes (flags and page count), then title and
        /// author, each a length and that many UTF-8 bytes (no null, as ClassicUO sends; ModernUO drops a title over 60
        /// bytes or an author over 30).
        /// </summary>
        public static byte[] BookHeaderRequest(uint serial, string title, string author)
        {
            byte[] t = Encoding.UTF8.GetBytes(title ?? ""), a = Encoding.UTF8.GetBytes(author ?? "");
            return new PacketWriter(0xD4).UInt32(serial).Byte(0).Byte(0).UInt16(0)
                .UInt16(t.Length).Utf8(t).UInt16(a.Length).Utf8(a).ToArray();
        }

        /// <summary>0x66 as ModernUO's ContentChange reads it: serial, 1 page, its number (1-based), line count, lines (UTF-8, null-terminated).</summary>
        public static byte[] BookPageContentRequest(uint serial, int page, IReadOnlyList<string> lines)
        {
            PacketWriter w = new PacketWriter(0x66).UInt32(serial).UInt16(1).UInt16(page).UInt16(lines.Count);
            foreach (string line in lines)
                w.Utf8Z(line);
            return w.ToArray();
        }

        /// <summary>0x66 page request: serial, 1 page, its number, 0xFFFF lines (ModernUO ignores it: more than 8 lines).</summary>
        public static byte[] BookPageRequest(uint serial, int page) =>
            new PacketWriter(0x66).UInt32(serial).UInt16(1).UInt16(page).UInt16(0xFFFF).ToArray();
    }
}
