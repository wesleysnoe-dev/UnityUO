namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout.
    public sealed partial class BulletinBoardWindow
    {
        /// <summary>TazUO's BulletinBoardGump: the board 0x087A, its name centred in 170 at 159, 36.</summary>
        public const int Background = 0x087A, NameX = 159, NameY = 36, NameWidth = 170;
        /// <summary>The post-a-message spot is part of the board art: an invisible 80 x 80 hit box at 15, 170.</summary>
        public const int PostX = 15, PostY = 170, PostSize = 80;
        /// <summary>The list: a scroll area at 127, 159, 241 wide and nine 18-high rows tall.</summary>
        public const int ListX = 127, ListY = 159, ListWidth = 241, RowHeight = 18, ListRows = 9;
        /// <summary>Each row (BulletinBoardObject): the icon 0x1523, then the text at 23, 1 in the rest of 230.</summary>
        public const int RowIcon = 0x1523, RowWidth = 230, RowTextX = 23, RowTextY = 1;

        /// <summary>The board opens centred: (window width / 2 - 245, window height / 2 - 205).</summary>
        public static (float X, float Y) OpenAt(float viewWidth, float viewHeight) => (viewWidth / 2 - 245, viewHeight / 2 - 205);
    }

    /// <summary>
    /// TazUO's BulletinBoardItem: a message on the expandable scroll 0x0820 (408 high), its author, date and title
    /// labels, a divider, the text, and one button at the bottom.
    /// </summary>
    public static class BulletinItemLayout
    {
        public const int Scroll = 0x0820, ScrollHeight = 408, Expander = 0x082E;
        /// <summary>The compose window's header art, and the buttons: Post (compose), Reply (another's), Remove (ours).</summary>
        public const int PostHeader = 0x0883, PostButton = 0x0886, ReplyButton = 0x0884, RemoveButton = 0x0885;
        public static readonly (int X, int Y) PostHeaderAt = (97, 12);
        /// <summary>Post and Reply at 37, Remove at 235, all 50 up from the bottom.</summary>
        public const int LeftButtonX = 37, RemoveButtonX = 235, ButtonUp = 50;
        /// <summary>The "Author:", "Date:" and "Title:" labels at x 30; the values follow on the same line.</summary>
        public const int LabelX = 30, AuthorY = 40, DateY = 58, TitleY = 77, SubjectWidth = 150;
        /// <summary>The divider: 0x0835 tiled 235 x 4 at 30, 106. The text: 220 wide at 40 in a 272 x 224 area at 0, 120.</summary>
        public const int Divider = 0x0835, DividerX = 30, DividerY = 106, DividerWidth = 235, DividerHeight = 4;
        public const int TextX = 40, TextY = 120, TextWidth = 220, TextAreaWidth = 272, TextAreaHeight = 224;
        /// <summary>A message read opens at 40, 40; a new one or a reply at 400, 335.</summary>
        public static readonly (int X, int Y) ReadAt = (40, 40), ComposeAt = (400, 335);

        public struct Piece
        {
            public int X, Y, Width, Height;
        }

        /// <summary>
        /// TazUO's ExpandableScroll pieces for a scroll of the given height, from the part sizes: the top (graphic),
        /// the right edge (+1) and the middle (+2) tiled down between them, the bottom (+3), and the expander 0x082E.
        /// </summary>
        public static (int Width, Piece Top, Piece Middle, Piece Bottom, Piece Expander) ScrollPieces(int height,
            (int W, int H) top, (int W, int H) right, (int W, int H) middle, (int W, int H) bottom, (int W, int H) expander)
        {
            int width = top.W;
            if (right.W > width) width = right.W;
            if (middle.W > width) width = middle.W;
            if (bottom.W > width) width = bottom.W;
            int off = top.W - bottom.W;
            var t = new Piece { X = 0, Y = 0, Width = top.W, Height = top.H };
            var m = new Piece { X = (width - right.W) / 2, Y = top.H, Width = middle.W, Height = height - top.H - bottom.H - expander.H };
            var b = new Piece { X = off / 2 + off / 4, Y = height - bottom.H - expander.H, Width = bottom.W, Height = bottom.H };
            var e = new Piece { X = (width - expander.W) >> 1, Y = height - expander.H - 2, Width = expander.W, Height = expander.H };
            return (width, t, m, b, e);
        }
    }
}
