using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// Death and the ghost state. ModernUO's <c>Mobile.OnDeath</c> sends the death status (<c>0x2C</c>, 2), turns war
    /// mode off and gives the player a ghost body; <c>Resurrect</c> gives the living body back and sends no packet of
    /// its own. So, as in ClassicUO (<c>Mobile.IsDead</c>), being dead is read off the player's body, and <c>0x2C</c>
    /// only starts the death screen.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>The player has a ghost body.</summary>
        public bool PlayerDead { get; private set; }

        /// <summary>The death status arrived (<c>0x2C</c>, 2): time for the death screen.</summary>
        public event Action PlayerDied;

        /// <summary><see cref="PlayerDead"/> changed: true on becoming a ghost, false on resurrection.</summary>
        public event Action<bool> PlayerDeadChanged;

        /// <summary>
        /// Another mobile we know of fell (<c>0xAF</c>): its serial, its corpse's (0 if none), and whether to play the
        /// second fall. ModernUO's <c>Mobile.Kill</c> sends it to everyone in range but the one dying (Mobile.cs:4836-4852)
        /// and then deletes an NPC or ghosts a player; TazUO's DisplayDeath plays the fall on the dying mobile and ignores
        /// it for the player and for mobiles it does not know (DisplayDeath.cs:21-24).
        /// </summary>
        public event Action<uint, uint, bool> MobileDying;

        private void RegisterDeathHandlers()
        {
            // ModernUO only ever sends action 2. ClassicUO treats anything but 1 as death.
            _handlers[0x2C] = r => { if (r.ReadByte() != 1) PlayerDied?.Invoke(); };
            _handlers[0xAF] = DisplayDeath;
            World.MobileUpdated += CheckPlayerDead;
            World.MobileMoved += CheckPlayerDead;
        }

        private void DisplayDeath(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            uint corpse = r.ReadUInt32();
            bool running = r.ReadUInt32() != 0;
            if (serial == World.PlayerSerial || !World.Mobiles.ContainsKey(serial))
                return;
            MobileDying?.Invoke(serial, corpse, running);
        }

        private void CheckPlayerDead(Mobile m)
        {
            if (m.Serial != World.PlayerSerial || m.IsDead == PlayerDead)
                return;
            PlayerDead = m.IsDead;
            PlayerDeadChanged?.Invoke(PlayerDead);
        }
    }
}
