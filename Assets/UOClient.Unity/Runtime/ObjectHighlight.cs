using System;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// TazUO's mouse-over highlight: the thing the mouse can act on is drawn in hue 0x0035, nothing is outlined. Window
    /// items (ItemGump.cs:119-123, HighlightOnMouseOver, on in containers and trades) and ground items (ItemView.cs:118-145)
    /// take it always; mobiles, corpses and statics only with the profile's HighlightGameObjects (MobileView.cs:92,
    /// ItemView.cs:398-404, StaticView.cs:84). The world selects only what no window covers, and only on opaque pixels.
    /// </summary>
    public static class ObjectHighlight
    {
        /// <summary>The selected-object hue (ShaderHueTranslator.SelectedItemHue, Constants.HIGHLIGHT_CURRENT_OBJECT_HUE).</summary>
        public const int Hue = 0x0035;

        /// <summary>
        /// Added to a hue for art caches: hue every pixel, ignoring the tiledata's partial-hue flag, as TazUO does when it
        /// sets <c>partial = false</c> for a highlight. Above the 16 bits a UO hue uses, so it never meets a server hue.
        /// </summary>
        public const int WholeArt = 0x10000;

        /// <summary>
        /// TazUO's Profile.HighlightGameObjects: off in a new profile and in this PC's TazUO profiles
        /// ("highlight_game_objects": false). We have no profile setting for it, so it is TazUO's default, in code.
        /// </summary>
        public static bool HighlightGameObjects = false;

        /// <summary>Whether <paramref name="hue"/> carries <see cref="WholeArt"/>.</summary>
        public static bool IsWholeArt(int hue) => (hue & WholeArt) != 0;

        /// <summary>
        /// An item drawn in a window (container, trade, paperdoll): the highlight hue over the whole art while the mouse
        /// is on its pixels, else its own hue (ItemGump.Draw).
        /// </summary>
        public static int WindowItemHue(int hue, bool mouseOver, bool highlightOnMouseOver = true) =>
            mouseOver && highlightOnMouseOver ? Hue | WholeArt : hue;

        /// <summary>TazUO's Item.IsLocked: not movable (0x20 unset) and heavier than 90 stones (fixtures, furniture).</summary>
        public static bool IsLocked(byte itemFlags, byte weight) => (itemFlags & 0x20) == 0 && weight > 90;

        /// <summary>
        /// A ground item's hue (ItemView.Draw): with HighlightGameObjects the selected item is hued whole; without it only
        /// a selected item that is neither locked nor a multi is, keeping its partial-hue flag. Corpses are drawn as bodies
        /// and only highlight with HighlightGameObjects (<see cref="HighlightsBody"/>).
        /// </summary>
        public static int GroundItemHue(int hue, bool selected, bool locked, bool multi, bool highlightGameObjects)
        {
            if (!selected)
                return hue;
            if (highlightGameObjects)
                return Hue | WholeArt;
            return !locked && !multi ? Hue : hue;
        }

        /// <summary>A mobile or a corpse is drawn in the highlight hue only when selected with HighlightGameObjects on.</summary>
        public static bool HighlightsBody(bool selected, bool highlightGameObjects) => selected && highlightGameObjects;

        /// <summary>What the world selects under the mouse: nothing while a window (or the journal) is over it.</summary>
        public static uint WorldSelection(bool overUi, uint picked) => overUi ? 0 : picked;

        /// <summary>The last of <paramref name="count"/> things drawn in order (so the topmost) that <paramref name="hit"/> accepts, or -1.</summary>
        public static int Topmost(int count, Func<int, bool> hit)
        {
            for (int i = count - 1; i >= 0; i--)
                if (hit(i))
                    return i;
            return -1;
        }

        /// <summary>
        /// The world's pick: of the <paramref name="count"/> sprites <paramref name="hit"/> accepts (an opaque pixel under
        /// the mouse), the one drawn in front (smallest depth), or -1.
        /// </summary>
        public static int Nearest(int count, Func<int, float> depth, Func<int, bool> hit)
        {
            int best = -1;
            float bestDepth = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float d = depth(i);
                if (d < bestDepth && hit(i))
                {
                    best = i;
                    bestDepth = d;
                }
            }
            return best;
        }

        /// <summary>
        /// The sprite pixel (from its unmirrored top-left) under a point in a drawn rectangle whose y grows upwards,
        /// or null outside it. The rectangle may be drawn at another size than the sprite's own.
        /// </summary>
        public static (int X, int Y)? SpritePixel(float px, float py, float left, float bottom, float width, float height,
                                                   int spriteWidth, int spriteHeight, bool mirror)
        {
            if (width <= 0 || height <= 0 || px < left || py < bottom || px >= left + width || py >= bottom + height)
                return null;
            int x = Math.Min(spriteWidth - 1, (int)((px - left) / width * spriteWidth));
            int y = Math.Min(spriteHeight - 1, (int)((bottom + height - py) / height * spriteHeight));
            return (mirror ? spriteWidth - 1 - x : x, y);
        }
    }
}
