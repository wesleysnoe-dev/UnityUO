using System;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the rule.
    public sealed partial class ContextMenuWindow
    {
        /// <summary>
        /// Where a menu of the given size opens for a cursor at (mouseX, mouseY), all in UI units (top-left origin):
        /// its corner at the cursor, flipped to the cursor's other side when it would run off the view, and all of it
        /// kept on the view, so every entry can be clicked.
        /// </summary>
        public static (float x, float y) Place(float mouseX, float mouseY, float width, float height, float viewWidth, float viewHeight)
        {
            float x = mouseX, y = mouseY;
            if (x + width > viewWidth) x = mouseX - width;
            if (y + height > viewHeight) y = mouseY - height;
            x = Math.Max(0, Math.Min(x, viewWidth - width));
            y = Math.Max(0, Math.Min(y, viewHeight - height));
            return (x, y);
        }
    }
}
