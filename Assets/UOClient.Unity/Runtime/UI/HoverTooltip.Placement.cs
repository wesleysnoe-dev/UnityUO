namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the rule.
    public sealed partial class HoverTooltip
    {
        /// <summary>TazUO's GameCursor.DrawToolTip passes the cursor 24 down; Tooltip.Draw then draws 4 left and 2 up of that.</summary>
        public const float CursorDrop = 24, InsetX = 4, InsetY = 2;

        /// <summary>
        /// Where a tooltip of the given size goes for a cursor at (mouseX, mouseY), all in UI units (top-left origin),
        /// as TazUO's Tooltip.Draw places it: under the cursor, pushed back onto the view at the right and bottom
        /// edges (it never flips to the other side of the cursor), then offset by the inset.
        /// </summary>
        public static (float x, float y) Place(float mouseX, float mouseY, float width, float height, float viewWidth, float viewHeight)
        {
            float x = mouseX, y = mouseY + CursorDrop;
            if (x < 0) x = 0;
            else if (x > viewWidth - width) x = viewWidth - width;
            if (y < 0) y = 0;
            else if (y > viewHeight - height) y = viewHeight - height;
            return (x - InsetX, y - InsetY);
        }
    }
}
