using System;
using System.Collections.Generic;

namespace UOClient.Core
{
    /// <summary>
    /// TazUO's AutoOpenCorpses (PlayerMobile.TryOpenCorpses, PlayerMobile.cs:368-415): every corpse within
    /// <see cref="Range"/> tiles of the player (Item.Distance, the larger of the x and y gaps) is opened once, the serial
    /// remembered so it is never opened again this session. <see cref="Options"/> (TazUO's CorpseOpenOptions) holds off
    /// while a target cursor is up and/or while the player is hidden; a corpse held off is tried again later, not marked.
    /// TazUO checks on every step and when a corpse appears; here <see cref="Tick"/> runs each frame and looks again
    /// when the player's tile or the number of world items changes, or something was held off. TazUO queues the open
    /// behind the player's own uses (ActionPriority.OpenCorpse); here a corpse waits until nothing else is queued, and
    /// only one goes per call, so the auto-opens never delay a double-click of the player's.
    /// </summary>
    public sealed class CorpseAutoOpen
    {
        public const int MaxRange = 5;

        /// <summary>TazUO's CorpseOpenOptions: 0 none, 1 not while targeting, 2 not while hidden, 3 both.</summary>
        public const int OpenAlways = 0, NotTargeting = 1, NotHiding = 2, NotTargetingOrHiding = 3;

        private readonly HashSet<uint> _opened = new HashSet<uint>();
        private GameSession _session;
        private int _lastX = int.MinValue, _lastY = int.MinValue, _lastCount = -1;
        private bool _retry;
        private bool _enabled = true;
        private int _range = 2, _options = NotTargetingOrHiding;

        /// <summary>TazUO's AutoOpenCorpses (on by default there and here).</summary>
        public bool Enabled { get => _enabled; set { _enabled = value; _retry = true; } }

        /// <summary>TazUO's AutoOpenCorpseRange, 0-5 tiles (2 by default).</summary>
        public int Range { get => _range; set { _range = Math.Max(0, Math.Min(MaxRange, value)); _retry = true; } }

        /// <summary>TazUO's CorpseOpenOptions (<see cref="NotTargetingOrHiding"/> by default).</summary>
        public int Options { get => _options; set { _options = Math.Max(OpenAlways, Math.Min(NotTargetingOrHiding, value)); _retry = true; } }

        /// <summary>Tiles between two points as TazUO's GameObject.Distance counts them: the larger of the x and y gaps.</summary>
        public static int Distance(int px, int py, int x, int y) => Math.Max(Math.Abs(x - px), Math.Abs(y - py));

        /// <summary>Whether <see cref="Options"/> holds the opening off right now (TazUO's two CorpseOpenOptions checks).</summary>
        public static bool HeldOff(int options, bool targeting, bool hidden) =>
            ((options == NotTargeting || options == NotTargetingOrHiding) && targeting)
            || ((options == NotHiding || options == NotTargetingOrHiding) && hidden);

        /// <summary>Whether a corpse this far away, not opened before, opens now.</summary>
        public static bool Opens(bool enabled, int range, int options, int distance, bool alreadyOpened, bool targeting, bool hidden) =>
            enabled && !alreadyOpened && distance <= Math.Max(0, Math.Min(MaxRange, range)) && !HeldOff(options, targeting, hidden);

        /// <summary>The corpse has been opened by this, this session.</summary>
        public bool Opened(uint serial) => _opened.Contains(serial);

        /// <summary>
        /// Opens at most one corpse in range (the player at <paramref name="playerX"/>, <paramref name="playerY"/>, where it
        /// is drawn); returns its serial, or 0. A new session forgets what the last one opened.
        /// </summary>
        public uint Tick(GameSession session, int playerX, int playerY)
        {
            if (session != _session)
            {
                _session = session;
                _opened.Clear();
                _retry = true;
            }
            if (session == null || !_enabled || session.State != SessionState.InWorld)
                return 0;
            int count = session.World.Items.Count;
            if (!_retry && playerX == _lastX && playerY == _lastY && count == _lastCount)
                return 0;
            _lastX = playerX;
            _lastY = playerY;
            _lastCount = count;
            _retry = false;

            Mobile player = session.World.Player;
            bool hidden = player != null && (player.Flags & 0x80) != 0; // TazUO's Flags.Hidden
            bool targeting = session.Target != null;
            foreach (WorldItem item in session.World.Items.Values)
            {
                if (!item.IsCorpse || _opened.Contains(item.Serial))
                    continue;
                int distance = Distance(playerX, playerY, item.X, item.Y);
                if (distance > _range)
                    continue;
                if (!Opens(_enabled, _range, _options, distance, false, targeting, hidden) || session.QueuedActions > 0)
                {
                    _retry = true; // in range but held off: look again next frame
                    return 0;
                }
                _opened.Add(item.Serial);
                session.OpenCorpse(item.Serial);
                _retry = true; // there may be more in range
                return item.Serial;
            }
            return 0;
        }
    }
}
