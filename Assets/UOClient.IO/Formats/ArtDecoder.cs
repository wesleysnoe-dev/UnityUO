using System;
using System.Buffers.Binary;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// Decodes art.mul / artLegacyMUL.uop entries. Indices below 0x4000 are land tiles
    /// (raw 44x44 diamonds); 0x4000 and above are statics (run-length encoded).
    /// </summary>
    public static class ArtDecoder
    {
        public const int LandSize = 44;

        public static Bitmap32 DecodeLand(byte[] data)
        {
            var bmp = new Bitmap32(LandSize, LandSize);
            uint[] px = bmp.Pixels;
            int src = 0;

            // Top half: rows widen from 2 to 44 pixels.
            for (int row = 0; row < 22; row++)
            {
                int start = 21 - row;
                int width = (row + 1) * 2;
                int pos = row * LandSize + start;
                for (int i = 0; i < width; i++, src += 2)
                    px[pos++] = Color16.ToArgb32(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(src)));
            }

            // Bottom half: rows narrow from 44 to 2 pixels.
            for (int row = 0; row < 22; row++)
            {
                int width = (22 - row) * 2;
                int pos = (row + 22) * LandSize + row;
                for (int i = 0; i < width; i++, src += 2)
                    px[pos++] = Color16.ToArgb32(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(src)));
            }

            return bmp;
        }

        public static Bitmap32 DecodeStatic(byte[] data)
        {
            ReadOnlySpan<byte> d = data;
            // uint32 flags, uint16 width, uint16 height, uint16 rowOffsets[height], then row data.
            int width = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(4));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(6));
            if (width == 0 || height == 0 || width >= 1024 || height >= 1024)
                return null;

            var bmp = new Bitmap32(width, height);
            uint[] px = bmp.Pixels;
            int lookupStart = 8;
            int dataStart = lookupStart + height * 2;

            for (int y = 0; y < height; y++)
            {
                int pos = dataStart + BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(lookupStart + y * 2)) * 2;
                int x = 0;

                while (pos + 4 <= d.Length)
                {
                    int xOffset = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(pos));
                    int run = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(pos + 2));
                    pos += 4;

                    if (xOffset + run == 0)
                        break;

                    x += xOffset;
                    if (x + run > width)
                        return bmp; // corrupt row; keep what decoded so far

                    int dst = y * width + x;
                    for (int i = 0; i < run; i++, pos += 2)
                    {
                        ushort c = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(pos));
                        if (c != 0)
                            px[dst + i] = Color16.ToArgb32(c);
                    }
                    x += run;
                }
            }

            return bmp;
        }
    }
}
