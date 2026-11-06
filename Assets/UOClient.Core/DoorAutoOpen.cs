using System;

namespace UOClient.Core
{
    /// <summary>
    /// TazUO's AutoOpenDoors (PlayerMobile.TryOpenDoors, PlayerMobile.cs:423-453): when the player steps or turns and the
    /// tile it now faces holds a closed door within 15 of its z, the open-door macro goes out
    /// (<see cref="GameSession.OpenDoor"/>; the server finds and uses the door). Not for a ghost, and with
    /// <see cref="IfHidden"/> off not while hidden. TazUO checks on every position and direction change; <see cref="Tick"/>
    /// does the same by remembering the last tile and facing it saw. Open doors are left alone (TazUO's DoorData.IsOpenDoor;
    /// its global AutoCloseDoors is not offered).
    /// </summary>
    public sealed class DoorAutoOpen
    {
        private GameSession _session;
        private int _lastX = int.MinValue, _lastY = int.MinValue, _lastDirection = -1;

        /// <summary>
        /// TazUO's AutoOpenDoors. Off by default here (TazUO: on): it sends the open-door macro to the server, so it waits
        /// until the player turns it on.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>TazUO's AutoOpenDoorsIfHidden (on by default there and here); only matters with <see cref="Enabled"/> on.</summary>
        public bool IfHidden { get; set; } = true;

        /// <summary>The tile one step from (x, y) in a UO direction (0 north, clockwise; TazUO's Pathfinder.GetNewXY).</summary>
        public static (int X, int Y) Ahead(int x, int y, int direction)
        {
            switch (direction & 7)
            {
                case 0: return (x, y - 1);
                case 1: return (x + 1, y - 1);
                case 2: return (x + 1, y);
                case 3: return (x + 1, y + 1);
                case 4: return (x, y + 1);
                case 5: return (x - 1, y + 1);
                case 6: return (x - 1, y);
                default: return (x - 1, y - 1);
            }
        }

        // TazUO's DoorData: the first (facing 0, closed) graphic of each known 16-graphic door block.
        private static readonly ushort[] DoorBlocks =
        {
            0x00E8, 0x0314, 0x0324, 0x0334, 0x0344, 0x0354, 0x0675, 0x0685, 0x0695, 0x06A5, 0x06B5, 0x06C5, 0x06D5, 0x06E5,
            0x0824, 0x0839, 0x084C, 0x0866, 0x1FED,
        };

        /// <summary>
        /// A door graphic in its open position: TazUO's DoorData.IsOpenDoor. Doors come in blocks of 16, closed and open
        /// alternating per facing, so an odd offset from the block's start is open; a door outside the known blocks counts
        /// from the start of its run of tiledata doors (<paramref name="isDoor"/>). Anything else is not open.
        /// </summary>
        public static bool IsOpenDoor(ushort graphic, Func<ushort, bool> isDoor)
        {
            foreach (ushort start in DoorBlocks)
                if (graphic >= start && graphic < start + 16)
                    return ((graphic - start) & 1) == 1;
            if (isDoor == null || !isDoor(graphic))
                return false;
            int run = graphic;
            for (int steps = 0; steps < 256 && run > 0 && isDoor((ushort)(run - 1)); steps++)
                run--;
            return ((graphic - run) & 1) == 1;
        }

        /// <summary>Whether a door at <paramref name="doorZ"/> is in reach of a player at <paramref name="z"/> (TazUO: within 15).</summary>
        public static bool InReach(int doorZ, int z) => doorZ - 15 <= z && doorZ + 15 >= z;

        /// <summary>
        /// Sends the open-door macro once when the player at (x, y, z) facing <paramref name="direction"/> has just stepped or
        /// turned to face a closed door; true when it did. <paramref name="isDoor"/> is tiledata's door flag.
        /// </summary>
        public bool Tick(GameSession session, int x, int y, int z, int direction, Func<ushort, bool> isDoor)
        {
            direction &= 7;
            if (session != _session)
            {
                // Where a new session starts is no step or turn.
                _session = session;
                (_lastX, _lastY, _lastDirection) = (x, y, direction);
                return false;
            }
            if (x == _lastX && y == _lastY && direction == _lastDirection)
                return false;
            _lastX = x;
            _lastY = y;
            _lastDirection = direction;
            if (session == null || !Enabled || session.State != SessionState.InWorld || isDoor == null)
                return false;
            Mobile player = session.World.Player;
            if (player == null || player.IsDead || (!IfHidden && (player.Flags & 0x80) != 0))
                return false;
            var (ax, ay) = Ahead(x, y, direction);
            foreach (WorldItem item in session.World.Items.Values)
            {
                if (item.IsMulti || item.X != ax || item.Y != ay || !isDoor(item.Graphic) || !InReach(item.Z, z)
                    || IsOpenDoor(item.Graphic, isDoor))
                    continue;
                session.OpenDoor();
                return true;
            }
            return false;
        }
    }
}
