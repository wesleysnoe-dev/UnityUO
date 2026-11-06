using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// The spells and abilities the server says are toggled on (0xBF 0x25), as TazUO's World.ActiveSpellIcons
    /// (ActiveIconsManager.cs): spell icons draw in hue 38 while theirs is on. Only that packet sets or clears one.
    /// </summary>
    public sealed partial class GameSession
    {
        private readonly HashSet<int> _activeSpells = new HashSet<int>();

        /// <summary>TazUO's highlight hue for an active spell icon (UseSpellButtonGump, SpellbookGump.HueGumpPic).</summary>
        public const int ActiveSpellHue = 38;

        /// <summary>A spell's active flag changed: its id and whether it is now on.</summary>
        public event Action<int, bool> ActiveSpellChanged;

        /// <summary>Whether the server last said this spell id is on (ActiveSpellIconsManager.IsActive).</summary>
        public bool IsSpellActive(int spellId) => _activeSpells.Contains(spellId);

        /// <summary>The spell ids toggled on now.</summary>
        public IReadOnlyCollection<int> ActiveSpells => _activeSpells;

        /// <summary>
        /// 0xBF 0x25, as ModernUO's WeaponAbilityPackets.SendToggleSpecialAbility writes it (8 bytes): the id (int16)
        /// and a bool. TazUO's ExtendedCommand (:508-533) adds or removes it unconditionally. True if consumed.
        /// </summary>
        private bool ActiveSpellExtended(int sub, PacketReader r)
        {
            if (sub != 0x25)
                return false;
            int spell = r.ReadUInt16();
            bool active = r.ReadByte() != 0;
            bool changed = active ? _activeSpells.Add(spell) : _activeSpells.Remove(spell);
            if (changed)
                ActiveSpellChanged?.Invoke(spell, active);
            return true;
        }

        /// <summary>
        /// World.Clear (World.cs:1054) empties the set: here on a new login (0x1B) and on disconnect. The weapon ability
        /// toggled on goes with it (a new PlayerMobile starts with neither flag).
        /// </summary>
        private void ClearActiveSpells()
        {
            _activeSpells.Clear();
            _weaponAbilityOn = 0;
        }
    }
}
