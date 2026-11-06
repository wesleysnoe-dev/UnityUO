using System;
using System.Collections.Generic;

namespace UOClient.Core
{
    public struct EquippedItem
    {
        public uint Serial;
        public ushort Graphic;
        public byte Layer;
        public ushort Hue;
    }

    public sealed class Mobile
    {
        public uint Serial;
        public ushort Body;
        public int X, Y;
        public sbyte Z;
        /// <summary>0-7; see <see cref="Running"/> for the run flag the server sends in the same byte.</summary>
        public byte Direction;
        public bool Running;
        public ushort Hue;
        public byte Flags;
        public byte Notoriety;
        public string Name;
        public int Hits, HitsMax, Mana, ManaMax, Stamina, StaminaMax;

        // From the full status (0x11, our own character).
        public bool Female;
        public int Str, Dex, Int, Gold, Armor, Weight, MaxWeight, StatCap, Followers, FollowersMax;
        public int FireResist, ColdResist, PoisonResist, EnergyResist, Luck, DamageMin, DamageMax, Tithe;
        public bool HasFullStatus;

        /// <summary>Stat gain locks (0 up, 1 down, 2 locked) from 0xBF 0x19 version 2, our own character only.</summary>
        public byte StrLock, DexLock, IntLock;

        /// <summary>
        /// The health bar's poison and yellow (blessed / invulnerable) states from 0x17, as TazUO's NewHealthBarUpdate
        /// keeps them for 7.0+ clients (where flag 0x04 means flying, not poisoned).
        /// </summary>
        public bool Poisoned, YellowHits;

        /// <summary>0x11's "can rename" byte: one of our pets (TazUO hues its health bar name 0x000E).</summary>
        public bool Renamable;

        public readonly Dictionary<byte, EquippedItem> Equipment = new Dictionary<byte, EquippedItem>();

        /// <summary>Changes whenever appearance changes (body, hue or equipment), so renderers can cache by it.</summary>
        public int AppearanceVersion;

        /// <summary>
        /// A ghost body: ModernUO's <c>Race.GhostBody</c> (human 402/403, elf 607/608, gargoyle 694/695), plus 970,
        /// which ClassicUO's <c>Mobile.IsDead</c> also counts.
        /// </summary>
        public bool IsDead => IsGhostBody(Body);

        /// <summary>In war mode: flag 0x40 of 0x20/0x77/0x78, as ModernUO's <c>Mobile.GetPacketFlags</c> sets it and TazUO's <c>Mobile.InWarMode</c> reads it.</summary>
        public bool WarMode => (Flags & 0x40) != 0;

        public static bool IsGhostBody(ushort body) =>
            body == 402 || body == 403 || body == 607 || body == 608 || body == 694 || body == 695 || body == 970;

        public bool IsHuman => Body == 400 || Body == 401 || Body == 402 || Body == 403 || Body == 605 || Body == 606 || Body == 666 || Body == 667 || Body == 744 || Body == 745 || Body == 987;
    }

    public sealed class WorldItem
    {
        public uint Serial;

        /// <summary>The item's art id, or for a multi (house, boat) its multi id.</summary>
        public ushort Graphic;

        public bool IsMulti;
        public int Amount;
        public int X, Y;
        public sbyte Z;
        public ushort Hue;
        public byte Flags;

        /// <summary>The server lets it be picked up (ModernUO sets 0x20 for Movable items); multis never are.</summary>
        public bool Movable => !IsMulti && (Flags & 0x20) != 0;

        public const ushort CorpseGraphic = 0x2006;

        /// <summary>
        /// The direction byte of 0xF3 / 0x1A. ModernUO writes <c>Item.Light</c> there, the same field as
        /// <c>Item.Direction</c> (Item.cs:679-704), and a corpse takes its owner's (Corpse.cs:216).
        /// </summary>
        public byte Direction;

        /// <summary>A corpse (TazUO <c>Item.IsCorpse</c>, Item.cs:98-99). Its <see cref="Amount"/> is the body it was (Corpse.cs:211).</summary>
        public bool IsCorpse => !IsMulti && Graphic == CorpseGraphic;

        /// <summary>
        /// Corpse only: the facing it lies in, drawn from the low 3 bits (TazUO DrawCorpse, ItemView.cs:191). Set from the
        /// direction byte (Item.CheckGraphicChange, Item.cs:432-445) and again when its owner's fall ends
        /// (CorpseManager.Remove, CorpseManager.cs:72).
        /// </summary>
        public byte Layer;

