using System;
using System.Collections.Generic;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Core
{
    /// <summary>
    /// A customized house's design (0xD8): the parts the owner placed, which replace the foundation's
    /// multi.mul layout. The packet stores most parts as per-floor grids whose size comes from the foundation,
    /// so the layout is resolved against the foundation's multi.mul components in <see cref="Components"/>.
    /// </summary>
    public sealed class HouseDesign
    {
        public uint Serial;
        public int Revision;

        /// <summary>The number of parts the server says the design has.</summary>
        public int ComponentCount;

        internal readonly List<HouseDesignPlane> Planes = new List<HouseDesignPlane>();

        /// <summary>
        /// The design's parts, relative to the house's position (all fixed). <paramref name="foundation"/> is
        /// the multi.mul layout of the house's multi id: ModernUO's design is that layout's bounds plus one row
        /// of steps to the south, and the floor grids are laid out over those bounds.
        /// </summary>
        public List<MultiComponent> Components(MultiComponent[] foundation)
        {
            int minX = 0, minY = 0, maxY = 0;
            if (foundation != null && foundation.Length > 0)
            {
                minX = minY = int.MaxValue;
                maxY = int.MinValue;
                foreach (MultiComponent c in foundation)
                {
                    minX = Math.Min(minX, c.X);
                    minY = Math.Min(minY, c.Y);
                    maxY = Math.Max(maxY, c.Y);
                }
            }
            return Components(minX, minY, maxY);
        }

        /// <summary>The design's parts for a foundation spanning (minX, minY) to (?, maxY) in multi.mul.</summary>
        public List<MultiComponent> Components(int minX, int minY, int maxY)
        {
            var parts = new List<MultiComponent>(Math.Max(ComponentCount, 16));
            foreach (HouseDesignPlane p in Planes)
            {
                byte[] d = p.Data;
                switch (p.Mode)
                {
                    case 0: // free list: id, x, y, z (stairs, and anything off the floor grids)
                        for (int i = 0; i + 5 <= d.Length; i += 5)
                            Add(parts, (ushort)((d[i] << 8) | d[i + 1]), (sbyte)d[i + 2], (sbyte)d[i + 3], (sbyte)d[i + 4]);
                        break;
                    case 1: // list at the plane's floor height: id, x, y
                        for (int i = 0; i + 4 <= d.Length; i += 4)
                            Add(parts, (ushort)((d[i] << 8) | d[i + 1]), (sbyte)d[i + 2], (sbyte)d[i + 3], PlaneZ(p.Index));
                        break;
                    case 2: // grid, column by column (x major): one id per tile, 0 = empty
                    {
                        int xOffset = minX, yOffset = minY, column;
                        if (p.Index == 0)
                            column = maxY - minY + 2;           // the foundation plus its row of steps
                        else if (p.Index <= 4)
                        {
                            xOffset++;                          // floors leave the outer edge to the walls below
                            yOffset++;
                            column = maxY - minY;
                        }
                        else
                            column = maxY - minY + 1;           // walls and other items with height
                        if (column <= 0)
                            break;
                        int z = PlaneZ(p.Index);
                        for (int i = 0; i + 2 <= d.Length; i += 2)
                        {
                            int tile = i / 2;
                            Add(parts, (ushort)((d[i] << 8) | d[i + 1]), tile / column + xOffset, tile % column + yOffset, z);
                        }
                        break;
                    }
                }
            }
            return parts;
        }

        /// <summary>Floor heights: the ground plane at 0, then 7, 27, 47 and 67 (planes 5-8 repeat 1-4 for walls).</summary>
        public static int PlaneZ(int index) => index <= 0 ? 0 : ((index - 1) % 4) * 20 + 7;

        private static void Add(List<MultiComponent> parts, ushort graphic, int x, int y, int z)
        {
            if (graphic == 0)
                return;
            parts.Add(new MultiComponent { Graphic = graphic, X = (short)x, Y = (short)y, Z = (short)z, Fixed = true });
        }
    }

    internal struct HouseDesignPlane
    {
        /// <summary>0 = (id, x, y, z) list, 1 = (id, x, y) list, 2 = grid.</summary>
        public int Mode;
        /// <summary>Grid planes: 0 ground, 1-4 floors, 5-8 walls; lists: 9 and up.</summary>
        public int Index;
        public byte[] Data;
    }

    /// <summary>Custom houses: design revisions (0xBF 0x1D), designs (0xD8) and the customize mode (0xBF 0x20).</summary>
    public sealed partial class GameSession
    {
        /// <summary>The house being customized by us (between 0xBF 0x20 begin and end), or 0.</summary>
        public uint CustomizingHouse { get; private set; }

        /// <summary>Customization started (true) or ended (false) for a house.</summary>
        public event Action<uint, bool> HouseCustomizationChanged;

        // The revision last asked for, per house, so a repeated 0xBF 0x1D doesn't ask again.
        private readonly Dictionary<uint, int> _designRequested = new Dictionary<uint, int>();

        private void RegisterHousingHandlers()
        {
            _handlers[0xD8] = HouseDesignDetailed;
        }

        /// <summary>0xBF sub-commands handled here; true if consumed.</summary>
        private bool HousingExtended(int sub, PacketReader r)
        {
            switch (sub)
            {
                case 0x1D: // design revision: serial, revision
                {
                    uint serial = r.ReadUInt32();
                    int revision = r.ReadInt32();
                    World.HouseRevisions[serial] = revision;
                    if (World.HouseDesigns.TryGetValue(serial, out HouseDesign known) && known.Revision == revision)
                        return true; // cached
                    if (_designRequested.TryGetValue(serial, out int asked) && asked == revision)
                        return true;
                    if (RequestHouseDesign(serial))
                        _designRequested[serial] = revision;
                    return true;
                }
                case 0x20: // customization: serial, command (4 begin, 5 end), then fixed filler
                {
                    uint serial = r.ReadUInt32();
                    byte command = r.ReadByte();
                    if (command == 0x04)
                        CustomizingHouse = serial;
                    else if (command == 0x05 && CustomizingHouse == serial)
                        CustomizingHouse = 0;
                    else if (command != 0x05)
                        return true;
                    _exitAskedAt = double.NegativeInfinity; // the server has answered (or begun again): a new exit may be asked
                    HouseCustomizationChanged?.Invoke(serial, command == 0x04);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The player's Exit in the house designer: 0xD7 (encoded command) with our serial, command 0x0C and 0x0A, as TazUO's
        /// Send_CustomHouseBuildingExit (OutgoingPackets.cs:3954-3983), which ModernUO takes as Designer_Close
        /// (HouseFoundation.cs:1025, 1731-1770): it ends the design context without committing, answers 0xBF 0x20 end,
        /// resends the house as committed, and keeps the unfinished design (DesignState, saved with the house) for the
        /// next time the owner customizes. Sends nothing outside customize mode or the world, and only once until the
        /// server answers (another try after <see cref="ExitRetrySeconds"/>). True if it was sent.
        /// </summary>
        public bool ExitHouseCustomization()
        {
            if (CustomizingHouse == 0 || State != SessionState.InWorld || _conn == null || World.PlayerSerial == 0)
                return false;
            if (_now - _exitAskedAt < ExitRetrySeconds)
                return false;
            _exitAskedAt = _now;
            _conn.Send(HouseCustomizationExit(World.PlayerSerial));
            return true;
        }

        /// <summary>An exit was asked and the server has not ended customize mode yet.</summary>
        public bool HouseExitPending => CustomizingHouse != 0 && _now - _exitAskedAt < ExitRetrySeconds;

        public const double ExitRetrySeconds = 5;

        private double _exitAskedAt = double.NegativeInfinity;

        /// <summary>0xD7, length 10: the player's serial, 0x000C (Designer_Close), 0x0A.</summary>
        public static byte[] HouseCustomizationExit(uint player) =>
            new PacketWriter(0xD7).UInt32(player).UInt16(0x0C).Byte(0x0A).ToArray();

        /// <summary>0xBF 0x1E: asks for a house's design; the server answers with 0xD8.</summary>
        public static byte[] HouseDesignQuery(uint serial) => new PacketWriter(0xBF).UInt16(0x1E).UInt32(serial).ToArray();

        private bool RequestHouseDesign(uint serial)
        {
            // Houses in view at login announce their revision before 0x55, so this can't wait for InWorld.
            if (_conn == null || (State != SessionState.InWorld && State != SessionState.EnteringWorld))
                return false;
            _conn.Send(HouseDesignQuery(serial));
            return true;
        }

        /// <summary>
        /// 0xD8: compression type (3), 0, serial, revision, component count (u16), buffer length (u16), plane
        /// count (u8); then per plane a 4-byte header (mode &lt;&lt; 4 | index, sizes' low bytes, high nibbles) and
        /// its zlib data.
        /// </summary>
        private void HouseDesignDetailed(PacketReader r)
        {
            r.ReadByte(); // compression type
            r.ReadByte();
            var design = new HouseDesign { Serial = r.ReadUInt32(), Revision = r.ReadInt32(), ComponentCount = r.ReadUInt16() };
            r.ReadUInt16(); // buffer length
            int planes = r.ReadByte();
            for (int i = 0; i < planes && r.Remaining >= 4; i++)
            {
                byte id = r.ReadByte();
                byte b1 = r.ReadByte(), b2 = r.ReadByte(), b3 = r.ReadByte();
                int unpacked = b1 | ((b3 & 0xF0) << 4);
                int packed = b2 | ((b3 & 0x0F) << 8);
                if (packed > r.Remaining)
                    break;
                var data = new byte[packed];
                for (int b = 0; b < packed; b++)
                    data[b] = r.ReadByte();
                design.Planes.Add(new HouseDesignPlane { Mode = id >> 4, Index = id & 0x0F, Data = Inflate(data, unpacked) });
            }
            _designRequested.Remove(design.Serial);
            World.SetHouseDesign(design);
        }
    }
}
