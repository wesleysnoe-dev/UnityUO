using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using UOClient.IO.Files;

namespace UOClient.IO.Formats
{
    public struct LandCell
    {
        public ushort Graphic;
        public sbyte Z;
    }

    public struct StaticCell
    {
        public ushort Graphic;
        public byte X; // 0-7 within the block
        public byte Y;
        public sbyte Z;
        public ushort Hue;
    }

    /// <summary>
    /// One facet: land from map{n}.mul or map{n}LegacyMUL.uop, statics from staidx{n}.mul + statics{n}.mul.
    /// The world is stored in 8x8 blocks ordered column-major: index = blockX * BlockHeight + blockY.
    /// </summary>
    public sealed class MapFile : IDisposable
    {
        public const int BlockSize = 8;
        private const int LandBlockBytes = 4 + 64 * 3;
        private const int StaticRecordBytes = 7;

        // Default facet sizes (tiles) for modern clients: Felucca, Trammel, Ilshenar, Malas, Tokuno, Ter Mur.
        private static readonly (int Width, int Height)[] DefaultSizes =
        {
            (7168, 4096), (7168, 4096), (2304, 1600), (2560, 2048), (1448, 1448), (1280, 4096)
        };

        private readonly UOFile _land;
        private readonly long[] _landSegmentStarts; // virtual offset where each UOP entry begins
        private readonly FileEntry[] _landSegments;
        private readonly MulFile _statics;

        public MapFile(string directory, int index)
        {
            Index = index;
            (int w, int h) = index < DefaultSizes.Length ? DefaultSizes[index] : throw new ArgumentOutOfRangeException(nameof(index));
            Width = w;
            Height = h;

            string uop = Path.Combine(directory, $"map{index}LegacyMUL.uop");
            if (File.Exists(uop))
            {
                var file = new UopFile(uop, $"build/map{index}legacymul/{{0:D8}}.dat");
                var segments = new List<FileEntry>();
                foreach (FileEntry e in file.MappedEntries)
                    if (e.IsValid)
                        segments.Add(e);

                _land = file;
                _landSegments = segments.ToArray();
                _landSegmentStarts = new long[_landSegments.Length];
                long virtualOffset = 0;
                for (int i = 0; i < _landSegments.Length; i++)
                {
                    _landSegmentStarts[i] = virtualOffset;
                    virtualOffset += _landSegments[i].Length;
                }
                LandDataLength = virtualOffset;
            }
            else
            {
                string mul = Path.Combine(directory, $"map{index}.mul");
                // A MUL map is one continuous block array; wrap it as a single segment.
                _land = new RawFile(mul);
                _landSegments = new[] { new FileEntry(0, (int)Math.Min(int.MaxValue, _land.DataLength), 0, 0, CompressionType.None) };
                _landSegmentStarts = new long[] { 0 };
                LandDataLength = _land.DataLength;
            }

            string staIdx = Path.Combine(directory, $"staidx{index}.mul");
            string sta = Path.Combine(directory, $"statics{index}.mul");
            if (File.Exists(staIdx) && File.Exists(sta))
                _statics = new MulFile(sta, staIdx);
        }

        public int Index { get; }
        public int Width { get; }
        public int Height { get; }
        public int BlockWidth => Width / BlockSize;
        public int BlockHeight => Height / BlockSize;
        public long LandDataLength { get; }

        public int GetBlockIndex(int blockX, int blockY) => blockX * BlockHeight + blockY;

        // World editor: land cells the server derived from the painted world layer, by block then cell.
        private readonly object _overrideLock = new object();
        private readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.Dictionary<int, LandCell>> _landOverride =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.Dictionary<int, LandCell>>();

        /// <summary>
        /// World editor: tile (x, y) reads as <paramref name="cell"/> from now on (the server's tile derived from the
        /// painted world layer), for every reader of this map; <see cref="ReadOriginalLandBlock"/> still gives the file's.
        /// </summary>
        public void SetLandOverride(int x, int y, LandCell cell)
        {
            long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            lock (_overrideLock)
            {
                if (!_landOverride.TryGetValue(key, out var cells))
                    _landOverride[key] = cells = new System.Collections.Generic.Dictionary<int, LandCell>();
                cells[((y & 7) << 3) | (x & 7)] = cell;
                OverrideVersion++;
            }
        }

