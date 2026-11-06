using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>The cursor's rules, with no UnityEngine dependency so the tests compile them.</summary>
    public sealed partial class TargetCursor
    {
        /// <summary>The first of the 16 pointer statics in peace (gold) and war (red): TazUO GameCursor._cursorData[0/1, 0].</summary>
        public const int PeaceBase = 0x206A;
        public const int WarBase = 0x2053;

        /// <summary>
        /// The pointer off the world (over a window, or before the player is drawn): TazUO's FatPointingWest
        /// (GameCursorVisualType 9, its AssignGraphicByState default).
        /// </summary>
        public const int FatPointingWest = 9;

        /// <summary>The pointer art for cursor index <paramref name="index"/> (0..15): TazUO _cursorData[war, index].</summary>
        public static int CursorGraphic(int index, bool war) => (war ? WarBase : PeaceBase) + index;

        /// <summary>
        /// TazUO GameCursor.GetMouseDirection: the screen direction from (x1, y1) to (toX, toY) in pixels (y down), 0..7
        /// clockwise from screen up: the gauntlet indices GameCursorVisualType PointingNorthWest (0, screen up) ..
        /// PointingWest (7, screen up-left). It is one ahead of the UO walk direction (GameSceneInputHandler walks
        /// <c>facing - 1</c>). A diagonal needs the slope between 2:5 and 5:2; flatter or steeper snaps to the axis. On the
        /// origin itself it returns <paramref name="currentFacing"/> (TazUO passes 1).
        /// </summary>
        public static int MouseDirection(int x1, int y1, int toX, int toY, int currentFacing)
        {
            int shiftX = toX - x1;
            int shiftY = toY - y1;
            int hashf = 100 * (Sgn(shiftX) + 2) + 10 * (Sgn(shiftY) + 2);

            if (shiftX != 0 && shiftY != 0)
            {
                shiftX = System.Math.Abs(shiftX);
                shiftY = System.Math.Abs(shiftY);
                if (shiftY * 5 <= shiftX * 2)
                    hashf += 1;
                else if (shiftY * 2 >= shiftX * 5)
                    hashf += 3;
                else
                    hashf += 2;
            }
            else if (shiftX == 0 && shiftY == 0)
            {
                return currentFacing;
            }

            switch (hashf)
            {
                case 111: return 6; // W
                case 112: return 7; // Up (NW)
                case 113: return 0; // N
                case 120: return 6; // W
                case 131: return 6; // W
                case 132: return 5; // Left (SW)
                case 133: return 4; // S
                case 210: return 0; // N
                case 230: return 4; // S
                case 311: return 2; // E
                case 312: return 1; // Right (NE)
                case 313: return 0; // N
                case 320: return 2; // E
                case 331: return 2; // E
                case 332: return 3; // Down (SE)
                case 333: return 4; // S
            }
            return currentFacing;
        }

        private static int Sgn(int v) => (0 < v ? 1 : 0) - (v < 0 ? 1 : 0);

        /// <summary>
        /// The cursor index to show: the targeting crosshair and a held item are drawn elsewhere, so this is only the
        /// pointer. Over the world it points from the player toward the mouse (TazUO AssignGraphicByState:
        /// GetMouseDirection(camera center, mouse, 1)); elsewhere it is <see cref="FatPointingWest"/>.
        /// </summary>
        public static int PointerIndex(bool overWorld, int originX, int originY, int mouseX, int mouseY) =>
            overWorld ? MouseDirection(originX, originY, mouseX, mouseY, 1) : FatPointingWest;

        /// <summary>
        /// A pointer's picture and hotspot (<see cref="CursorImage"/>) from its stock art.mul entry, as TazUO's art
        /// loader reads it. Not the HD pack's copy: its upscale blends the pure-green markers into other greens, so no
        /// marker is found and the hotspot falls to (0, 0), the picture's corner instead of the fingertip. Null if missing.
        /// </summary>
        public static Bitmap32 PointerImage(UOClient.IO.UOData data, int graphic, out int hotX, out int hotY)
        {
            hotX = hotY = 0;
            Bitmap32 art = data?.GetStockStatic(graphic);
            return art == null ? null : CursorImage(art, out hotX, out hotY);
        }

        /// <summary>
        /// TazUO Art.CreateCursorSurfacePtr: the cursor art carries its hotspot as pure green marker pixels on its top row
        /// (x) and left column (y); every non-black pixel on the border (top row, left column, last row, last column) is a
        /// marker and is cleared. Returns a cleaned copy; the hotspot is (0, 0) when the art has no marker.
        /// </summary>
        public static Bitmap32 CursorImage(Bitmap32 art, out int hotX, out int hotY)
        {
            hotX = hotY = 0;
            var bmp = new Bitmap32(art.Width, art.Height);
            System.Array.Copy(art.Pixels, bmp.Pixels, art.Pixels.Length);
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint p = bmp[x, y];
                    if (p == 0 || p == 0xFF000000)
                        continue;
                    if (x >= bmp.Width - 1 || y >= bmp.Height - 1)
                    {
                        bmp[x, y] = 0;
                    }
                    else if (x == 0 || y == 0)
                    {
                        if (p == 0xFF00FF00)
                        {
                            if (x == 0)
                                hotY = y;
                            if (y == 0)
                                hotX = x;
                        }
                        bmp[x, y] = 0;
                    }
                }
            return bmp;
        }
    }
}
