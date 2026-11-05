using System;
using System.IO;
using System.IO.Compression;

namespace UOClient.IO.Files
{
    /// <summary>
    /// An indexed container of UO assets (art, gumps, sounds...). Implemented by
    /// <see cref="MulFile"/> (data + idx pair) and <see cref="UopFile"/> (MYP package).
    /// Reads are serialized on a lock, so one instance can be shared across threads.
    /// </summary>
    public abstract class UOFile : IDisposable
    {
        private readonly FileStream _stream;
        private readonly object _sync = new object();

        protected UOFile(string dataPath)
        {
            FilePath = dataPath;
            _stream = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
        }

        public string FilePath { get; }

        public long DataLength => _stream.Length;

        /// <summary>Number of addressable indices (some may be empty).</summary>
        public abstract int Count { get; }

        public abstract bool TryGetEntry(int index, out FileEntry entry);

        /// <summary>Reads and, if needed, decompresses the entry at <paramref name="index"/>. Returns null when empty.</summary>
        public byte[] Read(int index)
        {
            return TryGetEntry(index, out FileEntry entry) ? Read(entry) : null;
        }

        public byte[] Read(in FileEntry entry)
        {
            if (!entry.IsValid)
                return null;

            byte[] raw = ReadRaw(entry.Offset, entry.Length);

            switch (entry.Compression)
            {
                case CompressionType.None:
                    return raw;
                case CompressionType.Zlib:
                    return InflateZlib(raw, entry.DecompressedLength);
                case CompressionType.Mythic:
                    return MythicDecompress.Decompress(InflateZlib(raw, entry.DecompressedLength));
                default:
                    throw new NotSupportedException($"Compression {entry.Compression} is not supported ({FilePath}).");
            }
        }

        public byte[] ReadRaw(long offset, int length)
        {
            byte[] buffer = new byte[length];
            ReadRaw(offset, buffer);
            return buffer;
        }

        public void ReadRaw(long offset, Span<byte> buffer)
        {
            lock (_sync)
            {
                _stream.Position = offset;
                int total = 0;
                while (total < buffer.Length)
                {
                    int read = _stream.Read(buffer.Slice(total));
                    if (read == 0)
                        throw new EndOfStreamException($"Unexpected end of {FilePath} at {offset + total}.");
                    total += read;
                }
            }
        }

        private static byte[] InflateZlib(byte[] data, int decompressedLength)
        {
            // Skip the 2-byte zlib header; DeflateStream reads the raw deflate body.
            byte[] output = new byte[decompressedLength];
            using (var input = new MemoryStream(data, 2, data.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < output.Length)
                {
                    int read = deflate.Read(output, total, output.Length - total);
                    if (read == 0)
                        break;
                    total += read;
                }
            }
            return output;
        }

        public virtual void Dispose()
        {
            _stream.Dispose();
        }
    }
}
