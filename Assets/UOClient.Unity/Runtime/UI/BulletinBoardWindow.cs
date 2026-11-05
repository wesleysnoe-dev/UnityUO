using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using static UOClient.Unity.UI.BulletinItemLayout;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A bulletin board (0x71 0), laid out like TazUO's BulletinBoardGump: the board art 0x087A with its name on top,
    /// the post spot drawn into the art, and the message summaries (0x71 1) as a list of rows in the order they came.
    /// Double-clicking a row asks for the message (0x71 3), which opens in a <see cref="BulletinPostWindow"/>.
    /// </summary>
    public sealed partial class BulletinBoardWindow : UiWindow
    {
        private const float DoubleClickTime = 0.4f;
        private static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f), Light = new Color(1f, 0.9f, 0.6f);

        private BulletinBoardInfo _board;
        private Vector2 _scroll;
        private uint _lastClicked;
        private float _lastClick;

        public BulletinBoardWindow(BulletinBoardInfo board)
        {
            _board = board;
            Rect = new Rect(160, 80, 400, 380);
        }

        public override string Key => KeyFor(_board.Serial);

        public static string KeyFor(uint serial) => "bboard:" + serial;

        public override bool StillValid => Session.BulletinBoards.TryGetValue(_board.Serial, out BulletinBoardInfo open) && open == _board;

        /// <summary>Opened again while open: the server resends the messages, so take the fresh board.</summary>
        public void SetBoard(BulletinBoardInfo board) => _board = board;

        public override void OnOpened()
        {
            (float x, float y) = OpenAt(Screen.width / Ui.Scale, Screen.height / Ui.Scale);
            Rect.position = new Vector2(x, y);
        }

        public override void OnClosed() => Session.CloseBulletinBoard(_board);

        public override void Draw()
        {
            UiImage bg = Art.Gump(Background);
            Rect list;
            bool post;
            if (bg != null)
            {
                Rect.width = bg.Width;
                Rect.height = bg.Height;
                DrawImage(bg, 0, 0);
                Text(new Rect(NameX, NameY, NameWidth, 20), _board.Name, Ink, 13, TextAnchor.UpperCenter);
                post = GUI.Button(new Rect(PostX, PostY, PostSize, PostSize), GUIContent.none, GUIStyle.none);
                list = new Rect(ListX, ListY, ListWidth, RowHeight * ListRows);
            }
            else
            {
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
                Text(new Rect(16, 12, Rect.width - 32, 22), _board.Name, Light, 15, TextAnchor.UpperCenter, true);
                post = GUI.Button(new Rect(16, 40, 60, 22), "Post");
                list = new Rect(16, 70, Rect.width - 32, Rect.height - 86);
            }
            if (post)
                Ui.Open(new BulletinComposeWindow(_board, null));

            List<BulletinPost> posts = Session.BulletinSummaries(_board);
            UiImage icon = Art.Gump(RowIcon);
            _scroll = GUI.BeginScrollView(list, _scroll, new Rect(0, 0, RowWidth, Mathf.Max(posts.Count * RowHeight, list.height)));
            for (int i = 0; i < posts.Count; i++)
            {
                BulletinPost p = posts[i];
                float top = i * RowHeight;
                DrawImage(icon, 0, top);
                Text(new Rect(RowTextX, top + RowTextY, RowWidth - RowTextX, RowHeight - RowTextY), p.Summary, Ink, 12);
                if (GUI.Button(new Rect(0, top, RowWidth, RowHeight), GUIContent.none, GUIStyle.none))
                {
                    // TazUO reads a message on a double-click.
                    if (_lastClicked == p.Serial && Time.realtimeSinceStartup - _lastClick < DoubleClickTime)
                    {
                        Session.ReadBulletinPost(_board, p);
                        _lastClicked = 0;
                    }
                    else
                    {
                        _lastClicked = p.Serial;
                        _lastClick = Time.realtimeSinceStartup;
                    }
                }
            }
            GUI.EndScrollView();
        }
    }

    /// <summary>Draws TazUO's BulletinBoardItem frame: the expandable scroll 0x0820 and its header and divider.</summary>
    public abstract class BulletinScrollWindow : UiWindow
    {
        protected static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f), Faint = new Color(0.35f, 0.3f, 0.25f);

        protected void DrawScroll()
        {
            UiImage top = Art.Gump(Scroll), right = Art.Gump(Scroll + 1), middle = Art.Gump(Scroll + 2), bottom = Art.Gump(Scroll + 3);
            UiImage expander = Art.Gump(Expander);
            if (top == null || right == null || middle == null || bottom == null)
            {
                Rect.size = new Vector2(TextAreaWidth + 40, ScrollHeight);
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
                return;
            }
            var p = ScrollPieces(ScrollHeight, (top.Width, top.Height), (right.Width, right.Height), (middle.Width, middle.Height),
                (bottom.Width, bottom.Height), (expander?.Width ?? 0, expander?.Height ?? 0));
            Rect.size = new Vector2(p.Width, ScrollHeight);
            DrawImage(top, p.Top.X, p.Top.Y);
            DrawTiled(right, p.Middle.X, p.Middle.Y, right.Width, p.Middle.Height);
            DrawTiled(middle, p.Middle.X, p.Middle.Y, middle.Width, p.Middle.Height);
            DrawImage(bottom, p.Bottom.X, p.Bottom.Y);
            DrawImage(expander, p.Expander.X, p.Expander.Y);
        }

        /// <summary>A label, then its value right after it on the same line (TazUO: x = 30 + label width).</summary>
        protected void Field(string label, string value, int y)
        {
            Text(new Rect(LabelX, y, 60, 18), label, Ink, 12);
            Text(new Rect(LabelX + 60, y, SubjectWidth, 18), value, Ink, 12);
        }

        protected void Divider() => DrawTiled(Art.Gump(BulletinItemLayout.Divider), DividerX, DividerY, DividerWidth, DividerHeight);

        protected bool BottomButton(int gump, float x, string fallback)
        {
            float y = Rect.height - ButtonUp;
            if (Art.Gump(gump) != null)
                return GumpButton(gump, gump, x, y);
            return GUI.Button(new Rect(x, y, 70, 22), fallback);
        }
    }

    /// <summary>
    /// A message read off a board (0x71 2), as TazUO's BulletinBoardItem shows it: author, date and title, then the text.
    /// Someone else's message offers Reply; our own offers Remove instead (ModernUO: the poster or staff only).
    /// </summary>
    public sealed class BulletinPostWindow : BulletinScrollWindow
    {
        private readonly BulletinBoardInfo _board;
        private BulletinPost _post;
        private GUIStyle _wrap;
        private Vector2 _scroll;

        public BulletinPostWindow(BulletinBoardInfo board, BulletinPost post)
        {
            _board = board;
            _post = post;
            Rect = new Rect(ReadAt.X, ReadAt.Y, TextAreaWidth, ScrollHeight);
        }

        public override string Key => "bpost:" + _post.Serial;

        public override bool StillValid => Session.BulletinBoards.TryGetValue(_board.Serial, out BulletinBoardInfo open) && open == _board;

        public void SetPost(BulletinPost post) => _post = post;

        public override void Draw()
        {
            DrawScroll();
            Field("Author:", _post.Poster, AuthorY);
            Field("Date:", _post.Time, DateY);
            Field("Title:", _post.Subject, TitleY);
            Divider();

            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 12, normal = { textColor = Ink } };
            string body = string.Join("\n", _post.Lines ?? new string[0]).TrimStart();
            var view = new Rect(0, TextY, TextAreaWidth, TextAreaHeight);
            float height = _wrap.CalcHeight(new GUIContent(body), TextWidth);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, Mathf.Max(height, view.height)));
            GUI.Label(new Rect(TextX, 0, TextWidth, height), body, _wrap);
            GUI.EndScrollView();

            if (Session.IsOwnBulletinPost(_post))
            {
                if (BottomButton(RemoveButton, RemoveButtonX, "Remove"))
                {
                    Session.RemoveBulletinPost(_board, _post);
                    Close();
                }
            }
            else if (BottomButton(ReplyButton, LeftButtonX, "Reply"))
            {
                Ui.Open(new BulletinComposeWindow(_board, _post));
                Close();
            }
        }
    }

    /// <summary>
    /// A new message or a reply (0x71 5) on TazUO's BulletinBoardItem in its posting form: the 0x0883 header, our name,
    /// the subject and text to fill in, and Post (ModernUO drops one with either empty). Right-click closes it unsent.
    /// </summary>
    public sealed class BulletinComposeWindow : BulletinScrollWindow
    {
        private readonly BulletinBoardInfo _board;
        private readonly BulletinPost _replyTo;
        private string _subject, _body = "";

        public BulletinComposeWindow(BulletinBoardInfo board, BulletinPost replyTo)
        {
            _board = board;
            _replyTo = replyTo;
            _subject = replyTo == null ? "" : GameSession.BulletinReplySubject(replyTo.Subject);
            Rect = new Rect(ComposeAt.X, ComposeAt.Y, TextAreaWidth, ScrollHeight);
        }

        public override string Key => $"bcompose:{_board.Serial}:{_replyTo?.Serial ?? 0}";

        public override bool StillValid => Session.BulletinBoards.TryGetValue(_board.Serial, out BulletinBoardInfo open) && open == _board;

        public override void Draw()
        {
            DrawScroll();
            DrawImage(Art.Gump(PostHeader), PostHeaderAt.X, PostHeaderAt.Y);
            Field("Author:", Session.World.Player?.Name ?? "", AuthorY);
            Field("Date:", _replyTo?.Time ?? "Date/Time", DateY);
            Text(new Rect(LabelX, TitleY, 60, 18), "Title:", Ink, 12);
            _subject = GUI.TextField(new Rect(LabelX + 60, TitleY, SubjectWidth, 18), _subject ?? "", GameSession.MaxBulletinTextBytes - 1);
            Divider();
            _body = GUI.TextArea(new Rect(TextX, TextY, TextWidth, TextAreaHeight), _body ?? "");

            if (BottomButton(PostButton, LeftButtonX, "Post") && Session.PostBulletinMessage(_board, _replyTo, _subject, _body))
                Close();
        }
    }
}
