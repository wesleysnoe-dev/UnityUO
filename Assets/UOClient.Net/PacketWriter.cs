using System;
using System.Buffers.Binary;
using System.Text;

namespace UOClient.Net
{
    /// <summary>Big-endian packet builder. Variable-length packets reserve bytes 1-2 for the length.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private readonly bool _variable;

        public PacketWriter(byte id, int fixedLength = 0)
        {
            _variable = fixedLength <= 0;
            _buffer = new byte[_variable ? 64 : fixedLength];
            _buffer[0] = id;
            Position = _variable ? 3 : 1;
        }

        public int Position { get; private set; }

        public PacketWriter Byte(int v) { Ensure(1); _buffer[Position++] = (byte)v; return this; }
        public PacketWriter Bool(bool v) => Byte(v ? 1 : 0);
        public PacketWriter UInt16(int v) { Ensure(2); BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(Position), (ushort)v); Position += 2; return this; }
        public PacketWriter UInt32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(Position), v); Position += 4; return this; }
        public PacketWriter Int32(int v) => UInt32((uint)v);
        public PacketWriter Zero(int count) { Ensure(count); Position += count; return this; }

        /// <summary>Fixed-size, zero-padded Latin-1 string (truncated if longer).</summary>
        public PacketWriter Ascii(string s, int length)
        {
            Ensure(length);
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(s ?? "");
            Array.Copy(bytes, 0, _buffer, Position, Math.Min(bytes.Length, length));
            Position += length;
            return this;
        }

        /// <summary>Zero-terminated Latin-1 string.</summary>
        public PacketWriter AsciiZ(string s)
        {
            byte[] bytes = Encoding.GetEncoding(28591).GetBytes(s ?? "");
            Ensure(bytes.Length + 1);
            Array.Copy(bytes, 0, _buffer, Position, bytes.Length);
            Position += bytes.Length + 1;
            return this;
        }

        /// <summary>Zero-terminated big-endian UTF-16 string.</summary>
        public PacketWriter UnicodeZ(string s)
        {
            foreach (char c in s ?? "")
                UInt16(c);
            return UInt16(0);
        }

        public byte[] ToArray()
        {
            if (!_variable)
            {
                if (Position != _buffer.Length)
                    throw new InvalidOperationException($"Packet 0x{_buffer[0]:X2} is {Position} bytes, expected {_buffer.Length}.");
                return _buffer;
            }

            var result = new byte[Position];
            Array.Copy(_buffer, result, Position);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(1), (ushort)Position);
            return result;
        }

        private void Ensure(int count)
        {
            if (Position + count <= _buffer.Length)
                return;
            if (!_variable)
                throw new InvalidOperationException($"Packet 0x{_buffer[0]:X2} overflows its fixed length {_buffer.Length}.");
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Position + count));
        }
    }
}
