using UnityEngine;
using UOClient.WorldLayer;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The world editor's panel (WORLD_EDITOR.md step 2, after the owner's mockup): the modes (Raise, Lower, Set Z, Soft,
    /// Paint, Look), the brush (size, edge softness, flow, strength, Set Z's target), the ground materials, and undo /
    /// redo. F5 or the corner button closes it; the server saves every edit by itself. Objects and blending tools come
    /// in later steps.
    /// </summary>
    public sealed class WorldEditorWindow : UiWindow
    {
        public const string WindowKey = "worldeditor";
        private const float W = 372f, H = 540f;

        private readonly WorldLayerClient _layer;
        private static GUIStyle _title, _label, _value, _button, _active, _small;
        private static Texture2D _bg, _panel, _accent;
        private static Texture2D[] _swatches;

        public WorldEditorWindow(WorldLayerClient layer)
        {
            _layer = layer;
        }

        public override string Key => WindowKey;

        public override void OnOpened()
        {
            Rect = new Rect(Screen.width / Ui.Scale - W - 16f, 80f, W, H);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static void Styles()
        {
            if (_title != null)
                return;
            _bg = Solid(new Color(0.07f, 0.08f, 0.06f, 0.94f));
            _panel = Solid(new Color(0.14f, 0.16f, 0.12f, 1f));
            _accent = Solid(new Color(0.42f, 0.55f, 0.22f, 1f));
            _title = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.92f, 0.95f, 0.85f) } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.85f, 0.88f, 0.78f) } };
            _value = new GUIStyle(_label) { alignment = TextAnchor.MiddleRight };
            _small = new GUIStyle(_label) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.7f, 0.74f, 0.64f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { background = _panel, textColor = new Color(0.85f, 0.88f, 0.78f) }, hover = { background = _panel, textColor = Color.white } };
            _active = new GUIStyle(_button) { normal = { background = _accent, textColor = Color.white }, hover = { background = _accent, textColor = Color.white } };
            _swatches = new Texture2D[TerrainMaterialsCount];
            for (int i = 0; i < _swatches.Length; i++)
                _swatches[i] = Resources.Load<Texture2D>($"TerrainMaterials/m{i}_{WorldMaterials.Names[i]}_albedo");
        }

        private const int TerrainMaterialsCount = 8; // the painted materials (classic has no swatch)

        public override void Draw()
        {
            Styles();
            // Keep it on screen (the UI scale can change after it opened: a wide or tall screen, a resize).
            float maxX = Screen.width / Ui.Scale - Rect.width, maxY = Screen.height / Ui.Scale - 40f;
            if (Rect.x > maxX || Rect.y > maxY)
                Rect = new Rect(Mathf.Clamp(Rect.x, 0f, Mathf.Max(0f, maxX - 16f)), Mathf.Clamp(Rect.y, 0f, Mathf.Max(0f, maxY)), Rect.width, Rect.height);
            WorldBrushSettings b = _layer.Brush;
            GUI.DrawTexture(new Rect(0, 0, Rect.width, Rect.height), _bg);
            GUI.Label(new Rect(12, 8, 200, 22), "World editor", _title);
            if (GUI.Button(new Rect(Rect.width - 70, 8, 58, 20), "F5 close", _button))
            {
                Close();
                return;
            }

            // Modes.
            string[] modes = { "RAISE", "LOWER", "SET Z", "SOFT", "PAINT", "LOOK" };
            float y = 38f, bw = (Rect.width - 24f - 5 * 4f) / 6f;
            for (int i = 0; i < modes.Length; i++)
            {
                var mode = (WorldBrushSettings.Mode)i;
                if (GUI.Button(new Rect(12 + i * (bw + 4), y, bw, 26), modes[i], b.Current == mode ? _active : _button))
                    b.Current = mode;
            }
            y += 36f;

            // Brush.
            b.Size = Slider(ref y, "Brush size", b.Size, 0.5f, BrushStroke.MaxRadius, $"{b.Size:0.0} tiles");
            b.Softness = Slider(ref y, "Edge softness", b.Softness, 0f, 1f, $"{b.Softness * 100:0}% fade");
            b.Flow = Slider(ref y, "Flow", b.Flow, 1f, 20f, $"{b.Flow:0.0}/sec");
            b.Strength = Slider(ref y, "Strength", b.Strength, 0.05f, 1f, $"{b.Strength * 100:0}%");
            if (b.Current == WorldBrushSettings.Mode.SetZ || b.Current == WorldBrushSettings.Mode.Look)
                b.TargetZ = Mathf.Round(Slider(ref y, "Set Z target", b.TargetZ, -60f, 100f, $"Z {b.TargetZ:0}"));
            else
                y += 28f;

            // Materials.
            GUI.Label(new Rect(12, y, 200, 20), b.Current == WorldBrushSettings.Mode.Paint ? "Ground (PAINT)" : "Ground (choose, then PAINT)", _label);
            y += 22f;
            float sw = (Rect.width - 24f - 3 * 6f) / 4f;
            for (int i = 0; i < TerrainMaterialsCount; i++)
            {
                var cell = new Rect(12 + (i % 4) * (sw + 6), y + (i / 4) * (sw * 0.62f + 26), sw, sw * 0.62f);
                if (b.Material == i)
                    GUI.DrawTexture(new Rect(cell.x - 2, cell.y - 2, cell.width + 4, cell.height + 4), _accent);
                if (_swatches[i] != null)
                    GUI.DrawTexture(cell, _swatches[i], ScaleMode.ScaleAndCrop);
                else
                    GUI.DrawTexture(cell, _panel);
                GUI.Label(new Rect(cell.x, cell.yMax + 1, cell.width, 18), WorldMaterials.Names[i], _small);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    b.Material = (byte)i;
                    b.Current = WorldBrushSettings.Mode.Paint;
                }
            }
            y += 2 * (sw * 0.62f + 26) + 8;

            // Undo / redo.
            float hb = (Rect.width - 24f - 6f) / 2f;
            if (GUI.Button(new Rect(12, y, hb, 26), "UNDO (Ctrl+Z)", _button))
                _layer.Undo(false);
            if (GUI.Button(new Rect(18 + hb, y, hb, 26), "REDO (Ctrl+Y)", _button))
                _layer.Undo(true);
            y += 34f;

            GUI.Label(new Rect(12, y, Rect.width - 24, 54),
                "Hold the left mouse on the ground to use the brush; the right mouse still walks. Every stroke is saved on the server by itself.",
                _small);
            y += 56f;
            GUI.Label(new Rect(12, y, Rect.width - 24, 36), _layer.Status, _small);
        }

        private float Slider(ref float y, string name, float value, float min, float max, string shown)
        {
            GUI.Label(new Rect(12, y, 110, 20), name, _label);
            float v = GUI.HorizontalSlider(new Rect(122, y + 6, Rect.width - 122 - 96, 16), value, min, max);
            GUI.Label(new Rect(Rect.width - 92, y, 80, 20), shown, _value);
            y += 28f;
            return v;
        }
    }
}
