using System;
using System.Collections.Generic;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A target cursor the server asked for (spells, skills, commands, "select an item").</summary>
    public sealed class TargetRequest
    {
        public uint CursorId;
        /// <summary>0 = objects only, 1 = objects or ground.</summary>
        public byte Type;
        /// <summary>0 neutral, 1 harmful, 2 beneficial.</summary>
        public byte Flags;
        public bool AllowGround => Type == 1;

        /// <summary>
        /// House or boat placement (0x99): preview <see cref="MultiGraphic"/> under the cursor. The server
        /// centers the multi at the targeted tile minus <see cref="MultiOffsetX"/>/Y/Z.
        /// </summary>
        public bool IsMulti;
        public ushort MultiGraphic;
        public int MultiOffsetX, MultiOffsetY, MultiOffsetZ;
    }

    public struct SkillEntry
    {
        public int Id;          // 0-based
        public double Value;    // shown value (tenths in the packet)
        public double Base;
        public double Cap;
        public byte Lock;       // 0 up, 1 down, 2 locked
    }

    /// <summary>Targeting, war mode and combat, live stats, skills and spells.</summary>
    public sealed partial class GameSession
    {
        public TargetRequest Target { get; private set; }
        public bool WarMode { get; private set; }

        /// <summary>The mobile we are fighting (the server's current combatant), or 0.</summary>
        public uint Combatant { get; private set; }

        /// <summary>
        /// The mobile we last attacked or the server last made our combatant (TazUO's TargetManager.LastAttack, set by
        /// GameActions.Attack and by 0xAA), or 0.
        /// </summary>
        public uint LastAttack { get; private set; }

        /// <summary>
        /// <see cref="LastAttack"/> was set to a mobile (never raised for 0 or an item serial): TazUO opens or retargets
        /// the last attack health bar here (OpenHealthBarForLastAttack, on by default).
        /// </summary>
        public event Action<uint> LastAttackSet;

        public readonly Dictionary<int, SkillEntry> Skills = new Dictionary<int, SkillEntry>();

        public event Action<TargetRequest> TargetRequested;
        public event Action TargetEnded;
        public event Action<bool> WarModeChanged;
        public event Action<uint, int> Damaged;
        public event Action SkillsUpdated;

        private void RegisterCombatHandlers()
        {
            _handlers[0x6C] = TargetCursor;
            _handlers[0x99] = MultiTargetCursor;
            _handlers[0x72] = r => { WarMode = r.ReadBool(); WarModeChanged?.Invoke(WarMode); };
            _handlers[0xAA] = r => { Combatant = r.ReadUInt32(); SetLastAttack(Combatant); };
            _handlers[0x17] = HealthBarState;
            _handlers[0x2F] = _ => { };   // swing animation
            _handlers[0xA1] = r => Stat(r, (m, max, cur) => { m.HitsMax = max; m.Hits = cur; });
            _handlers[0xA2] = r => Stat(r, (m, max, cur) => { m.ManaMax = max; m.Mana = cur; });
            _handlers[0xA3] = r => Stat(r, (m, max, cur) => { m.StaminaMax = max; m.Stamina = cur; });
            _handlers[0x2D] = MobileAttributes;
            _handlers[0x0B] = r => { uint s = r.ReadUInt32(); Damaged?.Invoke(s, r.ReadUInt16()); };
            _handlers[0x3A] = SkillList;
            _handlers[0xD1] = _ => Fail("Logged out."); // logout acknowledged
        }

        /// <summary>Asks to log out; the server saves and confirms, then the connection closes.</summary>
        public void Logout()
        {
            if (State == SessionState.InWorld)
                _conn.Send(new byte[] { 0xD1, 0x00 });
        }

        // ---- targeting ----

        private void TargetCursor(PacketReader r)
        {
            byte type = r.ReadByte();
            uint id = r.ReadUInt32();
            byte flags = r.ReadByte();
            if (flags == 3)
            {
                // The server cancelled the cursor.
                Target = null;
                TargetEnded?.Invoke();
                return;
            }
            Target = new TargetRequest { Type = type, CursorId = id, Flags = flags };
            TargetRequested?.Invoke(Target);
        }

        /// <summary>
        /// 0x99 (30 bytes on 7.0.9+, 26 before): allow ground, cursor id, flags, 11 zero bytes, multi id,
        /// x/y/z offset (int16 each), and on newer clients 4 more bytes (0 from ModernUO). Answered like any
        /// cursor with 0x6C, normally a ground location.
        /// </summary>
        private void MultiTargetCursor(PacketReader r)
        {
            var t = new TargetRequest { Type = r.ReadByte(), CursorId = r.ReadUInt32(), Flags = r.ReadByte(), IsMulti = true };
            r.Skip(11);
            int multi = r.ReadUInt16();
            t.MultiGraphic = (ushort)(multi >= 0x4000 ? multi - 0x4000 : multi); // some servers send the old 0x4000 form
            t.MultiOffsetX = r.ReadInt16();
            t.MultiOffsetY = r.ReadInt16();
            t.MultiOffsetZ = r.ReadInt16();
            Target = t;
            TargetRequested?.Invoke(Target);
        }

        /// <summary>Answers the target cursor with a mobile or item.</summary>
        public void TargetObject(uint serial) =>
            AnswerTarget(serial, serial == World.PlayerSerial ? "targetself" : "target " + MacroRecorder.Hex(serial));

        private void AnswerTarget(uint serial, string macro)
        {
            TargetRequest t = Target;
            if (t == null || State != SessionState.InWorld)
                return;
            Macros.Note(macro);
            Target = null;
            LastTarget = serial;
            _conn.Send(new PacketWriter(0x6C, 19).Byte(0).UInt32(t.CursorId).Byte(t.Flags).UInt32(serial)
                .UInt16(0).UInt16(0).Byte(0).Byte(0).UInt16(0).ToArray());
            TargetEnded?.Invoke();
        }

        /// <summary>The last object we targeted (for the "last target" macro), or 0.</summary>
        public uint LastTarget { get; private set; }

        /// <summary>Answers the cursor with the last target, if there was one.</summary>
        public bool TargetLast()
        {
            if (Target == null || LastTarget == 0)
                return false;
            AnswerTarget(LastTarget, "lasttarget");
            return true;
        }

        public void TargetSelf() => TargetObject(World.PlayerSerial);

        /// <summary>Answers the target cursor with a map location (graphic 0 = the land; else a static there).</summary>
        public void TargetGround(int x, int y, int z, int graphic = 0)
        {
            TargetRequest t = Target;
            if (t == null || State != SessionState.InWorld)
                return;
            Target = null;
            _conn.Send(GroundTargetReply(t, x, y, z, graphic));
            TargetEnded?.Invoke();
        }

        /// <summary>0x6C answering a cursor with a map location (also the answer to a 0x99 placement cursor).</summary>
        public static byte[] GroundTargetReply(TargetRequest t, int x, int y, int z, int graphic = 0) =>
            new PacketWriter(0x6C, 19).Byte(1).UInt32(t.CursorId).Byte(t.Flags).UInt32(0)
                .UInt16(x).UInt16(y).Byte(0).Byte((byte)(sbyte)z).UInt16(graphic).ToArray();

        /// <summary>
        /// Answers the target cursor with a static (a tree, a rock, a water tile, a floor) standing at (x, y, z).
        /// ModernUO turns it into a StaticTarget only if a static with that graphic stands at exactly that z, so
        /// trees for lumberjacking and the like can't be hit with a land target.
        /// </summary>
        public void TargetStatic(int x, int y, int z, ushort graphic, bool surface, int height)
        {
            TargetRequest t = Target;
            if (t == null || State != SessionState.InWorld)
                return;
            Target = null;
            _conn.Send(StaticTargetReply(t, x, y, z, graphic, surface, height, _settings.Version));
            TargetEnded?.Invoke();
        }

        /// <summary>
        /// 0x6C answering a cursor with a static. 7.0.9+ (High Seas) clients send a surface's top rather than its
        /// base, and ModernUO takes the tile's height back off before matching, as ClassicUO does it.
        /// </summary>
        public static byte[] StaticTargetReply(TargetRequest t, int x, int y, int z, ushort graphic, bool surface, int height, ClientVersion version)
        {
            if (surface && version >= new ClientVersion(7, 0, 9, 0))
                z += height;
            return GroundTargetReply(t, x, y, z, graphic);
        }

        /// <summary>
        /// Which static on a clicked tile a target cursor means: the highest one not over the head of someone
        /// standing at <paramref name="refZ"/> (so a tree trunk, not the roof above us). -1 when there is none.
        /// </summary>
        public static int PickTargetStatic(IReadOnlyList<StaticCell> cells, int refZ)
        {
            int best = -1;
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].Z < refZ + 14 && (best < 0 || cells[i].Z >= cells[best].Z))
                    best = i;
            return best;
        }

        /// <summary>Escape: tells the server the cursor was cancelled (location -1, -1, no serial).</summary>
        public void CancelTarget()
        {
            TargetRequest t = Target;
            if (t == null)
                return;
            Target = null;
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x6C, 19).Byte(t.Type).UInt32(t.CursorId).Byte(t.Flags).UInt32(0)
                    .UInt16(0xFFFF).UInt16(0xFFFF).Byte(0).Byte(0).UInt16(0).ToArray());
            TargetEnded?.Invoke();
        }

        // ---- combat ----

        public void SetWarMode(bool on)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x72, 5).Bool(on).Byte(0).Byte(0x32).Byte(0).ToArray());
        }

        /// <summary>ModernUO's Notoriety.Innocent and Notoriety.Ally (Notoriety.cs:7-8), as 0x77 / 0x78 / 0x22 carry them.</summary>
        public const byte NotorietyInnocent = 1, NotorietyAlly = 2;

        /// <summary>
        /// Ask before attacking an innocent while we are innocent or an ally (TazUO's Profile.EnabledCriminalActionQuery,
        /// on by default).
        /// </summary>
        public bool CriminalActionQuery { get; set; } = true;

        /// <summary>The mobile an attack is waiting on the "this may flag you criminal" answer for, or 0.</summary>
        public uint PendingAttackQuery { get; private set; }

        /// <summary>
        /// An attack needs the player's yes first (TazUO's QuestionGump of type Attack): show the question for this serial
        /// and answer with <see cref="AnswerAttackQuery"/>. A question already up for another serial is replaced by this one.
        /// Nothing is sent until the answer is yes.
        /// </summary>
        public event Action<uint> AttackQueryRequested;

        /// <summary>
        /// TazUO's GameActions.Attack (GameActions.cs:584-635): with the query on, attacking a known innocent mobile while
        /// we are innocent or an ally asks first (only the same serial's question already up is left alone); otherwise
        /// 0x05 goes straight out and the mobile becomes our last attack.
        /// </summary>
        public void Attack(uint serial)
        {
            if (State != SessionState.InWorld)
                return;
            if (NeedsCriminalQuery(serial))
            {
                if (PendingAttackQuery == serial)
                    return;
                PendingAttackQuery = serial;
                AttackQueryRequested?.Invoke(serial);
                return;
            }
            SetLastAttack(serial);
            _conn.Send(AttackRequest(serial));
        }

        /// <summary>
        /// The player's answer to the attack question for <paramref name="serial"/>: yes sends 0x05 (only that, as TazUO's
        /// callback does; the server's 0xAA then makes it our last attack); no, or an answer to a question no longer up,
        /// sends nothing.
        /// </summary>
        public void AnswerAttackQuery(uint serial, bool yes)
        {
            if (serial == 0 || PendingAttackQuery != serial)
                return;
            PendingAttackQuery = 0;
            if (yes && State == SessionState.InWorld)
                _conn.Send(AttackRequest(serial));
        }

        /// <summary>GameActions.Attack's test: the query on, us innocent or an ally, the target a known innocent mobile and not us.</summary>
        public bool NeedsCriminalQuery(uint serial) =>
            CriminalActionQuery && World.Player is Mobile p && serial != World.PlayerSerial
            && World.Mobiles.TryGetValue(serial, out Mobile m)
            && QueriesAttack(p.Notoriety, m.Notoriety);

        /// <summary>The notoriety half of the test: an innocent target, while we are innocent or an ally.</summary>
        public static bool QueriesAttack(byte player, byte target) =>
            (player == NotorietyInnocent || player == NotorietyAlly) && target == NotorietyInnocent;

        /// <summary>0x05 AttackRequest (TazUO's Send_AttackRequest; ModernUO's AttackReq reads the serial).</summary>
        public static byte[] AttackRequest(uint serial) => new PacketWriter(0x05, 5).UInt32(serial).ToArray();

        private void SetLastAttack(uint serial)
        {
            LastAttack = serial;
            if (IsMobileSerial(serial))
                LastAttackSet?.Invoke(serial);
        }

        /// <summary>ClassicUO's SerialHelper.IsMobile: 1 through 0x3FFFFFFF.</summary>
        public static bool IsMobileSerial(uint serial) => serial != 0 && serial < 0x40000000;

        /// <summary>
        /// 0x17 (ModernUO's CreateMobileHealthbar): serial, a count, then per entry a uint16 type (1 poison, 2 yellow) and
        /// a byte level (0 off), as TazUO's NewHealthBarUpdate reads it.
        /// </summary>
        private void HealthBarState(PacketReader r)
        {
            uint serial = r.ReadUInt32();
            if (!World.Mobiles.TryGetValue(serial, out Mobile m))
                return;
            int count = r.ReadUInt16();
            for (int i = 0; i < count && r.Remaining >= 3; i++)
            {
                int type = r.ReadUInt16();
                bool on = r.ReadByte() != 0;
                if (type == 1)
                    m.Poisoned = on;
                else if (type == 2)
                    m.YellowHits = on;
            }
            World.RaiseMobileUpdated(m);
        }

        /// <summary>
        /// Changes a stat's gain lock (0 str, 1 dex, 2 int; 0 up, 1 down, 2 locked): 0xBF 0x1A, as TazUO's
        /// Send_StatLockStateRequest writes it and ModernUO's StatLockChange reads it (in game only, a lock above 2 taken as up).
        /// </summary>
        public void SetStatLock(int stat, byte lockState)
        {
            if (State != SessionState.InWorld || stat < 0 || stat > 2)
                return;
            if (World.Player is Mobile p)
            {
                if (stat == 0) p.StrLock = lockState;
                else if (stat == 1) p.DexLock = lockState;
                else p.IntLock = lockState;
            }
            _conn.Send(StatLockRequest(stat, lockState));
        }

        public static byte[] StatLockRequest(int stat, byte lockState) =>
            new PacketWriter(0xBF).UInt16(0x1A).Byte(stat).Byte(lockState).ToArray();

        /// <summary>
        /// 0xBF 0x19 extended stats. Version 2 (ModernUO's SendStatLockInfo, sent with our own 0x11 status): serial, a
        /// byte, then the locks packed str &lt;&lt; 4 | dex &lt;&lt; 2 | int. Version 0 (bonded) is not kept.
        /// </summary>
        private bool ExtendedStats(int sub, PacketReader r)
        {
            if (sub != 0x19)
                return false;
            byte version = r.ReadByte();
            uint serial = r.ReadUInt32();
            if (version == 2 && serial == World.PlayerSerial && r.Remaining >= 2 && World.Player is Mobile p)
            {
                r.ReadByte();
                byte state = r.ReadByte();
                p.StrLock = (byte)((state >> 4) & 3);
                p.DexLock = (byte)((state >> 2) & 3);
                p.IntLock = (byte)(state & 3);
                World.RaiseMobileUpdated(p);
            }
            return true;
        }

        private void Stat(PacketReader r, Action<Mobile, int, int> set)
        {
            Mobile m = World.GetOrCreateMobile(r.ReadUInt32());
            int max = r.ReadUInt16(), cur = r.ReadUInt16();
            set(m, max, cur);
            World.RaiseMobileUpdated(m);
        }

        private void MobileAttributes(PacketReader r)
        {
            Mobile m = World.GetOrCreateMobile(r.ReadUInt32());
            m.HitsMax = r.ReadUInt16();
            m.Hits = r.ReadUInt16();
            m.ManaMax = r.ReadUInt16();
            m.Mana = r.ReadUInt16();
            m.StaminaMax = r.ReadUInt16();
            m.Stamina = r.ReadUInt16();
            World.RaiseMobileUpdated(m);
        }

        // ---- skills and spells (text commands, 0x12) ----

        private void TextCommand(byte type, string command)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x12).Byte(type).AsciiZ(command).ToArray());
        }

        /// <summary>Uses a skill by 0-based id (e.g. 21 Hiding, 46 Meditation); targeted skills then send a cursor.</summary>
        public void UseSkill(int skillId)
        {
            if (State == SessionState.InWorld)
                Macros.Note($"skill {skillId}");
            TextCommand(0x24, $"{skillId} 0");
        }

        /// <summary>Casts a spell by number (1 = Clumsy ... 64 = Water Elemental for Magery).</summary>
        public void CastSpell(int spellId)
        {
            if (State == SessionState.InWorld)
                Macros.Note($"cast {spellId}");
            TextCommand(0x56, spellId.ToString());
        }

        /// <summary>Opens a spellbook (1 = Magery, 2 = Necromancy, 3 = Chivalry, ...).</summary>
        public void OpenSpellbook(int type = 1) => TextCommand(0x43, type.ToString());

        /// <summary>Plays an emote animation (bow = "bow", salute = "salute").</summary>
        public void Animate(string action) => TextCommand(0xC7, action);

        /// <summary>Asks for our skill list (answered with 0x3A).</summary>
        public void RequestSkills()
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x34, 10).UInt32(0xEDEDEDED).Byte(5).UInt32(World.PlayerSerial).ToArray());
        }

        /// <summary>Changes a skill's gain lock: 0 up, 1 down, 2 locked.</summary>
        public void SetSkillLock(int skillId, byte lockState)
        {
            if (State == SessionState.InWorld)
                _conn.Send(new PacketWriter(0x3A).UInt16(skillId).Byte(lockState).ToArray());
        }

        /// <summary>
        /// 0x3A: type 0x00 full list, 0x02 full list with caps, 0xFF single update, 0xDF single update with cap.
        /// Entries: uint16 id (1-based in full lists), uint16 value, uint16 base, byte lock, [uint16 cap].
        /// </summary>
        private void SkillList(PacketReader r)
        {
            byte type = r.ReadByte();
            bool caps = type == 0x02 || type == 0xDF;
            bool single = type == 0xFF || type == 0xDF;
            if (!single)
                Skills.Clear();
            while (r.Remaining >= 7)
            {
                int id = r.ReadUInt16();
                if (!single)
                {
                    if (id == 0)
                        break;
                    id--;
                }
                var s = new SkillEntry
                {
                    Id = id,
                    Value = r.ReadUInt16() / 10.0,
                    Base = r.ReadUInt16() / 10.0,
                    Lock = r.ReadByte(),
                    Cap = caps ? r.ReadUInt16() / 10.0 : 100.0
                };
                Skills[id] = s;
                if (single)
                    break;
            }
            SkillsUpdated?.Invoke();
        }

        /// <summary>Skill names by id (AOS through Stygian Abyss).</summary>
        public static readonly string[] SkillNames =
        {
            "Alchemy", "Anatomy", "Animal Lore", "Item Identification", "Arms Lore", "Parrying", "Begging", "Blacksmithy",
            "Bowcraft/Fletching", "Peacemaking", "Camping", "Carpentry", "Cartography", "Cooking", "Detecting Hidden",
            "Discordance", "Evaluating Intelligence", "Healing", "Fishing", "Forensic Evaluation", "Herding", "Hiding",
            "Provocation", "Inscription", "Lockpicking", "Magery", "Resisting Spells", "Tactics", "Snooping", "Musicianship",
            "Poisoning", "Archery", "Spirit Speak", "Stealing", "Tailoring", "Animal Taming", "Taste Identification",
            "Tinkering", "Tracking", "Veterinary", "Swordsmanship", "Mace Fighting", "Fencing", "Wrestling", "Lumberjacking",
            "Mining", "Meditation", "Stealth", "Remove Trap", "Necromancy", "Focus", "Chivalry", "Bushido", "Ninjitsu",
            "Spellweaving", "Mysticism", "Imbuing", "Throwing"
        };

        /// <summary>Skills that can be used directly from the skills window (the classic "use" button).</summary>
        public static readonly HashSet<int> UsableSkills = new HashSet<int>
        {
            1, 2, 3, 4, 6, 9, 12, 14, 15, 16, 17, 19, 21, 22, 23, 30, 32, 33, 35, 36, 38, 46, 47, 48, 56
        };
    }
}
