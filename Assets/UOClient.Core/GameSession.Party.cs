using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>0xBF 0x06 parties, as ModernUO's PartyPackets write them and its PartyMessage handler reads them.</summary>
    public sealed partial class GameSession
    {
        /// <summary>The journal type of a party message (ClassicUO's MessageType.Party); journal only, never overhead.</summary>
        public const byte PartyMessageType = 0xFF;

        /// <summary>ClassicUO's default party message hue.</summary>
        public const ushort PartyHue = 0x44;

        /// <summary>The party in the server's order, us included; the first is the leader. Empty when not in a party.</summary>
        public readonly List<uint> PartyMembers = new List<uint>();

        /// <summary>Who invited us (0x07), until we accept, decline or the server drops the invitation; else 0.</summary>
        public uint PartyInviter { get; private set; }

        /// <summary>Whether we let the party loot our corpse, as last sent (the server doesn't report it).</summary>
        public bool PartyCanLoot { get; private set; }

        public uint PartyLeader => PartyMembers.Count > 0 ? PartyMembers[0] : 0;
        public bool InParty => PartyMembers.Count > 0;

        /// <summary>The member list or the invitation changed.</summary>
        public event Action PartyChanged;
        /// <summary>Someone invited us (their serial); ModernUO declines on its own after 30 seconds.</summary>
        public event Action<uint> PartyInvited;

        // Names of members seen at some point, so a member out of range keeps a name in the list and their messages.
        private readonly Dictionary<uint, string> _partyNames = new Dictionary<uint, string>();

        /// <summary>A member's name: the mobile's if in range, else the last one seen, else "(unknown)".</summary>
        public string PartyMemberName(uint serial)
        {
            if (World.Mobiles.TryGetValue(serial, out Mobile m) && !string.IsNullOrEmpty(m.Name))
                return _partyNames[serial] = m.Name;
            return _partyNames.TryGetValue(serial, out string name) ? name : "(unknown)";
        }

        /// <summary>0xBF 0x06; true if consumed.</summary>
        private bool PartyExtended(int sub, PacketReader r)
        {
            if (sub != 0x06)
                return false;
            int code = r.ReadByte();
            switch (code)
            {
                case 0x01: // member list: count, serials
                case 0x02: // removed: count, the removed serial, then the remaining serials
                {
                    bool removal = code == 0x02;
                    int count = r.ReadByte();
                    if (removal)
                        r.ReadUInt32();
                    PartyMembers.Clear();
                    // ClassicUO: a party of one or none is no party (ModernUO sends 0 to whoever left, declined or was disbanded).
                    if (count > 1)
                        for (int i = 0; i < count; i++)
                        {
                            uint serial = r.ReadUInt32();
                            PartyMembers.Add(serial);
                            PartyMemberName(serial);
                        }
                    if (PartyMembers.Count == 0)
                        PartyCanLoot = false;
                    PartyInviter = 0;
                    PartyChanged?.Invoke();
                    return true;
                }
                case 0x03: // private message: from, text
                case 0x04: // public message: from, text
                {
                    bool toAll = code == 0x04;
                    uint from = r.ReadUInt32();
                    string text = r.ReadUnicodeZ();
                    if (string.IsNullOrWhiteSpace(text))
                        return true;
                    string name = PartyMemberName(from);
                    World.AddJournal(new JournalEntry
                    {
                        Time = DateTime.Now,
                        Speaker = toAll ? $"[Party][{name}]" : $"[Party][{name}] (private)",
                        Name = name,
                        Text = text.Trim(),
                        Hue = SpeechOptions.Party,
                        Type = PartyMessageType,
                        Serial = from
                    });
                    return true;
                }
                case 0x07: // invitation: the leader
                    PartyInviter = r.ReadUInt32();
                    PartyMemberName(PartyInviter);
                    PartyInvited?.Invoke(PartyInviter);
                    PartyChanged?.Invoke();
                    return true;
            }
            return true;
        }

        private void SendParty(byte[] packet)
        {
            if (State == SessionState.InWorld)
                _conn.Send(packet);
        }

        /// <summary>Asks to add someone; the server gives a target cursor (only the leader, or someone not in a party, may).</summary>
        public void PartyInvite() => SendParty(PartyAddRequest());

        /// <summary>The leader removes a member; anyone removing themselves leaves.</summary>
        public void PartyRemove(uint serial) => SendParty(PartyRemoveRequest(serial));

        public void LeaveParty() => PartyRemove(World.PlayerSerial);

        /// <summary>Says something to the whole party, or privately to one member.</summary>
        public void PartySay(string text, uint to = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            text = text.Trim();
            if (text.Length > 128) // ModernUO drops longer party messages
                text = text.Substring(0, 128);
            SendParty(to != 0 ? PartyPrivateMessageRequest(to, text) : PartyPublicMessageRequest(text));
        }

        public void SetPartyCanLoot(bool canLoot)
        {
            PartyCanLoot = canLoot;
            SendParty(PartyCanLootRequest(canLoot));
            PartyChanged?.Invoke();
        }

        public void AcceptPartyInvite()
        {
            if (PartyInviter == 0)
                return;
            SendParty(PartyAcceptRequest(PartyInviter));
            PartyInviter = 0;
            PartyChanged?.Invoke();
        }

        public void DeclinePartyInvite()
        {
            if (PartyInviter == 0)
                return;
            SendParty(PartyDeclineRequest(PartyInviter));
            PartyInviter = 0;
            PartyChanged?.Invoke();
        }

        /// <summary>
        /// A typed line starting with "/" (ClassicUO's party chat): "/accept" and "/decline" answer an invitation,
        /// "/add" invites, "/quit" leaves, "/N text" whispers the Nth member (1-based, list order), anything else goes to
        /// the whole party. Not in a party, the server answers "You are not in a party."
        /// </summary>
        public void PartyLine(string line)
        {
            var (command, member, text) = ParsePartyLine(line);
            switch (command)
            {
                case PartyLineCommand.Accept: AcceptPartyInvite(); break;
                case PartyLineCommand.Decline: DeclinePartyInvite(); break;
                case PartyLineCommand.Add: PartyInvite(); break;
                case PartyLineCommand.Quit: LeaveParty(); break;
                case PartyLineCommand.Say:
                    PartySay(text, member > 0 && member <= PartyMembers.Count ? PartyMembers[member - 1] : 0);
                    break;
            }
        }

        // ---- party health bars: style and quick heal / cure ----

        /// <summary>Party members' health bars use the party style (TazUO's Profile.UsePartyHealthBars, on by default).</summary>
        public bool UsePartyHealthBars { get; set; } = true;

        /// <summary>TazUO's BaseHealthBarGump.ShowPartyStyleBar: the mobile is in our party (us included) and the style is on.</summary>
        public bool ShowsPartyHealthBar(uint serial) => UsePartyHealthBars && PartyMembers.Contains(serial);

        /// <summary>The heal and cure buttons' spells (TazUO's Profile.QuickHealSpell 29 Greater Heal, QuickCureSpell 11 Cure).</summary>
        public int QuickHealSpell { get; set; } = 29;
        public int QuickCureSpell { get; set; } = 11;

        /// <summary>TazUO's PartyManager.PartyHealTimer, as session seconds: the next cursor counts only once this has passed.</summary>
        public const double PartyHealDelay = 0.05;

        private double _partyHealAfter;

        /// <summary>Who the next target cursor goes to after a quick heal or cure (TazUO's PartyHealTarget), or 0.</summary>
        public uint PartyHealTarget { get; private set; }

        /// <summary>GameActions.QuickHeal: casts the heal spell; the cursor it brings is answered with <paramref name="target"/>.</summary>
        public void QuickHeal(uint target) => QuickCast(QuickHealSpell, target);

        /// <summary>GameActions.QuickCure: the same with the cure spell.</summary>
        public void QuickCure(uint target) => QuickCast(QuickCureSpell, target);

        private void QuickCast(int spell, uint target)
        {
            if (State != SessionState.InWorld)
                return;
            CastSpell(spell);
            _partyHealAfter = _now + PartyHealDelay;
            PartyHealTarget = target;
        }

        /// <summary>
        /// TazUO's TargetCursor handler (TargetCursor.cs:17-22): a cursor arriving after the heal delay goes to the heal
        /// target through <see cref="TargetObject"/>, so the criminal question can still hold it, and the target is
        /// forgotten. A cursor before the delay leaves it for the next one. A cancel clears it without sending anything,
        /// and so does a target no longer in view (TargetManager.Target sends nothing for an unknown entity; the cursor
        /// stays up for the player).
        /// </summary>
        private void AnswerPartyHeal()
        {
            if (PartyHealTarget == 0 || _partyHealAfter >= _now)
                return;
            uint serial = PartyHealTarget;
            PartyHealTarget = 0;
            _partyHealAfter = 0;
            if (Target != null && World.Mobiles.ContainsKey(serial))
                TargetObject(serial);
        }

        public enum PartyLineCommand { None, Say, Accept, Decline, Add, Quit }

        /// <summary>Splits a "/..." line into a command, a 1-based member number for a private message (0 = everyone) and the text.</summary>
        public static (PartyLineCommand Command, int Member, string Text) ParsePartyLine(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '/')
                return (PartyLineCommand.None, 0, "");
            string rest = line.Substring(1).Trim();
            switch (rest.ToLowerInvariant())
            {
                case "": return (PartyLineCommand.None, 0, "");
                case "accept": return (PartyLineCommand.Accept, 0, "");
                case "decline": return (PartyLineCommand.Decline, 0, "");
                case "add": return (PartyLineCommand.Add, 0, "");
                case "quit": case "leave": return (PartyLineCommand.Quit, 0, "");
            }
            int digits = 0;
            while (digits < rest.Length && digits < 2 && char.IsDigit(rest[digits]))
                digits++;
            if (digits > 0 && digits < rest.Length && rest[digits] == ' ')
            {
                int member = int.Parse(rest.Substring(0, digits));
                string said = rest.Substring(digits + 1).Trim();
                if (member >= 1 && member <= 10 && said.Length > 0)
                    return (PartyLineCommand.Say, member, said);
            }
            return (PartyLineCommand.Say, 0, rest);
        }

        /// <summary>0xBF 0x06 0x01: add a member. ModernUO reads nothing more; ClassicUO sends a zero serial (10 bytes).</summary>
        public static byte[] PartyAddRequest() => new PacketWriter(0xBF).UInt16(0x06).Byte(0x01).UInt32(0).ToArray();

        /// <summary>0x02: remove the member (ourselves to leave), 10 bytes.</summary>
        public static byte[] PartyRemoveRequest(uint serial) => new PacketWriter(0xBF).UInt16(0x06).Byte(0x02).UInt32(serial).ToArray();

        /// <summary>0x03: to one member, big-endian UTF-16 null-terminated text.</summary>
        public static byte[] PartyPrivateMessageRequest(uint to, string text) =>
            new PacketWriter(0xBF).UInt16(0x06).Byte(0x03).UInt32(to).UnicodeZ(text).ToArray();

        /// <summary>0x04: to the whole party (the server echoes it back to us too).</summary>
        public static byte[] PartyPublicMessageRequest(string text) => new PacketWriter(0xBF).UInt16(0x06).Byte(0x04).UnicodeZ(text).ToArray();

        /// <summary>0x06: let the party loot our corpse, one bool (7 bytes).</summary>
        public static byte[] PartyCanLootRequest(bool canLoot) => new PacketWriter(0xBF).UInt16(0x06).Byte(0x06).Bool(canLoot).ToArray();

        /// <summary>0x08: accept the leader's invitation (10 bytes).</summary>
        public static byte[] PartyAcceptRequest(uint leader) => new PacketWriter(0xBF).UInt16(0x06).Byte(0x08).UInt32(leader).ToArray();

        /// <summary>0x09: decline it (10 bytes).</summary>
        public static byte[] PartyDeclineRequest(uint leader) => new PacketWriter(0xBF).UInt16(0x06).Byte(0x09).UInt32(leader).ToArray();
    }
}
