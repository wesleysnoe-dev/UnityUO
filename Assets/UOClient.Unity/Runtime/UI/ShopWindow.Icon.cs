using System;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    public sealed partial class ShopWindow
    {
        /// <summary>TazUO's ShopItem boxes: a body's frame is at most 45 x 45, item art fits a 50 wide box.</summary>
        public const int BodyBox = 45, ItemBox = 50, MinLineHeight = 50;

        /// <summary>
        /// TazUO's ShopItem.Draw (ShopGump.cs:773): a buy line whose serial is a mobile (an animal or pet for sale) is that
        /// creature, its graphic a body, so it shows the body's standing frame; everything else, and every sell line,
        /// shows the item's static art. A body number read as static art is some other tile (a horse's 0xCC is a pillar).
        /// </summary>
        public static bool DrawsBody(bool selling, uint serial) => !selling && serial != 0 && serial < 0x40000000;

        /// <summary>A line's height: at least 50, and an item's at least its tiledata height (ShopItem's Height).</summary>
        public static int LineHeight(uint serial, int tileHeight) =>
            serial >= 0x40000000 && serial < 0x80000000 ? Math.Max(MinLineHeight, tileHeight) : MinLineHeight;

        /// <summary>
        /// Where the picture goes in a line's 50-wide icon column: a body's frame squeezed to at most 45 x 45; an item's
        /// opaque part (<paramref name="width"/> x <paramref name="height"/>, see <see cref="OpaqueBounds"/>) centered when
        /// smaller than 50 x the line, else squeezed to it, as ShopItem.Draw does. Both are centered in the column here.
        /// </summary>
        public static (int X, int Y, int W, int H) IconRect(bool body, int width, int height, int lineHeight)
        {
            int w, h;
            if (body)
            {
                w = Math.Min(width, BodyBox);
                h = Math.Min(height, BodyBox);
            }
            else
            {
                w = width < ItemBox ? width : ItemBox;
                h = height < lineHeight ? height : lineHeight;
            }
            return ((ItemBox - w) / 2, (lineHeight - h) / 2, w, h);
        }

        /// <summary>The opaque part of a picture (Arts.GetRealArtBounds), or the whole of it if nothing is opaque.</summary>
        public static (int X, int Y, int W, int H) OpaqueBounds(Bitmap32 bmp)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if ((bmp.Pixels[y * bmp.Width + x] >> 24) != 0)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
            return maxX < 0 ? (0, 0, bmp.Width, bmp.Height) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
    }
}
