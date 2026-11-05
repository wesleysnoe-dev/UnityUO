using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A new character's choices (stats total 90, four skills totalling 120, each at most 50).</summary>
    public sealed class NewCharacter
    {
        public string Name = "";
        public bool Female;
        public int Str = 35, Dex = 30, Int = 25;
        public (int Id, int Value)[] Skills = { (40, 30), (27, 30), (17, 30), (1, 30) }; // swords, tactics, healing, anatomy
        public ushort SkinHue = 0x03EA;
        public ushort HairStyle = 0x203B, HairHue = 0x044E;
        public ushort BeardStyle, BeardHue;
        public ushort ShirtHue = 0x0515, PantsHue = 0x01BB;
        public int City;
    }

    public struct StartCity
    {
        public int Index;
        public string Name, Building;
    }

    /// <summary>Choosing a shard and a character by hand (the login screens), instead of from the settings.</summary>
    public sealed partial class GameSession
    {
        public readonly List<(int Index, string Name)> Servers = new List<(int, string)>();

        /// <summary>Character names by slot; empty for a free slot.</summary>
        public readonly List<string> Characters = new List<string>();

        public readonly List<StartCity> Cities = new List<StartCity>();

        /// <summary>The server list arrived (interactive logins with more than one shard).</summary>
        public event Action ServersListed;

        /// <summary>The character list arrived or changed (interactive logins).</summary>
        public event Action CharactersListed;

        public void SelectServer(int index)
        {
            if (State != SessionState.LoggingIn)
                return;
            SetStatus("Connecting to the shard…");
            _conn.Send(new PacketWriter(0xA0, 3).UInt16(index).ToArray());
        }

        public void PlayCharacter(int slot)
        {
            if (State != SessionState.CharacterSelect || slot < 0 || slot >= Characters.Count || Characters[slot].Length == 0)
                return;
            _settings.CharacterName = Characters[slot];
            SetState(SessionState.EnteringWorld, $"Playing '{Characters[slot]}'");
            SendPlayCharacter(slot);
        }

        public void CreateCharacter(NewCharacter c)
        {
            if (State != SessionState.CharacterSelect)
                return;
            _settings.CharacterName = c.Name;
            SetState(SessionState.EnteringWorld, $"Creating '{c.Name}'");
            var w = new PacketWriter(0xF8, 106)
                .UInt32(0xEDEDEDED).UInt32(0xFFFFFFFF).Byte(0)
                .Ascii(c.Name, 30).UInt16(0)
                .UInt32(0x1F).UInt32(1).UInt32(0)
                .Byte(0).Zero(15)                    // profession: custom
                .Byte(c.Female ? 3 : 2)              // human (post-7.0 numbering: 2 male, 3 female)
                .Byte(c.Str).Byte(c.Dex).Byte(c.Int);
            for (int i = 0; i < 4; i++)
            {
                var (id, value) = i < c.Skills.Length ? c.Skills[i] : (0, 0);
                w.Byte(id).Byte(value);
            }
            w.UInt16(c.SkinHue).UInt16(c.HairStyle).UInt16(c.HairHue)
                .UInt16(c.Female ? 0 : c.BeardStyle).UInt16(c.Female ? 0 : c.BeardHue)
                .Byte(0).Byte(c.City)
                .Int32(0).UInt32(0x0100007F)
                .UInt16(c.ShirtHue).UInt16(c.PantsHue);
            _conn.Send(w.ToArray());
        }

        /// <summary>Deletes a character (the server re-sends the list, or refuses: too young, logged in, ...).</summary>
        public void DeleteCharacter(int slot)
        {
            if (State != SessionState.CharacterSelect || slot < 0 || slot >= Characters.Count)
                return;
            _conn.Send(new PacketWriter(0x83, 39).Ascii(_settings.Password ?? "", 30).Int32(slot).UInt32(0x0100007F).ToArray());
        }

        private void ReadServers(PacketReader r)
        {
            Servers.Clear();
            r.ReadByte(); // flags
            int count = r.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                int index = r.ReadUInt16();
                string name = r.ReadAscii(32);
                r.Skip(6); // percent full, time zone, address
                Servers.Add((index, name));
            }
        }

        /// <summary>0x86: the character list after a deletion.</summary>
        private void CharacterListUpdate(PacketReader r)
        {
            int slots = r.ReadByte();
            Characters.Clear();
            for (int i = 0; i < slots; i++)
            {
                Characters.Add(r.ReadAscii(30));
                r.Skip(30);
            }
            CharactersListed?.Invoke();
        }

        /// <summary>0x85: the server refused to delete a character.</summary>
        private void DeleteRefused(PacketReader r)
        {
            byte reason = r.ReadByte();
            SetStatus(reason switch
            {
                0 => "That password is not correct.",
                1 => "That character does not exist.",
                2 => "That character is in the world right now.",
                3 => "That character is not old enough to delete.",
                4 => "That character is queued for backup and cannot be deleted.",
                5 => "The server could not process that request.",
                _ => "The character could not be deleted."
            });
        }
    }
}
