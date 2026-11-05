using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace UOClient.IO.Files
{
    /// <summary>
    /// UOP ("MYP") package. Entries are addressed by a hash of their virtual filename,
    /// e.g. "build/artlegacymul/00000123.tga"; this class maps those back to sequential indices.
    /// </summary>
    public sealed class UopFile : UOFile
    {
        private const uint Magic = 0x0050594D; // "MYP\0"

        private readonly Dictionary<ulong, FileEntry> _byHash = new Dictionary<ulong, FileEntry>();
        private readonly FileEntry[] _entries;
        private readonly bool _hasExtra;
        private readonly bool[] _extraResolved;

        /// <param name="nameFormat">Virtual filename pattern with one index placeholder, e.g. "build/artlegacymul/{0:D8}.tga".</param>
        /// <param name="count">Number of indices to map. Pass 0 to use the number of entries in the package.</param>
        /// <param name="hasExtra">Entries begin with int32 width + int32 height (gumps); these are moved into <see cref="FileEntry.Extra"/>.</param>
        public UopFile(string path, string nameFormat, int count = 0, bool hasExtra = false) : base(path)
        {
            _hasExtra = hasExtra;
            ReadTables();

            if (count <= 0)
                count = _byHash.Count;

            _entries = new FileEntry[count];
            _extraResolved = new bool[count];

            for (int i = 0; i < count; i++)
            {
                ulong hash = UopHash.Create(string.Format(nameFormat, i));
                _entries[i] = _byHash.TryGetValue(hash, out FileEntry e) ? e : FileEntry.Invalid;
            }
        }

        /// <summary>A package read only by name hash (<see cref="TryGetByHash"/>), with no sequential indices.</summary>
        public UopFile(string path) : base(path)
        {
            ReadTables();
            _entries = Array.Empty<FileEntry>();
            _extraResolved = Array.Empty<bool>();
        }

        public override int Count => _entries.Length;

        /// <summary>Total number of entries stored in the package, mapped or not.</summary>
        public int PackageEntryCount => _byHash.Count;

        /// <summary>The entry stored under a virtual filename's hash (<see cref="UopHash.Create"/>).</summary>
        public bool TryGetByHash(ulong hash, out FileEntry entry)
        {
            if (_byHash.TryGetValue(hash, out entry))
                return true;
            entry = FileEntry.Invalid;
            return false;
        }

        /// <summary>Every entry in the package, in no particular order.</summary>
        public IEnumerable<FileEntry> AllEntries => _byHash.Values;

        public override bool TryGetEntry(int index, out FileEntry entry)
        {
            if ((uint)index >= (uint)_entries.Length || !_entries[index].IsValid)
            {
                entry = FileEntry.Invalid;
                return false;
            }

            if (_hasExtra && !_extraResolved[index])
                ResolveExtra(index);

            entry = _entries[index];
            return entry.IsValid;
        }

        private void ResolveExtra(int index)
        {
            FileEntry e = _entries[index];
            _extraResolved[index] = true;

            // Compressed entries carry width/height inside the decompressed data; the reader handles those.
            if (e.Compression != CompressionType.None)
                return;

            Span<byte> header = stackalloc byte[8];
            ReadRaw(e.Offset, header);
            int width = BinaryPrimitives.ReadInt32LittleEndian(header);
            int height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));

            _entries[index] = e.Length > 8
                ? new FileEntry(e.Offset + 8, e.Length - 8, Math.Max(0, e.DecompressedLength - 8), (width << 16) | (height & 0xFFFF), e.Compression)
                : FileEntry.Invalid;
        }

        private void ReadTables()
        {
            byte[] header = ReadRaw(0, 28);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic)
                throw new InvalidDataException($"{FilePath} is not a UOP file.");

            // uint version, uint timestamp, long firstBlock, uint blockSize, int fileCount
            long nextBlock = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12));

            const int recordSize = 34;
            while (nextBlock != 0)
            {
                byte[] blockHeader = ReadRaw(nextBlock, 12);
                int filesInBlock = BinaryPrimitives.ReadInt32LittleEndian(blockHeader);
                long blockStart = nextBlock + 12;
                nextBlock = BinaryPrimitives.ReadInt64LittleEndian(blockHeader.AsSpan(4));

                byte[] records = ReadRaw(blockStart, filesInBlock * recordSize);
                for (int i = 0; i < filesInBlock; i++)
                {
                    ReadOnlySpan<byte> r = records.AsSpan(i * recordSize, recordSize);
                    long offset = BinaryPrimitives.ReadInt64LittleEndian(r);
                    int headerLength = BinaryPrimitives.ReadInt32LittleEndian(r.Slice(8));
                    int compressedLength = BinaryPrimitives.ReadInt32LittleEndian(r.Slice(12));
                    int decompressedLength = BinaryPrimitives.ReadInt32LittleEndian(r.Slice(16));
                    ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(r.Slice(20));
                    // uint32 data hash at 28 is unused
                    short flag = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(32));

                    if (offset == 0 || compressedLength <= 0)
                        continue;

                    _byHash[hash] = new FileEntry(offset + headerLength, compressedLength, decompressedLength, 0, (CompressionType)flag);
                }
            }
        }

        /// <summary>Package entries in ascending index order, for formats stored as one continuous stream (maps).</summary>
        public IEnumerable<FileEntry> MappedEntries
        {
            get
            {
                for (int i = 0; i < _entries.Length; i++)
                    yield return _entries[i];
            }
        }
    }
}
