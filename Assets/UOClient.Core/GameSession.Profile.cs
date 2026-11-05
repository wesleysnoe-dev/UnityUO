using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A character's profile, as the paperdoll's scroll shows it.</summary>
    public sealed class CharacterProfile
    {
        /// <summary>Whose profile it is; 0 when ModernUO hides the serial (our own, locked profile).</summary>
        public uint Serial;

        /// <summary>The character's title line.</summary>
        public string Header;

        /// <summary>The profile text the player wrote.</summary>
        public string Body;

        /// <summary>A note under the text (the account's age on our own profile, or that it is locked).</summary>
        public string Footer;
    }

    /// <summary>Character profiles (0xB8), asked for from the paperdoll.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The server sent a profile to show.</summary>
        public event Action<CharacterProfile> ProfileReceived;

        private void RegisterProfileHandlers()
        {
            _handlers[0xB8] = DisplayProfile;
        }

        /// <summary>
        /// 0xB8 (variable): serial, header (zero-terminated Latin-1), footer, then body (zero-terminated big-endian
        /// UTF-16 each), in that order, as ModernUO's SendDisplayProfile writes it.
        /// </summary>
        private void DisplayProfile(PacketReader r)
        {
            var p = new CharacterProfile { Serial = r.ReadUInt32(), Header = r.ReadAsciiZ() };
            p.Footer = r.ReadUnicodeZ();
            p.Body = r.ReadUnicodeZ();
            ProfileReceived?.Invoke(p);
        }

        /// <summary>Asks for a character's profile; the server answers with 0xB8 if it is a player in range.</summary>
        public void RequestProfile(uint serial)
        {
            if (State == SessionState.InWorld)
                _conn.Send(ProfileRequest(serial));
        }

        /// <summary>0xB8 request: length, mode 0 (display), serial.</summary>
        public static byte[] ProfileRequest(uint serial) => new PacketWriter(0xB8).Byte(0).UInt32(serial).ToArray();
    }
}