        /// <summary>
        /// Corpse only: the direction byte had the run bit (0x80), so the corpse lies in the body's second fall. TazUO's
        /// <c>UsedLayer</c> (Item.cs:434-441), passed to <c>GetDeathAction</c> as <c>second</c> (ItemView.cs:200-205).
        /// </summary>
        public bool UsedLayer;

        /// <summary>
        /// Corpse only: what 0x89 says it wears, layer to item serial (backpack left out, CorpseEquipment.cs:31); null until
        /// then. The items themselves (graphic, hue) arrive by 0x3C into <see cref="World.Contained"/>.
        /// </summary>
        public Dictionary<byte, uint> CorpseEquipment;

        /// <summary>Takes the packet's direction byte; a corpse reads its facing and fall from it, as CheckGraphicChange does.</summary>
        internal void SetDirection(byte direction)
        {
            Direction = direction;
            if (!IsCorpse)
                return;
            UsedLayer = (direction & 0x80) != 0;
            Layer = (byte)(direction & 0x7F);
        }
    }

    /// <summary>
    /// Corpses whose owners are still playing their fall, as TazUO's <c>CorpseManager</c>: added on 0xAF
    /// (DisplayDeath.cs:40-48), hidden while listed (ItemView.cs:183), and when the fall ends the corpse is turned to the
    /// way its owner faced (CorpseManager.Remove, called from Mobile.cs:706-730).
    /// </summary>
    public sealed class CorpseFalls
    {
        private readonly World _world;
        private readonly List<(uint Corpse, uint Owner, byte Direction, bool Running)> _falls = new List<(uint, uint, byte, bool)>();

        internal CorpseFalls(World world) => _world = world;

        public int Count => _falls.Count;

        internal void Add(uint corpse, uint owner, byte direction, bool running)
        {
            foreach (var f in _falls)
                if (f.Corpse == corpse)
                    return;
            _falls.Add((corpse, owner, direction, running));
        }

        /// <summary>A fall for this corpse, or by this owner, is still playing (CorpseManager.Exists).</summary>
        public bool Exists(uint corpse, uint owner)
        {
            foreach (var f in _falls)
                if ((corpse != 0 && f.Corpse == corpse) || (owner != 0 && f.Owner == owner))
                    return true;
            return false;
        }

        /// <summary>The owner's fall is over: its corpse faces the way the owner did, with the run bit if 0xAF set it.</summary>
        public void Finish(uint owner)
        {
            for (int i = _falls.Count - 1; i >= 0; i--)
            {
                var f = _falls[i];
                if (f.Owner != owner)
                    continue;
                if (_world.Items.TryGetValue(f.Corpse, out WorldItem corpse))
                    corpse.Layer = (byte)((f.Direction & 7) | (f.Running ? 0x80 : 0));
                _falls.RemoveAt(i);
            }
        }

        internal void Clear() => _falls.Clear();
    }

    public struct JournalEntry
    {
        public DateTime Time;
        public string Speaker;
        public string Text;
        public ushort Hue;
        /// <summary>Who or what said it (0xFFFFFFFF for system messages); overhead text is drawn above this.</summary>
        public uint Serial;
        /// <summary>0 regular speech, 1 broadcast, 2 emote, 6 system, 7 message, 8 whisper, 9 yell, 13 guild, 14 alliance, 0xC0 encoded (UO Offline chat)...</summary>
        public byte Type;

        /// <summary>The name as sent, even for system lines (UO Offline's news arrives as a system line named "News").</summary>
        public string Name;

        /// <summary>A UO Offline chat link ("join 12", "view 38", "fill 5"), or null; send "[" + Link to act on it.</summary>
        public string Link;
    }

    /// <summary>
    /// Client-side view of the game world, updated from server packets on the game thread.
    /// Renderers subscribe to the events instead of polling.
    /// </summary>
    public sealed class World
    {
        public World()
        {
            CorpseFalls = new CorpseFalls(this);
        }

        /// <summary>Corpses hidden while their owners fall (0xAF).</summary>
        public CorpseFalls CorpseFalls { get; }

        public uint PlayerSerial { get; internal set; }
        public int Map { get; internal set; }
        public int MapWidth { get; internal set; }
        public int MapHeight { get; internal set; }

