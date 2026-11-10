using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The whole facet from radarcol colors, one pixel per tile with the land's relief shaded in, built on a background
    /// thread with its own map reader and cached under Library/UOClient; mipmapped, so it stays smooth zoomed out. Over it
    /// the shard's places (Theme/worldmap/markers.csv, made by Tools/make_worldmap_markers.py): town names, moongates,
    /// dungeon entrances and, zoomed in, the shops, each a badge with its trade's item art, named on hover. Drag to pan,
    /// scroll to zoom; it follows the player until dragged, and Center brings it back.
    /// </summary>
    public sealed class WorldMapWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        internal const int TilesPerPixel = 1;
        private static readonly string[] Facets = { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "Ter Mur" };

        // One image per facet for the whole session (closing the window keeps it).
        private static Texture2D _texture;
        private static int _textureMap = -1;
        private static Task<Bitmap32> _building;
        private static int _buildingMap = -1, _failedMap = -1;

        private readonly UOData _data;
        private readonly string _cacheDir;
        private float _zoom = 1f;         // screen pixels per map pixel
        private bool _shops = true;
        private Vector2 _center;          // in tiles
        private bool _follow = true;
        private Vector2 _dragFrom;
        private bool _dragging;

        public WorldMapWindow(UOData data, string cacheDir)
        {
            _data = data;
            _cacheDir = cacheDir;
            Rect = new Rect(200, 90, 760, 600);
        }

        /// <summary>Screen pixels per tile (1/8 to 8).</summary>
        public float Zoom
        {
            get => _zoom;
            set => _zoom = Mathf.Clamp(value, 0.125f, 8f);
        }

        public override string Key => "worldmap";

        /// <summary>True once the facet's image has been drawn (for the capture tour).</summary>
        public static bool Ready(int map) => _texture != null && _textureMap == map;

        /// <summary>The facet's image if it has been drawn this session (one pixel per <see cref="TilesPerPixel"/> tiles), else null.</summary>
        internal static Texture2D Cached(int map) => Ready(map) ? _texture : null;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            int map = Session.World.Map;
            var p = Session.World.Player;
            string facet = map >= 0 && map < Facets.Length ? Facets[map] : $"Map {map}";
            Text(new Rect(14, 8, 300, 20), $"{facet}   {(p != null ? $"{p.X}, {p.Y}" : "")}", new Color(1f, 0.9f, 0.6f), 14, TextAnchor.UpperLeft, true);
            if (GUI.Button(new Rect(Rect.width - 90, 6, 76, 22), "Center"))
                _follow = true;
            if (GUI.Button(new Rect(Rect.width - 172, 6, 76, 22), _shops ? "Shops: on" : "Shops: off"))
                _shops = !_shops;

            var view = new Rect(12, 34, Rect.width - 24, Rect.height - 46);
            _viewSize = view.size;
            HandleResize();
            Texture2D tex = Image(map);
            if (tex == null)
            {
                Text(new Rect(view.x + 10, view.y + 10, 460, 20), _failedMap == map ? "This facet's map files could not be read." : "Drawing the map… (the first time takes a few seconds)", new Color(0.85f, 0.82f, 0.72f));
                return;
            }
            if (_follow && p != null)
                _center = new Vector2(p.X, p.Y);

            HandleMouse(view);

            GUI.DrawTexture(view, Texture2D.blackTexture);
            DrawTurned(view, tex);

            HoveredText = null;
            GUI.BeginGroup(view);
            DrawMarkers(map, new Rect(0, 0, view.width, view.height));
            GUI.EndGroup();
            DrawGrip();

            if (p != null)
            {
                Vector2 at = view.position + ToLocal(p.X, p.Y);
                if (view.Contains(at))
                {
                    GUI.color = new Color(0.3f, 0.65f, 1f);
                    GUI.DrawTexture(new Rect(at.x - 3, at.y - 3, 7, 7), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    Text(new Rect(at.x + 6, at.y - 9, 160, 18), p.Name ?? "You", new Color(0.7f, 0.85f, 1f), 12);
                }
            }
        }

        // ---- the map turned as the game sees it ----

        // The game draws a tile as a diamond: the map's x runs down-right and y down-left on screen, a 45° turn (UO's
        // diamonds are 44 by 44, so the turn needs no squash). The map is drawn that way, north up and to the right.
        private const float Turn = 0.70710678f; // cos 45° = sin 45°
        private Vector2 _viewSize;

        /// <summary>A tile's position in the view (view-local pixels), the map turned 45° about the centre tile.</summary>
        private Vector2 ToLocal(float tx, float ty)
        {
            float rx = tx - _center.x, ry = ty - _center.y, k = _zoom * Turn;
            return new Vector2(_viewSize.x / 2f + (rx - ry) * k, _viewSize.y / 2f + (rx + ry) * k);
        }

        /// <summary>The tile at a view-local point (the inverse of <see cref="ToLocal"/>).</summary>
        private Vector2 ToTile(Vector2 local)
        {
            float k = _zoom * Turn;
            float dx = (local.x - _viewSize.x / 2f) / k, dy = (local.y - _viewSize.y / 2f) / k;
            return _center + new Vector2((dx + dy) / 2f, (dy - dx) / 2f);
        }

        private static Material _turnMaterial;

        /// <summary>
        /// The facet image laid into the view turned 45°: one quad over the view whose corners take the map at the tiles
        /// under them (an affine turn, so the four corners say it exactly). GL in the repaint, in the GUI's own space.
        /// </summary>
        private void DrawTurned(Rect view, Texture2D tex)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            if (_turnMaterial == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null)
                    return;
                _turnMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            _turnMaterial.mainTexture = tex;
            _turnMaterial.SetPass(0);
            Vector2 Uv(Vector2 local)
            {
                Vector2 t = ToTile(local) / TilesPerPixel;
                return new Vector2(t.x / tex.width, 1f - t.y / tex.height);
            }
            GL.PushMatrix(); // IMGUI's repaint already has the GUI matrix and the window's offset loaded
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            Vector2[] corners = { Vector2.zero, new Vector2(view.width, 0), view.size, new Vector2(0, view.height) };
            foreach (Vector2 c in corners)
            {
                Vector2 uv = Uv(c);
                GL.TexCoord2(uv.x, uv.y);
                GL.Vertex3(view.x + c.x, view.y + c.y, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        // ---- resizing ----

        private const float Grip = 18f;
        private static readonly Vector2 MinSize = new Vector2(360, 280);
        private bool _resizing;
        private Vector2 _resizeGrab;

        /// <summary>The bottom-right corner drags the window's size (the rest of the frame still moves it).</summary>
        private void HandleResize()
        {
            Event e = Event.current;
            // The grip holds the mouse while dragged (hotControl), so the drag keeps coming once it leaves the window.
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var grip = new Rect(Rect.width - Grip, Rect.height - Grip, Grip, Grip);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && grip.Contains(e.mousePosition):
                    _resizing = true;
                    _resizeGrab = Rect.size - e.mousePosition;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseDrag when _resizing && GUIUtility.hotControl == id:
                    float s = Ui.Scale;
                    var max = new Vector2(Screen.width / s - Rect.x, Screen.height / s - Rect.y);
                    ResizeTo = Vector2.Max(MinSize, Vector2.Min(max, e.mousePosition + _resizeGrab));
                    e.Use();
                    break;
                case EventType.MouseUp when _resizing:
                    _resizing = false;
                    if (GUIUtility.hotControl == id)
                        GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        /// <summary>Three short gilt diagonals in the bottom-right corner: the resize grip.</summary>
        private void DrawGrip()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            GUI.color = new Color(0.85f, 0.7f, 0.4f, 0.9f);
            for (int i = 0; i < 3; i++)
            {
                float o = 5f + i * 4f;
                Matrix4x4 m = GUI.matrix;
                var pivot = new Vector2(Rect.width - o, Rect.height - o);
                GUIUtility.RotateAroundPivot(-45f, pivot);
                GUI.DrawTexture(new Rect(pivot.x - o * 0.7f, pivot.y - 0.75f, o * 1.4f, 1.5f), Texture2D.whiteTexture);
                GUI.matrix = m;
            }
            GUI.color = Color.white;
        }

        private void HandleMouse(Rect view)
        {
            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                _zoom = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.25f : 0.8f), 0.125f, 8f);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0 && view.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragFrom = e.mousePosition;
                e.Use(); // the map pans instead of the window moving
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                _center -= ToTile(e.mousePosition - _dragFrom + _viewSize / 2f) - _center; // the drag turned as the map is
                _dragFrom = e.mousePosition;
                _follow = false;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _dragging)
            {
                _dragging = false;
                e.Use();
            }
        }

        /// <summary>The facet's image: ready, or being built (null meanwhile).</summary>
        private Texture2D Image(int map)
        {
            if (_texture != null && _textureMap == map)
                return _texture;
            if (_failedMap == map)
                return null; // drawing this facet failed once (missing files?): don't retry every frame
            if (_building == null || _buildingMap != map)
            {
                _buildingMap = map;
                string dir = _data.Directory, cache = _cacheDir;
                ushort[] colors = _data.RadarColors;
                _building = Task.Run(() => Build(dir, map, colors, cache));
                return null;
            }
            if (!_building.IsCompleted)
                return null;
            Bitmap32 bmp = _building.Status == TaskStatus.RanToCompletion ? _building.Result : null;
            if (bmp == null)
                Debug.LogWarning($"UOClient: could not draw the world map for map {map}: {_building.Exception?.GetBaseException().Message}");
            _building = null;
            if (bmp == null)
            {
                _failedMap = map;
                return null;
            }
            if (_texture != null)
                UnityEngine.Object.Destroy(_texture);
            // Mipmapped (trilinear), so a zoomed-out map is smooth instead of shimmering.
            _texture = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, true)
            {
                name = $"world map {map}", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4
            };
            var texels = new Color32[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                int from = y * bmp.Width, to = (bmp.Height - 1 - y) * bmp.Width; // texture rows run bottom up
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint c = bmp.Pixels[from + x];
                    texels[to + x] = new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));
                }
            }
            _texture.SetPixels32(texels);
            _texture.Apply(true, true); // no CPU copy kept: the facet is large
            _textureMap = map;
            return _texture;
        }

        private static Bitmap32 Build(string dataDir, int map, ushort[] colors, string cacheDir)
        {
            // Cached per facet and map file version (size and time), so map edits redraw it.
            string mapFile = File.Exists(Path.Combine(dataDir, $"map{map}LegacyMUL.uop")) ? Path.Combine(dataDir, $"map{map}LegacyMUL.uop") : Path.Combine(dataDir, $"map{map}.mul");
            string staticsFile = Path.Combine(dataDir, $"statics{map}.mul");
            long stamp = 0;
            foreach (string f in new[] { mapFile, staticsFile })
                if (File.Exists(f))
                    stamp ^= new FileInfo(f).Length * 31 ^ File.GetLastWriteTimeUtc(f).Ticks;
            string cache = Path.Combine(cacheDir, $"worldmap-{map}-{stamp:X}-tile.png"); // one pixel per tile, shaded
            try
            {
                if (File.Exists(cache))
                    return Png.Read(cache);
            }
            catch
            {
                // a bad cache file is just rebuilt
            }

            using var m = new MapFile(dataDir, map);
            int w = m.Width / TilesPerPixel, h = m.Height / TilesPerPixel;
            var bmp = new Bitmap32(w, h);
            var landZ = new sbyte[w * h];
            var topZ = new int[64];
            var cell = new uint[64];
            for (int bx = 0; bx < m.BlockWidth; bx++)
                for (int by = 0; by < m.BlockHeight; by++)
                {
                    LandCell[] land = m.ReadLandBlock(bx, by);
                    for (int i = 0; i < 64; i++)
                    {
                        int g = land[i].Graphic & 0x3FFF;
                        cell[i] = Color16.ToArgb32(g < colors.Length ? colors[g] : (ushort)0) | 0xFF000000;
                        topZ[i] = land[i].Z;
                        int lx = bx * 8 + (i & 7), ly = by * 8 + (i >> 3);
                        if (lx < w && ly < h)
                            landZ[ly * w + lx] = land[i].Z;
                    }
                    foreach (StaticCell s in m.ReadStaticBlock(bx, by))
                    {
                        int i = s.Y * 8 + s.X, ci = 0x4000 + s.Graphic;
                        if (s.Z >= topZ[i] && ci < colors.Length)
                        {
                            topZ[i] = s.Z;
                            cell[i] = Color16.ToArgb32(colors[ci]) | 0xFF000000;
                        }
                    }
                    for (int yy = 0; yy < 8; yy += TilesPerPixel)
                        for (int xx = 0; xx < 8; xx += TilesPerPixel)
                        {
                            int px = (bx * 8 + xx) / TilesPerPixel, py = (by * 8 + yy) / TilesPerPixel;
                            if (px < w && py < h)
                                bmp.Pixels[py * w + px] = cell[yy * 8 + xx];
                        }
                }
            Shade(bmp, landZ);
            try
            {
                Directory.CreateDirectory(cacheDir);
                Png.Write(cache, bmp);
            }
            catch
            {
                // no cache this time
            }
            return bmp;
        }

        /// <summary>
        /// The land's relief: lit from the top left (the way the game's art is), a tile higher than its upper-left
        /// neighbour lighter, a lower one darker, gently, so hills and valleys read without hiding the colours.
        /// </summary>
        private static void Shade(Bitmap32 bmp, sbyte[] z)
        {
            int w = bmp.Width, h = bmp.Height;
            uint[] px = bmp.Pixels;
            for (int y = h - 1; y >= 1; y--)
                for (int x = w - 1; x >= 1; x--)
                {
                    int i = y * w + x;
                    int d = z[i] - z[i - w - 1];
                    if (d == 0)
                        continue;
                    float f = Mathf.Clamp(1f + d * 0.035f, 0.72f, 1.3f);
                    uint p = px[i];
                    uint C(int shift) => (uint)Mathf.Min(255, Mathf.RoundToInt(((p >> shift) & 0xFF) * f)) << shift;
                    px[i] = (p & 0xFF000000) | C(16) | C(8) | C(0);
                }
        }

        // ---- markers ----

        private readonly struct Marker
        {
            public readonly int Map, X, Y;
            public readonly string Kind, Label;

            public Marker(int map, int x, int y, string kind, string label)
            {
                Map = map; X = x; Y = y; Kind = kind; Label = label;
            }
        }

        private static Marker[] _markers;

        /// <summary>The item art each kind of place is shown with, and the order they claim room (earlier wins).</summary>
        private static readonly (string Kind, int Graphic)[] Icons =
        {
            ("moongate", 0x0F6C), ("bank", 0x0EEF), ("healer", 0x0E21), ("dungeon", 0x1F18), ("inn", 0x09EF),
            ("mage", 0x0EFA), ("alchemist", 0x0F0B), ("smith", 0x0FAF), ("provisioner", 0x0E3F), ("stable", 0x2120),
            ("tailor", 0x0F9D), ("food", 0x103B), ("docks", 0x0DC0), ("map", 0x14EB), ("bowyer", 0x13B2),
            ("tinker", 0x1EBC), ("carpenter", 0x1028), ("jeweler", 0x108A), ("scribe", 0x0FBF), ("bard", 0x0EB3),
            ("miner", 0x0E86), ("warrior", 0x0F5E),
        };

        private const float Badge = 28f, ShopsFromZoom = 1.5f;
        private static Texture2D _badge;
        private static readonly System.Collections.Generic.Dictionary<int, Rect> ArtBounds = new System.Collections.Generic.Dictionary<int, Rect>();
        private readonly System.Collections.Generic.List<Rect> _taken = new System.Collections.Generic.List<Rect>();
        private GUIStyle _townStyle;

        private static Marker[] Markers()
        {
            if (_markers != null)
                return _markers;
            var list = new System.Collections.Generic.List<Marker>();
            string path = Path.Combine(GameContext.ProjectRoot, "Theme", "worldmap", "markers.csv");
            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    string[] f = line.Split(new[] { ',' }, 5);
                    if (f.Length == 5 && int.TryParse(f[0], out int m) && int.TryParse(f[1], out int x) && int.TryParse(f[2], out int y))
                        list.Add(new Marker(m, x, y, f[3], f[4].Trim('"')));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"UOClient: world map markers: {ex.Message}");
            }
            // Towns first (their names go under everything), then by the icons' order.
            list.Sort((a, b) => Rank(a.Kind).CompareTo(Rank(b.Kind)));
            return _markers = list.ToArray();
        }

        private static int Rank(string kind)
        {
            if (kind == "town")
                return -1;
            for (int i = 0; i < Icons.Length; i++)
                if (Icons[i].Kind == kind)
                    return i;
            return Icons.Length;
        }

        private static int GraphicOf(string kind)
        {
            foreach (var (k, g) in Icons)
                if (k == kind)
                    return g;
            return 0;
        }

        /// <summary>The towns' names, then each place's badge where there is room for it; the one under the mouse is named.</summary>
        private void DrawMarkers(int map, Rect view)
        {
            _taken.Clear();
            _towns.Clear();
            Vector2 mouse = Event.current.mousePosition;
            Marker? hovered = null;
            _townStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            if (PaperdollSkin.SerifFont is Font serif)
                _townStyle.font = serif;
            _townStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(13f + _zoom * 2f, 13f, 20f));
            foreach (Marker mk in Markers())
            {
                if (mk.Map != map)
                    continue;
                Vector2 at = ToLocal(mk.X, mk.Y);
                if (at.x < -60 || at.y < -30 || at.x > view.width + 60 || at.y > view.height + 30)
                    continue;
                if (mk.Kind == "town")
                {
                    _towns.Add((mk.Label, at)); // drawn last, over the badges
                    continue;
                }
                bool shop = mk.Kind != "moongate" && mk.Kind != "dungeon";
                if (shop && (!_shops || _zoom < ShopsFromZoom))
                    continue;
                var box = new Rect(at.x - Badge / 2f, at.y - Badge / 2f, Badge, Badge);
                bool clash = false;
                foreach (Rect t in _taken)
                    if (t.Overlaps(box))
                    {
                        clash = true;
                        break;
                    }
                if (clash)
                    continue;
                _taken.Add(box);
                DrawBadge(box, GraphicOf(mk.Kind), mk.Kind);
                if (box.Contains(mouse))
                    hovered = mk;
            }
            // Town names over everything, lifted clear of the middle of town (where the player's dot often is).
            foreach (var (name, at) in _towns)
                Shadowed(new Rect(at.x - 100, at.y - 34, 200, 24), name, _townStyle, new Color(1f, 0.88f, 0.55f));
            if (hovered is Marker h)
                HoveredText = h.Label;
        }

        private readonly System.Collections.Generic.List<(string Name, Vector2 At)> _towns = new System.Collections.Generic.List<(string, Vector2)>();

        /// <summary>A round dark badge with a thin gilt rim (blue for a moongate, red for a dungeon) and the item art in it.</summary>
        private void DrawBadge(Rect box, int graphic, string kind)
        {
            _badge ??= BadgeTexture();
            Color rim = kind == "moongate" ? new Color(0.55f, 0.7f, 1f) : kind == "dungeon" ? new Color(0.85f, 0.35f, 0.3f) : new Color(0.85f, 0.7f, 0.4f);
            GUI.color = rim;
            GUI.DrawTexture(box, _badge);
            GUI.color = Color.white;
            UiImage art = graphic > 0 ? Art.Item(graphic) : null;
            if (art == null)
                return;
            Rect b = Bounds(graphic, art);
            float room = Badge - 8f, scale = Mathf.Min(room / b.width, room / b.height);
            var dst = new Rect(box.center.x - b.width * scale / 2f, box.center.y - b.height * scale / 2f, b.width * scale, b.height * scale);
            var uv = new Rect(b.x / art.Width, 1f - (b.y + b.height) / art.Height, b.width / art.Width, b.height / art.Height);
            GUI.DrawTextureWithTexCoords(dst, art.Texture, uv);
        }

        /// <summary>Where an item's art actually has pixels (UO art has empty room round it).</summary>
        private static Rect Bounds(int graphic, UiImage art)
        {
            if (ArtBounds.TryGetValue(graphic, out Rect r))
                return r;
            Bitmap32 bmp = art.Bitmap;
            int x0 = bmp.Width, y0 = bmp.Height, x1 = -1, y1 = -1;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if ((bmp[x, y] >> 24) != 0)
                    {
                        x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
                    }
            r = x1 < 0 ? new Rect(0, 0, bmp.Width, bmp.Height) : new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            ArtBounds[graphic] = r;
            return r;
        }

        /// <summary>The badge: a soft-edged dark disc with a light rim (tinted by GUI.color, the rim only reads the tint).</summary>
        private static Texture2D BadgeTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float edge = Mathf.Clamp01((1f - d) * n / 2f);           // anti-aliased outside
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) * 12f); // a band near the edge
                    var inside = new Color(0.08f, 0.07f, 0.06f, 0.88f);
                    Color c = Color.Lerp(inside, Color.white, rim);
                    // The fill keeps its own dark colour under the tint: tinting multiplies, and dark stays dark.
                    c.a *= edge;
                    px[y * n + x] = c;
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Color saved = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x - 1, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + 1, r.y - 1, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = saved;
        }
    }
}
