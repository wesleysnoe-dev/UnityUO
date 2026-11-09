using System;

namespace UOClient.Core
{
    /// <summary>
    /// Whether other characters block the player's step, by the classic rule (ClassicUO Pathfinder, ModernUO's
    /// Mobile.CheckShove): with full stamina you shove through people (the server takes stamina for it); below full they
    /// block you. Ghosts walk through everyone and are walked through, and so does a GM body (0x3DB).
    /// </summary>
    public static class Shove
    {
        /// <summary>A character fills this much height above its feet (UO's person height).</summary>
        public const int BodyHeight = 16;

        public const ushort GmBody = 0x03DB;

        /// <summary>Whether the player may pass through characters right now (full stamina, a ghost, a GM).</summary>
        public static bool CanPassCharacters(Mobile player)
        {
            if (player == null)
                return true;
            if (player.IsDead || player.Body == GmBody)
                return true;
            return player.StaminaMax <= 0 || player.Stamina >= player.StaminaMax; // unknown stamina: as the server decides
        }

        /// <summary>The living character standing on (x, y) at about Z <paramref name="z"/> that blocks the player, or null.</summary>
        public static Mobile Blocker(World world, int x, int y, int z)
        {
            if (world == null)
                return null;
            Mobile player = world.Player;
            if (CanPassCharacters(player))
                return null;
            foreach (Mobile m in world.Mobiles.Values)
            {
                if (m.Serial == world.PlayerSerial || m.X != x || m.Y != y || m.IsDead)
                    continue;
                if (Math.Abs(m.Z - z) < BodyHeight)
                    return m;
            }
            return null;
        }
    }
}
