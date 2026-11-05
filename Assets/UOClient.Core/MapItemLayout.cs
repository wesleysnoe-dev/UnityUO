using System;

namespace UOClient.Core
{
    /// <summary>
    /// Converts between a map item's own pixels (the 0x90/0xF5 width and height, which 0x56 pins use), the view it is
    /// drawn in, and world tiles (its X1..X2, Y1..Y2 rectangle).
    /// </summary>
    public static class MapItemLayout
    {
        /// <summary>ModernUO's MapItem ignores adds and inserts past this many pins.</summary>
        public const int MaxPins = 50;

        /// <summary>Where map pixel (x, y) falls in a view of <paramref name="viewWidth"/> by <paramref name="viewHeight"/>.</summary>
        public static (float X, float Y) ToView(MapItemDetails map, int x, int y, float viewWidth, float viewHeight)
        {
            if (map.Width <= 0 || map.Height <= 0)
                return (0, 0);
            return (x * viewWidth / map.Width, y * viewHeight / map.Height);
        }

        /// <summary>
        /// The map pixel under a point in the view, kept within 0..Width-1 and 0..Height-1 as ModernUO's Validate does;
        /// false when the point is outside the view (or the map has no size).
        /// </summary>
        public static bool TryToMap(MapItemDetails map, float viewX, float viewY, float viewWidth, float viewHeight, out int x, out int y)
        {
            x = y = 0;
            if (map.Width <= 0 || map.Height <= 0 || viewWidth <= 0 || viewHeight <= 0)
                return false;
            if (viewX < 0 || viewY < 0 || viewX >= viewWidth || viewY >= viewHeight)
                return false;
            x = Math.Min(map.Width - 1, (int)(viewX * map.Width / viewWidth));
            y = Math.Min(map.Height - 1, (int)(viewY * map.Height / viewHeight));
            return true;
        }

        /// <summary>The world tile under map pixel (x, y).</summary>
        public static (int X, int Y) ToWorld(MapItemDetails map, int x, int y)
        {
            if (map.Width <= 0 || map.Height <= 0)
                return (map.X1, map.Y1);
            return (map.X1 + x * (map.X2 - map.X1) / map.Width, map.Y1 + y * (map.Y2 - map.Y1) / map.Height);
        }

        /// <summary>
        /// The view size for a map: its own size, shrunk (keeping its shape) to fit within <paramref name="maxSize"/>.
        /// </summary>
        public static (float Width, float Height) ViewSize(MapItemDetails map, float maxSize)
        {
            float w = Math.Max(1, map.Width), h = Math.Max(1, map.Height);
            float scale = Math.Min(1f, maxSize / Math.Max(w, h));
            return (w * scale, h * scale);
        }

        /// <summary>Whether a pin can be added: the server said the map is editable and it has room.</summary>
        public static bool CanAddPin(MapItemDetails map) => map.Editable && map.Pins.Count < MaxPins;
    }
}
