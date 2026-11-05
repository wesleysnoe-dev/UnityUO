namespace UOClient.IO.Formats
{
    /// <summary>
    /// Applies a hues.mul hue to decoded art. UO art is authored so that a pixel's red channel (5-bit)
    /// indexes the hue's 32 shades; partial hues only recolor gray pixels.
    /// </summary>
    public static class HueApplier
    {
        public static Bitmap32 Apply(Bitmap32 src, HueEntry hue, bool partial)
        {
            var dst = new Bitmap32(src.Width, src.Height);
            var shades = new uint[32];
            for (int i = 0; i < 32; i++)
                shades[i] = Color16.ToArgb32(hue.Colors[i]);

            for (int i = 0; i < src.Pixels.Length; i++)
            {
                uint p = src.Pixels[i];
                if ((p >> 24) == 0)
                    continue;

                uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                if (partial && !(r == g && g == b))
                {
                    dst.Pixels[i] = p;
                    continue;
                }
                dst.Pixels[i] = shades[r >> 3];
            }
            return dst;
        }

        /// <summary>Index (as used in packets, i.e. entry + 1) of the hue whose mid shade is closest to an RGB color.</summary>
        public static int FindClosest(Hues hues, int r, int g, int b)
        {
            int best = 1;
            long bestDistance = long.MaxValue;
            for (int i = 1; i < hues.Entries.Length; i++)
            {
                uint c = Color16.ToArgb32(hues.Entries[i].Colors[20]);
                long dr = (int)((c >> 16) & 0xFF) - r, dg = (int)((c >> 8) & 0xFF) - g, db = (int)(c & 0xFF) - b;
                long distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i + 1;
                }
            }
            return best;
        }
    }
}
