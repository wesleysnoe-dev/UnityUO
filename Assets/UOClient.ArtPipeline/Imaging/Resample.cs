using System;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline.Imaging
{
    public static class Resample
    {
        /// <summary>Integer nearest-neighbor upscale; keeps UO's hard pixel edges and alpha mask exact.</summary>
        public static Bitmap32 Nearest(Bitmap32 src, int scale)
        {
            if (scale == 1)
                return src;

            var dst = new Bitmap32(src.Width * scale, src.Height * scale);
            for (int y = 0; y < dst.Height; y++)
            {
                int srcRow = (y / scale) * src.Width;
                int dstRow = y * dst.Width;
                for (int x = 0; x < dst.Width; x++)
                    dst.Pixels[dstRow + x] = src.Pixels[srcRow + x / scale];
            }
            return dst;
        }

        /// <summary>Bilinear resize to an exact size, for restyled images that came back slightly off (e.g. padded to a multiple of 8).</summary>
        public static Bitmap32 Bilinear(Bitmap32 src, int width, int height)
        {
            if (src.Width == width && src.Height == height)
                return src;

            var dst = new Bitmap32(width, height);
            float sx = (float)src.Width / width, sy = (float)src.Height / height;

            for (int y = 0; y < height; y++)
            {
                float fy = Math.Max(0, (y + 0.5f) * sy - 0.5f);
                int y0 = Math.Min((int)fy, src.Height - 1), y1 = Math.Min(y0 + 1, src.Height - 1);
                float ty = fy - y0;

                for (int x = 0; x < width; x++)
                {
                    float fx = Math.Max(0, (x + 0.5f) * sx - 0.5f);
                    int x0 = Math.Min((int)fx, src.Width - 1), x1 = Math.Min(x0 + 1, src.Width - 1);
                    float tx = fx - x0;

                    uint p00 = src[x0, y0], p10 = src[x1, y0], p01 = src[x0, y1], p11 = src[x1, y1];
                    uint result = 0;
                    for (int shift = 0; shift <= 24; shift += 8)
                    {
                        float c00 = (p00 >> shift) & 0xFF, c10 = (p10 >> shift) & 0xFF;
                        float c01 = (p01 >> shift) & 0xFF, c11 = (p11 >> shift) & 0xFF;
                        float top = c00 + (c10 - c00) * tx, bottom = c01 + (c11 - c01) * tx;
                        result |= (uint)Math.Min(255, (int)(top + (bottom - top) * ty + 0.5f)) << shift;
                    }
                    dst.Pixels[y * width + x] = result;
                }
            }
            return dst;
        }
    }
}
