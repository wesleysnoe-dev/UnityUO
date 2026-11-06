using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// IMGUI overlay: "SAMPLE NOT LIVE MAP" banner, side names, tile/Z convention, view-mode buttons
    /// (keys 1 = Terrain, 2 = Mesh, 3 = Side by side) and the Play-mode parity numbers.
    /// P2 adds "SAMPLE TEXTURES NOT MAP ART", the toggle state and honest notes (blend mismatches,
    /// emission vs real light, water limits).
    /// </summary>
    public sealed class CompareOverlayLabels : MonoBehaviour
    {
        public IsoCompareCamera compareCamera;
        public VisualCompareToggles toggles;
        public string conventionLine = "";
        public string parityLine = "";
        /// <summary>P2 honesty notes, drawn bottom-up above the convention line.</summary>
        public string[] noteLines = new string[0];

        private GUIStyle _banner, _label, _small;

        private void OnGUI()
        {
            EnsureStyles();
            HandleKeys();

            float w = Screen.width, h = Screen.height;
            GUI.Label(new Rect(0, 8, w, 30), "SAMPLE NOT LIVE MAP  —  SAMPLE TEXTURES NOT MAP ART  —  GroundPrototype P2", _banner);

            var mode = compareCamera != null ? compareCamera.Mode : CompareMode.SideBySide;
            switch (mode)
            {
                case CompareMode.Terrain:
                    GUI.Label(new Rect(12, 44, w - 24, 24), "Unity Terrain (in-memory TerrainData, heightmap 33x33)", _label);
                    break;
                case CompareMode.Mesh:
                    GUI.Label(new Rect(12, 44, w - 24, 24), "Custom Mesh (2 planar tris / quad, SW-NE diagonal)", _label);
                    break;
                default:
                    GUI.Label(new Rect(12, 44, w * 0.5f - 24, 24), "LEFT: Unity Terrain", _label);
                    GUI.Label(new Rect(w * 0.5f + 12, 44, w * 0.5f - 24, 24), "RIGHT: Custom Mesh", _label);
                    break;
            }

            if (toggles != null)
                GUI.Label(new Rect(12, 72, Mathf.Max(200f, w - 4 * 138f - 30f), 22), toggles.StateLine + "   (keys T/F/L/N)", _small);

            for (int i = 0; i < noteLines.Length; i++)
                GUI.Label(new Rect(12, h - 48 - 20 * (noteLines.Length - i), w - 24, 20), noteLines[i], _small);
            GUI.Label(new Rect(12, h - 48, w - 24, 20), conventionLine, _small);
            GUI.Label(new Rect(12, h - 28, w - 24, 20), parityLine, _small);

            if (compareCamera == null) return;
            float bx = w - 3 * 112 - 12;
            if (GUI.Button(new Rect(bx, 44, 104, 24), "1 Terrain")) compareCamera.SetMode(CompareMode.Terrain);
            if (GUI.Button(new Rect(bx + 112, 44, 104, 24), "2 Mesh")) compareCamera.SetMode(CompareMode.Mesh);
            if (GUI.Button(new Rect(bx + 224, 44, 104, 24), "3 Side by side")) compareCamera.SetMode(CompareMode.SideBySide);
        }

        private void HandleKeys()
        {
            var e = Event.current;
            if (compareCamera == null || e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.Alpha1: case KeyCode.Keypad1: compareCamera.SetMode(CompareMode.Terrain); e.Use(); break;
                case KeyCode.Alpha2: case KeyCode.Keypad2: compareCamera.SetMode(CompareMode.Mesh); e.Use(); break;
                case KeyCode.Alpha3: case KeyCode.Keypad3: compareCamera.SetMode(CompareMode.SideBySide); e.Use(); break;
            }
        }

        private void EnsureStyles()
        {
            if (_banner != null) return;
            _banner = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _banner.normal.textColor = new Color(1f, 0.45f, 0.35f, 1f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _small.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        }
    }
}
