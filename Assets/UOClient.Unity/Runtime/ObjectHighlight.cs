using System;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The mouse-over cue: a thin sprite-edge outline (<see cref="Outline"/>, TazUO's OUTLINE shader rule) around the
    /// thing under the mouse, and nothing else. The thing keeps its own hues: TazUO's 0x0035 re-hue (ItemGump.cs:119-123,
    /// ItemView.cs:118-145, MobileView.cs:92) is not drawn, as Wes asked (border outline only, no fill or tint). The
    /// outline is draw-only: the pick (<see cref="MousePick"/>) tests the art's own drawn pixels, never the ring around
    /// them. The world selects only what no window covers. Notoriety outlines and target states are not this.
    /// </summary>
    public static class ObjectHighlight
    {
        /// <summary>
        /// TazUO's Profile.HighlightGameObjects, on here: characters, creatures, corpses and fixtures (locked furniture,
        /// doors) are outlined under the mouse too. TazUO ships it off ("highlight_game_objects": false); Wes wants hover
        /// feedback on everything interactable, so our code default is on. No profile or UserSettings is written.
        /// </summary>
        public static bool HighlightGameObjects = true;

        /// <summary>The outline's colour (0xAARRGGBB): white, which reads against the world and the window art.</summary>
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
        /// pixels: every pixel not drawn with an opaque one within that many pixels (the square around it) is drawn in
        /// <paramref name="argb"/>, every other pixel is left clear, so the ring lies wholly outside the silhouette and
        /// never covers or tints the art. A pixel counts as opaque from <paramref name="minAlpha"/> up: the world passes
        /// the shader's cutoff (<see cref="MousePick.AlphaCutoff"/>), so the ring hugs the edge the eye and the pick see,
        /// not an invisible soft halo. The picture grows by the thickness on each side, so art that touches its own edge
        /// is outlined there too; draw it offset by -thickness under or over the sprite.
        /// </summary>
        public static Bitmap32 Outline(Bitmap32 src, int thickness, uint argb, byte minAlpha = 1)
        {
            int t = Math.Max(1, thickness), w = src.Width + 2 * t, h = src.Height + 2 * t;
            byte min = Math.Max((byte)1, minAlpha);
            var o = new Bitmap32(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int sx = x - t, sy = y - t;
                    if (OpaqueAt(src, sx, sy, min))
                        continue;
                    for (int dy = -t; dy <= t && o.Pixels[y * w + x] == 0; dy++)
                        for (int dx = -t; dx <= t; dx++)
                            if (OpaqueAt(src, sx + dx, sy + dy, min))
                            {
                                o.Pixels[y * w + x] = argb;
                                break;
                            }
                }
            return o;
        }

        private static bool OpaqueAt(Bitmap32 b, int x, int y, byte minAlpha) =>
            x >= 0 && y >= 0 && x < b.Width && y < b.Height && (b.Pixels[y * b.Width + x] >> 24) >= minAlpha;

        /// <summary>TazUO's Item.IsLocked: not movable (0x20 unset) and heavier than 90 stones (fixtures, furniture).</summary>
        public static bool IsLocked(byte itemFlags, byte weight) => (itemFlags & 0x20) == 0 && weight > 90;

        /// <summary>
        /// Whether the selected world sprite gets the outline: a body (mobile, corpse) or a fixture with
        /// HighlightGameObjects; any other ground item always, as TazUO's mouse-over rules choose what it marks.
        /// </summary>
        public static bool OutlinesInWorld(bool selected, bool body, bool locked, bool highlightGameObjects) =>
            selected && (highlightGameObjects || (!body && !locked));

        /// <summary>What the world selects under the mouse: nothing while a window (or the journal) is over it.</summary>
        public static uint WorldSelection(bool overUi, uint picked) => overUi ? 0 : picked;

        /// <summary>
        /// As <see cref="WorldSelection(bool, uint)"/>, but while the mouse is over a container window with TazUO's
        /// HighlightContainerWhenSelected on (ContainerGump.Update sets SelectedObject.SelectedContainer, cleared each
        /// frame), that container (<paramref name="hoveredContainer"/>, 0 for none or the option off) is selected instead, so
        /// it gets the same outline wherever it is drawn. Nothing is picked through the window.
        /// </summary>
        public static uint WorldSelection(bool overUi, uint picked, uint hoveredContainer) =>
            overUi ? hoveredContainer : picked;

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
