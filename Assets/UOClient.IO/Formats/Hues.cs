using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace UOClient.IO.Formats
{
    public struct HueEntry
    {
        /// <summary>32 shades, 15-bit color, dark to light.</summary>
        public ushort[] Colors;
        public ushort TableStart;
        public ushort TableEnd;
        public string Name;
    }

    /// <summary>
    /// hues.mul: groups of 8 hues, each group led by a 4-byte header.
    /// Hue N in packets/statics refers to <see cref="Entries"/>[N - 1].
    /// </summary>
    public sealed class Hues
    {
        private const int EntrySize = 32 * 2 + 2 + 2 + 20;
        private const int GroupSize = 4 + 8 * EntrySize;

        public Hues(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            int groups = data.Length / GroupSize;
            Entries = new HueEntry[groups * 8];

            int pos = 0;
            for (int g = 0; g < groups; g++)
            {
                pos += 4;
                for (int h = 0; h < 8; h++)
                {
                    ReadOnlySpan<byte> e = data.AsSpan(pos, EntrySize);
                    var colors = new ushort[32];
                    for (int c = 0; c < 32; c++)
                        colors[c] = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(c * 2));

                    ReadOnlySpan<byte> name = e.Slice(68, 20);
                    int len = name.IndexOf((byte)0);
                    Entries[g * 8 + h] = new HueEntry
                    {
                        Colors = colors,
                        TableStart = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(64)),
                        TableEnd = BinaryPrimitives.ReadUInt16LittleEndian(e.Slice(66)),
                        Name = Encoding.ASCII.GetString(name.Slice(0, len < 0 ? 20 : len).ToArray())
                    };
                    pos += EntrySize;
                }
            }
        }

        public HueEntry[] Entries { get; }
    }
}
