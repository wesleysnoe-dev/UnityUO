using System;
using System.Collections.Generic;
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
            _handlers[0x89] = CorpseEquip;
            World.MobileUpdated += CheckPlayerDead;
            World.MobileMoved += CheckPlayerDead;
        }

        private void DisplayDeath(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            uint corpse = r.ReadUInt32();
            bool running = r.ReadUInt32() != 0;
            if (serial == World.PlayerSerial || !World.Mobiles.TryGetValue(serial, out Mobile owner))
                return;
            // The corpse stays hidden until the fall is over, then faces the way its owner did (DisplayDeath.cs:40-48).
            if (corpse != 0 && corpse < 0x80000000)
                World.CorpseFalls.Add(corpse, serial, owner.Direction, running);
            MobileDying?.Invoke(serial, corpse, running);
        }

        /// <summary>
        /// What a human corpse wears (<c>0x89</c>): serial, then (layer + 1, item serial) pairs up to a 0 layer. ModernUO
        /// sends it with the corpse's 0x3C contents to everyone who sees a human corpse (Corpse.SendInfoTo, Corpse.cs:739-747;
        /// CorpsePackets.cs:25-70). TazUO's CorpseEquipment skips anything not a corpse and the backpack (CorpseEquipment.cs).
        /// The list is the whole outfit, so it replaces the last one.
        /// </summary>
        private void CorpseEquip(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            if (!World.Items.TryGetValue(serial, out WorldItem corpse) || !corpse.IsCorpse)
                return;
            var wears = new Dictionary<byte, uint>();
            byte layer = r.ReadByte();
            while (layer != 0 && r.Remaining >= 4)
            {
                uint item = r.ReadUInt32();
                if (layer - 1 != 21)
                    wears[(byte)(layer - 1)] = item;
                layer = r.ReadByte();
            }
            corpse.CorpseEquipment = wears;
            World.RaiseItemUpdated(corpse);
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