        /// <summary>Bumped by every <see cref="SetLandOverride"/>: caches of land blocks read before must be dropped.</summary>
        public int OverrideVersion { get; private set; }

        /// <summary>Reads the 64 land cells of a block, indexed [cellY * 8 + cellX], with the world editor's overrides.</summary>
        public LandCell[] ReadLandBlock(int blockX, int blockY)
        {
            LandCell[] cells = ReadOriginalLandBlock(blockX, blockY);
            long key = ((long)blockX << 32) | (uint)blockY;
            lock (_overrideLock)
                if (_landOverride.TryGetValue(key, out var over))
                    foreach (var kv in over)
                        cells[kv.Key] = kv.Value;
            return cells;
        }

        /// <summary>Reads the 64 land cells of a block as the map file has them (no world editor overrides).</summary>
        public LandCell[] ReadOriginalLandBlock(int blockX, int blockY)
        {
            var cells = new LandCell[64];
            if ((uint)blockX >= (uint)BlockWidth || (uint)blockY >= (uint)BlockHeight)
                return cells;

            long virtualOffset = (long)GetBlockIndex(blockX, blockY) * LandBlockBytes;
            if (virtualOffset + LandBlockBytes > LandDataLength)
                return cells;

            int seg = Array.BinarySearch(_landSegmentStarts, virtualOffset);
            if (seg < 0)
                seg = ~seg - 1;

            Span<byte> block = stackalloc byte[LandBlockBytes];
            long within = virtualOffset - _landSegmentStarts[seg];
            _land.ReadRaw(_landSegments[seg].Offset + within, block);

            for (int i = 0; i < 64; i++)
            {
                int o = 4 + i * 3;
                cells[i].Graphic = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(o));
                cells[i].Z = (sbyte)block[o + 2];
            }
            return cells;
        }

        public StaticCell[] ReadStaticBlock(int blockX, int blockY)
        {
            if (_statics == null || (uint)blockX >= (uint)BlockWidth || (uint)blockY >= (uint)BlockHeight)
                return Array.Empty<StaticCell>();

            byte[] data = _statics.Read(GetBlockIndex(blockX, blockY));
            if (data == null)
                return Array.Empty<StaticCell>();

            var result = new StaticCell[data.Length / StaticRecordBytes];
            for (int i = 0; i < result.Length; i++)
            {
                ReadOnlySpan<byte> r = data.AsSpan(i * StaticRecordBytes, StaticRecordBytes);
                result[i] = new StaticCell
                {
                    Graphic = BinaryPrimitives.ReadUInt16LittleEndian(r),
                    X = r[2],
                    Y = r[3],
                    Z = (sbyte)r[4],
                    Hue = BinaryPrimitives.ReadUInt16LittleEndian(r.Slice(5))
                };
            }
            return result;
        }

        // GetLand's blocks: single-tile readers (walking, shadows, falling leaves) ask for the same few blocks thousands
        // of times a second, and each ReadLandBlock is a file read and a new array. Dropped when an override changes.
        private readonly System.Collections.Generic.Dictionary<long, LandCell[]> _landCache =
            new System.Collections.Generic.Dictionary<long, LandCell[]>();
        private int _landCacheVersion = -1;

        public LandCell GetLand(int x, int y)
        {
            long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            int cell = ((y & 7) << 3) | (x & 7);
            lock (_landCache)
            {
                if (_landCacheVersion != OverrideVersion || _landCache.Count > 4096)
                {
                    _landCache.Clear();
                    _landCacheVersion = OverrideVersion;
                }
                if (_landCache.TryGetValue(key, out LandCell[] cached))
                    return cached[cell];
            }
            int version = OverrideVersion;
            LandCell[] block = ReadLandBlock(x >> 3, y >> 3);
            lock (_landCache)
                if (version == OverrideVersion && _landCacheVersion == version)
                    _landCache[key] = block;
            return block[cell];
        }

        public void Dispose()
        {
            _land.Dispose();
            _statics?.Dispose();
        }

        /// <summary>A file read only by offset; it has no index of its own.</summary>
        private sealed class RawFile : UOFile
        {
            public RawFile(string path) : base(path) { }
            public override int Count => 0;
            public override bool TryGetEntry(int index, out FileEntry entry)
            {
                entry = FileEntry.Invalid;
                return false;
            }
        }
    }
}
