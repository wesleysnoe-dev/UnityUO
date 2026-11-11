using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// What the assistant's healing tools send. They act for the player without being the player's own hand, so none of
    /// these is written into a macro being recorded, and none changes the "last object", "last spell" or "last target"
    /// the player's own macros rely on.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>A double-click (0x06) at the server's action pace, as <see cref="Use(uint)"/> sends it, left out of macros and <see cref="LastObject"/>.</summary>
        public void UseUnrecorded(uint serial)
        {
            if (State != SessionState.InWorld || serial == 0)
                return;
            _actions.Use(serial, new PacketWriter(0x06, 5).UInt32(serial).ToArray());
            SendQueuedActions();
        }

        /// <summary>Casts a spell as <see cref="CastSpell"/> does, left out of macros and <see cref="LastSpell"/>.</summary>
        public void CastUnrecorded(int spellId)
        {
            if (State == SessionState.InWorld && spellId > 0)
                _conn.Send(CastSpellRequest(spellId, Version));
        }

        /// <summary>
        /// Answers the server's target cursor with an object, as <see cref="TargetObject"/> does, left out of macros and
        /// <see cref="LastTarget"/> (a heal on ourselves must not replace the enemy the player's "last target" means).
        /// No criminal question is asked: it is meant for ourselves. False when no server cursor is up (a cursor of the
        /// client's own, or a house placement, is left alone).
        /// </summary>
        public bool AnswerTargetUnrecorded(uint serial)
        {
            TargetRequest t = Target;
            if (t == null || t.Local != null || t.IsMulti || State != SessionState.InWorld || serial == 0)
                return false;
            Target = null;
            _conn.Send(new PacketWriter(0x6C, 19).Byte(0).UInt32(t.CursorId).Byte(t.Flags).UInt32(serial)
                .UInt16(0).UInt16(0).Byte(0).Byte(0).UInt16(0).ToArray());
            TargetEnded?.Invoke();
            return true;
        }

        /// <summary>
        /// Uses a bandage on a mobile in one request, with no target cursor: 0xBF 0x2C, TazUO's
        /// Send_TargetSelectedObject, which ModernUO's BandageTarget reads (bandage serial, target serial). The server
        /// holds it to the action delay like a double-click, so it waits its turn in the action queue.
        /// </summary>
        public void BandageTarget(uint bandage, uint target)
        {
            if (State != SessionState.InWorld || bandage == 0 || target == 0)
                return;
            _actions.Paced(BandageTargetRequest(bandage, target));
            SendQueuedActions();
        }

        public static byte[] BandageTargetRequest(uint bandage, uint target) =>
            new PacketWriter(0xBF).UInt16(0x2C).UInt32(bandage).UInt32(target).ToArray();
    }
}
