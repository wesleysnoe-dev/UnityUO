using System;
using System.Buffers.Binary;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// texmaps.mul entries: square, uncompressed 15-bit textures (64x64 or 128x128), row-major.
    /// The client maps them onto the land diamond rotated 45 degrees.
    /// </summary>
    public static class TexmapDecoder
    {
        public static Bitmap32 Decode(byte[] data)
        {
            int size = data.Length >= 128 * 128 * 2 ? 128 : 64;
            if (data.Length < size * size * 2)
                return null;

            var bmp = new Bitmap32(size, size);
            for (int i = 0; i < size * size; i++)
                bmp.Pixels[i] = Color16.ToArgb32(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2)));
            return bmp;
        }
    }
}
