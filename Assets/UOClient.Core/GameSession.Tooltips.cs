using System;
using System.Collections.Generic;
using System.Text;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>An object's property list (the tooltip): its lines, and the revision they belong to.</summary>
    public sealed class PropertyList
    {
        public uint Serial;
        public uint Revision;
        public readonly List<string> Lines = new List<string>();
        public string Text => string.Join("\n", Lines);
    }

    /// <summary>A spell or item effect: a missile flying between two points, lightning, or an animation in place.</summary>
    public sealed class GraphicEffect
    {
        public const byte Moving = 0, Lightning = 1, AtLocation = 2, OnMobile = 3;

        public byte Type;
        public uint Source, Target;
        public ushort Graphic;
        public int SourceX, SourceY, SourceZ, TargetX, TargetY, TargetZ;
        public int Speed, Duration;
        public bool FixedDirection, Explode;
        public ushort Hue;
        public int RenderMode;
    }

    /// <summary>Tooltips (object property lists): 0xDC announces a revision, 0xD6 asks for and carries the lines.</summary>
    public sealed partial class GameSession
    {
        public readonly Dictionary<uint, PropertyList> Properties = new Dictionary<uint, PropertyList>();
        private readonly Dictionary<uint, uint> _knownRevision = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, double> _askedAt = new Dictionary<uint, double>();

        public event Action<PropertyList> PropertiesUpdated;

        private void RegisterTooltipHandlers()
        {
            _handlers[0xDC] = r => _knownRevision[r.ReadUInt32()] = r.ReadUInt32();
            _handlers[0xD6] = PropertyListPacket;
            _handlers[0x70] = r => GraphicEffect(r, false);
            _handlers[0xC0] = r => GraphicEffect(r, true);
            _handlers[0xC7] = r => GraphicEffect(r, true); // particle effect: the 0xC0 fields, then particle extras we skip
            _handlers[0x54] = r =>
            {
                r.ReadByte(); // mode: 0 repeat, 1 once
                int id = r.ReadUInt16();
                r.ReadUInt16(); // volume (unused by the classic client)
                int x = r.ReadUInt16(), y = r.ReadUInt16(), z = r.ReadInt16();
                SoundPlayed?.Invoke(id, x, y, z);
            };
            _handlers[0x6D] = r => MusicRequested?.Invoke(r.ReadUInt16());
        }

        /// <summary>A sound effect at a spot (sound id, x, y, z).</summary>
        public event Action<int, int, int, int> SoundPlayed;

        /// <summary>The region's music changed (music id, from Music/Digital/Config.txt).</summary>
        public event Action<int> MusicRequested;

        public event Action<GraphicEffect> EffectStarted;

        private void GraphicEffect(PacketReader r, bool hued)
        {
            var e = new GraphicEffect
            {
                Type = r.ReadByte(),
                Source = r.ReadUInt32(),
                Target = r.ReadUInt32(),
                Graphic = r.ReadUInt16(),
                SourceX = r.ReadUInt16(), SourceY = r.ReadUInt16(), SourceZ = r.ReadSByte(),
                TargetX = r.ReadUInt16(), TargetY = r.ReadUInt16(), TargetZ = r.ReadSByte(),
                Speed = r.ReadByte(),
                Duration = r.ReadByte()
            };
            r.ReadUInt16();
            e.FixedDirection = r.ReadBool();
            e.Explode = r.ReadBool();
            if (hued)
            {
                e.Hue = (ushort)r.ReadUInt32();
                e.RenderMode = (int)r.ReadUInt32();
            }
            EffectStarted?.Invoke(e);
        }

        /// <summary>
        /// The tooltip for an object, asking the server when we have none (or an older revision).
        /// Returns what we have meanwhile, possibly null.
        /// </summary>
        public PropertyList GetProperties(uint serial)
        {
            Properties.TryGetValue(serial, out PropertyList have);
            bool stale = have == null || (_knownRevision.TryGetValue(serial, out uint rev) && rev != have.Revision);
            if (stale && State == SessionState.InWorld && (!_askedAt.TryGetValue(serial, out double at) || _now - at > 2.0))
            {
                _askedAt[serial] = _now;
                _conn.Send(new PacketWriter(0xD6).UInt32(serial).ToArray());
            }
            return have;
        }

        private void PropertyListPacket(PacketReader r)
        {
            r.ReadUInt16(); // 1
            var list = new PropertyList { Serial = r.ReadUInt32() };
            r.ReadUInt16(); // 0
            list.Revision = r.ReadUInt32();
            while (r.Remaining >= 4)
            {
                int cliloc = (int)r.ReadUInt32();
                if (cliloc == 0)
                    break;
                int length = r.ReadUInt16();
                string args = "";
                if (length > 0 && r.Remaining >= length)
                {
                    var bytes = new byte[length];
                    for (int i = 0; i < length; i++)
                        bytes[i] = r.ReadByte();
                    args = Encoding.Unicode.GetString(bytes);
                }
                string line = Cliloc != null ? Cliloc.Format(cliloc, args) : (args.Length > 0 ? args : $"#{cliloc}");
                if (!string.IsNullOrWhiteSpace(line))
                    list.Lines.Add(line.Trim());
            }
            if (list.Lines.Count > 0 && list.Lines[0].Length > 0)
                list.Lines[0] = char.ToUpperInvariant(list.Lines[0][0]) + list.Lines[0].Substring(1);
            Properties[list.Serial] = list;
            _knownRevision[list.Serial] = list.Revision;
            PropertiesUpdated?.Invoke(list);
        }
    }
}