        /// <summary>Server light level: 0 = full daylight, 0x1F (and above) = darkest.</summary>
        public int GlobalLight { get; internal set; }
        public int PersonalLight { get; internal set; }

        public readonly Dictionary<uint, Mobile> Mobiles = new Dictionary<uint, Mobile>();
        public readonly Dictionary<uint, WorldItem> Items = new Dictionary<uint, WorldItem>();
        public readonly List<JournalEntry> Journal = new List<JournalEntry>();

        /// <summary>Items inside containers, by item serial.</summary>
        public readonly Dictionary<uint, ContainedItem> Contained = new Dictionary<uint, ContainedItem>();

        /// <summary>Containers the server opened for us, by container serial.</summary>
        public readonly Dictionary<uint, OpenContainer> OpenContainers = new Dictionary<uint, OpenContainer>();

        /// <summary>Server gumps currently shown, newest last.</summary>
        public readonly List<ServerGump> Gumps = new List<ServerGump>();

        public event Action<OpenContainer> ContainerOpened;
        /// <summary>A container's contents changed (item added, moved or removed); argument is the container serial.</summary>
        public event Action<uint> ContainerChanged;
        public event Action<uint, string> PaperdollOpened;
        public event Action<ServerGump> GumpOpened;
        public event Action<ServerGump> GumpClosed;

        public IEnumerable<ContainedItem> ItemsIn(uint container)
        {
            foreach (ContainedItem i in Contained.Values)
                if (i.Container == container)
                    yield return i;
        }

        /// <summary>
        /// What a corpse is drawn wearing: each 0x89 layer whose item is still inside it (0x3C), as TazUO's
        /// <c>FindItemByLayer</c> on the corpse (ItemView.cs:279-284). Looted pieces drop out.
        /// </summary>
        public IEnumerable<(byte Layer, int Graphic, int Hue)> CorpseWears(WorldItem corpse)
        {
            if (corpse?.CorpseEquipment == null)
                yield break;
            foreach (KeyValuePair<byte, uint> e in corpse.CorpseEquipment)
                if (Contained.TryGetValue(e.Value, out ContainedItem i) && i.Container == corpse.Serial)
                    yield return (e.Key, i.Graphic, i.Hue);
        }

        /// <summary>The serial of the item worn on a layer (21 = backpack), or 0.</summary>
        public uint Worn(Mobile m, byte layer) => m != null && m.Equipment.TryGetValue(layer, out EquippedItem e) ? e.Serial : 0;

        internal void RaiseContainerOpened(OpenContainer c) => ContainerOpened?.Invoke(c);
        internal void RaiseContainerChanged(uint container) => ContainerChanged?.Invoke(container);
        internal void RaisePaperdollOpened(uint serial, string title) => PaperdollOpened?.Invoke(serial, title);

        internal void AddGump(ServerGump g)
        {
            // A gump replaces an earlier one with the same type and serial (the server re-sends to refresh).
            for (int i = Gumps.Count - 1; i >= 0; i--)
                if (Gumps[i].TypeId == g.TypeId && Gumps[i].Serial == g.Serial)
                {
                    ServerGump old = Gumps[i];
                    Gumps.RemoveAt(i);
                    GumpClosed?.Invoke(old);
                }
            Gumps.Add(g);
            GumpOpened?.Invoke(g);
        }

        internal void RemoveGump(ServerGump g)
        {
            if (Gumps.Remove(g))
                GumpClosed?.Invoke(g);
        }

        public Mobile Player => PlayerSerial != 0 && Mobiles.TryGetValue(PlayerSerial, out Mobile m) ? m : null;

        public event Action<Mobile> MobileUpdated;
        public event Action<Mobile> MobileMoved;
        public event Action<uint> EntityRemoved;
        public event Action<WorldItem> ItemUpdated;
        public event Action<JournalEntry> JournalAdded;
        public event Action<int> LightChanged;

        /// <summary>Custom house designs by house serial (kept when the house leaves view; the revision says when to refresh).</summary>
        public readonly Dictionary<uint, HouseDesign> HouseDesigns = new Dictionary<uint, HouseDesign>();

        /// <summary>The latest design revision the server announced per house (0xBF 0x1D).</summary>
        public readonly Dictionary<uint, int> HouseRevisions = new Dictionary<uint, int>();

        /// <summary>A house's design arrived or changed: redraw it from <see cref="HouseDesigns"/>.</summary>
        public event Action<HouseDesign> HouseDesignChanged;

