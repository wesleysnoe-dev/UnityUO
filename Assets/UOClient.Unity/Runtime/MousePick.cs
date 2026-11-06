using System;
using System.Collections.Generic;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own, so the pick the game runs is the one tested.
    /// <summary>
    /// The one mouse pick: what the eye sees under the cursor's hotspot. The world's hover, click, drag start and drop
    /// all go through <see cref="World"/> (EntityViews.PickAt), and every window's items through <see cref="WindowItem"/>,
    /// so a thing that lights up is the thing a click takes. The world's rules follow what WorldSprite.shader draws:
    /// a texel shows only at alpha 0.5 and up, and nothing at or above the upper-floor cut shows (TazUO's selection
    /// also skips what is over _maxGroundZ, GameSceneDrawingSorting.cs:662-681).
    /// </summary>
    public static class MousePick
    {
        /// <summary>WorldSprite.shader's <c>clip(c.a - _Cutoff)</c> with _Cutoff 0.5: an 8-bit alpha of 128 and up is drawn.</summary>
        public const int AlphaCutoff = 128;

        /// <summary>Whether a texel's 8-bit alpha is drawn by the world shader (and so can be picked).</summary>
        public static bool Drawn(byte alpha) => alpha >= AlphaCutoff;

        /// <summary>
        /// The centre of the display pixel under the cursor's hotspot, from Unity's <c>Input.mousePosition</c> (bottom-left
        /// origin). Unity reports x as the pixel's left edge and y as <c>Screen.height - row</c>, the pixel's top edge, the
        /// same row IMGUI's Event.mousePosition (<c>height - y</c>) reports; the pixel's centre is half a pixel right and down.
        /// </summary>
        public static (float X, float Y) PixelCenter(float x, float y) => ((float)Math.Floor(x) + 0.5f, (float)Math.Ceiling(y) - 0.5f);

        /// <summary>
        /// A screen point (bottom-left origin, display pixels) on the world plane of an orthographic camera at
        /// (<paramref name="camX"/>, <paramref name="camY"/>) showing <paramref name="orthoSize"/> world units above and
        /// below its centre over a viewport at (<paramref name="viewX"/>, <paramref name="viewY"/>) of
        /// <paramref name="viewWidth"/> x <paramref name="viewHeight"/> pixels: Camera.ScreenToWorldPoint for an orthographic
        /// camera, where depth changes nothing on screen.
        /// </summary>
        public static (float X, float Y) ScreenToWorld(float screenX, float screenY, float camX, float camY, float orthoSize,
                                                       float viewX, float viewY, float viewWidth, float viewHeight)
        {
            float unitsPerPixel = 2f * orthoSize / viewHeight;
            return (camX + (screenX - viewX - viewWidth / 2f) * unitsPerPixel, camY + (screenY - viewY - viewHeight / 2f) * unitsPerPixel);
        }

        /// <summary>WorldSprite.shader's upper-floor cut: a sprite at <paramref name="height"/> is not drawn under <paramref name="cutZ"/> (both +1000, 0 = none).</summary>
        public static bool IsCut(float cutZ, float height) => cutZ > 0.5f && height > 0.5f && height >= cutZ;

        /// <summary>A world sprite as last drawn: its quad (Unity units, y up), the atlas sprite on it, its depth and cut height.</summary>
        public struct Sprite
        {
            public float Left, Bottom, Width, Height;
            public int SpriteWidth, SpriteHeight;
            public bool Mirror;
            public float Depth;
            public float CutHeight;
        }

        /// <summary>
        /// Whether the world point (<paramref name="x"/>, <paramref name="y"/>) is on a texel of <paramref name="s"/> the
        /// shader draws: inside the quad, not cut away, and <paramref name="alphaAt"/> (sprite pixel from its top-left)
        /// at or over <see cref="AlphaCutoff"/>.
        /// </summary>
        public static bool Hits(in Sprite s, float x, float y, float cutZ, Func<int, int, byte> alphaAt) =>
            Under(s, x, y, cutZ) is { } p && Drawn(alphaAt(p.X, p.Y));

        // The sprite pixel under a world point, where the sprite is drawn at all there (not cut away), else null.
        private static (int X, int Y)? Under(in Sprite s, float x, float y, float cutZ) =>
            s.SpriteWidth <= 0 || s.SpriteHeight <= 0 || IsCut(cutZ, s.CutHeight)
                ? null
                : ObjectHighlight.SpritePixel(x, y, s.Left, s.Bottom, s.Width, s.Height, s.SpriteWidth, s.SpriteHeight, s.Mirror);

        /// <summary>
        /// The world pick at a world point: of the sprites hit (<see cref="Hits"/>), the one in front, the smallest depth;
        /// at equal depth the later one, as TazUO's selection (<c>obj.CalculateDepthZ() &gt;= prev</c>) and ZTest LEqual
        /// both keep the last drawn. -1 for none. <paramref name="alphaAt"/> takes the sprite's index and pixel.
        /// </summary>
        public static int World(IReadOnlyList<Sprite> sprites, float x, float y, float cutZ, Func<int, int, int, byte> alphaAt)
        {
            int best = -1;
            float bestDepth = float.MaxValue;
            for (int i = 0; i < sprites.Count; i++)
            {
                Sprite s = sprites[i];
                if (s.Depth > bestDepth)
                    continue;
                if (Under(s, x, y, cutZ) is { } p && Drawn(alphaAt(i, p.X, p.Y)))
                {
                    best = i;
                    bestDepth = s.Depth;
                }
            }
            return best;
        }

        /// <summary>
        /// The pick for a screen point: its pixel's centre (<see cref="PixelCenter"/>) on the world plane
        /// (<see cref="ScreenToWorld"/>), then <see cref="World(IReadOnlyList{Sprite}, float, float, float, Func{int, int, int, byte})"/>.
        /// </summary>
        public static int World(IReadOnlyList<Sprite> sprites, float screenX, float screenY, float camX, float camY, float orthoSize,
                                float viewX, float viewY, float viewWidth, float viewHeight, float cutZ, Func<int, int, int, byte> alphaAt)
        {
            (float cx, float cy) = PixelCenter(screenX, screenY);
            (float wx, float wy) = ScreenToWorld(cx, cy, camX, camY, orthoSize, viewX, viewY, viewWidth, viewHeight);
            return World(sprites, wx, wy, cutZ, alphaAt);
        }

        /// <summary>
        /// Depth ranks for sprites that share a sort key (the same diagonal, priority Z and kind give the same depth):
        /// each one's place among those placed before it, from 0, held under <paramref name="cap"/>. TazUO's tile list puts
        /// a newly added object after those of equal priority (Chunk.AddGameObject), so the later one is drawn in front;
        /// ranked into the depth key's tie levels, our depth test and our pick agree with that instead of fighting over it.
        /// </summary>
        public static void TieRanks(IReadOnlyList<(long Key, long Placed)> sprites, List<int> order, int[] ranks, int cap)
        {
            order.Clear();
            for (int i = 0; i < sprites.Count; i++)
                order.Add(i);
            order.Sort((a, b) => sprites[a].Key != sprites[b].Key ? sprites[a].Key.CompareTo(sprites[b].Key) : sprites[a].Placed.CompareTo(sprites[b].Placed));
            for (int n = 0, r = 0; n < order.Count; n++)
            {
                r = n > 0 && sprites[order[n]].Key == sprites[order[n - 1]].Key ? r + 1 : 0;
                ranks[order[n]] = Math.Min(r, cap - 1);
            }
        }

        /// <summary>The sort key <see cref="TieRanks"/> groups by: equal for sprites the depth key cannot tell apart.</summary>
        public static long TieKey(int diagonal, int priorityZ) => ((long)diagonal << 32) | (uint)priorityZ;

        /// <summary>
        /// The pixel of a picture drawn at (<paramref name="originX"/>, <paramref name="originY"/>) under a window point
        /// (y down, UI pixels, as IMGUI's Event.mousePosition under the UI scale). That point is the display pixel's
        /// top-left corner; the picture shows there what the display pixel's centre samples, half a display pixel
        /// (0.5 / <paramref name="uiScale"/> UI pixels) in, which differs at a fractional scale (Screen.height / 1000).
        /// Floored, so a point just left of or above the picture is outside it (an <c>(int)</c> cast took -0.5 as 0).
        /// </summary>
        public static (int X, int Y) UiPixel(float x, float y, float originX, float originY, float uiScale)
        {
            float half = 0.5f / (uiScale > 0 ? uiScale : 1f);
            return ((int)Math.Floor(x + half - originX), (int)Math.Floor(y + half - originY));
        }

        /// <summary>
        /// The topmost of <paramref name="count"/> window items drawn in order under a window point, and the pixel of its
        /// art there, or (-1, 0, 0). Item i's art is drawn at <paramref name="origin"/>(i); a stack drawn twice
        /// (<paramref name="doubled"/>, the second copy 5 pixels down-right, ItemGump.Draw) is hit on either copy, as
        /// ItemGump.Contains tests both. <paramref name="opaque"/> takes the item and its art's pixel. The same rule for
        /// the container, the paperdoll and the secure trade.
        /// </summary>
        public static (int Index, int X, int Y) WindowItem(int count, float x, float y, float uiScale, Func<int, (float X, float Y)> origin,
                                                           Func<int, bool> doubled, Func<int, int, int, bool> opaque)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                (float ox, float oy) = origin(i);
                (int px, int py) = UiPixel(x, y, ox, oy, uiScale);
                if (opaque(i, px, py) || doubled(i) && opaque(i, px - StackOffset, py - StackOffset))
                    return (i, px, py);
            }
            return (-1, 0, 0);
        }

        /// <summary>
        /// Where a dropped item's top-left lands in a window: the UI pixel the drag cursor shows it on, the cursor's pixel
        /// less the grab point (<see cref="DragCursor"/>), found as <see cref="UiPixel"/> finds the pick. So a press at a
        /// point that took the grab point <see cref="WindowItem"/> reports, let go at that same point, puts the item back
        /// where it was, at every UI scale (an <c>(int)(x - grab)</c> moved it a pixel up-left at fractional scales).
        /// </summary>
        public static (int X, int Y) DropOrigin(float x, float y, float grabX, float grabY, float uiScale) =>
            UiPixel(x, y, grabX, grabY, uiScale);

        /// <summary>
        /// The grab point for an item lifted by its middle (from the ground or the paperdoll): a whole art pixel, as a
        /// press in a container gives. A half pixel (<c>width / 2f</c>) let <see cref="DragCursor"/>'s rounding (22.5 to
        /// 22, 23.5 to 24) and <see cref="DropOrigin"/>'s floor disagree by a pixel on odd-sized art.
        /// </summary>
        public static int CentreGrab(int size) => size / 2;

        /// <summary>Where a stack's second copy is drawn, down-right of the first (ItemGump.Draw, rect.X/Y += 5).</summary>
        public const int StackOffset = 5;

        /// <summary>
        /// The dragged item's cursor: its stock art scaled to how windows draw it (<paramref name="uiScale"/> display pixels
        /// per UO pixel), and the grab point scaled with it, so the art under the cursor is where the drop will put it.
        /// </summary>
        public static (int Width, int Height, int HotX, int HotY) DragCursor(int artWidth, int artHeight, float grabX, float grabY, float uiScale)
        {
            float s = uiScale > 0 ? uiScale : 1f;
            int w = Math.Max(1, (int)Math.Round(artWidth * s)), h = Math.Max(1, (int)Math.Round(artHeight * s));
            int hx = Math.Max(0, Math.Min(w - 1, (int)Math.Round(grabX * s)));
            int hy = Math.Max(0, Math.Min(h - 1, (int)Math.Round(grabY * s)));
            return (w, h, hx, hy);
        }
    }
}
