using System;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The mouse-over highlight. TazUO's own cue is a hue: the thing the mouse can act on is drawn in 0x0035. Window
    /// items (ItemGump.cs:119-123, HighlightOnMouseOver, on in containers and trades) and ground items (ItemView.cs:118-145)
    /// take it always; mobiles, corpses and statics only with the profile's HighlightGameObjects (MobileView.cs:92,
    /// ItemView.cs:398-404, StaticView.cs:84). On top of that hue we draw a sprite-edge outline (<see cref="Outline"/>,
    /// TazUO's OUTLINE shader rule, which TazUO itself only uses for OutlineColor, not for the mouse-over) as the clear
    /// cue Wes asked for. The world selects only what no window covers, and only on opaque pixels.
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
        /// TazUO's Profile.HighlightGameObjects, on here: characters, creatures, corpses and fixtures (locked furniture,
        /// doors) highlight under the mouse too. TazUO ships it off ("highlight_game_objects": false); Wes wants hover
        /// feedback on everything interactable, so our code default is on. No profile or UserSettings is written.
        /// </summary>
        public static bool HighlightGameObjects = true;

        /// <summary>The outline's colour (0xAARRGGBB): white, which reads against the 0x0035 yellow and against the world.</summary>
        public const uint OutlineArgb = 0xFFFFFFFF;

        /// <summary>What a world pick can be; only what <see cref="Hovers"/> accepts is picked, and so highlighted.</summary>
        public enum PickKind { Mobile, Corpse, GroundItem, LockedItem, MultiPart, MapStatic, Land, NoDraw }

        /// <summary>
        /// Whether the world pick (EntityViews.PickAt), and so the highlight, takes a kind of thing: characters, corpses
        /// and server items (locked fixtures too). Never land, the fixed parts of houses and boats, the map's own statics
        /// (nothing to use: a double-click on one sends nothing) or the no-draw markers.
        /// </summary>
        public static bool Hovers(PickKind kind) =>
            kind == PickKind.Mobile || kind == PickKind.Corpse || kind == PickKind.GroundItem || kind == PickKind.LockedItem;

        /// <summary>
        /// A sprite's outline, TazUO's OUTLINE shader rule (IsometricWorld.fx:149-173) at <paramref name="thickness"/>
        /// pixels: every transparent pixel with an opaque one within that many pixels (the square around it) is drawn in
        /// <paramref name="argb"/>, every other pixel is left clear. The picture grows by the thickness on each side, so
        /// art that touches its own edge is outlined there too; draw it offset by -thickness under or over the sprite.
        /// </summary>
        public static Bitmap32 Outline(Bitmap32 src, int thickness, uint argb)
        {
            int t = Math.Max(1, thickness), w = src.Width + 2 * t, h = src.Height + 2 * t;
            var o = new Bitmap32(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = x - t, sy = y - t;
                    if (OpaqueAt(src, sx, sy))
                        continue;
                    for (int dy = -t; dy <= t && o.Pixels[y * w + x] == 0; dy++)
                        for (int dx = -t; dx <= t; dx++)
                            if (OpaqueAt(src, sx + dx, sy + dy))
                            {
                                o.Pixels[y * w + x] = argb;
                                break;
                            }
                }
            return o;
        }

        private static bool OpaqueAt(Bitmap32 b, int x, int y) =>
            x >= 0 && y >= 0 && x < b.Width && y < b.Height && (b.Pixels[y * b.Width + x] >> 24) != 0;

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

        /// <summary>
        /// Whether the selected world sprite gets the outline: a body (mobile, corpse) with HighlightGameObjects, as its hue;
        /// a ground item whenever it is hued (<see cref="GroundItemHue"/> changed it), so a fixture without
        /// HighlightGameObjects stays plain as TazUO's does.
        /// </summary>
        public static bool OutlinesInWorld(bool selected, bool body, bool locked, bool highlightGameObjects) =>
            selected && (highlightGameObjects || (!body && !locked));

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
