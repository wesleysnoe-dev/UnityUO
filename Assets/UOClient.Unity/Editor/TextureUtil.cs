using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Editor
{
    internal static class TextureUtil
    {
        /// <summary>Uploads a decoded bitmap, flipping rows (Bitmap32 is top-left origin, Unity is bottom-left).</summary>
        public static Texture2D ToTexture(Bitmap32 bmp)
        {
            var tex = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var colors = new Color32[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                int src = y * bmp.Width;
                int dst = (bmp.Height - 1 - y) * bmp.Width;
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint p = bmp.Pixels[src + x];
                    colors[dst + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
                }
            }

            tex.SetPixels32(colors);
            tex.Apply(false, false);
            return tex;
        }
    }
}
