using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>One message on a bulletin board: its summary (0x71 1), and its body once read (0x71 2).</summary>
    public sealed class BulletinPost
    {
        public uint Serial;
        /// <summary>The message this one replies to; 0 for a new thread.</summary>
        public uint Thread;
        public string Poster = "", Subject = "", Time = "";
        /// <summary>The poster's body and hue, and their equipment as (graphic, hue), as of posting.</summary>
        public ushort Body, Hue;
        public (ushort Graphic, ushort Hue)[] Equipment = Array.Empty<(ushort, ushort)>();
        /// <summary>The body lines; null until the message has been read.</summary>
        public string[] Lines;
    }

    /// <summary>A bulletin board the server opened (0x71 0), with the message summaries it has sent.</summary>
    public sealed class BulletinBoardInfo
    {
        public uint Serial;
        public string Name = "";
        public readonly Dictionary<uint, BulletinPost> Posts = new Dictionary<uint, BulletinPost>();
    }

    /// <summary>Bulletin boards: 0x71 as ModernUO's BulletinBoardPackets write and read it.</summary>
    public sealed partial class GameSession
    {
        /// <summary>ModernUO reads each string as a length byte and that many UTF-8 bytes; we send a null in that count.</summary>
        public const int MaxBulletinTextBytes = 254, MaxBulletinLines = 255;

        /// <summary>Open bulletin boards by serial.</summary>
        public readonly Dictionary<uint, BulletinBoardInfo> BulletinBoards = new Dictionary<uint, BulletinBoardInfo>();

        /// <summary>A board was double-clicked (0x71 0): open its window (again for the same board refreshes it).</summary>
        public event Action<BulletinBoardInfo> BulletinBoardOpened;
        /// <summary>A message's body arrived (0x71 2): open it.</summary>
        public event Action<BulletinBoardInfo, BulletinPost> BulletinPostOpened;

        private void RegisterBulletinBoardHandlers()
        {
            _handlers[0x71] = BulletinBoardPacket;
        }

        private void BulletinBoardPacket(PacketReader r)
        {
            int command = r.ReadByte();
            uint boardSerial = r.ReadUInt32();
            if (command == 0)
            {
                // SendBBDisplayBoard: the name in 30 bytes (UTF-8, null-padded). The messages follow as 0x3C into the board.
                if (!BulletinBoards.TryGetValue(boardSerial, out BulletinBoardInfo opened))
                    BulletinBoards[boardSerial] = opened = new BulletinBoardInfo { Serial = boardSerial };
                opened.Name = r.ReadUtf8(30);
                opened.Posts.Clear();
                BulletinBoardOpened?.Invoke(opened);
                return;
            }
            if (command != 1 && command != 2)
                return;
            // A summary or body for a board with no window is dropped, as ClassicUO does.
            if (!BulletinBoards.TryGetValue(boardSerial, out BulletinBoardInfo board))
                return;

            // SendBBMessage: message serial, the thread (summary only), then poster, subject and time, each a length
            // byte and that many UTF-8 bytes ending in a null (two in a body line).
            uint serial = r.ReadUInt32();
            uint thread = command == 1 ? r.ReadUInt32() : 0;
            if (!board.Posts.TryGetValue(serial, out BulletinPost post))
                board.Posts[serial] = post = new BulletinPost { Serial = serial };
            if (command == 1)
                post.Thread = thread;
            post.Poster = r.ReadUtf8(r.ReadByte());
            post.Subject = r.ReadUtf8(r.ReadByte());
            post.Time = r.ReadUtf8(r.ReadByte());
            if (command == 1)
                return;

            post.Body = r.ReadUInt16();
            post.Hue = r.ReadUInt16();
            var equipment = new (ushort, ushort)[r.ReadByte()];
            for (int i = 0; i < equipment.Length; i++)
                equipment[i] = (r.ReadUInt16(), r.ReadUInt16());
            post.Equipment = equipment;
            var lines = new string[r.ReadByte()];
            for (int i = 0; i < lines.Length; i++)
                lines[i] = r.ReadUtf8(r.ReadByte());
            post.Lines = lines;
            BulletinPostOpened?.Invoke(board, post);
        }

        /// <summary>
        /// A message item went into an open board (0x3C/0x25 after 0x71 0): ask for its summary (0x71 4), as
        /// ClassicUO does for each item added to a board with its gump open.
        /// </summary>
        private void NoteBulletinItem(ContainedItem item)
        {
            if (BulletinBoards.TryGetValue(item.Container, out BulletinBoardInfo board) && !board.Posts.ContainsKey(item.Serial) && State == SessionState.InWorld)
                _conn.Send(BulletinRequest(4, board.Serial, item.Serial));
        }

        /// <summary>
        /// The board's messages still in it, threads in arrival order with each reply under the message it answers,
        /// and how deep. A deleted message (0x1D) leaves the board, and its replies move up to its place.
        /// </summary>
        public List<(BulletinPost Post, int Depth)> BulletinThreads(BulletinBoardInfo board)
        {
            var result = new List<(BulletinPost, int)>();
            if (board == null)
                return result;
            List<BulletinPost> live = board.Posts.Values
                .Where(p => World.Contained.TryGetValue(p.Serial, out ContainedItem c) && c.Container == board.Serial).ToList();
            var ids = new HashSet<uint>(live.Select(p => p.Serial));
            void Add(uint parent, int depth)
            {
                foreach (BulletinPost p in live)
                {
                    bool root = p.Thread == 0 || !ids.Contains(p.Thread) || p.Thread == p.Serial;
                    if (parent == 0 ? root : !root && p.Thread == parent)
                    {
                        result.Add((p, depth));
                        if (depth < 10)
                            Add(p.Serial, depth + 1);
                    }
                }
            }
            Add(0, 0);
            return result;
        }

        /// <summary>The window closed: forget the board (the server sends it whole again when it is next opened).</summary>
        public void CloseBulletinBoard(BulletinBoardInfo board)
        {
            if (board != null && BulletinBoards.TryGetValue(board.Serial, out BulletinBoardInfo open) && open == board)
                BulletinBoards.Remove(board.Serial);
        }

        /// <summary>Asks for a message's body (0x71 3); it arrives as 0x71 2.</summary>
        public void ReadBulletinPost(BulletinBoardInfo board, BulletinPost post)
        {
            if (board != null && post != null && State == SessionState.InWorld)
                _conn.Send(BulletinRequest(3, board.Serial, post.Serial));
        }

        /// <summary>Removes a message (0x71 6); ModernUO allows it for the poster or staff and deletes the item (0x1D).</summary>
        public void RemoveBulletinPost(BulletinBoardInfo board, BulletinPost post)
        {
            if (board != null && post != null && State == SessionState.InWorld)
                _conn.Send(BulletinRequest(6, board.Serial, post.Serial));
        }

        /// <summary>
        /// Posts a new thread, or a reply when <paramref name="replyTo"/> is set (0x71 5). False when ModernUO would drop
        /// it: no subject or no text. The new message comes back as an item in the board, then its summary.
        /// </summary>
        public bool PostBulletinMessage(BulletinBoardInfo board, BulletinPost replyTo, string subject, string body)
        {
            if (board == null)
                return false;
            byte[] packet = BulletinPostRequest(board.Serial, replyTo?.Serial ?? 0, subject, BulletinLines(body));
            if (packet == null)
                return false;
            if (State == SessionState.InWorld)
                _conn.Send(packet);
            return true;
        }

        /// <summary>A message body as lines (ClassicUO splits on new lines), trailing empty ones dropped, at most 255.</summary>
        public static string[] BulletinLines(string body)
        {
            var lines = (body ?? "").Replace("\r", "").Split('\n').ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines.Take(MaxBulletinLines).ToArray();
        }

        /// <summary>0x71 with a board and a message serial: 3 read, 4 summary, 6 remove.</summary>
        public static byte[] BulletinRequest(int command, uint board, uint message) =>
            new PacketWriter(0x71).Byte(command).UInt32(board).UInt32(message).ToArray();

        /// <summary>
        /// 0x71 5 as ModernUO's BBPostMessage reads it: board, thread (0 for new), subject, line count, lines; each string
        /// a length byte and that many UTF-8 bytes, a null included (as ClassicUO sends). Null when there is no subject
        /// or no line, which ModernUO ignores.
        /// </summary>
        public static byte[] BulletinPostRequest(uint board, uint thread, string subject, IReadOnlyList<string> lines)
        {
            subject = Utf8Prefix((subject ?? "").Replace("\r", "").Replace("\n", " "), MaxBulletinTextBytes - 1);
            if (subject.Length == 0 || lines == null || lines.Count == 0)
                return null;
            PacketWriter w = new PacketWriter(0x71).Byte(5).UInt32(board).UInt32(thread);
            BulletinString(w, subject);
            int count = Math.Min(MaxBulletinLines, lines.Count);
            w.Byte(count);
            for (int i = 0; i < count; i++)
                BulletinString(w, Utf8Prefix(lines[i] ?? "", MaxBulletinTextBytes - 1));
            return w.ToArray();
        }

        private static void BulletinString(PacketWriter w, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            w.Byte(bytes.Length + 1).Utf8(bytes).Byte(0);
        }
    }
}
