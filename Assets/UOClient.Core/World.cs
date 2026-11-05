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
        public readonly Dictionary<byte, EquippedItem> Equipment = new Dictionary<byte, EquippedItem>();

        /// <summary>Changes whenever appearance changes (body, hue or equipment), so renderers can cache by it.</summary>
        public int AppearanceVersion;

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
