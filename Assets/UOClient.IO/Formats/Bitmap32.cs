namespace UOClient.IO.Formats
{
    /// <summary>
    /// Decoded image, top-left origin, pixels as 0xAARRGGBB. Kept engine-agnostic;
    /// the Unity layer flips rows when uploading to a Texture2D.
    /// </summary>
    public sealed class Bitmap32
    {
        public Bitmap32(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
        }

        public int Width { get; }
        public int Height { get; }
        public uint[] Pixels { get; }

        public uint this[int x, int y]
        {
            get => Pixels[y * Width + x];
            set => Pixels[y * Width + x] = value;
        }
    }

    public static class Color16
    {
        /// <summary>Converts UO's 15-bit RGB (x1555) to opaque 0xFFRRGGBB.</summary>
        public static uint ToArgb32(ushort c)
        {
            uint r = (uint)(c >> 10) & 0x1F;
            uint g = (uint)(c >> 5) & 0x1F;
            uint b = (uint)c & 0x1F;
            r = (r << 3) | (r >> 2);
            g = (g << 3) | (g >> 2);
            b = (b << 3) | (b >> 2);
            return 0xFF000000 | (r << 16) | (g << 8) | b;
        }
    }
}
