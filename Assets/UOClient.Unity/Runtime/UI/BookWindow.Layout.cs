namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout.
    public sealed partial class BookWindow
    {
        /// <summary>TazUO's ModernBookGump: the open book 0x01FE, the back corner 0x01FF at 0,0 and the forward one 0x0200.</summary>
        public const int Background = 0x01FE, PrevCorner = 0x01FF, NextCorner = 0x0200;
        public const int NextCornerX = 356;
        /// <summary>The pages: LEFT_X, RIGHT_X, UPPER_MARGIN, the text box width and PAGE_HEIGHT.</summary>
        public const int LeftX = 38, RightX = 223, UpperMargin = 34, PageWidth = 156, PageHeight = 166;
        /// <summary>The cover's title and author entries (155 x 25) and the "by" between them.</summary>
        public const int CoverX = 40, TitleY = 60, ByY = 130, AuthorY = 160, CoverWidth = 155, CoverHeight = 25;
        /// <summary>TazUO's entry limits: 47 characters of title, 29 of author, 53 a line (MAX_BOOK_CHARS_PER_LINE).</summary>
        public const int TitleMaxChars = 47, AuthorMaxChars = 29, LineMaxChars = 53;
        /// <summary>Page numbers sit 80 in from the page's left edge, 200 down.</summary>
        public const int PageNumberDX = 80, PageNumberY = 200;

        /// <summary>The spread (0 = the cover) a 0-based page is on: TazUO's ActivePage - 1.</summary>
        public static int SpreadOf(int page) => (page + 1) / 2;

        /// <summary>The last spread of a book: TazUO's MaxPage - 1, (pages >> 1).</summary>
        public static int LastSpreadOf(int pageCount) => pageCount >> 1;

        /// <summary>The left edge of a 0-based page: the first is on the right of the cover, then left, right...</summary>
        public static int PageX(int page) => page % 2 == 0 ? RightX : LeftX;

        /// <summary>Where TazUO's label for a 0-based page's number goes.</summary>
        public static (int X, int Y) PageNumberAt(int page) => (PageX(page) + PageNumberDX, PageNumberY);
    }
}
