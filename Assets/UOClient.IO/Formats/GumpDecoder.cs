using System;
using System.Buffers.Binary;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// Decodes gumpart entries: a uint32 row-offset table (in 4-byte units), then per row
    /// a list of (uint16 color, uint16 run) pairs. Color 0 is transparent.
    /// </summary>
    public static class GumpDecoder
    {
        public static Bitmap32 Decode(byte[] data, int width, int height)
        {
            if (width <= 0 || height <= 0 || data.Length < height * 4)
                return null;

            ReadOnlySpan<byte> d = data;
            var bmp = new Bitmap32(width, height);
            uint[] px = bmp.Pixels;
            int totalUnits = d.Length / 4;

            for (int y = 0; y < height; y++)
            {
                int rowStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(y * 4));
                int rowEnd = y < height - 1
                    ? (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice((y + 1) * 4))
                    : totalUnits;

                int dst = y * width;
                int rowLimit = dst + width;

                for (int unit = rowStart; unit < rowEnd && unit < totalUnits; unit++)
                {
                    ushort color = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(unit * 4));
                    int run = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(unit * 4 + 2));
                    if (dst + run > rowLimit)
                        run = rowLimit - dst;

                    if (color != 0)
                    {
                        uint argb = Color16.ToArgb32(color);
                        for (int i = 0; i < run; i++)
                            px[dst + i] = argb;
                    }
                    dst += run;
                }
            }

            return bmp;
        }
    }
}
