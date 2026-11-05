using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A book (0xD4 cover, 0x66 pages), laid out like ClassicUO's book gump: the open-book art 0x01FE with the page-turn
    /// corners 0x01FF / 0x0200, the cover (title and author) on the first left page, then two pages per spread, 8 lines
    /// each. A writable book edits in place; changed pages go to the server when the page is turned or the book closed,
    /// and a changed title or author when it is closed.
    /// </summary>
    public sealed class BookWindow : UiWindow
    {
        private const int Background = 0x01FE, PrevCorner = 0x01FF, NextCorner = 0x0200;
        private const float LeftX = 38, RightX = 223, PageWidth = 155, TopY = 32, LineHeight = 18;
        private static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f), Faint = new Color(0.35f, 0.3f, 0.25f);

        private BookInfo _book;
        private int _spread;
        private string _title, _author;
        /// <summary>The lines being edited per page (0-based); filled from the book once its page has arrived.</summary>
        private string[][] _edit;

        public BookWindow(BookInfo book)
        {
            Rect = new Rect(200, 100, 382, 230);
            SetBook(book);
        }

        public override string Key => KeyFor(_book.Serial);

        public static string KeyFor(uint serial) => "book:" + serial;

        public override bool StillValid => Session.Books.TryGetValue(_book.Serial, out BookInfo open) && open == _book;

        /// <summary>The server sent the cover again (opened while open, or renamed): take its title, author and size.</summary>
        public void SetBook(BookInfo book)
        {
            _book = book;
            _title = book.Title;
            _author = book.Author;
            if (_edit == null || _edit.Length != book.PageCount)
            {
                var edit = new string[book.PageCount][];
                if (_edit != null)
                    System.Array.Copy(_edit, edit, Mathf.Min(_edit.Length, edit.Length));
                _edit = edit;
            }
            _spread = Mathf.Min(_spread, LastSpread);
        }

        /// <summary>Spread 0 is the cover and page 1; spread n holds pages 2n and 2n+1.</summary>
        private int LastSpread => _book.PageCount / 2;

        public override void OnClosed()
        {
            SaveVisible();
            if (_book.Writable)
                Session.SetBookHeader(_book, _title, _author);
            Session.CloseBook(_book);
        }

        public override void Draw()
        {
            UiImage bg = Art.Gump(Background);
            if (bg != null)
            {
                Rect.width = bg.Width;
                Rect.height = bg.Height;
                DrawImage(bg, 0, 0);
            }
            else
            {
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            }

            if (_spread == 0)
                DrawCover();
            else
                DrawPage(2 * _spread - 1, LeftX);
            DrawPage(2 * _spread, RightX);

            if (_spread > 0 && TurnButton(PrevCorner, 0, 0, "<"))
                Turn(-1);
            if (_spread < LastSpread && TurnButton(NextCorner, Rect.width - 26, 0, ">"))
                Turn(1);
        }

        private bool TurnButton(int gump, float x, float y, string fallback)
        {
            if (Art.Gump(gump) != null)
                return GumpButton(gump, gump, x, y);
            return GUI.Button(new Rect(x + 4, y + 4, 22, 20), fallback);
        }

        private void Turn(int by)
        {
            SaveVisible();
            _spread = Mathf.Clamp(_spread + by, 0, LastSpread);
        }

        private void DrawCover()
        {
            if (_book.Writable)
            {
                Text(new Rect(LeftX, TopY, PageWidth, 18), "Title", Faint, 12);
                _title = GUI.TextField(new Rect(LeftX, TopY + 20, PageWidth, 20), _title ?? "", GameSession.MaxBookTitleBytes);
                Text(new Rect(LeftX, TopY + 80, PageWidth, 18), "by", Faint, 12);
                _author = GUI.TextField(new Rect(LeftX, TopY + 100, PageWidth, 20), _author ?? "", GameSession.MaxBookAuthorBytes);
            }
            else
            {
                Text(new Rect(LeftX, TopY + 20, PageWidth, 54), _book.Title, Ink, 13, TextAnchor.UpperCenter, true);
                Text(new Rect(LeftX, TopY + 80, PageWidth, 18), "by", Faint, 12, TextAnchor.UpperCenter);
                Text(new Rect(LeftX, TopY + 100, PageWidth, 20), _book.Author, Ink, 12, TextAnchor.UpperCenter);
            }
        }

        private void DrawPage(int page, float x)
        {
            if (page < 0 || page >= _book.PageCount)
                return;
            Text(new Rect(x, Rect.height - 36, PageWidth, 16), (page + 1).ToString(), Faint, 11, TextAnchor.UpperCenter);

            string[] lines = _book.Pages[page];
            if (lines == null)
            {
                // Not sent yet (ModernUO sends every page with the cover, so only other servers get here).
                Session.RequestBookPage(_book, page);
                Text(new Rect(x, TopY, PageWidth, 18), "...", Faint, 12);
                return;
            }

            if (!_book.Writable)
            {
                for (int i = 0; i < lines.Length; i++)
                    Text(new Rect(x, TopY + i * LineHeight, PageWidth, LineHeight), lines[i], Ink, 12);
                return;
            }

            string[] edit = _edit[page];
            if (edit == null)
            {
                edit = _edit[page] = new string[GameSession.BookLinesPerPage];
                for (int i = 0; i < edit.Length; i++)
                    edit[i] = i < lines.Length ? lines[i] : "";
            }
            for (int i = 0; i < edit.Length; i++)
                edit[i] = GUI.TextField(new Rect(x, TopY + i * LineHeight, PageWidth, LineHeight - 1), edit[i] ?? "", GameSession.MaxBookLineLength);
        }

        /// <summary>Sends the pages on view that were edited (the session skips unchanged ones).</summary>
        private void SaveVisible()
        {
            if (!_book.Writable)
                return;
            for (int page = 2 * _spread - 1; page <= 2 * _spread; page++)
                if (page >= 0 && page < _edit.Length && _edit[page] != null)
                    Session.SetBookPage(_book, page, _edit[page]);
        }
    }
}
