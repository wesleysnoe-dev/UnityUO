using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UOClient.IO.Formats
{
    [Flags]
    public enum TileFlags : ulong
    {
        None = 0,
        Background = 0x1,
        Weapon = 0x2,
        Transparent = 0x4,
        Translucent = 0x8,
        Wall = 0x10,
        Damaging = 0x20,
        Impassable = 0x40,
        Wet = 0x80,
        Surface = 0x200,
        Bridge = 0x400,
        Generic = 0x800,
        Window = 0x1000,
        NoShoot = 0x2000,
        ArticleA = 0x4000,
        ArticleAn = 0x8000,
        Internal = 0x10000,
        Foliage = 0x20000,
        PartialHue = 0x40000,
        NoHouse = 0x80000,
        Map = 0x100000,
        Container = 0x200000,
        Wearable = 0x400000,
        LightSource = 0x800000,
        Animation = 0x1000000,
        HoverOver = 0x2000000,
        NoDiagonal = 0x4000000,
        Armor = 0x8000000,
        Roof = 0x10000000,
        Door = 0x20000000,
        StairBack = 0x40000000,
        StairRight = 0x80000000,
        AlphaBlend = 0x100000000,
        UseNewArt = 0x200000000,
        ArtUsed = 0x400000000,
        NoShadow = 0x1000000000,
        PixelBleed = 0x2000000000,
        PlayAnimOnce = 0x4000000000,
        MultiMovable = 0x10000000000
    }

    public struct LandTileData
    {
        public TileFlags Flags;
        public ushort TexId;
        public string Name;
    }

    public struct StaticTileData
    {
        public TileFlags Flags;
        public byte Weight;
        public byte Layer;
        public int Count;
        public ushort AnimId;
        public ushort Hue;
        public ushort LightIndex;
        public byte Height;
        public string Name;

        public bool IsBackground => (Flags & TileFlags.Background) != 0;
        public bool IsSurface => (Flags & TileFlags.Surface) != 0;
        public bool IsRoof => (Flags & TileFlags.Roof) != 0;
        public bool IsFoliage => (Flags & TileFlags.Foliage) != 0;
    }

    /// <summary>
    /// tiledata.mul. Since High Seas the flags are 64-bit; the layout is detected from the file length.
    /// Both sections are stored in groups of 32 entries, each group led by a 4-byte header.
    /// </summary>
    public sealed class TileData
    {
        public const int LandCount = 0x4000;

        private const int GroupHeader = 4;
        private const int GroupSize = 32;

        public TileData(string path)
        {
            byte[] data = File.ReadAllBytes(path);

            IsNewFormat = DetectNewFormat(data.Length);
            int flagSize = IsNewFormat ? 8 : 4;
            int landEntrySize = flagSize + 2 + 20;
            int staticEntrySize = flagSize + 1 + 1 + 4 + 2 + 2 + 2 + 1 + 20;

            Land = new LandTileData[LandCount];
            int pos = 0;
            for (int i = 0; i < LandCount; i++)
            {
                if ((i & (GroupSize - 1)) == 0)
                    pos += GroupHeader;

                ReadOnlySpan<byte> e = data.AsSpan(pos, landEntrySize);
                Land[i] = new LandTileData
                {
                    Flags = ReadFlags(e, flagSize),
                    TexId = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(flagSize)),
                    Name = ReadName(e.Slice(flagSize + 2, 20))
                };
                pos += landEntrySize;
            }

            int staticGroupBytes = GroupHeader + GroupSize * staticEntrySize;
            int staticCount = (data.Length - pos) / staticGroupBytes * GroupSize;
            Statics = new StaticTileData[staticCount];

            for (int i = 0; i < staticCount; i++)
            {
                if ((i & (GroupSize - 1)) == 0)
                    pos += GroupHeader;

                ReadOnlySpan<byte> e = data.AsSpan(pos, staticEntrySize);
                int o = flagSize;
                Statics[i] = new StaticTileData
                {
                    Flags = ReadFlags(e, flagSize),
                    Weight = e[o],
                    Layer = e[o + 1],
                    Count = BinaryPrimitives.ReadInt32LittleEndian(e.Slice(o + 2)),
                    AnimId = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(o + 6)),
                    Hue = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(o + 8)),
                    LightIndex = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(o + 10)),
                    Height = e[o + 12],
                    Name = ReadName(e.Slice(o + 13, 20))
                };
                pos += staticEntrySize;
            }
        }

        public bool IsNewFormat { get; }
        public LandTileData[] Land { get; }
        public StaticTileData[] Statics { get; }

        public StaticTileData GetStatic(int graphic)
        {
            return (uint)graphic < (uint)Statics.Length ? Statics[graphic] : default;
        }

        private static bool DetectNewFormat(int length)
        {
            const int newLand = (LandCount / GroupSize) * (GroupHeader + GroupSize * 30);
            const int newStaticGroup = GroupHeader + GroupSize * 41;
            return length > newLand && (length - newLand) % newStaticGroup == 0;
        }

        private static TileFlags ReadFlags(ReadOnlySpan<byte> e, int size)
        {
            return size == 8
                ? (TileFlags)BinaryPrimitives.ReadUInt64LittleEndian(e)
                : (TileFlags)BinaryPrimitives.ReadUInt32LittleEndian(e);
        }

        private static string ReadName(ReadOnlySpan<byte> raw)
        {
            int len = raw.IndexOf((byte)0);
            if (len < 0)
                len = raw.Length;
            return Encoding.ASCII.GetString(raw.Slice(0, len).ToArray());
        }
    }
}
