using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The mouse cursor: a crosshair while the server wants a target (white neutral, red harmful, blue beneficial),
    /// else the classic client's gauntlet: red in war mode, gold in peace (the system pointer only until art loads).
    /// Over the world the gauntlet points from the player toward the mouse, the way a right-drag would walk (TazUO
    /// GameCursor.AssignGraphicByState); over a window it is the fat west-pointing one. A dragged item owns the cursor.
    /// </summary>
    public sealed partial class TargetCursor
    {
        private Texture2D _crossNeutral, _crossHarmful, _crossBeneficial;
        private readonly Dictionary<int, (Texture2D Tex, Vector2 Hot)> _pointers = new Dictionary<int, (Texture2D, Vector2)>();
        private bool _cursorSet;
        private Texture2D _cursorTex;
        /// <summary>
        /// Where the player stands on screen (GUI pixels, y down), set by the world each frame; null while the mouse is
        /// off the world (over the journal, or no world drawn), which shows the window pointer.
        /// </summary>
        public Vector2? PlayerScreen { get; set; }

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
                // The crosshair's centre is texel (16, 16) counted up from the bottom (Crosshair), so row 15 from the
                // top, which is what SetCursor's hotspot counts from.
                hotspot = new Vector2(16, 15);
            }
            else if (ui?.Art != null)
            {
                Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                Vector2 o = PlayerScreen ?? Vector2.zero;
                bool overWorld = PlayerScreen.HasValue && !ui.IsOverUi(Input.mousePosition);
                int index = PointerIndex(overWorld, Mathf.RoundToInt(o.x), Mathf.RoundToInt(o.y), Mathf.RoundToInt(m.x), Mathf.RoundToInt(m.y));
                (tex, hotspot) = Pointer(ui.Art.Data, CursorGraphic(index, session != null && session.WarMode));
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

        /// <summary>
        /// A pointer from its stock art (not HD: a cursor is drawn 1:1), its border markers cleared and its hotspot read
        /// from them (<see cref="CursorImage"/>). Readable, as Cursor.SetCursor needs. (null, 0) if the art is missing.
        /// </summary>
        private (Texture2D, Vector2) Pointer(UOClient.IO.UOData data, int graphic)
        {
            if (_pointers.TryGetValue(graphic, out var p))
                return p;
            Bitmap32 art = data?.GetStatic(graphic);
            if (art != null)
            {
                Bitmap32 bmp = CursorImage(art, out int hotX, out int hotY);
                var tex = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false)
                {
                    name = $"cursor 0x{graphic:X4}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
                };
                var colors = new Color32[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        uint c = bmp[x, y];
                        colors[(bmp.Height - 1 - y) * bmp.Width + x] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));
                    }
                tex.SetPixels32(colors);
                tex.Apply(false, false);
                p = (tex, new Vector2(hotX, hotY));
            }
            _pointers[graphic] = p;
            return p;
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
