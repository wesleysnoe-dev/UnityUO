using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A facet's season, as ModernUO's map definitions number them.</summary>
    public enum Season : byte
    {
        Spring = 0,
        Summer = 1,
        Fall = 2,
        Winter = 3,
        Desolation = 4
    }

    /// <summary>The season (0xBC) the server sends on login and on every facet change.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The current season; summer (ModernUO's default) until the server says otherwise.</summary>
        public Season Season { get; private set; } = Season.Summer;

        /// <summary>The server set the season (also when it is unchanged); the flag asks for the season's sound.</summary>
        public event Action<Season, bool> SeasonChanged;

        private void RegisterSeasonHandlers()
        {
            _handlers[0xBC] = SeasonChange;
        }

        /// <summary>
        /// 0xBC (3 bytes): season, play-sound flag, as ModernUO's SendSeasonChange writes them. Values above
        /// desolation are read as spring, like the classic client.
        /// </summary>
        private void SeasonChange(PacketReader r)
        {
            byte season = r.ReadByte();
            bool playSound = r.ReadBool();
            Season = season > (byte)Season.Desolation ? Season.Spring : (Season)season;
            SeasonChanged?.Invoke(Season, playSound);
        }
    }
}
