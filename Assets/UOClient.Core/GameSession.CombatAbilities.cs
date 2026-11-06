using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// Weapon special moves, as TazUO's GameActions.UsePrimaryAbility / UseSecondaryAbility: the player's worn weapon has
    /// two (WeaponAbilities.Of); using one sends 0xD7 0x19 with its index and marks it on, using it again sends index 0
    /// (off). Only one is on at a time. The server turns it off with 0xBF 0x21 (ModernUO's SendClearWeaponAbility, after
    /// the hit or when it fails); ModernUO never sends 0xBF 0x25 for weapon abilities (only for Bushido / Ninjitsu moves),
    /// so the "on" mark is the client's own, as in TazUO.
    /// </summary>
    public sealed partial class GameSession
    {
        /// <summary>0xB9's flags (ModernUO's FeatureFlags for the shard's expansion and the account).</summary>
        public uint SupportedFeatures { get; private set; }

        /// <summary>FeatureFlags.AOS (TazUO's LockedFeatureFlags.AOS): the 0xD7 0x19 request applies.</summary>
        public bool AosFeatures => (SupportedFeatures & 0x10) != 0;

        private int _weaponAbilityOn;   // 0 none, 1 primary, 2 secondary
        private uint _weaponAbilityWeapon; // the weapon it was turned on with (another weapon: it is off, UpdateAbilities)

        /// <summary>0xB9: 4 bytes for the extended clients (6.0.14.2 and later), 2 before.</summary>
        private void SupportedFeature(PacketReader r) =>
            SupportedFeatures = r.Remaining >= 4 ? r.ReadUInt32() : r.ReadUInt16();

        /// <summary>The weapon the player wields (one-handed, else two-handed layer, as UpdateAbilities), or null.</summary>
        public EquippedItem? WornWeapon()
        {
            Mobile p = World.Player;
            if (p == null)
                return null;
            return p.Equipment.TryGetValue(1, out EquippedItem e) || p.Equipment.TryGetValue(2, out e) ? e : (EquippedItem?)null;
        }

        /// <summary>Whether the primary (<paramref name="secondary"/> false) or secondary ability is on.</summary>
        public bool IsWeaponAbilityOn(bool secondary) =>
            _weaponAbilityOn == (secondary ? 2 : 1) && _weaponAbilityWeapon == (WornWeapon()?.Serial ?? 0);

        /// <summary>
        /// The book's primary / secondary icon double-clicked: <paramref name="ability"/> is that slot's ability index
        /// (WeaponAbilities.Of the worn weapon). Off: sends it and marks it on (the other goes off). On: sends 0, off.
        /// Not in the world, or a shard without AoS features (TazUO sends the pre-AoS stun / disarm requests there,
        /// which we do not): nothing. Returns whether a request was sent.
        /// </summary>
        public bool UseWeaponAbility(bool secondary, int ability)
        {
            if (State != SessionState.InWorld || !AosFeatures || ability < 1 || ability > WeaponAbilities.Count)
                return false;
            if (IsWeaponAbilityOn(secondary))
            {
                _weaponAbilityOn = 0;
                _conn.Send(CombatAbilityRequest(World.PlayerSerial, 0));
                return true;
            }
            _weaponAbilityOn = secondary ? 2 : 1;
            _weaponAbilityWeapon = WornWeapon()?.Serial ?? 0;
            _conn.Send(CombatAbilityRequest(World.PlayerSerial, (byte)ability));
            return true;
        }

        /// <summary>
        /// TazUO's Send_UseCombatAbility: 0xD7, length 15, the player's serial, command 0x19, four zero bytes, the index,
        /// 0x0A. ModernUO's EncodedCommand reads the serial and command; WeaponAbilityPackets.SetAbility's
        /// EncodedReader.ReadInt32 takes the first zero as its type byte, then the int32 0x000000 + index (the 0x0A is
        /// not read). Index 1-31 sets that ability, anything else clears it.
        /// </summary>
        public static byte[] CombatAbilityRequest(uint player, byte index) =>
            new PacketWriter(0xD7).UInt32(player).UInt16(0x19).UInt32(0).Byte(index).Byte(0x0A).ToArray();

        /// <summary>0xBF 0x21 (ModernUO's SendClearWeaponAbility, TazUO ExtendedCommand :484-489): both go off.</summary>
        private bool WeaponAbilityExtended(int sub)
        {
            if (sub != 0x21)
                return false;
            _weaponAbilityOn = 0;
            return true;
        }
    }
}
