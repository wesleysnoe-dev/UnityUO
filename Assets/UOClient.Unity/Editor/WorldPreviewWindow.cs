using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Editor
{
    /// <summary>
    /// Renders a map region in the editor with the art pipeline's compositor: original art, baked
    /// per-placement HD art, or per-graphic HD sprites. Drag to pan, scroll to zoom, click a pixel
    /// to see which placement drew it. This is a preview of the art, not the game renderer.
    /// </summary>
    public sealed class WorldPreviewWindow : EditorWindow
    {
        // v2: the default moved to the game's data setting; an older saved EA path is not reused.
        private const string PathPrefKey = "UOClient.UOPath.v2";
        private const string ArtDirPrefKey = "UOClient.WorldPreview.ArtDir";
        private static string DefaultPath => UOClient.Unity.ClientSettingsStore.ReadSettingsFile().ResolvedDataPath;
        private const int MaxTiles = 128 * 128;

        private enum ArtSource { Original, Baked, Sprites }

        private string _uoPath;
        private string _artDir;
        private ArtSource _source = ArtSource.Baked;
        private int _map;
        private int _x = 1460, _y = 1664, _width = 14, _height = 14;
        private int _scale = 2;
        private bool _roofs = true;
        private bool _coverage;

        private UOData _data;
        private Composite _composite;
        private Texture2D _texture;
        private string _status = "";
        private string _picked = "";

        private float _zoom = 1f;
        private Vector2 _pan;
        private Vector2 _mouseDown;
        private bool _dragging;
        private bool _fitOnNextDraw;

        /// <summary>Touch this file (relative to the project) to have the editor open and render the preview after its next script reload.</summary>
        public const string OpenRequestFile = "Library/UOClient/open-world-preview";

        [MenuItem("UOClient/World Preview")]
        private static void Open()
        {
            GetWindow<WorldPreviewWindow>("UO World Preview");
        }

        private static double _nextPoll;

        [InitializeOnLoadMethod]
        private static void WatchForRequests()
        {
            EditorApplication.update -= OpenIfRequested;
            EditorApplication.update += OpenIfRequested;
        }

        private static void OpenIfRequested()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 1.0;

            string request = Path.Combine(ProjectRoot, OpenRequestFile);
            if (!File.Exists(request) || EditorApplication.isCompiling)
                return;
            File.Delete(request);

            EditorApplication.delayCall += () =>
            {
                WorldPreviewWindow window = GetWindow<WorldPreviewWindow>("UO World Preview");
                window.Focus();
                window.Render();
                Debug.Log("UOClient: opened World Preview on request.");
            };
        }

        private void OnEnable()
        {
            _uoPath = EditorPrefs.GetString(PathPrefKey, DefaultPath);
            _artDir = EditorPrefs.GetString(ArtDirPrefKey, DefaultArtDir());
            wantsMouseMove = false;
        }

        private void OnDisable()
        {
            ClearTexture();
            _data?.Dispose();
            _data = null;
        }

        private void OnGUI()
        {
            DrawSettings();
            DrawViewport();
        }

        private void DrawSettings()
        {
            EditorGUI.BeginChangeCheck();
            _uoPath = EditorGUILayout.TextField("UO install", _uoPath);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PathPrefKey, _uoPath);
                _data?.Dispose();
                _data = null;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _source = (ArtSource)EditorGUILayout.EnumPopup("Art", _source);
                using (new EditorGUI.DisabledScope(_source == ArtSource.Original))
                {
                    EditorGUI.BeginChangeCheck();
                    _artDir = EditorGUILayout.TextField(_artDir);
                    if (GUILayout.Button("â€¦", GUILayout.Width(24)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Baked or sprite folder", ArtDirOrProject(), "");
                        if (!string.IsNullOrEmpty(picked))
                            _artDir = picked;
                    }
                    if (EditorGUI.EndChangeCheck())
                        EditorPrefs.SetString(ArtDirPrefKey, _artDir);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 34;
                _map = EditorGUILayout.IntField("Map", _map, GUILayout.Width(70));
                _x = EditorGUILayout.IntField("X", _x, GUILayout.Width(90));
                _y = EditorGUILayout.IntField("Y", _y, GUILayout.Width(90));
                _width = EditorGUILayout.IntField("W", _width, GUILayout.Width(70));
                _height = EditorGUILayout.IntField("H", _height, GUILayout.Width(70));
                using (new EditorGUI.DisabledScope(_source != ArtSource.Original))
                    _scale = EditorGUILayout.IntSlider("Scale", _scale, 1, 4, GUILayout.Width(180));
                EditorGUIUtility.labelWidth = 0;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _roofs = GUILayout.Toggle(_roofs, "Roofs", GUILayout.Width(60));
                using (new EditorGUI.DisabledScope(_source == ArtSource.Original))
                    _coverage = GUILayout.Toggle(_coverage, "Coverage", GUILayout.Width(80));
                if (GUILayout.Button("Render", GUILayout.Width(80)))
                    Render();
                if (GUILayout.Button("Fit", GUILayout.Width(50)))
                    _fitOnNextDraw = true;
                if (GUILayout.Button("1:1", GUILayout.Width(40)))
                {
                    _zoom = 1f;
                    _pan = Vector2.zero;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"zoom {_zoom:0.##}x", EditorStyles.miniLabel);
            }

            EditorGUILayout.HelpBox(string.IsNullOrEmpty(_picked) ? _status : _status + "\n" + _picked, MessageType.None);
        }

        private void DrawViewport()
        {
            Rect area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(area, new Color(0.09f, 0.09f, 0.09f));
            if (_texture == null)
            {
                GUI.Label(area, "  Press Render", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (_fitOnNextDraw && Event.current.type == EventType.Repaint)
            {
                _zoom = Mathf.Min(area.width / _texture.width, area.height / _texture.height);
                _pan = new Vector2((area.width - _texture.width * _zoom) / 2, (area.height - _texture.height * _zoom) / 2);
                _fitOnNextDraw = false;
            }

            HandleInput(area);

            GUI.BeginClip(area);
            var dst = new Rect(_pan.x, _pan.y, _texture.width * _zoom, _texture.height * _zoom);
            GUI.DrawTexture(dst, _texture, ScaleMode.StretchToFill, true);
            GUI.EndClip();
        }

        private void HandleInput(Rect area)
        {
            Event e = Event.current;
            if (!area.Contains(e.mousePosition) && e.type != EventType.MouseUp && e.type != EventType.MouseDrag)
                return;
            Vector2 local = e.mousePosition - area.position;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    float old = _zoom;
                    _zoom = Mathf.Clamp(_zoom * (e.delta.y > 0 ? 1f / 1.15f : 1.15f), 0.05f, 16f);
                    _pan = local - (local - _pan) * (_zoom / old); // zoom around the cursor
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseDown when e.button == 0 || e.button == 2:
                    _mouseDown = e.mousePosition;
                    _dragging = false;
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0 || e.button == 2:
                    if ((e.mousePosition - _mouseDown).sqrMagnitude > 9)
                        _dragging = true;
                    if (_dragging)
                    {
                        _pan += e.delta;
                        Repaint();
                    }
                    e.Use();
                    break;
                case EventType.MouseUp when e.button == 0:
                    if (!_dragging && area.Contains(e.mousePosition))
                        Pick(local);
                    _dragging = false;
                    e.Use();
                    break;
            }
        }

        private void Pick(Vector2 local)
        {
            if (_composite == null)
                return;

            int px = Mathf.FloorToInt((local.x - _pan.x) / _zoom);
            int py = Mathf.FloorToInt((local.y - _pan.y) / _zoom);
            Bitmap32 img = _composite.Image;
            if (px < 0 || py < 0 || px >= img.Width || py >= img.Height)
            {
                _picked = "";
                return;
            }

            int owner = _composite.Owner[py * img.Width + px];
            if (owner < 0)
            {
                _picked = $"pixel {px},{py}: empty";
                return;
            }

            DrawItem it = _composite.Items[owner];
            string name = "";
            if (_data != null)
            {
                name = it.Kind == DrawKind.Static
                    ? _data.TileData.GetStatic(it.Graphic).Name
                    : _data.TileData.Land[it.Graphic & 0x3FFF].Name;
            }
            string slope = it.Stretched ? $" sloped (z {it.Z}/{it.ZRight}/{it.ZBottom}/{it.ZLeft})" : "";
            _picked = $"pixel {px},{py}: {it.Key}  \"{name}\"{slope}";
            Repaint();
        }

        private void Render()
        {
            ClearTexture();
            _picked = "";
            if (_width * _height > MaxTiles)
            {
                _status = $"Region is {_width}x{_height} tiles; keep it under {MaxTiles} tiles for the editor preview.";
                return;
            }

            try
            {
                if (_data == null)
                    _data = new UOData(_uoPath);
                UOClient.Unity.ClientSettingsStore.ApplyOverrides(_data, UOClient.Unity.ClientSettingsStore.ReadSettingsFile()); // the shard's custom art, as in the game

                var compositor = new IsoCompositor(_data) { HideRoofs = !_roofs };
                string art = "original art";
                switch (_source)
                {
                    case ArtSource.Original:
                        compositor.Scale = _scale;
                        break;
                    case ArtSource.Baked:
                        BakedRegion region = BakedRegion.Open(_artDir, _coverage);
                        if (region.Map != _map)
                            Debug.LogWarning($"Baked folder is for map {region.Map}, previewing map {_map}: no placements will match.");
                        compositor.Scale = region.Scale;
                        compositor.PlacementOverride = region.Get;
                        art = $"{region.Count} baked placements";
                        break;
                    case ArtSource.Sprites:
                        SpriteFolder folder = SpriteFolder.Open(_artDir, _coverage);
                        compositor.Scale = folder.Scale;
                        compositor.SpriteOverride = folder.Get;
                        art = "per-graphic sprites";
                        break;
                }

                var watch = System.Diagnostics.Stopwatch.StartNew();
                EditorUtility.DisplayProgressBar("UO World Preview", "Composing regionâ€¦", 0.3f);
                _composite = compositor.Compose(_map, _x, _y, _width, _height);
                EditorUtility.DisplayProgressBar("UO World Preview", "Uploading textureâ€¦", 0.9f);
                _texture = TextureUtil.ToTexture(_composite.Image);

                _status = $"Map {_map} ({_x},{_y}) {_width}x{_height} tiles at {compositor.Scale}x from {art}: "
                          + $"{_composite.Image.Width}x{_composite.Image.Height}px, {_composite.Items.Count} placements, {watch.ElapsedMilliseconds} ms"
                          + (_coverage && _source != ArtSource.Original ? "  [coverage colors]" : "");
                _fitOnNextDraw = true;
            }
            catch (Exception ex)
            {
                _status = $"{ex.GetType().Name}: {ex.Message}";
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ClearTexture()
        {
            if (_texture != null)
                DestroyImmediate(_texture);
            _texture = null;
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static string DefaultArtDir()
        {
            string baked = Path.Combine(ProjectRoot, "Exports", "comfy_test", "baked");
            return Directory.Exists(baked) ? baked : Path.Combine(ProjectRoot, "Exports");
        }

        private string ArtDirOrProject() => Directory.Exists(_artDir) ? _artDir : ProjectRoot;
    }
}
