using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// A one-shot (or repeating) action the server plays on a mobile: an attack swing, a spell cast, a bow, a hit.
    /// The numbers are as sent; which action a body draws for them depends on its art and is worked out where the art is
    /// (<c>AnimationLibrary.ServerAction</c> / <c>NewServerAction</c>).
    /// </summary>
    public sealed class ServerAnimation
    {
        public uint Serial;

        /// <summary>0xE2 (type, action, mode) rather than 0x6E (action, frame count, repeat, direction, delay).</summary>
        public bool IsNew;

        /// <summary>0x6E's action, or 0xE2's action.</summary>
        public int Action;

        /// <summary>0x6E: frames to play (0 = all of them), times to play them (0 = for ever), the repeat flag.</summary>
        public int FrameCount, RepeatCount;
        public bool Repeat;

        /// <summary>0x6E: false when the server asked for it backwards (the byte on the wire is "reverse").</summary>
        public bool Forward = true;

        /// <summary>0x6E: the delay byte; each frame lasts 80 ms × (delay + 2) (Mobile.ProcessAnimation).</summary>
        public int Delay;

        /// <summary>0xE2: animation type (0 attack, 1-2 block / parry, 3 death, ... 11 spell) and mode.</summary>
        public int Type, Mode;
    }

    /// <summary>
    /// Server-driven mobile animations. ModernUO's <c>Mobile.Animate</c> sends 0x6E (OutgoingMobilePackets.CreateMobileAnimation,
    /// 14 bytes) to everyone in range who can see the mobile; weapons, spells and emotes call it. 0xE2
    /// (CreateNewMobileAnimation, 10 bytes) is the newer form. TazUO plays both on known mobiles only (CharacterAnimation.cs,
    /// NewCharacterAnimation.cs); 0xE2 also waits for the player.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>The server played an action on a mobile we know (the player included).</summary>
        public event Action<ServerAnimation> MobileAnimated;

        /// <summary>
        /// 0x2F swing: the player swung at <see cref="LastAttack"/> while in war mode. TazUO turns the player to face the
        /// enemy then (Swing.cs), if the pathfinder allows the turn and no step is pending; it plays nothing. Not wired to the
        /// player's turning here (that lives with the walk prediction), so this only reports it; the swing itself is drawn
        /// from the 0x6E that ModernUO sends with it.
        /// </summary>
        public event Action<uint> SwungAtLastAttack;

        // Packet bodies after the id: 0x6E 4+2+2+2+1+1+1, 0xE2 4+2+2+1, 0x2F 1+4+4.
        private const int CharacterAnimationBody = 13, NewCharacterAnimationBody = 9, SwingBody = 9;

        private void RegisterAnimationHandlers()
        {
            _handlers[0x6E] = CharacterAnimation;
            _handlers[0xE2] = NewCharacterAnimation;
            _handlers[0x2F] = Swing;
        }

        /// <summary>0x6E: serial, action, frame count, repeat count, reverse, repeat, delay (CharacterAnimation.Receive).</summary>
        private void CharacterAnimation(PacketReader r)
        {
            if (r.Remaining < CharacterAnimationBody)
                return;
            uint serial = r.ReadUInt32();
            if (!World.Mobiles.ContainsKey(serial))
                return;
            var a = new ServerAnimation
            {
                Serial = serial,
                Action = r.ReadUInt16(),
                // SetAnimation takes the frame count as a byte.
                FrameCount = (byte)r.ReadUInt16(),
                RepeatCount = r.ReadUInt16(),
                Forward = !r.ReadBool(),
                Repeat = r.ReadBool(),
                Delay = r.ReadByte()
            };
            MobileAnimated?.Invoke(a);
        }

        /// <summary>
        /// 0xE2: serial, type, action, mode (NewCharacterAnimation.Receive); TazUO plays it once, forwards, repeating only
        /// a block or parry (types 1-2) on body 0x15.
        /// </summary>
        private void NewCharacterAnimation(PacketReader r)
        {
            if (r.Remaining < NewCharacterAnimationBody || World.Player == null)
                return;
            uint serial = r.ReadUInt32();
            if (!World.Mobiles.TryGetValue(serial, out Mobile m))
                return;
            int type = r.ReadUInt16();
            var a = new ServerAnimation
            {
                Serial = serial,
                IsNew = true,
                Type = type,
                Action = r.ReadUInt16(),
                Mode = r.ReadByte(),
                RepeatCount = 1,
                Repeat = (type == 1 || type == 2) && m.Body == 0x0015
            };
            MobileAnimated?.Invoke(a);
        }

        /// <summary>0x2F: a flag byte, attacker, defender. Only the player's own swings at its last attack matter (Swing.cs).</summary>
        private void Swing(PacketReader r)
        {
            if (r.Remaining < SwingBody || State != SessionState.InWorld)
                return;
            r.Skip(1);
            uint attacker = r.ReadUInt32();
            if (attacker != World.PlayerSerial)
                return;
            uint defender = r.ReadUInt32();
            if (defender != 0 && defender == LastAttack && WarMode && World.Mobiles.ContainsKey(defender))
                SwungAtLastAttack?.Invoke(defender);
        }
    }
}
