using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
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
        private const float W = 372f, H = 726f;

        private readonly WorldLayerClient _layer;
        private static GUIStyle _title, _label, _value, _button, _active, _small, _listItem, _listActive, _field;
        private static Texture2D _bg, _panel, _accent;
        private static Texture2D[] _swatches;

        private bool _surface;               // SURFACE tab (else TERRAIN)
        private bool _objects;               // OBJECTS tab (over the other two)
        private string _objSearch = "";
        private string _category = "walls";
        private Vector2 _categoryScroll, _objectScroll;
        private static Dictionary<string, int[]> _catalog;
        private string _search = "";
        private string _family;              // the chosen UO ground family
        private Vector2 _familyScroll, _tileScroll;
        private static List<(string Name, int[] Graphics)> _families;

        public WorldEditorWindow(WorldLayerClient layer)
        {
            _layer = layer;
            _surface = layer.Brush.Ground != 0;
            _objects = layer.Brush.ObjectMode;
        }

        /// <summary>Opens the OBJECTS tab on a category (the self-test shows the gallery this way).</summary>
        public void ShowObjects(string category, string search = "")
        {
            _objects = true;
            _category = category;
            _objSearch = search ?? "";
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
            // Modes, two rows: shaping, then painting, erasing (back to the original map) and picking a height.
            (string Label, WorldBrushSettings.Mode Mode)[][] rows =
            {
                new[] { ("RAISE", WorldBrushSettings.Mode.Raise), ("LOWER", WorldBrushSettings.Mode.Lower), ("FLAT", WorldBrushSettings.Mode.SetZ), ("SOFT", WorldBrushSettings.Mode.Soft) },
                new[] { ("PAINT", WorldBrushSettings.Mode.Paint), ("ERASE", WorldBrushSettings.Mode.Erase), ("SLOPE", WorldBrushSettings.Mode.Slope), ("LOOK", WorldBrushSettings.Mode.Look) },
            };
            float y = 38f, bw = (Rect.width - 24f - 3 * 4f) / 4f;
            foreach (var row in rows)
            {
                for (int i = 0; i < row.Length; i++)
                    if (GUI.Button(new Rect(12 + i * (bw + 4), y, bw, 24), row[i].Label, b.Current == row[i].Mode ? _active : _button))
                        b.Current = row[i].Mode;
                y += 28f;
            }
            // Brush shape: a circle, a square (a diamond on screen, on the tile grid) or whole tiles (hard edged).
            GUI.Label(new Rect(12, y + 2, 60, 20), "Shape", _label);
            (string Label, BrushShape Shape)[] shapes = { ("CIRCLE", BrushShape.Circle), ("SQUARE", BrushShape.Square), ("TILE", BrushShape.Tile) };
            float sw = (Rect.width - 72f - 12f - 2 * 4f) / 3f;
            for (int i = 0; i < shapes.Length; i++)
                if (GUI.Button(new Rect(72 + i * (sw + 4), y, sw, 22), shapes[i].Label, b.Shape == shapes[i].Shape ? _active : _button))
                    b.Shape = shapes[i].Shape;
            y += 34f;

            // Brush.
            b.Size = b.Shape == BrushShape.Tile
                ? Mathf.Max(0.5f, Mathf.Round(Slider(ref y, "Brush size", b.Size * 2f, 1f, 16f, $"{Mathf.Max(1, Mathf.RoundToInt(b.Size * 2f))} x {Mathf.Max(1, Mathf.RoundToInt(b.Size * 2f))} tiles")) / 2f)
                : Slider(ref y, "Brush size", b.Size, 0.5f, BrushStroke.MaxRadius, $"{b.Size:0.0} tiles");
            b.Softness = Slider(ref y, "Edge softness", b.Softness, 0f, 1f, $"{b.Softness * 100:0}% fade");
            b.Flow = Slider(ref y, "Flow", b.Flow, 1f, 20f, $"{b.Flow:0.0}/sec");
            b.Strength = Slider(ref y, "Strength", b.Strength, 0.05f, 1f, $"{b.Strength * 100:0}%");
            if (b.Current == WorldBrushSettings.Mode.SetZ || b.Current == WorldBrushSettings.Mode.Look)
            {
                // FLAT: the height the ground is pulled to (LOOK picks it from the ground; Z 0 is the plain).
                GUI.Label(new Rect(12, y, 110, 20), "Flatten to", _label);
                // Relative to your height: 0 levels the ground to where you stand (the readout shows the map Z too).
                b.TargetOffset = Mathf.Round(GUI.HorizontalSlider(new Rect(122, y + 6, Rect.width - 122 - 132, 16), b.TargetOffset, -60f, 60f));
                GUI.Label(new Rect(Rect.width - 132, y, 76, 20), $"{b.TargetOffset:+0;-0;0} (Z {b.TargetZ:0})", _value);
                if (GUI.Button(new Rect(Rect.width - 52, y, 40, 20), "MY Z", b.TargetOffset == 0f ? _active : _button))
                {
                    b.TargetOffset = 0f;
                    b.Current = WorldBrushSettings.Mode.SetZ;
                }
                y += 28f;
            }
            else
                y += 28f;

            // Ground palettes.
            float hb = (Rect.width - 24f - 6f) / 2f, tb = (Rect.width - 24f - 12f) / 3f;
            if (GUI.Button(new Rect(12, y, tb, 24), "TERRAIN", !_objects && !_surface ? _active : _button))
            {
                _objects = _surface = false;
                if (b.ObjectMode)
                    b.Current = WorldBrushSettings.Mode.Paint;
            }
            if (GUI.Button(new Rect(18 + tb, y, tb, 24), "SURFACE", !_objects && _surface ? _active : _button))
            {
                _objects = false;
                _surface = true;
                if (b.ObjectMode)
                    b.Current = WorldBrushSettings.Mode.Paint;
            }
            if (GUI.Button(new Rect(24 + 2 * tb, y, tb, 24), "OBJECTS", _objects ? _active : _button))
            {
                _objects = true;
                if (!b.ObjectMode)
                    b.Current = b.ObjectGraphic != 0 ? WorldBrushSettings.Mode.Object : WorldBrushSettings.Mode.Select;
            }
            y += 32f;
            float paletteHeight = 300f;
            if (_objects)
                DrawObjects(b, new Rect(12, y, Rect.width - 24, paletteHeight));
            else if (_surface)
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
            GUI.Label(new Rect(12, y, Rect.width - 24, 40), b.Current == WorldBrushSettings.Mode.Slope
                    ? "SLOPE: press on the ground at one end of the ramp, drag to the other end, let go: an even slope between the two ground heights, as wide as the brush."
                    : b.ObjectMode
                    ? "PLACE: click the ground. SELECT: click an object, drag it to move. PgUp/PgDn height, Delete removes. Right mouse walks."
                    : "Hold the left mouse on the ground to use the brush; the right mouse still walks. Every stroke is saved on the server by itself.",
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

        // ---- OBJECTS ----

        private static readonly string[] Categories =
            { "walls", "floors", "roofs", "doors", "windows", "stairs", "fences", "trees", "rocks", "plants", "furniture", "lights", "decor", "all" };

        private static bool Has(string name, params string[] words)
        {
            foreach (string w in words)
                if (name.Contains(w))
                    return true;
            return false;
        }

        /// <summary>Which category an item goes in (tiledata flags first, then its name), or null to leave it out.</summary>
        private static string CategoryOf(in StaticTileData t)
        {
            string n = (t.Name ?? "").Trim().ToLowerInvariant();
            TileFlags f = t.Flags;
            if (n.Length == 0 || (f & (TileFlags.Wearable | TileFlags.Weapon | TileFlags.Armor | TileFlags.Internal)) != 0)
                return null;
            if ((f & TileFlags.Door) != 0 || Has(n, "door", "portcullis"))
                return "doors";
            if ((f & TileFlags.Window) != 0 || Has(n, "window"))
                return "windows";
            if ((f & TileFlags.Roof) != 0 || Has(n, "roof", "shingle", "thatch"))
                return "roofs";
            if ((f & (TileFlags.StairBack | TileFlags.StairRight)) != 0 || Has(n, "stair", "steps", "ladder"))
                return "stairs";
            if (Has(n, "fence", "railing", "gate", "palisade", "picket"))
                return "fences";
            if (Has(n, "floor", "paving", "flagstone", "tile", "carpet", "rug", "boardwalk", "plank", "cobblestone"))
                return "floors";
            if ((f & TileFlags.Wall) != 0 || Has(n, "wall", "pillar", "column", "arch", "post", "battlement", "crenel"))
                return "walls";
            if (Has(n, "tree", "trunk", "leaves", "stump", "log", "branch", "palm", "willow", "cedar", "oak", "yew"))
                return "trees";
            if (Has(n, "rock", "boulder", "stone", "stalag", "crystal", "cave"))
                return "rocks";
            if ((f & TileFlags.Foliage) != 0 || Has(n, "plant", "flower", "grass", "bush", "shrub", "fern", "weed", "mushroom",
                    "cactus", "vine", "reed", "lily", "hedge", "ivy", "moss", "bulrush", "cattail", "poppy", "rose", "orchid"))
                return "plants";
            if ((f & TileFlags.LightSource) != 0 || Has(n, "lamp", "lantern", "candle", "torch", "brazier", "sconce", "chandelier", "fire"))
                return "lights";
            if (Has(n, "chair", "table", "bench", "bed", "chest", "shelf", "stool", "throne", "counter", "armoire", "dresser",
                    "bookcase", "drawer", "desk", "cabinet", "crate", "barrel", "cupboard", "wardrobe", "loom", "anvil", "forge",
                    "oven", "stove", "box", "keg", "vanity", "couch", "trough"))
                return "furniture";
            return "decor";
        }

        private static Dictionary<string, int[]> Catalog(GumpArt art)
        {
            if (_catalog != null)
                return _catalog;
            StaticTileData[] items = art.Data.TileData.Statics;
            var lists = Categories.ToDictionary(c => c, _ => new List<int>());
            for (int g = 1; g < items.Length; g++)
            {
                string c = CategoryOf(items[g]);
                if (c == null)
                    continue;
                lists[c].Add(g);
                lists["all"].Add(g);
            }
            _catalog = lists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
            return _catalog;
        }

        private static string ItemName(GumpArt art, int g) => (art.Data.TileData.GetStatic(g).Name ?? "").Trim();

        /// <summary>OBJECTS: place real static objects (walls, floors, trees, furniture...) and edit placed ones.</summary>
        private void DrawObjects(WorldBrushSettings b, Rect area)
        {
            Dictionary<string, int[]> catalog = Catalog(Art);
            float half = (area.width - 6f) / 2f;
            if (GUI.Button(new Rect(area.x, area.y, half, 22), "PLACE", b.Current == WorldBrushSettings.Mode.Object ? _active : _button))
                b.Current = WorldBrushSettings.Mode.Object;
            if (GUI.Button(new Rect(area.x + half + 6, area.y, half, 22), "SELECT / MOVE", b.Current == WorldBrushSettings.Mode.Select ? _active : _button))
                b.Current = WorldBrushSettings.Mode.Select;
            float y = area.y + 28f;
            GUI.Label(new Rect(area.x, y + 2, 50, 20), "Search", _label);
            _objSearch = GUI.TextField(new Rect(area.x + 52, y, area.width - 52, 20), _objSearch ?? "", 40, _field);
            string q = _objSearch.Trim().ToLowerInvariant();
            y += 26f;

            // Categories on the left.
            float listW = 96f, gridH = area.height - (y - area.y) - 58f;
            _categoryScroll = GUI.BeginScrollView(new Rect(area.x, y, listW, gridH), _categoryScroll, new Rect(0, 0, listW - 16, Categories.Length * 18f));
            for (int i = 0; i < Categories.Length; i++)
            {
                string c = Categories[i];
                if (GUI.Button(new Rect(0, i * 18f, listW - 16, 18), c, c == _category ? _listActive : _listItem))
                {
                    _category = c;
                    _objectScroll = Vector2.zero;
                }
            }
            GUI.EndScrollView();

            // The category's objects on the right (search runs across the category; in "all", across everything).
            int[] all = catalog.TryGetValue(_category ?? "", out int[] list) ? list : catalog["all"];
            int[] shown = q.Length == 0 ? all : all.Where(g => ItemName(Art, g).ToLowerInvariant().Contains(q) || $"0x{g:x4}" == q).ToArray();
            float gx = area.x + listW + 6, gw = area.width - listW - 6;
            const float cell = 54f;
            int cols = Math.Max(1, (int)((gw - 16) / cell));
            var grid = new Rect(0, 0, gw - 16, ((shown.Length + cols - 1) / cols) * cell);
            _objectScroll = GUI.BeginScrollView(new Rect(gx, y, gw, gridH), _objectScroll, grid);
            for (int i = 0; i < shown.Length; i++)
            {
                var r = new Rect((i % cols) * cell, (i / cols) * cell, cell - 4, cell - 4);
                if (r.yMax < _objectScroll.y || r.y > _objectScroll.y + gridH)
                    continue; // off the visible part: no texture made for it
                GUI.DrawTexture(r, b.ObjectGraphic == shown[i] ? _accent : _panel);
                UiImage img = Art.Item(shown[i]);
                if (img?.Texture != null)
                    GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), img.Texture, ScaleMode.ScaleToFit);
                if (GUI.Button(r, new GUIContent("", $"{ItemName(Art, shown[i])} 0x{shown[i]:X4}"), GUIStyle.none))
                {
                    b.ObjectGraphic = (ushort)shown[i];
                    b.Current = WorldBrushSettings.Mode.Object;
                }
            }
            GUI.EndScrollView();
            y += gridH + 4f;

            // What is chosen, and the selected object's controls.
            string chosen = b.ObjectGraphic != 0 ? $"{ItemName(Art, b.ObjectGraphic)} (0x{b.ObjectGraphic:X4})" : "nothing chosen";
            GUI.Label(new Rect(area.x, y, area.width, 18), $"Place: {chosen}   height +{b.ObjectLift}", _small);
            y += 18f;
            WorldItem sel = _layer.SelectedItem;
            if (sel == null)
            {
                GUI.Label(new Rect(area.x, y, area.width, 34), "Nothing selected. SELECT, then click a placed object (statics only).", _small);
                return;
            }
            GUI.Label(new Rect(area.x, y, 150, 34), $"{ItemName(Art, sel.Graphic)} at {sel.X},{sel.Y},{sel.Z}", _small);
            string[] nudges = { "N", "S", "W", "E", "UP", "DN", "DEL" };
            float nb = (area.width - 150f - 6 * 3f) / nudges.Length;
            for (int i = 0; i < nudges.Length; i++)
            {
                if (!GUI.Button(new Rect(area.x + 150 + i * (nb + 3), y, nb, 30), nudges[i], _button))
                    continue;
                if (i == 6)
                {
                    _layer.DeleteObject(sel.Serial);
                    continue;
                }
                int dx = i == 2 ? -1 : i == 3 ? 1 : 0, dy = i == 0 ? -1 : i == 1 ? 1 : 0, dz = i == 4 ? 1 : i == 5 ? -1 : 0;
                _layer.MoveObject(sel.Serial, sel.X + dx, sel.Y + dy, sel.Z + dz);
            }
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
