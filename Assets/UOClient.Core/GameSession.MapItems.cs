using System;
using System.Collections.Generic;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>A pin plotted on a map item, in the map gump's own pixels (0..Width-1, 0..Height-1).</summary>
    public readonly struct MapPin
    {
        public readonly int X, Y;

        public MapPin(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>A map item the server showed (treasure, city, world and blank maps): the area it covers and its pins.</summary>
    public sealed class MapItemDetails
    {
        public uint Serial;

        /// <summary>The gump art behind the map (ModernUO always sends 0x139D).</summary>
        public int Gump;

        /// <summary>The world rectangle the map covers, from (X1, Y1) to (X2, Y2).</summary>
        public int X1, Y1, X2, Y2;

        /// <summary>The size of the map gump in pixels; pins are in these coordinates.</summary>
        public int Width, Height;

        /// <summary>The facet (0xF5 on 7.0.13+ clients), or -1 when the server sent 0x90, which has none.</summary>
        public int Facet = -1;

        /// <summary>Whether the server lets us plot on it (0x56 command 7).</summary>
        public bool Editable;

        public readonly List<MapPin> Pins = new List<MapPin>();
    }

    /// <summary>Map items: their details (0x90, or 0xF5 on 7.0.13+) and plotted pins (0x56, both ways).</summary>
    public sealed partial class GameSession
    {
        private readonly Dictionary<uint, MapItemDetails> _maps = new Dictionary<uint, MapItemDetails>();

        /// <summary>Every map item the server has shown this session, by serial.</summary>
        public IReadOnlyDictionary<uint, MapItemDetails> Maps => _maps;

        /// <summary>The server opened a map item (its details arrived; pins follow in 0x56).</summary>
        public event Action<MapItemDetails> MapDisplayed;

        /// <summary>A map's pins or editable flag changed.</summary>
        public event Action<MapItemDetails> MapChanged;

        private void RegisterMapItemHandlers()
        {
            _handlers[0x90] = MapDetails;
            _handlers[0xF5] = MapDetails;
            _handlers[0x56] = MapCommand;
        }

        private MapItemDetails MapItem(uint serial)
        {
            if (!_maps.TryGetValue(serial, out MapItemDetails map))
                _maps[serial] = map = new MapItemDetails { Serial = serial };
            return map;
        }

        /// <summary>
        /// 0x90 (19 bytes): serial, gump art, x1, y1, x2, y2, width, height (16-bit each), as ModernUO's SendMapDetails
        /// writes it; 0xF5 (21 bytes, sent instead to 7.0.13+ clients) adds the facet.
        /// </summary>
        private void MapDetails(PacketReader r)
        {
            MapItemDetails map = MapItem(r.ReadUInt32());
            map.Gump = r.ReadUInt16();
            map.X1 = r.ReadInt16();
            map.Y1 = r.ReadInt16();
            map.X2 = r.ReadInt16();
            map.Y2 = r.ReadInt16();
            map.Width = r.ReadInt16();
            map.Height = r.ReadInt16();
            map.Facet = r.Remaining >= 2 ? r.ReadInt16() : -1;
            MapDisplayed?.Invoke(map);
        }

        /// <summary>
        /// 0x56 (11 bytes): serial, command, a byte (pin index, or the editable flag for command 7), x, y (int16).
        /// ModernUO sends 5 (clear, when it opens the map), 1 (add) per pin, then 7 (editable); the rest mirror what
        /// its MapItem does with our requests.
        /// </summary>
        private void MapCommand(PacketReader r)
        {
            MapItemDetails map = MapItem(r.ReadUInt32());
            int command = r.ReadByte(), number = r.ReadByte();
            int x = r.ReadInt16(), y = r.ReadInt16();
            List<MapPin> pins = map.Pins;
            switch (command)
            {
                case 1: pins.Add(new MapPin(x, y)); break;
                case 2:
                    if (number < pins.Count)
                        pins.Insert(number, new MapPin(x, y));
                    else
                        pins.Add(new MapPin(x, y));
                    break;
                case 3:
                    if (number < pins.Count)
                        pins[number] = new MapPin(x, y);
                    break;
                case 4:
                    if (number < pins.Count)
                        pins.RemoveAt(number);
                    break;
                case 5: pins.Clear(); break;
                case 6: map.Editable = !map.Editable; break;
                case 7: map.Editable = number != 0; break;
                default: return;
            }
            MapChanged?.Invoke(map);
        }

        /// <summary>Asks the server to change a map's pins (1 add, 2 insert, 3 move, 4 remove, 5 clear, 6 toggle editing).</summary>
        public void SendMapCommand(uint serial, int command, int number = 0, int x = 0, int y = 0)
        {
            if (State == SessionState.InWorld)
                _conn.Send(MapCommandRequest(serial, command, number, x, y));
        }

        /// <summary>0x56 request, as ModernUO's OnMapCommand reads it: serial, command, pin index, x, y.</summary>
        public static byte[] MapCommandRequest(uint serial, int command, int number, int x, int y) =>
            new PacketWriter(0x56, 11).UInt32(serial).Byte(command).Byte(number).UInt16(x).UInt16(y).ToArray();
    }
}
