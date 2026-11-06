using System;
using System.Collections.Generic;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout.

    /// <summary>
    /// TazUO's GridContainer (GridContainer.cs, GridSlotManager.SetGridPositions, GridScrollArea, GridItem) as far as this
    /// window draws it: a name label, a scroll area of square cells (GameOptions.GridItemSize) laid out left to right and
    /// wrapping, a 4-pixel border, the default 5 x 5 size. No top bar: TazUO's search box, sort, quick-drop and
    /// "original view" buttons are not drawn, so its height is left out too (the cells keep their 5 rows).
    /// </summary>
    public sealed partial class GridContainerWindow
    {
        /// <summary>GridContainer's X_SPACING, Y_SPACING, LABEL_HEIGHT, DEFAULT_BORDER_WIDTH; GridScrollArea.SCROLLBAR_WIDTH.</summary>
        public const int XSpacing = 1, YSpacing = 1, LabelHeight = 20, BorderWidth = 4, ScrollbarWidth = 14;

        /// <summary>Profile.Grid_DefaultColumns / Grid_DefaultRows.</summary>
        public const int DefaultColumns = 5, DefaultRows = 5;

        /// <summary>ScrollBarBase.ScrollStep: pixels per wheel notch.</summary>
        public const int ScrollStep = 50;

        /// <summary>The default border (WorldViewportGump BorderControl.DefaultGraphics): 0x0A8C across, 0x0A8D down, 4 wide.</summary>
        public const int BorderHorizontal = 0x0A8C, BorderVertical = 0x0A8D;

        /// <summary>GridItem: each cell's background is black at DEFAULT_BG_ALPHA, its edge white at GridBorderAlpha (75).</summary>
        public const float CellBackgroundAlpha = 0.25f, CellBorderAlpha = 0.75f;

        /// <summary>The name label's and the amount's hue (0x0481).</summary>
        public const int TextHue = 0x0481;

        /// <summary>GetContainerName: the name cut to 21 characters, then the count of what is listed.</summary>
        public const int NameLength = 21;

        /// <summary>
        /// The window's size for a cell size: GridContainer.GetWidth (borders, scroll bar, the columns and a gap before each
        /// and after the last) and GetHeight (label, borders, the rows), less TOP_BAR_HEIGHT (see the class).
        /// </summary>
        public static (int Width, int Height) WindowSize(int cell) =>
            (BorderWidth * 2 + ScrollbarWidth + cell * DefaultColumns + XSpacing * (DefaultColumns + 1),
             LabelHeight + BorderWidth * 2 + (cell + YSpacing) * DefaultRows);

        /// <summary>The scroll area inside the border, under the label (LayoutControls), in window pixels; its right 14 are the bar.</summary>
        public static (int X, int Y, int Width, int Height) ScrollArea(int windowWidth, int windowHeight) =>
            (BorderWidth, BorderWidth + LabelHeight, Math.Max(0, windowWidth - BorderWidth * 2),
             Math.Max(0, windowHeight - BorderWidth * 2 - LabelHeight));

        /// <summary>
        /// Each cell's top-left in the scroll area's content (before scrolling): GridSlotManager.SetGridPositions, a row
        /// starting at x = X_SPACING, y = 0, and a cell that would reach the scroll bar (<c>x + size &gt;= width - 14</c>)
        /// going to the next row.
        /// </summary>
        public static void CellOrigins(int count, int cell, int areaWidth, List<(int X, int Y)> origins)
        {
            origins.Clear();
            int x = XSpacing, y = 0;
            for (int i = 0; i < count; i++)
            {
                if (x + cell >= areaWidth - ScrollbarWidth)
                {
                    x = XSpacing;
                    y += cell + YSpacing;
                }
                origins.Add((x, y));
                x += cell + XSpacing;
            }
        }

        /// <summary>How far the content scrolls: its bottom (the lowest cell's) less the area's height, not below 0.</summary>
        public static int MaxScroll(IReadOnlyList<(int X, int Y)> origins, int cell, int areaHeight)
        {
            int bottom = 0;
            foreach (var (_, y) in origins)
                bottom = Math.Max(bottom, y + cell);
            return Math.Max(0, bottom - areaHeight);
        }

        /// <summary>The scroll position kept in range.</summary>
        public static int ClampScroll(int scroll, int maxScroll) => Math.Max(0, Math.Min(maxScroll, scroll));

        /// <summary>
        /// The cell under a point in the scroll area (area pixels, y down), or -1: only inside the area left of the bar,
        /// the point taken into the content by <paramref name="scroll"/>. A whole cell takes the mouse (GridItem), not only
        /// the art's pixels.
        /// </summary>
        public static int CellAt(IReadOnlyList<(int X, int Y)> origins, int cell, float x, float y, int areaWidth, int areaHeight, int scroll)
        {
            if (x < 0 || y < 0 || x >= areaWidth - ScrollbarWidth || y >= areaHeight)
                return -1;
            float cy = y + scroll;
            for (int i = 0; i < origins.Count; i++)
            {
                var (ox, oy) = origins[i];
                if (x >= ox && x < ox + cell && cy >= oy && cy < oy + cell)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// The scroll bar's thumb (top and height within the area's height) for a scroll position: as tall as the area's
        /// share of the content, at least 14, placed in proportion.
        /// </summary>
        public static (float Top, float Height) Thumb(int scroll, int maxScroll, int areaHeight)
        {
            if (areaHeight <= 0)
                return (0, 0);
            if (maxScroll <= 0)
                return (0, areaHeight);
            float h = Math.Max(ScrollbarWidth, Math.Min(areaHeight, areaHeight * (float)areaHeight / (areaHeight + maxScroll)));
            return ((areaHeight - h) * ClampScroll(scroll, maxScroll) / maxScroll, h);
        }

        /// <summary>The scroll position for a press or drag at <paramref name="y"/> on the bar (the thumb centred there).</summary>
        public static int ScrollFromBar(float y, int maxScroll, int areaHeight)
        {
            if (maxScroll <= 0 || areaHeight <= 0)
                return 0;
            var (_, h) = Thumb(0, maxScroll, areaHeight);
            float track = areaHeight - h;
            if (track <= 0)
                return 0;
            return ClampScroll((int)Math.Round((y - h / 2) / track * maxScroll), maxScroll);
        }

        /// <summary>
        /// The opaque bounds of art (TazUO's Arts.GetRealArtBounds): the smallest box holding every pixel
        /// <paramref name="opaque"/> says is drawn; the whole art when none is.
        /// </summary>
        public static (int X, int Y, int Width, int Height) OpaqueBounds(int width, int height, Func<int, int, bool> opaque)
        {
            int minX = width, minY = height, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (opaque(x, y))
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            return maxX < 0 ? (0, 0, width, height) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        /// Where in the art (stock pixels) a point in the drawn cell is, for the drag's grab point: the art's bounds are
        /// drawn at (<paramref name="drawX"/>, <paramref name="drawY"/>) <paramref name="drawW"/> x <paramref name="drawH"/>;
        /// the point is taken back through that and kept on the art.
        /// </summary>
        public static (int X, int Y) ArtPixel(float x, float y, float drawX, float drawY, float drawW, float drawH,
                                              (int X, int Y, int Width, int Height) bounds, int artWidth, int artHeight)
        {
            float fx = drawW > 0 ? (x - drawX) / drawW : 0.5f, fy = drawH > 0 ? (y - drawY) / drawH : 0.5f;
            int px = bounds.X + (int)Math.Floor(fx * bounds.Width), py = bounds.Y + (int)Math.Floor(fy * bounds.Height);
            return (Math.Max(0, Math.Min(Math.Max(0, artWidth - 1), px)), Math.Max(0, Math.Min(Math.Max(0, artHeight - 1), py)));
        }

        /// <summary>A tooltip line without its markup (<c>&lt;basefont ...&gt;</c> and the like), trimmed.</summary>
        public static string PlainText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') inTag = true;
                else if (c == '>' && inTag) inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>The label: GetContainerName, the name (or "a container") cut to 21 characters (StringHelper.Truncate), then " (count)".</summary>
        public static string Title(string name, int count)
        {
            string n = string.IsNullOrWhiteSpace(name) ? "a container" : name.Trim();
            if (n.Length > NameLength)
                n = n.Substring(0, NameLength - 3) + "...";
            return n + " (" + count + ")";
        }

        /// <summary>What a drop on the grid does (GridItem.DropHeldItem, GridContainer.ScrollArea_MouseUp).</summary>
        public enum DropKind
        {
            /// <summary>Into the item under it (a container, or a like stack to merge): 0xFFFF, 0xFFFF in that item.</summary>
            IntoItem,
            /// <summary>Back where it was in this container (it came from here and was let go on its own cell or empty space).</summary>
            BackInPlace,
            /// <summary>Into this container, the server choosing where (0xFFFF, 0xFFFF).</summary>
            IntoContainer,
        }

        /// <summary>
        /// The drop's kind: onto another item that takes it (<paramref name="underTakesIt"/>, GameSession.DropsInto) goes
        /// into that item; else an item from this same container goes back to its spot (no slot positions are kept here,
        /// so nothing else would change); else the server places it in this container.
        /// </summary>
        public static DropKind DropKindFor(bool overOtherItem, bool underTakesIt, bool fromThisContainer) =>
            overOtherItem && underTakesIt ? DropKind.IntoItem : fromThisContainer ? DropKind.BackInPlace : DropKind.IntoContainer;
    }
}
