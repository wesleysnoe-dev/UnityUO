using System;
using System.Buffers.Binary;
using System.IO;

namespace UOClient.IO.Files
{
    /// <summary>
    /// Legacy MUL container: a data file plus an idx file of 12-byte entries
    /// (int32 offset, int32 length, int32 extra). Offset -1 marks an empty slot.
    /// </summary>
    public sealed class MulFile : UOFile
    {
        private const int IndexEntrySize = 12;

        private readonly FileEntry[] _entries;

        public MulFile(string dataPath, string indexPath) : base(dataPath)
        {
            byte[] idx = File.ReadAllBytes(indexPath);
            _entries = new FileEntry[idx.Length / IndexEntrySize];

            for (int i = 0; i < _entries.Length; i++)
            {
                ReadOnlySpan<byte> e = idx.AsSpan(i * IndexEntrySize, IndexEntrySize);
                int offset = BinaryPrimitives.ReadInt32LittleEndian(e);
                int length = BinaryPrimitives.ReadInt32LittleEndian(e.Slice(4));
                int extra = BinaryPrimitives.ReadInt32LittleEndian(e.Slice(8));

                _entries[i] = offset < 0 || length <= 0
                    ? FileEntry.Invalid
                    : new FileEntry(offset, length & 0x7FFFFFFF, 0, extra, CompressionType.None);
            }
        }

        public override int Count => _entries.Length;

        public override bool TryGetEntry(int index, out FileEntry entry)
        {
            if ((uint)index < (uint)_entries.Length && _entries[index].IsValid)
            {
                entry = _entries[index];
                return true;
            }

            entry = FileEntry.Invalid;
            return false;
        }
    }
}
