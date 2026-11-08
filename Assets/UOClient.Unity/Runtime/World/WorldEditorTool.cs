using System;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.WorldLayer;

namespace UOClient.Unity
{
    /// <summary>
    /// The world editor's hands (WORLD_EDITOR.md step 2): F5 opens and closes the panel (<see cref="UI.WorldEditorWindow"/>,
    /// administrators only), the brush circle follows the mouse on the ground, and holding the left mouse over the world
    /// paints with the panel's mode at the panel's flow. While the panel is open and the mouse is over the world, the
    /// left mouse is the brush's (<see cref="CapturesLeftMouse"/>: WorldInput leaves it alone); the right mouse still walks.
    /// Ctrl+Z / Ctrl+Y undo and redo.
    /// </summary>
    public sealed class WorldEditorTool
    {
        /// <summary>Read by WorldInput: the left mouse belongs to the brush this frame.</summary>
        public static bool CapturesLeftMouse { get; private set; }

        private readonly GameContext _ctx;
        private readonly EntityViews _entities;
        private readonly Func<UI.UiManager> _ui;
        private float _nextStroke;
        private bool _painting;
        private int _strokes;

        public WorldEditorTool(GameContext ctx, EntityViews entities, Func<UI.UiManager> ui)
        {
            _ctx = ctx;
            _entities = entities;
            _ui = ui;
        }

        /// <summary>The brush centre under the mouse (world tile units), or null off the world.</summary>
        public Vector2? Cursor { get; private set; }

        private static WorldLayerClient Layer => WorldLayerClient.Current;

        private bool Open => _ui()?.Find(UI.WorldEditorWindow.WindowKey) != null;

        // -worldeditor-selftest: once connected as an editor, opens the panel and paints a short script near the player
        // (a hill, a sand path across it, a smoothed rim), so the whole loop can be checked without a hand on the mouse.
        private static readonly bool SelfTest = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-worldeditor-selftest", StringComparison.OrdinalIgnoreCase));
        private int _testStep = -1;
        private float _testAt;

