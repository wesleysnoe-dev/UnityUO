using System;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the rule.
    public sealed partial class HoverTooltip
    {
        public const float CursorOffset = 18, FlipGap = 8;

        /// <summary>
        /// Where a tooltip of the given size goes for a cursor at (mouseX, mouseY), all in UI units (top-left origin):
        /// just down-right of the cursor, flipped to the other side of it when that runs off the view, and kept on it.
        /// </summary>
        public static (float x, float y) Place(float mouseX, float mouseY, float width, float height, float viewWidth, float viewHeight)
        {
            float x = mouseX + CursorOffset, y = mouseY + CursorOffset;
            if (x + width > viewWidth) x = mouseX - width - FlipGap;
            if (y + height > viewHeight) y = mouseY - height - FlipGap;
            x = Math.Max(0, Math.Min(x, viewWidth - width));
            y = Math.Max(0, Math.Min(y, viewHeight - height));
            return (x, y);
        }
    }
}
