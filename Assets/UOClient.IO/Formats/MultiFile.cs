using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using UOClient.IO.Files;

namespace UOClient.IO.Formats
{
    /// <summary>One piece of a house or boat, relative to the multi's center.</summary>
    public struct MultiComponent
    {
        public ushort Graphic;
        public short X, Y, Z;

        /// <summary>
        /// False for placeholders of things the server sends as their own items (doors, signs, a boat's
        /// planks and tillerman); only fixed parts are drawn from the multi.
        /// </summary>
        public bool Fixed;
    }

    /// <summary>multi.mul / multi.idx: house and boat layouts (7.0.9+ layout, 16 bytes per component).</summary>
    public sealed class MultiFile : IDisposable
    {
        private readonly MulFile _file;
        private readonly Dictionary<int, MultiComponent[]> _cache = new Dictionary<int, MultiComponent[]>();

        public MultiFile(string directory)
        {
            string mul = Path.Combine(directory, "multi.mul"), idx = Path.Combine(directory, "multi.idx");
            if (File.Exists(mul) && File.Exists(idx))
                _file = new MulFile(mul, idx);
        }

        public MultiComponent[] Get(int multiId)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(multiId, out MultiComponent[] parts))
                    return parts;
                byte[] data = _file?.Read(multiId);
                if (data == null)
                    parts = Array.Empty<MultiComponent>();
                else
                {
                    // Older files used 12-byte entries; 16-byte ones add four unknown bytes.
                    int size = data.Length % 16 == 0 ? 16 : 12;
                    parts = new MultiComponent[data.Length / size];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        ReadOnlySpan<byte> e = data.AsSpan(i * size);
                        parts[i] = new MultiComponent
                        {
                            Graphic = BinaryPrimitives.ReadUInt16LittleEndian(e),
                            X = BinaryPrimitives.ReadInt16LittleEndian(e.Slice(2)),
                            Y = BinaryPrimitives.ReadInt16LittleEndian(e.Slice(4)),
                            Z = BinaryPrimitives.ReadInt16LittleEndian(e.Slice(6)),
                            Fixed = BinaryPrimitives.ReadUInt32LittleEndian(e.Slice(8)) != 0
                        };
                    }
                }
                _cache[multiId] = parts;
                return parts;
            }
        }

        public void Dispose() => _file?.Dispose();
    }
}
