using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A bulletin board (0x71 0), laid out like ClassicUO's BulletinBoardGump: the board art 0x087A with its name on
    /// top, a Post button, and the message summaries (0x71 1) as a list, replies under the message they answer.
    /// Clicking a summary asks for the message (0x71 3), which opens in a <see cref="BulletinPostWindow"/>.
    /// </summary>
    public sealed class BulletinBoardWindow : UiWindow
    {
        private const int Background = 0x087A, PostButton = 0x0883;
        private static readonly Color Ink = new Color(0.12f, 0.09f, 0.06f), Light = new Color(1f, 0.9f, 0.6f);

        private BulletinBoardInfo _board;
        private Vector2 _scroll;

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
                Text(new Rect(159, 36, 156, 20), _board.Name, Ink, 13, TextAnchor.UpperCenter, true);
                post = Art.Gump(PostButton) != null ? GumpButton(PostButton, PostButton, 15, 170) : GUI.Button(new Rect(15, 170, 60, 22), "Post");
                list = new Rect(127, 159, 241, 195);
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

            List<(BulletinPost Post, int Depth)> threads = Session.BulletinThreads(_board);
            const float row = 20;
            _scroll = GUI.BeginScrollView(list, _scroll, new Rect(0, 0, list.width - 18, Mathf.Max(threads.Count * row, list.height)));
            for (int i = 0; i < threads.Count; i++)
            {
                (BulletinPost p, int depth) = threads[i];
                float indent = Mathf.Min(depth, 4) * 10;
                string label = $"{p.Poster}  {p.Time}  {p.Subject}";
                if (GUI.Button(new Rect(indent, i * row, list.width - 18 - indent, row - 2), label, GUI.skin.label))
                    Session.ReadBulletinPost(_board, p);
            }
            GUI.EndScrollView();
        }
    }

    /// <summary>
    /// A message read off a board (0x71 2), as ClassicUO's BulletinBoardItem shows it: author, time and subject, then
    /// the text; Reply opens a reply and Remove asks the server to delete it (ModernUO: the poster or staff only).
    /// </summary>
    public sealed class BulletinPostWindow : UiWindow
    {
        private readonly BulletinBoardInfo _board;
        private BulletinPost _post;
        private GUIStyle _wrap;
        private Vector2 _scroll;

        public BulletinPostWindow(BulletinBoardInfo board, BulletinPost post)
        {
            _board = board;
            _post = post;
            Rect = new Rect(220, 110, 340, 360);
        }

        public override string Key => "bpost:" + _post.Serial;

        public override bool StillValid => Session.BulletinBoards.TryGetValue(_board.Serial, out BulletinBoardInfo open) && open == _board;

        public void SetPost(BulletinPost post) => _post = post;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            var label = new Color(0.75f, 0.72f, 0.62f);
            var value = new Color(0.95f, 0.92f, 0.84f);
            Text(new Rect(16, 12, 64, 18), "Author:", label, 12);
            Text(new Rect(80, 12, Rect.width - 96, 18), _post.Poster, value, 12);
            Text(new Rect(16, 30, 64, 18), "Time:", label, 12);
            Text(new Rect(80, 30, Rect.width - 96, 18), _post.Time, value, 12);
            Text(new Rect(16, 48, 64, 18), "Subject:", label, 12);
            Text(new Rect(80, 48, Rect.width - 96, 18), _post.Subject, value, 12, TextAnchor.UpperLeft, true);

            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, normal = { textColor = value } };
            string body = string.Join("\n", _post.Lines ?? new string[0]);
            var view = new Rect(16, 74, Rect.width - 32, Rect.height - 116);
            float height = _wrap.CalcHeight(new GUIContent(body), view.width - 18);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, Mathf.Max(height, view.height)));
            GUI.Label(new Rect(0, 0, view.width - 18, height), body, _wrap);
            GUI.EndScrollView();

            if (GUI.Button(new Rect(16, Rect.height - 34, 70, 22), "Reply"))
                Ui.Open(new BulletinComposeWindow(_board, _post));
            if (GUI.Button(new Rect(Rect.width - 86, Rect.height - 34, 70, 22), "Remove"))
            {
                Session.RemoveBulletinPost(_board, _post);
                Close();
            }
        }
    }

    /// <summary>A new message or a reply (0x71 5): subject and text, posted on Post (ModernUO drops one with either empty).</summary>
    public sealed class BulletinComposeWindow : UiWindow
    {
        private readonly BulletinBoardInfo _board;
        private readonly BulletinPost _replyTo;
        private string _subject, _body = "";

        public BulletinComposeWindow(BulletinBoardInfo board, BulletinPost replyTo)
        {
            _board = board;
            _replyTo = replyTo;
            _subject = replyTo == null ? "" : (replyTo.Subject.StartsWith("RE: ") ? replyTo.Subject : "RE: " + replyTo.Subject);
            Rect = new Rect(240, 130, 340, 340);
        }

        public override string Key => $"bcompose:{_board.Serial}:{_replyTo?.Serial ?? 0}";

        public override bool StillValid => Session.BulletinBoards.TryGetValue(_board.Serial, out BulletinBoardInfo open) && open == _board;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            var label = new Color(0.75f, 0.72f, 0.62f);
            Text(new Rect(16, 12, Rect.width - 32, 18), _replyTo == null ? "New message" : "Reply to " + _replyTo.Poster, new Color(1f, 0.9f, 0.6f), 13, TextAnchor.UpperCenter, true);
            Text(new Rect(16, 38, 64, 18), "Subject:", label, 12);
            _subject = GUI.TextField(new Rect(80, 36, Rect.width - 96, 20), _subject ?? "", GameSession.MaxBulletinTextBytes - 1);
            _body = GUI.TextArea(new Rect(16, 64, Rect.width - 32, Rect.height - 106), _body ?? "");

            if (GUI.Button(new Rect(16, Rect.height - 34, 70, 22), "Post") && Session.PostBulletinMessage(_board, _replyTo, _subject, _body))
                Close();
            if (GUI.Button(new Rect(Rect.width - 86, Rect.height - 34, 70, 22), "Cancel"))
                Close();
        }
    }
}
