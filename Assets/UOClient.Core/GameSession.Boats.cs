using System;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// Boats (0xF6): to High Seas clients (7.0.9+, ours) ModernUO's BaseBoat.Move sends one packet per step with the boat's
    /// new place and everything aboard, instead of the separate item and mobile updates older clients get. Without it the
    /// boat, its cargo and its passengers (the player too) stay behind on screen while the server has moved them.
    /// Placed at once, as TazUO without its smooth-boat option (off by default).
    /// </summary>
    public sealed partial class GameSession
    {
        private void RegisterBoatHandlers()
        {
            _handlers[0xF6] = BoatMoving;
        }

        /// <summary>
        /// 0xF6, as ModernUO's BoatPackets.CreateMoveBoatHS writes it: boat serial, speed, moving direction, facing, x, y,
        /// z (int16 each), then a count and per entity: serial, x, y, z.
        /// </summary>
        private void BoatMoving(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            r.ReadByte(); // speed
            r.ReadByte(); // moving direction
            r.ReadByte(); // facing (a turn arrives as the boat's own item update)
            int x = r.ReadUInt16(), y = r.ReadUInt16();
            sbyte z = (sbyte)r.ReadInt16();
            if (World.Items.TryGetValue(serial, out WorldItem boat))
            {
                boat.X = x;
                boat.Y = y;
                boat.Z = z;
                World.RaiseItemUpdated(boat);
            }
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                uint s = r.ReadUInt32();
                int ex = r.ReadUInt16(), ey = r.ReadUInt16();
                sbyte ez = (sbyte)r.ReadInt16();
                if (World.Mobiles.TryGetValue(s, out Mobile m))
                {
                    m.X = ex;
                    m.Y = ey;
                    m.Z = ez;
                    World.RaiseMobileCarried(m);
                }
                else if (World.Items.TryGetValue(s, out WorldItem it))
                {
                    it.X = ex;
                    it.Y = ey;
                    it.Z = ez;
                    World.RaiseItemUpdated(it);
                }
            }
        }
    }
}
