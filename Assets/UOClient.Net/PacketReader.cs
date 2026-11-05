using System;
using System.Buffers.Binary;
using System.Text;

namespace UOClient.Net
{
    /// <summary>Big-endian reader over one framed packet (byte 0 is the packet id).</summary>
    public struct PacketReader
    {
        private readonly byte[] _data;

        public PacketReader(byte[] data, bool variableLength)
        {
            _data = data;
            Position = variableLength ? 3 : 1;
        }

        public byte Id => _data[0];
        public int Length => _data.Length;
        public int Position { get; set; }
        public int Remaining => _data.Length - Position;

        public byte ReadByte() => Position < _data.Length ? _data[Position++] : (byte)0;
        public sbyte ReadSByte() => (sbyte)ReadByte();
        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            if (Remaining < 2) { Position = _data.Length; return 0; }
            ushort v = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(Position));
            Position += 2;
            return v;
        }

        public short ReadInt16() => (short)ReadUInt16();

        public uint ReadUInt32()
        {
            if (Remaining < 4) { Position = _data.Length; return 0; }
            uint v = BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public int ReadInt32() => (int)ReadUInt32();

        public void Skip(int count) => Position = Math.Min(_data.Length, Position + count);

        /// <summary>Fixed-size, zero-padded ASCII/Latin-1 string.</summary>
        public string ReadAscii(int length)
        {
            int n = Math.Max(0, Math.Min(length, Remaining));
            int end = Array.IndexOf(_data, (byte)0, Position, n);
            string s = Encoding.GetEncoding(28591).GetString(_data, Position, (end < 0 ? Position + n : end) - Position);
            Position += n;
            return s;
        }

        /// <summary>Zero-terminated ASCII/Latin-1 string.</summary>
        public string ReadAsciiZ()
        {
            int end = Array.IndexOf(_data, (byte)0, Position);
            if (end < 0)
                end = _data.Length;
            string s = Encoding.GetEncoding(28591).GetString(_data, Position, end - Position);
            Position = Math.Min(_data.Length, end + 1);
            return s;
        }

        /// <summary>Zero-terminated big-endian UTF-16 string.</summary>
        public string ReadUnicodeZ()
        {
            var sb = new StringBuilder();
            while (Remaining >= 2)
            {
                ushort c = ReadUInt16();
                if (c == 0)
                    break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }

        /// <summary>Zero-terminated little-endian UTF-16 string (cliloc arguments).</summary>
        public string ReadUnicodeLEZ()
        {
            var sb = new StringBuilder();
            while (Remaining >= 2)
            {
                ushort c = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Position));
                Position += 2;
                if (c == 0)
                    break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }
    }
}
