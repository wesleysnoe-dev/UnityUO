using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// How an item's amount shows, one rule for every view (ground, container, grid, trade, the held cursor), as TazUO:
    /// coins draw a bigger pile art for more coins (<c>Item.DisplayedGraphic</c>, Item.cs:53-73), and any other stackable
    /// (tiledata Generic) item of more than one draws a second copy 5 pixels off (ItemView.cs:154-157 on the ground, up-left
    /// and under; ItemGump.cs:139-151 in a container or the trade, down-right and over; GameCursor.cs:445-455 held). The
    /// grid draws one copy (GridItem.cs:225). Only the art drawn changes: the item's graphic and serial stay as sent.
    /// </summary>
    public static class ItemStack
    {
        /// <summary>Where the second copy goes: up-left on the ground, down-right in a gump and on the cursor.</summary>
        public const int GroundOffset = -5, GumpOffset = 5;

        /// <summary>Copper, gold and silver coins (TazUO <c>Item.IsCoin</c>, Item.cs:32).</summary>
        public static bool IsCoin(int graphic) => graphic == 0x0EEA || graphic == 0x0EED || graphic == 0x0EF0;

        /// <summary>The art drawn for an item: a coin's pile, by amount (2-5 the next art, over 5 the one after), else itself.</summary>
        public static int DisplayedGraphic(int graphic, int amount) =>
            !IsCoin(graphic) ? graphic : amount > 5 ? graphic + 2 : amount > 1 ? graphic + 1 : graphic;

        /// <summary>A non-coin stackable (Generic) item of more than one is drawn twice (not a multi; callers skip those).</summary>
        public static bool DrawsTwice(int graphic, int amount, TileFlags flags) =>
            !IsCoin(graphic) && amount > 1 && (flags & TileFlags.Generic) != 0;

        /// <summary>
        /// The held art (ItemHold.Set, ItemHold.cs:65): a single coin lifted off a pile shows one coin; otherwise the art the
        /// item showed where it lay (by its whole amount, <paramref name="total"/>).
        /// </summary>
        public static int HeldGraphic(int graphic, int lifted, int total) =>
            IsCoin(graphic) && lifted == 1 ? graphic : DisplayedGraphic(graphic, total);

        /// <summary>The held item draws twice when more than one is lifted and the art is the item's own (GameCursor.cs:445-449).</summary>
        public static bool HeldDrawsTwice(int graphic, int held, int lifted, TileFlags flags) =>
            lifted > 1 && held == graphic && (flags & TileFlags.Generic) != 0;

        /// <summary>
        /// The two copies as one picture, <see cref="GumpOffset"/> larger each way: the art at (0, 0), then again at (5, 5)
        /// over it, as ItemGump and the cursor draw them. A grab point measured on the first copy stays where it was.
        /// </summary>
        public static Bitmap32 Doubled(Bitmap32 src)
        {
            if (src == null)
                return null;
            var dst = new Bitmap32(src.Width + GumpOffset, src.Height + GumpOffset);
            Blit(src, dst, 0, 0);
            Blit(src, dst, GumpOffset, GumpOffset);
            return dst;
        }

        // Straight alpha over (the art's pixels are fully opaque or fully clear; an HD edge is blended).
        private static void Blit(Bitmap32 src, Bitmap32 dst, int ox, int oy)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    uint s = src[x, y];
                    uint a = s >> 24;
                    if (a == 0)
                        continue;
                    if (a == 255)
                    {
                        dst[x + ox, y + oy] = s;
                        continue;
                    }
                    uint d = dst[x + ox, y + oy];
                    uint da = d >> 24;
                    uint oa = a + da * (255 - a) / 255;
                    uint Mix(int shift)
                    {
                        uint sc = (s >> shift) & 0xFF, dc = (d >> shift) & 0xFF;
                        return oa == 0 ? 0 : (sc * a + dc * da * (255 - a) / 255) / oa;
                    }
                    dst[x + ox, y + oy] = (oa << 24) | (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
                }
        }
    }
}
