using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>A crosshair while the server wants a target: white neutral, red harmful, blue beneficial.</summary>
    public sealed class TargetCursor
    {
        private Texture2D _crossNeutral, _crossHarmful, _crossBeneficial;
        private bool _cursorSet;
        private Texture2D _cursorTex;

        public void Update(GameSession session, UiManager ui)
        {
            if (ui != null && ui.Dragging != null)
                return;
            TargetRequest t = session?.Target;
            Texture2D tex = null;
            if (t != null)
            {
                _crossNeutral ??= Crosshair(new Color(0.95f, 0.95f, 0.95f));
                _crossHarmful ??= Crosshair(new Color(1f, 0.3f, 0.25f));
                _crossBeneficial ??= Crosshair(new Color(0.4f, 0.7f, 1f));
                tex = t.Flags == 1 ? _crossHarmful : t.Flags == 2 ? _crossBeneficial : _crossNeutral;
            }
            if (_cursorSet && tex == _cursorTex)
                return;
            _cursorSet = true;
            _cursorTex = tex;
            if (tex == null)
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            else
                Cursor.SetCursor(tex, new Vector2(16, 16), CursorMode.ForceSoftware);
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