        private void RunSelfTest(UI.UiManager ui, WorldLayerClient layer)
        {
            if (!layer.CanEdit || Time.unscaledTime < _testAt)
                return;
            int px = _ctx.Player.X, py = _ctx.Player.Y;
            WorldBrushSettings b = layer.Brush;
            _testAt = Time.unscaledTime + 0.08f;
            _testStep++;
            if (_testStep == 0)
            {
                if (ui.Find(UI.WorldEditorWindow.WindowKey) == null)
                    ui.Open(new UI.WorldEditorWindow(layer));
                _ctx.Hud?.Log(null, "World editor self-test: painting near you.");
            }
            else if (_testStep <= 30) // raise a hill 5 tiles right-down of the player
            {
                b.Current = WorldBrushSettings.Mode.Raise; b.Size = 5f; b.Softness = 0.9f; b.Strength = 0.6f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep <= 70) // a sand path across it
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Material = WorldMaterials.Sand; b.Size = 1.6f; b.Softness = 0.7f; b.Strength = 0.7f;
                float t = (_testStep - 31) / 39f;
                layer.Stroke(px - 2f + t * 14f, py - 4f + t * 9f);
            }
            else if (_testStep <= 80) // rock on the hill top
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Material = WorldMaterials.Rock; b.Size = 2f; b.Softness = 0.8f; b.Strength = 0.5f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep <= 90) // soften the rim
            {
                b.Current = WorldBrushSettings.Mode.Soft; b.Size = 7f; b.Softness = 0.6f; b.Strength = 0.5f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep == 91)
            {
                layer.EndStroke();
                b.Current = WorldBrushSettings.Mode.Raise;
                _ctx.Hud?.Log(null, "World editor self-test: done.");
            }
        }

        /// <summary>Every frame. <paramref name="takesKeys"/>: the world has the keyboard (not typing in chat or a text box).</summary>
        public void Update(bool takesKeys)
        {
            UI.UiManager ui = _ui();
            WorldLayerClient layer = Layer;
            CapturesLeftMouse = false;
            Cursor = null;
            if (ui == null || layer == null || _ctx.World == null || _ctx.Player == null)
                return;
            if (SelfTest && _testStep < 91)
                RunSelfTest(ui, layer);

            if (takesKeys && Input.GetKeyDown(KeyCode.F5))
            {
                if (ui.Find(UI.WorldEditorWindow.WindowKey) is UI.UiWindow w)
                    ui.Close(w);
                else if (layer.CanEdit)
                    ui.Open(new UI.WorldEditorWindow(layer));
                else
                    _ctx.Hud?.Log(null, "World editor: " + (string.IsNullOrEmpty(layer.Status) ? "not connected to an editing server." : layer.Status));
            }
            if (!Open)
            {
                StopPainting();
                return;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (takesKeys && ctrl && Input.GetKeyDown(KeyCode.Z))
                layer.Undo(false);
            if (takesKeys && ctrl && Input.GetKeyDown(KeyCode.Y))
                layer.Undo(true);

            bool overWorld = !ui.IsOverUi(Input.mousePosition) && ui.Dragging == null;
            if (!overWorld)
            {
                if (!Input.GetMouseButton(0))
                    StopPainting();
                if (!_painting)
                    return;
            }
            Cursor = MouseTile();
            CapturesLeftMouse = true;
            if (Cursor is not Vector2 at)
                return;

            WorldBrushSettings brush = layer.Brush;
            if (brush.Current == WorldBrushSettings.Mode.Look)
            {
                if (Input.GetMouseButtonDown(0) && layer.HeightAt(at.x, at.y) is float picked)
                {
                    brush.TargetZ = Mathf.Round(picked);
                    brush.Current = WorldBrushSettings.Mode.SetZ;
                    _ctx.Hud?.Log(null, $"World editor: Set Z target {brush.TargetZ:0}");
                }
                return;
            }
            if (Input.GetMouseButtonDown(0) && overWorld)
            {
                _painting = true;
                _strokes = 0;
                _nextStroke = 0f;
            }
            if (_painting && Input.GetMouseButton(0))
            {
                if (Time.unscaledTime >= _nextStroke)
                {
                    _nextStroke = Time.unscaledTime + 1f / Mathf.Max(1f, brush.Flow);
                    layer.Stroke(at.x, at.y);
                    _strokes++;
                }
            }
            else
                StopPainting();
        }

        private void StopPainting()
        {
            if (!_painting)
                return;
            _painting = false;
            if (_strokes > 0)
                Layer?.EndStroke();
            _strokes = 0;
        }

        /// <summary>
        /// The world point (tile units) under the mouse on the ground: the screen point back through the iso projection,
        /// twice, the second time at the ground's height where the first guess landed.
        /// </summary>
        private Vector2? MouseTile()
        {
            Vector2 unity = _entities.ScreenToWorld(Input.mousePosition);
            Vector2 px = _ctx.World.FromUnity(unity);
            float s = _ctx.ArtScale;
            float z = _ctx.Player.Z;
            float x = 0, y = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                float u = px.x / (22f * s), v = (px.y / s + z * 4f) / 22f;
                x = (v + u) * 0.5f;
                y = (v - u) * 0.5f;
                z = GroundZ(x, y);
            }
            return new Vector2(x, y);
        }

        private float GroundZ(float x, float y)
        {
            if (Layer?.HeightAt(x, y) is float h)
                return h;
            MapFile map = _ctx.Data?.GetMap(_ctx.World.Map);
            if (map == null)
                return _ctx.Player.Z;
            int ix = Mathf.Clamp(Mathf.FloorToInt(x), 0, map.Width - 1), iy = Mathf.Clamp(Mathf.FloorToInt(y), 0, map.Height - 1);
            return map.GetLand(ix, iy).Z;
        }

        private static Texture2D _dot;

        /// <summary>The brush circle on the ground under the mouse (OnGUI, before the windows).</summary>
        public void DrawOverlay()
        {
            if (Cursor is not Vector2 at || Layer == null || _ctx.Camera == null || Event.current.type != EventType.Repaint)
                return;
            _dot ??= Texture2D.whiteTexture;
            WorldBrushSettings brush = Layer.Brush;
            float r = brush.Current == WorldBrushSettings.Mode.Look ? 0.3f : brush.Size;
            float inner = r * (1f - brush.Softness);
            DrawRing(at, r, new Color(0.75f, 0.95f, 0.45f, 0.95f), 64);
            if (inner > 0.2f && inner < r - 0.1f)
                DrawRing(at, inner, new Color(0.75f, 0.95f, 0.45f, 0.45f), 48);
            DrawPoint(at, new Color(1f, 1f, 1f, 0.9f), 4f);
        }

        private void DrawRing(Vector2 centre, float radius, Color color, int points)
        {
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                DrawPoint(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, 3f);
            }
        }

        private void DrawPoint(Vector2 tile, Color color, float size)
        {
            float z = GroundZ(tile.x, tile.y), s = _ctx.ArtScale;
            Vector3 world = _ctx.World.ToUnity(new Vector2((tile.x - tile.y) * 22f, (tile.x + tile.y) * 22f - z * 4f) * s);
            Vector3 screen = _ctx.Camera.WorldToScreenPoint(world);
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size), _dot);
            GUI.color = saved;
        }
    }
}
