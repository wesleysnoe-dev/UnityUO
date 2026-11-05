namespace UOClient.IO.Files
{
    public enum CompressionType : short
    {
        None = 0,
        Zlib = 1,
        Mythic = 3
    }

    /// <summary>Location of one asset inside a MUL or UOP container.</summary>
    public readonly struct FileEntry
    {
        public static readonly FileEntry Invalid = new FileEntry(-1, 0, 0, 0, CompressionType.None);

        public readonly long Offset;
        public readonly int Length;
        public readonly int DecompressedLength;

        /// <summary>Format-specific extra value. For gumps this is (width &lt;&lt; 16) | height.</summary>
        public readonly int Extra;

        public readonly CompressionType Compression;

        public FileEntry(long offset, int length, int decompressedLength, int extra, CompressionType compression)
        {
            Offset = offset;
            Length = length;
            DecompressedLength = decompressedLength;
            Extra = extra;
            Compression = compression;
        }

        public bool IsValid => Offset >= 0 && Length > 0;
    }
}
