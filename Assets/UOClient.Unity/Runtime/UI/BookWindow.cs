using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A book (0xD4 cover, 0x66 pages), laid out like TazUO's ModernBookGump: the open-book art 0x01FE with the page-turn
    /// corners 0x01FF / 0x0200 (a double-click jumps to the first or last spread), the cover (title, "by", author) on
    /// the first left page, then two pages per spread, 8 lines each. A writable book edits in place; changed pages go to
    /// the server when the page is turned or the book closed, and a changed title or author when it is closed.
    /// </summary>
    public sealed partial class BookWindow : UiWindow
    {
        private const float LineHeight = (float)PageHeight / GameSession.BookLinesPerPage;
        private const float DoubleClickTime = 0.4f;
        private static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f), Faint = new Color(0.35f, 0.3f, 0.25f);

        private BookInfo _book;
        private int _spread;
        private string _title, _author;
        /// <summary>The lines being edited per page (0-based); filled from the book once its page has arrived.</summary>
        private string[][] _edit;
        private float _lastCornerClick;
        private int _lastCorner;

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
        private int LastSpread => LastSpreadOf(_book.PageCount);

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
                DrawPage(2 * _spread - 1);
            DrawPage(2 * _spread);

            if (_spread > 0 && TurnButton(PrevCorner, 0, 0, "<"))
                Turn(-1, PrevCorner, 0);
            if (_spread < LastSpread && TurnButton(NextCorner, NextCornerX, 0, ">"))
                Turn(1, NextCorner, LastSpread);
        }

        private bool TurnButton(int gump, float x, float y, string fallback)
        {
            if (Art.Gump(gump) != null)
                return GumpButton(gump, gump, x, y);
            return GUI.Button(new Rect(x + 4, y + 4, 22, 20), fallback);
        }

        /// <summary>A click turns one spread; a second click on the same corner (TazUO's double-click) goes to the end.</summary>
        private void Turn(int by, int corner, int end)
        {
            SaveVisible();
            bool twice = _lastCorner == corner && Time.realtimeSinceStartup - _lastCornerClick < DoubleClickTime;
            _spread = Mathf.Clamp(twice ? end : _spread + by, 0, LastSpread);
            _lastCorner = twice ? 0 : corner;
            _lastCornerClick = Time.realtimeSinceStartup;
        }

        /// <summary>The cover as TazUO's: title and author in the same boxes whether or not they can be edited.</summary>
        private void DrawCover()
        {
            var title = new Rect(CoverX, TitleY, CoverWidth, CoverHeight);
            var author = new Rect(CoverX, AuthorY, CoverWidth, CoverHeight);
            if (_book.Writable)
            {
                _title = GUI.TextField(title, _title ?? "", TitleMaxChars);
                _author = GUI.TextField(author, _author ?? "", AuthorMaxChars);
            }
            else
            {
                Text(title, _book.Title, Ink, 13);
                Text(author, _book.Author, Ink, 13);
            }
            Text(new Rect(CoverX, ByY, CoverWidth, 18), "by", Ink, 13);
        }

        private void DrawPage(int page)
        {
            if (page < 0 || page >= _book.PageCount)
                return;
            float x = PageX(page);
            (int nx, int ny) = PageNumberAt(page);
            Text(new Rect(nx, ny, 40, 16), (page + 1).ToString(), Faint, 11);

            string[] lines = _book.Pages[page];
            if (lines == null)
            {
                // Not sent yet (ModernUO sends every page with the cover, so only other servers get here).
                Session.RequestBookPage(_book, page);
                Text(new Rect(x, UpperMargin, PageWidth, LineHeight), "...", Faint, 12);
                return;
            }

            if (!_book.Writable)
            {
                for (int i = 0; i < lines.Length; i++)
                    Text(new Rect(x, UpperMargin + i * LineHeight, PageWidth, LineHeight), lines[i], Ink, 12);
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
                edit[i] = GUI.TextField(new Rect(x, UpperMargin + i * LineHeight, PageWidth, LineHeight - 1), edit[i] ?? "", LineMaxChars);
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
