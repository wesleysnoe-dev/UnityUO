using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The mouse cursor: a crosshair while the server wants a target (white neutral, red harmful, blue beneficial),
    /// else in war mode the classic client's red gauntlet, else the system pointer. A dragged item owns the cursor.
    /// </summary>
    public sealed class TargetCursor
    {
        /// <summary>
        /// The war-mode pointer in art: the classic cursors are 16 statics each, from 0x206A in peace and 0x2053 in war;
        /// the ninth (0x2073 / 0x205C) is the up-left gauntlet, its fingertip at the image's top-left corner.
        /// </summary>
        public const int WarPointer = 0x2053 + 9;

        private Texture2D _crossNeutral, _crossHarmful, _crossBeneficial, _warPointer;
        private bool _cursorSet;
        private Texture2D _cursorTex;

        public void Update(GameSession session, UiManager ui)
        {
            if (ui != null && ui.Dragging != null)
            {
                _cursorSet = false; // the drag set its own cursor and clears it to the system one: put ours back after
                return;
            }
            TargetRequest t = session?.Target;
            Texture2D tex = null;
            Vector2 hotspot = Vector2.zero;
            if (t != null)
            {
                _crossNeutral ??= Crosshair(new Color(0.95f, 0.95f, 0.95f));
                _crossHarmful ??= Crosshair(new Color(1f, 0.3f, 0.25f));
                _crossBeneficial ??= Crosshair(new Color(0.4f, 0.7f, 1f));
                tex = t.Flags == 1 ? _crossHarmful : t.Flags == 2 ? _crossBeneficial : _crossNeutral;
                hotspot = new Vector2(16, 16);
            }
            else if (session != null && session.WarMode && ui?.Art != null)
            {
                _warPointer ??= ui.Art.Item(WarPointer)?.Texture;
                tex = _warPointer;
            }
            if (_cursorSet && tex == _cursorTex)
                return;
            _cursorSet = true;
            _cursorTex = tex;
            if (tex == null)
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            else
                Cursor.SetCursor(tex, hotspot, CursorMode.ForceSoftware);
        }

        private static Texture2D Crosshair(Color color)
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            var dark = new Color(0, 0, 0, 0.9f);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int dx = x - 16, dy = y - 16;
                    bool ring = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 9) < 1.2f;
                    bool cross = (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) > 3 && Mathf.Abs(dy) < 15) || (Mathf.Abs(dy) <= 1 && Mathf.Abs(dx) > 3 && Mathf.Abs(dx) < 15);
                    bool edge = (Mathf.Abs(dx) == 2 && Mathf.Abs(dy) > 3 && Mathf.Abs(dy) < 15) || (Mathf.Abs(dy) == 2 && Mathf.Abs(dx) > 3 && Mathf.Abs(dx) < 15);
                    tex.SetPixel(x, y, ring || cross ? color : edge ? dark : clear);
                }
            tex.Apply();
            return tex;
        }
    }
}
