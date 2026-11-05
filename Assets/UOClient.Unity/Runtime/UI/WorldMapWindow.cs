using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The whole facet from radarcol colors (one pixel per 2x2 tiles), built on a background thread with
    /// its own map reader and cached under Library/UOClient. Drag to pan, scroll to zoom; it follows the
    /// player until dragged, and Center brings it back.
    /// </summary>
    public sealed class WorldMapWindow : UiWindow
    {
        internal const int TilesPerPixel = 2;
        private static readonly string[] Facets = { "Felucca", "Trammel", "Ilshenar", "Malas", "Tokuno", "Ter Mur" };

        // One image per facet for the whole session (closing the window keeps it).
        private static Texture2D _texture;
        private static int _textureMap = -1;
        private static Task<Bitmap32> _building;
        private static int _buildingMap = -1, _failedMap = -1;

        private readonly UOData _data;
        private readonly string _cacheDir;
        private float _zoom = 1f;         // screen pixels per map pixel
        private Vector2 _center;          // in tiles
        private bool _follow = true;
        private Vector2 _dragFrom;
        private bool _dragging;

        public WorldMapWindow(UOData data, string cacheDir)
        {
            _data = data;
            _cacheDir = cacheDir;
            Rect = new Rect(200, 90, 560, 470);
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

            var view = new Rect(12, 34, Rect.width - 24, Rect.height - 46);
            Texture2D tex = Image(map);
            if (tex == null)
            {
                Text(new Rect(view.x + 10, view.y + 10, 460, 20), _failedMap == map ? "This facet's map files could not be read." : "Drawing the map… (the first time takes a few seconds)", new Color(0.85f, 0.82f, 0.72f));
                return;
            }
            if (_follow && p != null)
                _center = new Vector2(p.X, p.Y);

            HandleMouse(view);

            // Visible part of the image, in map pixels.
            float vw = view.width / _zoom, vh = view.height / _zoom;
            float cx = _center.x / TilesPerPixel, cy = _center.y / TilesPerPixel;
            var src = new Rect(cx - vw / 2, cy - vh / 2, vw, vh);
            GUI.DrawTexture(view, Texture2D.blackTexture);
            GUI.DrawTextureWithTexCoords(view, tex,
                new Rect(src.x / tex.width, 1f - (src.y + src.height) / tex.height, src.width / tex.width, src.height / tex.height));

            if (p != null)
            {
                Vector2 at = new Vector2(view.x + (p.X / (float)TilesPerPixel - src.x) * _zoom, view.y + (p.Y / (float)TilesPerPixel - src.y) * _zoom);
                if (view.Contains(at))
                {
                    GUI.color = new Color(0.3f, 0.65f, 1f);
                    GUI.DrawTexture(new Rect(at.x - 3, at.y - 3, 7, 7), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    Text(new Rect(at.x + 6, at.y - 9, 160, 18), p.Name ?? "You", new Color(0.7f, 0.85f, 1f), 12);
                }
            }
        }

        private void HandleMouse(Rect view)
        {
            Event e = Event.current;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition))
            {
                _zoom = Mathf.Clamp(_zoom * (e.delta.y < 0 ? 1.25f : 0.8f), 0.25f, 8f);
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
                _center -= (e.mousePosition - _dragFrom) / _zoom * TilesPerPixel;
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
            _texture = UnityTextures.ToTexture(bmp, $"world map {map}");
            _texture.filterMode = FilterMode.Bilinear;
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
            string cache = Path.Combine(cacheDir, $"worldmap-{map}-{stamp:X}.png");
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
    }
}
