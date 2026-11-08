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

        // -worldeditor-stresstest: long, wide drags like painting by hand (snow, a big raise, a big smooth), started
        // once the world has settled, with pauses between drags; for the frame timing in the log ([editprofile]).
        private static readonly bool StressTest = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-worldeditor-stresstest", StringComparison.OrdinalIgnoreCase));
        private int _stressStep = -1;
        private float _stressAt = -1f;
        private int _stressX, _stressY;
        private const int StressEnd = 330;

        private void RunStressTest(UI.UiManager ui, WorldLayerClient layer)
        {
            if (!layer.CanEdit)
                return;
            if (_stressAt < 0f)
            {
                _stressAt = Time.unscaledTime + 15f;
                return;
            }
            if (Time.unscaledTime < _stressAt)
                return;
            WorldBrushSettings b = layer.Brush;
            _stressAt = Time.unscaledTime + 0.1f; // 10 strokes a second, the panel's default flow
            _stressStep++;
            int s = _stressStep;
            if (s == 0)
            {
                _stressX = _ctx.Player.X;
                _stressY = _ctx.Player.Y;
                if (ui.Find(UI.WorldEditorWindow.WindowKey) == null)
                    ui.Open(new UI.WorldEditorWindow(layer));
                Debug.Log("[editprofile] stress test: start");
            }
            else if (s <= 120) // drag 1: snow, 6 tiles wide, 30 tiles across
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Ground = 0; b.Material = WorldMaterials.Snow;
                b.Size = 6f; b.Softness = 0.6f; b.Strength = 0.5f;
                layer.Stroke(_stressX - 15f + s * 0.25f, _stressY + 6.5f);
            }
            else if (s == 121)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 1 done");
                _stressAt = Time.unscaledTime + 4f;
            }
            else if (s <= 221) // drag 2: a big raise, slowly across
            {
                b.Current = WorldBrushSettings.Mode.Raise; b.Size = 20f; b.Softness = 0.8f; b.Strength = 0.5f;
                layer.Stroke(_stressX - 8f + (s - 122) * 0.16f, _stressY - 6.5f);
            }
            else if (s == 222)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 2 done");
                _stressAt = Time.unscaledTime + 4f;
            }
            else if (s <= 302) // drag 3: flatten back to the Z 0 plain across both
            {
                b.Current = WorldBrushSettings.Mode.SetZ; b.TargetZ = 0f; b.Size = 10f; b.Softness = 0.5f; b.Strength = 1f;
                layer.Stroke(_stressX - 10f + (s - 223) * 0.25f, _stressY);
            }
            else if (s == 303)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 3 done");
                _stressAt = Time.unscaledTime + 12f;
            }
            else if (s == StressEnd)
                Debug.Log("[editprofile] stress test: end");
        }

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
            else if (_testStep <= 96) // SURFACE: a patch of UO wooden floor beside the player
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Size = 2f; b.Softness = 0.1f; b.Strength = 1f;
                LandTileData[] land = _ctx.Data.TileData.Land;
                b.Ground = (ushort)Math.Max(0, Array.FindIndex(land, l => string.Equals(l.Name?.Trim(), "wooden floor", StringComparison.OrdinalIgnoreCase)));
                layer.Stroke(px - 3.5f, py + 3.5f);
                b.Ground = 0;
            }
            else if (_testStep == 97)
            {
                layer.EndStroke();
                b.Current = WorldBrushSettings.Mode.Raise;
                (ui.Find(UI.WorldEditorWindow.WindowKey) as UI.WorldEditorWindow)?.ShowSurface("wooden floor");
            }
            else if (_testStep <= 101) // OBJECTS: a short stone wall north of the player
            {
                b.Current = WorldBrushSettings.Mode.Object;
                b.ObjectGraphic = StaticNamed("stone wall");
                int x = px + 1 + (_testStep - 98), y = py - 3;
                layer.PlaceObject(x, y, PlaceZ(x, y, 0));
            }
            else if (_testStep == 102) // and a tree west of it
            {
                b.ObjectGraphic = StaticNamed("tree");
                layer.PlaceObject(px - 3, py - 3, PlaceZ(px - 3, py - 3, 0));
            }
            else if (_testStep == 110 && layer.SelectedItem is WorldItem tree) // the tree (just placed, so selected) one tile south
                layer.MoveObject(tree.Serial, tree.X, tree.Y + 1, tree.Z);
            else if (_testStep == 116) // and back (undo the move)
                layer.Undo(false);
            else if (_testStep == TestEnd)
            {
                b.Current = WorldBrushSettings.Mode.Select;
                (ui.Find(UI.WorldEditorWindow.WindowKey) as UI.WorldEditorWindow)?.ShowObjects("walls", "stone wall");
                _ctx.Hud?.Log(null, "World editor self-test: done.");
            }
        }

        private const int TestEnd = 122;

        /// <summary>The first item graphic with this tiledata name (the self-test's objects).</summary>
        private ushort StaticNamed(string name)
        {
            StaticTileData[] items = _ctx.Data.TileData.Statics;
            int g = Array.FindIndex(items, 1, items.Length - 1, s => string.Equals(s.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return (ushort)Math.Max(0, g);
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
            if (SelfTest && _testStep < TestEnd)
                RunSelfTest(ui, layer);
            if (StressTest && _stressStep < StressEnd)
                RunStressTest(ui, layer);

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

            WorldBrushSettings brush = layer.Brush;
            if (brush.ObjectMode && takesKeys)
                ObjectKeys(layer, brush);

            bool overWorld = !ui.IsOverUi(Input.mousePosition) && ui.Dragging == null;
            if (!overWorld)
            {
                if (!Input.GetMouseButton(0))
                {
                    StopPainting();
                    _dragging = 0;
                }
                if (!_painting && _dragging == 0)
                    return;
            }
            Cursor = MouseTile();
            CapturesLeftMouse = true;
            if (Cursor is not Vector2 at)
                return;

            if (brush.ObjectMode)
            {
                StopPainting();
                UpdateObjects(layer, brush, at, overWorld);
                return;
            }
            if (brush.Current == WorldBrushSettings.Mode.Look)
            {
                if (Input.GetMouseButtonDown(0) && layer.HeightAt(at.x, at.y) is float picked)
                {
                    brush.TargetZ = Mathf.Round(picked);
                    brush.Current = WorldBrushSettings.Mode.SetZ;
                    _ctx.Hud?.Log(null, $"World editor: flatten to Z {brush.TargetZ:0}");
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

        // ---- OBJECTS: place, select, drag to move, PageUp / PageDown height, Delete ----

        private uint _dragging;
        private Vector2Int _dragFrom;

        private static int Tile(float v) => Mathf.FloorToInt(v);

        /// <summary>The height an object goes in at a tile: the ground's there, plus the panel's lift.</summary>
        private int PlaceZ(int x, int y, int lift) => Mathf.RoundToInt(GroundZ(x, y)) + lift;

        private void ObjectKeys(WorldLayerClient layer, WorldBrushSettings brush)
        {
            int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
            int dz = Input.GetKeyDown(KeyCode.PageUp) ? step : Input.GetKeyDown(KeyCode.PageDown) ? -step : 0;
            WorldItem sel = layer.SelectedItem;
            if (dz != 0)
            {
                if (brush.Current == WorldBrushSettings.Mode.Select && sel != null)
                    layer.MoveObject(sel.Serial, sel.X, sel.Y, sel.Z + dz);
                else
                    brush.ObjectLift = Mathf.Clamp(brush.ObjectLift + dz, -60, 120);
            }
            if (sel != null && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
                layer.DeleteObject(sel.Serial);
        }

        private void UpdateObjects(WorldLayerClient layer, WorldBrushSettings brush, Vector2 at, bool overWorld)
        {
            int tx = Tile(at.x), ty = Tile(at.y);
            if (brush.Current == WorldBrushSettings.Mode.Object)
            {
                if (Input.GetMouseButtonDown(0) && overWorld && brush.ObjectGraphic != 0)
                    layer.PlaceObject(tx, ty, PlaceZ(tx, ty, brush.ObjectLift));
                return;
            }
            if (Input.GetMouseButtonDown(0) && overWorld)
            {
                uint serial = _entities.PickAt(Input.mousePosition);
                bool isItem = serial != 0 && _ctx.Session.World.Items.TryGetValue(serial, out WorldItem item) && !item.IsMulti;
                brush.Selected = isItem ? serial : 0;
                _dragging = brush.Selected;
                _dragFrom = new Vector2Int(tx, ty);
            }
            if (_dragging != 0 && !Input.GetMouseButton(0))
            {
                // Dropped on another tile: it keeps its height above the ground.
                if ((tx != _dragFrom.x || ty != _dragFrom.y) && _ctx.Session.World.Items.TryGetValue(_dragging, out WorldItem moved))
                {
                    int above = moved.Z - Mathf.RoundToInt(GroundZ(moved.X, moved.Y));
                    layer.MoveObject(_dragging, tx, ty, PlaceZ(tx, ty, above));
                }
                _dragging = 0;
            }
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
            if (Layer == null || _ctx.Camera == null || Event.current.type != EventType.Repaint || !Open)
                return;
            _dot ??= Texture2D.whiteTexture;
            WorldBrushSettings brush = Layer.Brush;
            if (brush.ObjectMode)
            {
                DrawObjectOverlay(brush);
                return;
            }
            if (Cursor is not Vector2 at)
                return;
            float r = brush.Current == WorldBrushSettings.Mode.Look ? 0.3f : brush.Size;
            float inner = r * (1f - brush.Softness);
            DrawRing(at, r, new Color(0.75f, 0.95f, 0.45f, 0.95f), 64);
            if (inner > 0.2f && inner < r - 0.1f)
                DrawRing(at, inner, new Color(0.75f, 0.95f, 0.45f, 0.45f), 48);
            DrawPoint(at, new Color(1f, 1f, 1f, 0.9f), 4f);
        }

        /// <summary>OBJECTS: the selected object's tile outlined, and the object to place (or the one dragged) as a ghost.</summary>
        private void DrawObjectOverlay(WorldBrushSettings brush)
        {
            if (Layer.SelectedItem is WorldItem sel)
                DrawTileOutline(sel.X, sel.Y, sel.Z, new Color(1f, 0.85f, 0.25f, 0.95f));
            if (Cursor is not Vector2 at)
                return;
            int tx = Tile(at.x), ty = Tile(at.y);
            if (brush.Current == WorldBrushSettings.Mode.Object && brush.ObjectGraphic != 0)
            {
                int z = PlaceZ(tx, ty, brush.ObjectLift);
                DrawTileOutline(tx, ty, z, new Color(0.75f, 0.95f, 0.45f, 0.9f));
                DrawGhost(brush.ObjectGraphic, brush.ObjectHue, tx, ty, z, 0.6f);
            }
            else if (_dragging != 0 && _ctx.Session.World.Items.TryGetValue(_dragging, out WorldItem dragged))
            {
                int z = PlaceZ(tx, ty, dragged.Z - Mathf.RoundToInt(GroundZ(dragged.X, dragged.Y)));
                DrawTileOutline(tx, ty, z, new Color(0.75f, 0.95f, 0.45f, 0.9f));
                DrawGhost(dragged.Graphic, dragged.Hue, tx, ty, z, 0.5f);
            }
            else
                DrawTileOutline(tx, ty, Mathf.RoundToInt(GroundZ(tx, ty)), new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>A screen point (GUI, top-left origin) of a world point at height z.</summary>
        private Vector2 ScreenAt(float x, float y, float z)
        {
            float s = _ctx.ArtScale;
            Vector3 world = _ctx.World.ToUnity(new Vector2((x - y) * 22f, (x + y) * 22f - z * 4f) * s);
            Vector3 p = _ctx.Camera.WorldToScreenPoint(world);
            return new Vector2(p.x, UnityEngine.Screen.height - p.y);
        }

        private void DrawTileOutline(int x, int y, int z, Color color)
        {
            Vector2 a = ScreenAt(x, y, z), b = ScreenAt(x + 1, y, z), c = ScreenAt(x + 1, y + 1, z), d = ScreenAt(x, y + 1, z);
            Color saved = GUI.color;
            GUI.color = color;
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                foreach (Vector2 p in new[] { Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), Vector2.Lerp(c, d, t), Vector2.Lerp(d, a, t) })
                    GUI.DrawTexture(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), _dot);
            }
            GUI.color = saved;
        }

        /// <summary>An item's art standing on a tile as the world draws statics (bottom centre on the tile's lower corner).</summary>
        private void DrawGhost(int graphic, int hue, int x, int y, int z, float alpha)
        {
            UI.UiImage img = _ui()?.Art?.Item(graphic, hue);
            if (img?.Texture == null || img.Bitmap == null)
                return;
            Vector2 foot = ScreenAt(x + 1, y + 1, z);
            float px = (ScreenAt(x + 2, y + 2, z).y - foot.y) / 44f; // screen pixels per art pixel
            float w = img.Bitmap.Width * px, h = img.Bitmap.Height * px;
            Color saved = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(foot.x - w * 0.5f, foot.y - h, w, h), img.Texture);
            GUI.color = saved;
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
