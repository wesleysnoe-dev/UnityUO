using System;

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

        private void RegisterDeathHandlers()
        {
            // ModernUO only ever sends action 2. ClassicUO treats anything but 1 as death.
            _handlers[0x2C] = r => { if (r.ReadByte() != 1) PlayerDied?.Invoke(); };
            _handlers[0xAF] = _ => { };   // death animation; the corpse arrives as an item
            World.MobileUpdated += CheckPlayerDead;
            World.MobileMoved += CheckPlayerDead;
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