        internal void SetHouseDesign(HouseDesign design)
        {
            HouseDesigns[design.Serial] = design;
            HouseDesignChanged?.Invoke(design);
        }

        internal Mobile GetOrCreateMobile(uint serial)
        {
            if (!Mobiles.TryGetValue(serial, out Mobile m))
            {
                m = new Mobile { Serial = serial };
                Mobiles[serial] = m;
            }
            return m;
        }

        internal void RaiseMobileUpdated(Mobile m) => MobileUpdated?.Invoke(m);
        internal void RaiseMobileMoved(Mobile m) => MobileMoved?.Invoke(m);
        internal void RaiseItemUpdated(WorldItem i) => ItemUpdated?.Invoke(i);

        internal void Remove(uint serial)
        {
            bool removed = Mobiles.Remove(serial) | Items.Remove(serial);
            if (Contained.TryGetValue(serial, out ContainedItem inside))
            {
                Contained.Remove(serial);
                ContainerChanged?.Invoke(inside.Container);
                removed = true;
            }
            if (OpenContainers.Remove(serial))
                removed = true;
            foreach (Mobile m in Mobiles.Values)
            {
                foreach (KeyValuePair<byte, EquippedItem> e in m.Equipment)
                {
                    if (e.Value.Serial == serial)
                    {
                        m.Equipment.Remove(e.Key);
                        m.AppearanceVersion++;
                        MobileUpdated?.Invoke(m);
                        removed = true;
                        break;
                    }
                }
            }
            if (removed)
                EntityRemoved?.Invoke(serial);
        }

        /// <summary>
        /// An item is being put somewhere new (ground, a container, a paperdoll): forget the other places it
        /// was and tell whoever drew it there, so a picked-up item leaves the ground and a stowed one the paperdoll.
        /// </summary>
        internal void Detach(uint serial, bool ground = true, bool contained = true, bool worn = true)
        {
            if (ground && Items.Remove(serial))
                EntityRemoved?.Invoke(serial);
            if (contained && Contained.TryGetValue(serial, out ContainedItem inside))
            {
                Contained.Remove(serial);
                ContainerChanged?.Invoke(inside.Container);
            }
            if (!worn)
                return;
            foreach (Mobile m in Mobiles.Values)
            {
                byte layer = 0;
                bool found = false;
                foreach (KeyValuePair<byte, EquippedItem> e in m.Equipment)
                    if (e.Value.Serial == serial)
                    {
                        layer = e.Key;
                        found = true;
                        break;
                    }
                if (found)
                {
                    m.Equipment.Remove(layer);
                    m.AppearanceVersion++;
                    MobileUpdated?.Invoke(m);
                    return;
                }
            }
        }

        internal void AddJournal(JournalEntry e)
        {
            Journal.Add(e);
            if (Journal.Count > 200)
                Journal.RemoveAt(0);
            JournalAdded?.Invoke(e);
        }

        /// <summary>
        /// The newest line said by or about <paramref name="serial"/> within <paramref name="seconds"/> of
        /// <paramref name="now"/> (a single-click name, for an item drawn in a gump rather than the world), or null.
        /// </summary>
        public JournalEntry? RecentLine(uint serial, DateTime now, double seconds)
        {
            for (int i = Journal.Count - 1; i >= 0; i--)
            {
                JournalEntry e = Journal[i];
                if ((now - e.Time).TotalSeconds > seconds)
                    break;
                if (e.Serial == serial)
                    return e;
            }
            return null;
        }

        internal void SetLight(int global)
        {
            GlobalLight = global;
            LightChanged?.Invoke(global);
        }

        /// <summary>Removes everything farther than <paramref name="range"/> tiles from the player (the server stops updating it).</summary>
        public void PruneOutOfRange(int range)
        {
            Mobile p = Player;
            if (p == null)
                return;
            var remove = new List<uint>();
            foreach (Mobile m in Mobiles.Values)
                if (m.Serial != PlayerSerial && Math.Max(Math.Abs(m.X - p.X), Math.Abs(m.Y - p.Y)) > range)
                    remove.Add(m.Serial);
            foreach (WorldItem i in Items.Values)
                if (Math.Max(Math.Abs(i.X - p.X), Math.Abs(i.Y - p.Y)) > range)
                    remove.Add(i.Serial);
            foreach (uint s in remove)
                Remove(s);
        }
    }
}
