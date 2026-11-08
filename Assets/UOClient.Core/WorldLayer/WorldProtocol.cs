using System;
using System.IO;

namespace UOClient.WorldLayer
{
    // Shared with the server (ModernUO) as source: only System, C# 9, no Unity, no client types.
    /// <summary>
    /// The world layer's messages: 0xBF extended commands (both directions), subcommand ids below. Each side frames
    /// them with its own packet writer (0xBF, length, subcommand as ushort, then the payload here); payloads are
    /// big-endian like the rest of UO. Version <see cref="ProtocolVersion"/> is in the hello so mismatched builds refuse.
    /// </summary>
    public static class WorldProtocol
    {
        public const int ProtocolVersion = 2; // 2: strokes carry a UO ground graphic

        // Client -> server.
        public const ushort Hello = 0xD0;          // version: int; asks whether this account may edit
        public const ushort RequestRegion = 0xD1;  // map: byte, rx: ushort, ry: ushort, haveVersion: int (-1: none)
        public const ushort Stroke = 0xD2;         // map: byte, then the stroke (WriteStroke)
        public const ushort Undo = 0xD3;           // redo: bool
        public const ushort EndStroke = 0xD4;      // closes one mouse drag into one undo step

        // Objects (step 4, the OBJECTS tab): real static items in the world save, so every client sees them.
        public const ushort PlaceObject = 0xD6;    // map: byte, graphic: ushort, x: ushort, y: ushort, z: sbyte, hue: ushort
        public const ushort ObjectOp = 0xD7;       // serial: uint, op: byte (ObjectDelete / ObjectMove), x: ushort, y: ushort, z: sbyte
        public const byte ObjectDelete = 0, ObjectMove = 1;

        // Server -> client (same subcommand numbers, other direction).
        public const ushort HelloReply = 0xD0;     // version: int, canEdit: bool, materialCount: byte
        // map: byte, rx: ushort, ry: ushort, version: int, total: int, offset: int, then this part's bytes to the packet's end.
        // The parts of WorldRegion.Serialize() in order (a UO packet holds at most 64 KB); total 0: no painted region there.
        public const ushort RegionData = 0xD1;

        /// <summary>Bytes of region data per RegionData packet.</summary>
        public const int RegionPart = 60000;
        public const ushort Result = 0xD2;         // ok: bool, message: ascii (byte length + bytes)
        public const ushort RegionChanged = 0xD5;  // map: byte, rx: ushort, ry: ushort, version: int (re-request it)
        public const ushort ObjectPlaced = 0xD6;   // serial: uint (the object just placed, so the editor can select it)

        /// <summary>Fixed-point scale for fractional fields (tile units, Z) on the wire.</summary>
        public const float Fixed = 1000f;

        public static void WriteStroke(BinaryWriter w, in BrushStroke s)
        {
            w.Write((byte)s.Tool);
            WriteInt(w, (int)Math.Round(s.X * Fixed));
            WriteInt(w, (int)Math.Round(s.Y * Fixed));
            WriteInt(w, (int)Math.Round(s.Radius * Fixed));
            WriteInt(w, (int)Math.Round(s.Softness * Fixed));
            WriteInt(w, (int)Math.Round(s.Strength * Fixed));
            w.Write(s.Material);
            WriteInt(w, (int)Math.Round(s.TargetZ * Fixed));
            w.Write((byte)(s.Ground >> 8));
            w.Write((byte)s.Ground);
        }

        public static BrushStroke ReadStroke(Func<byte> readByte, Func<int> readIntBigEndian) => new BrushStroke
        {
            Tool = (BrushTool)readByte(),
            X = readIntBigEndian() / Fixed,
            Y = readIntBigEndian() / Fixed,
            Radius = readIntBigEndian() / Fixed,
            Softness = readIntBigEndian() / Fixed,
            Strength = readIntBigEndian() / Fixed,
            Material = readByte(),
            TargetZ = readIntBigEndian() / Fixed,
            Ground = (ushort)((readByte() << 8) | readByte())
        }.Clamped();

        /// <summary>Stroke payload length in bytes.</summary>
        public const int StrokeLength = 1 + 4 * 5 + 1 + 4 + 2;

        private static void WriteInt(BinaryWriter w, int v)
        {
            w.Write((byte)(v >> 24));
            w.Write((byte)(v >> 16));
            w.Write((byte)(v >> 8));
            w.Write((byte)v);
        }

        /// <summary>A stroke as big-endian bytes (for either side's packet writer).</summary>
        public static byte[] StrokeBytes(in BrushStroke s)
        {
            using var m = new MemoryStream();
            using (var w = new BinaryWriter(m))
                WriteStroke(w, s);
            return m.ToArray();
        }

        /// <summary>Reads a stroke from big-endian bytes.</summary>
        public static BrushStroke StrokeFrom(byte[] b, int offset)
        {
            int p = offset;
            byte Byte() => b[p++];
            int Int()
            {
                int v = (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3];
                p += 4;
                return v;
            }
            return ReadStroke(Byte, Int);
        }
    }
}
