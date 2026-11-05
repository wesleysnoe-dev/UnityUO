using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>Where the server's quest (or tracking) arrow points.</summary>
    public sealed class QuestArrow
    {
        public int X, Y;

        /// <summary>The tracked mobile on High Seas clients (7.0.9+), 0 on older ones.</summary>
        public uint Serial;
    }

    /// <summary>The quest arrow (0xBA) that tracking and quests use to point at a map location.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The arrow on screen, or null when there is none.</summary>
        public QuestArrow QuestArrow { get; private set; }

        /// <summary>The arrow was shown, moved, or (with null) cleared.</summary>
        public event Action<QuestArrow> QuestArrowChanged;

        private void RegisterTrackingHandlers()
        {
            _handlers[0xBA] = QuestArrowUpdate;
        }

        /// <summary>
        /// 0xBA (6 bytes, 10 on 7.0.9+): active flag, x, y (int16 each), then on High Seas clients the tracked
        /// serial. ModernUO sends -1, -1 when an older client's arrow is cancelled.
        /// </summary>
        private void QuestArrowUpdate(PacketReader r)
        {
            bool active = r.ReadBool();
            int x = r.ReadInt16(), y = r.ReadInt16();
            uint serial = r.ReadUInt32();
            QuestArrow = active ? new QuestArrow { X = x, Y = y, Serial = serial } : null;
            QuestArrowChanged?.Invoke(QuestArrow);
        }

        /// <summary>0xBF 0x07: the player clicked the quest arrow (ModernUO reads one bool, true for a right click).</summary>
        public static byte[] QuestArrowClick(bool rightClick) => new PacketWriter(0xBF).UInt16(0x07).Bool(rightClick).ToArray();

        /// <summary>
        /// Tells the server the arrow was clicked. A right click is the classic cancel: ModernUO's tracking arrow
        /// stops and answers with a cancelling 0xBA, so the arrow is cleared here at once rather than waiting.
        /// A left click only notifies; the server decides what (if anything) it means.
        /// </summary>
        public void ClickQuestArrow(bool rightClick)
        {
            if (QuestArrow == null)
                return;
            if (State == SessionState.InWorld)
                _conn?.Send(QuestArrowClick(rightClick));
            if (rightClick)
            {
                QuestArrow = null;
                QuestArrowChanged?.Invoke(null);
            }
        }
    }
}
