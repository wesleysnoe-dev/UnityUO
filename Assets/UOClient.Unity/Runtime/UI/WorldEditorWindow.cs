using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.IO.Formats;
using UOClient.WorldLayer;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The world editor's panel (WORLD_EDITOR.md, after the owner's mockup): the modes (Raise, Lower, Set Z, Soft,
    /// Paint, Look), the brush (size, edge softness, flow, strength, Set Z's target), and two ground palettes:
    /// TERRAIN, the blended natural materials, and SURFACE, every UO ground family with its own tiles, laid as is
    /// (floors, roads, caves, lava...). Undo / redo. F5 or the corner button closes it; the server saves every edit itself.
    /// </summary>
    public sealed class WorldEditorWindow : UiWindow
    {
        public const string WindowKey = "worldeditor";
        private const float W = 372f, H = 660f;

        private readonly WorldLayerClient _layer;
        private static GUIStyle _title, _label, _value, _button, _active, _small, _listItem, _listActive, _field;
        private static Texture2D _bg, _panel, _accent;
        private static Texture2D[] _swatches;

        private bool _surface;               // SURFACE tab (else TERRAIN)
        private string _search = "";
        private string _family;              // the chosen UO ground family
        private Vector2 _familyScroll, _tileScroll;
        private static List<(string Name, int[] Graphics)> _families;

        public WorldEditorWindow(WorldLayerClient layer)
        {
            _layer = layer;
            _surface = layer.Brush.Ground != 0;
        }

        public override string Key => WindowKey;

        /// <summary>Opens the SURFACE tab on a family (the self-test shows the gallery this way).</summary>
        public void ShowSurface(string family)
        {
            _surface = true;
            _family = family;
        }

        public override void OnOpened()
        {
            Rect = new Rect(Screen.width / Ui.Scale - W - 16f, 60f, W, H);
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
            _listItem = new GUIStyle(_label) { fontSize = 11, padding = new RectOffset(4, 2, 1, 1), hover = { textColor = Color.white, background = _panel } };
            _listActive = new GUIStyle(_listItem) { normal = { textColor = Color.white, background = _accent }, hover = { textColor = Color.white, background = _accent } };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 12 };
            _swatches = new Texture2D[NaturalCount];
            for (int i = 0; i < _swatches.Length; i++)
                _swatches[i] = Resources.Load<Texture2D>($"TerrainMaterials/m{i}_{WorldMaterials.Names[i]}_albedo");
        }

        private const int NaturalCount = 8; // the blended materials (classic has no swatch)

        /// <summary>The UO ground families: land graphics grouped by tiledata name, the drawn ones only.</summary>
        private List<(string Name, int[] Graphics)> Families()
        {
            if (_families != null)
                return _families;
            LandTileData[] land = Art.Data.TileData.Land;
            var byName = new Dictionary<string, List<int>>();
            for (int g = 3; g < land.Length; g++)
            {
                if (g == 0x1DB || (g >= 0x1AE && g <= 0x1B5))
                    continue; // no-draw
                string name = (land[g].Name ?? "").Trim().ToLowerInvariant();
                if (name.Length == 0 || name == "nodraw" || name.StartsWith("void") || name.StartsWith("terrainfal"))
                    continue;
                if (name.StartsWith("decimated_"))
                    name = name.Substring("decimated_".Length) + " (worn)";
                if (!byName.TryGetValue(name, out List<int> list))
                    byName[name] = list = new List<int>();
                list.Add(g);
            }
            _families = byName.OrderBy(kv => kv.Key == "noname" ? "~" : kv.Key)
                .Select(kv => (kv.Key == "noname" ? "(unnamed)" : kv.Key, kv.Value.ToArray())).ToList();
            return _families;
        }

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

            // Ground palettes.
            float hb = (Rect.width - 24f - 6f) / 2f;
            if (GUI.Button(new Rect(12, y, hb, 24), "TERRAIN", !_surface ? _active : _button))
                _surface = false;
            if (GUI.Button(new Rect(18 + hb, y, hb, 24), "SURFACE", _surface ? _active : _button))
                _surface = true;
            y += 32f;
            float paletteHeight = 300f;
            if (_surface)
                DrawSurface(b, new Rect(12, y, Rect.width - 24, paletteHeight));
            else
                DrawTerrain(b, new Rect(12, y, Rect.width - 24, paletteHeight));
            y += paletteHeight + 8f;

            // Undo / redo.
            if (GUI.Button(new Rect(12, y, hb, 26), "UNDO (Ctrl+Z)", _button))
                _layer.Undo(false);
            if (GUI.Button(new Rect(18 + hb, y, hb, 26), "REDO (Ctrl+Y)", _button))
                _layer.Undo(true);
            y += 32f;
            GUI.Label(new Rect(12, y, Rect.width - 24, 40),
                "Hold the left mouse on the ground to use the brush; the right mouse still walks. Every stroke is saved on the server by itself.",
                _small);
            y += 40f;
            GUI.Label(new Rect(12, y, Rect.width - 24, 34), _layer.Status, _small);
        }

        /// <summary>TERRAIN: the blended natural materials (soft edges).</summary>
        private void DrawTerrain(WorldBrushSettings b, Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 20), "Natural ground, blended (choose, then paint)", _label);
            float sw = (area.width - 3 * 6f) / 4f, top = area.y + 24f;
            for (int i = 0; i < NaturalCount; i++)
            {
                var cell = new Rect(area.x + (i % 4) * (sw + 6), top + (i / 4) * (sw * 0.62f + 26), sw, sw * 0.62f);
                if (b.Ground == 0 && b.Material == i)
                    GUI.DrawTexture(new Rect(cell.x - 2, cell.y - 2, cell.width + 4, cell.height + 4), _accent);
                GUI.DrawTexture(cell, _swatches[i] != null ? _swatches[i] : _panel, ScaleMode.ScaleAndCrop);
                GUI.Label(new Rect(cell.x, cell.yMax + 1, cell.width, 18), WorldMaterials.Names[i], _small);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none))
                {
                    b.Material = (byte)i;
                    b.Ground = 0;
                    b.Current = WorldBrushSettings.Mode.Paint;
                }
            }
            GUI.Label(new Rect(area.x, top + 2 * (sw * 0.62f + 26) + 6, area.width, 60),
                "Edges blend softly. For floors, roads, caves, lava and every other UO ground laid tile by tile, use SURFACE.", _small);
        }

        /// <summary>SURFACE: every UO ground family, its tiles laid as is (crisp tile edges, as everyone's client draws them).</summary>
        private void DrawSurface(WorldBrushSettings b, Rect area)
        {
            List<(string Name, int[] Graphics)> families = Families();
            GUI.Label(new Rect(area.x, area.y + 2, 50, 20), "Search", _label);
            _search = GUI.TextField(new Rect(area.x + 52, area.y, area.width - 52, 20), _search ?? "", 40, _field);
            string q = _search.Trim().ToLowerInvariant();
            var shown = q.Length == 0 ? families : families.Where(f => f.Name.Contains(q)).ToList();

            // Families on the left.
            float listW = 128f, top = area.y + 26f, listH = area.height - 26f;
            var view = new Rect(0, 0, listW - 16, shown.Count * 18f);
            _familyScroll = GUI.BeginScrollView(new Rect(area.x, top, listW, listH), _familyScroll, view);
            for (int i = 0; i < shown.Count; i++)
                if (GUI.Button(new Rect(0, i * 18f, listW - 16, 18), $"{shown[i].Name} ({shown[i].Graphics.Length})", shown[i].Name == _family ? _listActive : _listItem))
                {
                    _family = shown[i].Name;
                    _tileScroll = Vector2.zero;
                }
            GUI.EndScrollView();

            // The family's tiles on the right.
            float gx = area.x + listW + 6, gw = area.width - listW - 6;
            int[] tiles = families.FirstOrDefault(f => f.Name == _family).Graphics;
            if (tiles == null)
            {
                GUI.Label(new Rect(gx, top, gw, 60), "Choose a ground on the left, then a tile.", _small);
                return;
            }
            const float cell = 50f;
            int cols = Math.Max(1, (int)(gw / cell));
            var grid = new Rect(0, 0, gw - 16, ((tiles.Length + cols - 1) / cols) * cell);
            _tileScroll = GUI.BeginScrollView(new Rect(gx, top, gw, listH), _tileScroll, grid);
            for (int i = 0; i < tiles.Length; i++)
            {
                var r = new Rect((i % cols) * cell, (i / cols) * cell, cell - 4, cell - 4);
                if (r.yMax < _tileScroll.y || r.y > _tileScroll.y + listH)
                    continue; // off the visible part: no texture made for it
                if (b.Ground == tiles[i])
                    GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), _accent);
                UiImage img = Art.Land(tiles[i]);
                if (img?.Texture != null)
                    GUI.DrawTexture(r, img.Texture, ScaleMode.ScaleToFit);
                if (GUI.Button(r, new GUIContent("", $"0x{tiles[i]:X4}"), GUIStyle.none))
                {
                    b.Ground = (ushort)tiles[i];
                    b.Current = WorldBrushSettings.Mode.Paint;
                }
            }
            GUI.EndScrollView();
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
