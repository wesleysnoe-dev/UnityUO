using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Mockup-style HUD for the world demo, drawn with IMGUI so it needs no UI package or assets:
    /// status bars, a tabbed journal, a round minimap from radarcol.mul and an overhead name.
    /// Placeholder look for direction-setting; the real UI will be gumps with HD skins.
    /// </summary>
    public sealed class DemoHud : IDisposable
    {
        private const int MinimapSize = 176;
        private const int MinimapTiles = 200;
        private const float PixelsPerTile = 2.4f;

        private static readonly string[] Tabs = { "All", "Guild", "Party", "Private", "System", "Emote" };

        private readonly List<(string Time, string Speaker, string Text)> _journal = new List<(string, string, string)>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private Texture2D _white, _panel, _border, _hits, _mana, _stamina, _vignette, _ring, _minimap, _arrow;
        private Color32[] _radar;
        private Color32[] _minimapPixels;
        private int _radarX0, _radarY0;
        private int _playerX = int.MinValue, _playerY;
        private int _tab;
        private float _staminaValue = 92f;
        private GUIStyle _label, _small, _title, _bar, _name, _tabStyle, _tabActive, _overhead;

        public bool Visible { get; set; } = true;
        public string Summary { get; set; } = "";
        public string TimeName { get; set; } = "Day";
        public float Vignette { get; set; }

        /// <summary>Connection state shown at the top of the screen when online (empty offline).</summary>
        public string ConnectionStatus { get; set; } = "";

        /// <summary>Status bar values; when <see cref="HasStats"/> is false the demo's placeholder values are shown.</summary>
        public bool HasStats { get; set; }
        public int Hits = 78, HitsMax = 100, Mana = 56, ManaMax = 100, Stamina = 92, StaminaMax = 100;

        /// <summary>Overhead names for other mobiles, in screen pixels (top-left origin), redrawn every frame.</summary>
        public readonly List<(string Name, Vector2 Screen, Color Color)> Labels = new List<(string, Vector2, Color)>();

        /// <summary>Speech and messages over heads (bottom-centered at the point), redrawn every frame.</summary>
        /// <summary>Overhead text, newest first; lines sharing a non-zero Stack key pile upward.</summary>
        public readonly List<(string Text, Vector2 Screen, Color Color, int Size, uint Stack)> Overheads = new List<(string, Vector2, Color, int, uint)>();
        private readonly Dictionary<uint, float> _stacked = new Dictionary<uint, float>();

        public readonly List<(Vector2 Screen, float Ratio, Color Color)> HealthBars = new List<(Vector2, float, Color)>();
        public bool WarMode { get; set; }

        /// <summary>
        /// The quest arrow's target in screen pixels (top-left origin), or null when there is none. On screen it
        /// is marked in place; off screen an arrow at the edge points toward it.
        /// </summary>
        public Vector2? QuestArrow { get; set; }
        public Vector2Int QuestArrowTile { get; set; }

        /// <summary>Room left at the top of the screen for the top bar (UI units).</summary>
        public float TopInset { get; set; }

        public bool MinimapVisible { get; set; } = true;
        public bool JournalVisible { get; set; } = true;
        public bool NamesVisible { get; set; } = true;

        /// <summary>The journal panel's screen rect (for clicks), valid after a draw.</summary>
        public Rect JournalRect { get; private set; }

        public void Log(string speaker, string text)
        {
            _journal.Add((DateTime.Now.ToString("HH:mm:ss"), speaker, text));
            if (_journal.Count > 50)
                _journal.RemoveAt(0);
        }

        public sealed class RadarData
        {
            public int X0, Y0, CenterX, CenterY;
            public Color32[] Colors;
        }

        /// <summary>Tile the current minimap grid is centered on (int.MinValue before the first one).</summary>
        public int RadarCenterX { get; private set; } = int.MinValue;
        public int RadarCenterY { get; private set; } = int.MinValue;

        /// <summary>
        /// Minimap colors around a tile: the topmost static's radar color, else the land's.
        /// Reads the map only, so it can run on a background thread.
        /// </summary>
        public static RadarData ComputeRadar(UOData data, int map, int centerX, int centerY)
        {
            ushort[] colors = data.RadarColors;
            MapFile m = data.GetMap(map);
            var r = new RadarData
            {
                X0 = centerX - MinimapTiles / 2,
                Y0 = centerY - MinimapTiles / 2,
                CenterX = centerX,
                CenterY = centerY,
                Colors = new Color32[MinimapTiles * MinimapTiles]
            };
            var topZ = new int[MinimapTiles * MinimapTiles];

            for (int by = Math.Max(0, r.Y0) >> 3; by <= (r.Y0 + MinimapTiles - 1) >> 3; by++)
            {
                for (int bx = Math.Max(0, r.X0) >> 3; bx <= (r.X0 + MinimapTiles - 1) >> 3; bx++)
                {
                    LandCell[] land = m.ReadLandBlock(bx, by);
                    for (int i = 0; i < 64; i++)
                    {
                        int tx = (bx << 3) + (i & 7) - r.X0, ty = (by << 3) + (i >> 3) - r.Y0;
                        if ((uint)tx >= MinimapTiles || (uint)ty >= MinimapTiles)
                            continue;
                        int g = land[i].Graphic & 0x3FFF;
                        r.Colors[ty * MinimapTiles + tx] = ToColor(g < colors.Length ? colors[g] : (ushort)0);
                        topZ[ty * MinimapTiles + tx] = land[i].Z;
                    }
                    foreach (StaticCell s in m.ReadStaticBlock(bx, by))
                    {
                        int tx = (bx << 3) + s.X - r.X0, ty = (by << 3) + s.Y - r.Y0;
                        if ((uint)tx >= MinimapTiles || (uint)ty >= MinimapTiles)
                            continue;
                        int index = ty * MinimapTiles + tx;
                        int ci = 0x4000 + s.Graphic;
                        if (s.Z >= topZ[index] && ci < colors.Length)
                        {
                            topZ[index] = s.Z;
                            r.Colors[index] = ToColor(colors[ci]);
                        }
                    }
                }
            }
            return r;
        }

        public void SetRadar(RadarData r)
        {
            _radar = r.Colors;
            _radarX0 = r.X0;
            _radarY0 = r.Y0;
            RadarCenterX = r.CenterX;
            RadarCenterY = r.CenterY;
            if (_minimap == null)
            {
                _minimap = new Texture2D(MinimapSize, MinimapSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                _owned.Add(_minimap);
                _minimapPixels = new Color32[MinimapSize * MinimapSize];
            }
            _playerX = int.MinValue; // redraw on the next update
        }

        public void UpdatePlayer(int x, int y, bool moving, float dt)
        {
            _staminaValue = Mathf.Clamp(_staminaValue + (moving ? -1.5f : 4f) * dt, 60f, 100f);
            if (x == _playerX && y == _playerY || _minimap == null || _radar == null)
                return;
            _playerX = x;
            _playerY = y;

            // Rotated 45 degrees so north matches the isometric view (map +X runs down-right).
            float c = MinimapSize / 2f, r = c - 3f, k = 1f / (Mathf.Sqrt(2f) * PixelsPerTile);
            for (int j = 0; j < MinimapSize; j++)
            {
                float sy = (MinimapSize - 1 - j) + 0.5f - c; // texture rows are bottom-up
                for (int i = 0; i < MinimapSize; i++)
                {
                    float sx = i + 0.5f - c;
                    float dist = Mathf.Sqrt(sx * sx + sy * sy);
                    if (dist > r)
                    {
                        _minimapPixels[j * MinimapSize + i] = new Color32(0, 0, 0, 0);
                        continue;
                    }
                    int tx = x - _radarX0 + Mathf.FloorToInt((sx + sy) * k);
                    int ty = y - _radarY0 + Mathf.FloorToInt((sy - sx) * k);
                    Color32 col = (uint)tx < MinimapTiles && (uint)ty < MinimapTiles ? _radar[ty * MinimapTiles + tx] : new Color32(10, 10, 12, 255);
                    float edge = Mathf.Clamp01(r - dist); // soft edge
                    col.a = (byte)(255 * edge);
                    _minimapPixels[j * MinimapSize + i] = col;
                }
            }
            _minimap.SetPixels32(_minimapPixels);
            _minimap.Apply(false);
        }

        public void Draw(string characterName, Vector2 headScreen)
        {
            EnsureResources();
            float s = Mathf.Clamp(Screen.height / 1000f, 0.75f, 2.5f);
            float w = Screen.width / s, h = Screen.height / s;

            // Vignette in screen space, under everything.
            if (Vignette > 0.01f)
            {
                GUI.color = new Color(1, 1, 1, Vignette);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _vignette, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));

            if (NamesVisible)
            {
                foreach (var (labelName, screen, color) in Labels)
                    DrawOutlined(new Rect(screen.x / s - 100, screen.y / s - 26, 200, 22), labelName, _name, color);
                DrawOutlined(new Rect(headScreen.x / s - 100, headScreen.y / s - 26, 200, 22), characterName, _name, new Color(0.55f, 0.75f, 1f));
            }

            foreach (var (screen, ratio, color) in HealthBars)
            {
                var r = new Rect(screen.x / s - 22, screen.y / s, 44, 6);
                GUI.color = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, 0.95f);
                GUI.DrawTexture(r, _white);
                GUI.color = new Color(0.1f, 0.02f, 0.02f, 0.95f);
                GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), _white);
                GUI.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.2f, 0.55f, 1f), ratio);
                GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(ratio), r.height - 2), _white);
                GUI.color = Color.white;
            }

            // Speech over heads: wrapped, growing upward from the anchor.
            _stacked.Clear();
            foreach (var (text, screen, color, size, stack) in Overheads)
            {
                _overhead.fontSize = size;
                float th = _overhead.CalcHeight(new GUIContent(text), 240);
                float used = 0;
                if (stack != 0)
                {
                    _stacked.TryGetValue(stack, out used);
                    _stacked[stack] = used + th + 2;
                }
                DrawOutlined(new Rect(screen.x / s - 120, screen.y / s - th - used, 240, th), text, _overhead, color);
            }

            if (QuestArrow is Vector2 quest)
                DrawQuestArrow(quest / s, w, h);

            if (WarMode)
                DrawOutlined(new Rect(w * 0.5f - 60, 40 + TopInset, 120, 22), "WAR", _bar, new Color(1f, 0.3f, 0.25f));

            if (!string.IsNullOrEmpty(ConnectionStatus))
                DrawOutlined(new Rect(w * 0.5f - 300, 12, 600, 22), ConnectionStatus, _bar, new Color(0.95f, 0.9f, 0.75f));

            if (Visible)
            {
                DrawStatus(new Rect(14, 14 + TopInset, 290, 112));
                if (MinimapVisible)
                    DrawMinimap(new Rect(w - MinimapSize - 22, 14 + TopInset, MinimapSize + 8, MinimapSize + 8));
                if (JournalVisible)
                {
                    var jr = new Rect(Mathf.Max(320, w * 0.5f - 330), h - 214, Mathf.Min(660, w - 340), 200);
                    JournalRect = new Rect(jr.x * s, jr.y * s, jr.width * s, jr.height * s);
                    DrawJournal(jr);
                }
                else
                {
                    JournalRect = Rect.zero;
                }
                GUI.Label(new Rect(16, h - 44, 300, 18), TimeName + "  ·  " + "T time  R roofs  H hud", _small);
                GUI.Label(new Rect(16, h - 26, Mathf.Max(300, w * 0.5f - 350), 18), Summary, _small);
            }

            GUI.matrix = saved;
        }

        private void DrawStatus(Rect r)
        {
            DrawPanel(r, null);
            int stamina = HasStats ? Stamina : Mathf.RoundToInt(_staminaValue);
            int staminaMax = HasStats ? StaminaMax : 100;
            DrawBar(new Rect(r.x + 10, r.y + 10, r.width - 20, 26), _hits, Ratio(Hits, HitsMax), $"Hits  {Hits}/{HitsMax}");
            DrawBar(new Rect(r.x + 10, r.y + 43, r.width - 20, 26), _mana, Ratio(Mana, ManaMax), $"Mana  {Mana}/{ManaMax}");
            DrawBar(new Rect(r.x + 10, r.y + 76, r.width - 20, 26), _stamina, Ratio(stamina, staminaMax), $"Stamina  {stamina}/{staminaMax}");
        }

        private static float Ratio(int value, int max) => max <= 0 ? 0f : Mathf.Clamp01(value / (float)max);

        /// <summary>Classic notoriety name colors.</summary>
        public static Color NotorietyColor(int notoriety)
        {
            switch (notoriety)
            {
                case 1: return new Color(0.45f, 0.7f, 1f);    // innocent
                case 2: return new Color(0.35f, 0.9f, 0.35f); // ally
                case 3:
                case 4: return new Color(0.72f, 0.72f, 0.72f); // attackable / criminal
                case 5: return new Color(1f, 0.6f, 0.2f);     // enemy
                case 6: return new Color(1f, 0.3f, 0.25f);    // murderer
                case 7: return new Color(1f, 0.9f, 0.3f);     // invulnerable (vendors, guards)
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        private void DrawBar(Rect r, Texture2D fill, float value, string text)
        {
            GUI.color = new Color(0.55f, 0.45f, 0.28f);
            GUI.DrawTexture(r, _white);
            GUI.color = new Color(0.06f, 0.05f, 0.05f);
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), _white);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(value), r.height - 4), fill);
            DrawOutlined(r, text, _bar, new Color(0.96f, 0.93f, 0.86f));
        }

        private void DrawMinimap(Rect r)
        {
            if (_minimap == null)
                return;
            var inner = new Rect(r.x + 4, r.y + 4, MinimapSize, MinimapSize);
            GUI.DrawTexture(inner, _minimap);
            GUI.DrawTexture(r, _ring);

            // Player marker.
            GUI.color = new Color(0.35f, 0.65f, 1f);
            GUI.DrawTexture(new Rect(inner.center.x - 3, inner.center.y - 3, 6, 6), _white);
            GUI.color = Color.white;
            GUI.Label(new Rect(inner.x, inner.yMax + 2, inner.width, 16), $"{_playerX}, {_playerY}", _small);
        }

        /// <summary>
        /// A pulsing diamond on the target tile while it is in view, else an arrow at the screen edge on the line
        /// from the screen center toward it. <paramref name="p"/> is in UI units.
        /// </summary>
        private void DrawQuestArrow(Vector2 p, float w, float h)
        {
            const float margin = 40f;
            var color = new Color(1f, 0.82f, 0.25f, 0.75f + 0.25f * Mathf.Sin(Time.time * 6f));
            var area = Rect.MinMaxRect(margin, margin + TopInset, w - margin, h - margin);
            Matrix4x4 saved = GUI.matrix;
            Vector2 at;
            if (area.Contains(p))
            {
                at = p;
                GUI.matrix = saved * Rotation(p, 45f);
                GUI.color = new Color(0.1f, 0.06f, 0f, color.a);
                GUI.DrawTexture(new Rect(p.x - 9, p.y - 9, 18, 18), _white);
                GUI.color = color;
                GUI.DrawTexture(new Rect(p.x - 7, p.y - 7, 14, 14), _white);
            }
            else
            {
                // Where the line from the center leaves the area.
                Vector2 c = area.center, d = p - c;
                float t = Mathf.Min(d.x != 0 ? (area.width * 0.5f) / Mathf.Abs(d.x) : float.MaxValue,
                                    d.y != 0 ? (area.height * 0.5f) / Mathf.Abs(d.y) : float.MaxValue);
                at = c + d * Mathf.Min(t, 1f);
                GUI.matrix = saved * Rotation(at, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                GUI.color = color;
                GUI.DrawTexture(new Rect(at.x - 16, at.y - 16, 32, 32), _arrow);
            }
            GUI.matrix = saved;
            GUI.color = Color.white;
            DrawOutlined(new Rect(at.x - 60, at.y + 16, 120, 18), $"{QuestArrowTile.x}, {QuestArrowTile.y}", _small, color);
        }

        /// <summary>A GUI matrix rotating by <paramref name="degrees"/> (clockwise on screen) around a point.</summary>
        private static Matrix4x4 Rotation(Vector2 pivot, float degrees) =>
            Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, degrees)) * Matrix4x4.Translate(-pivot);

        private void DrawJournal(Rect r)
        {
            DrawPanel(r, "Journal");
            var lines = new Rect(r.x + 12, r.y + 30, r.width - 24, r.height - 70);
            int max = Mathf.FloorToInt(lines.height / 18f);
            int start = Mathf.Max(0, _journal.Count - max);
            for (int i = start; i < _journal.Count; i++)
            {
                var (time, speaker, text) = _journal[i];
                float y = lines.y + (i - start) * 18f;
                GUI.color = new Color(0.65f, 0.6f, 0.52f);
                GUI.Label(new Rect(lines.x, y, 70, 18), time, _label);
                float x = lines.x + 72;
                if (speaker != null)
                {
                    GUI.color = new Color(0.98f, 0.82f, 0.38f);
                    float sw = _label.CalcSize(new GUIContent(speaker + ":")).x;
                    GUI.Label(new Rect(x, y, sw + 4, 18), speaker + ":", _label);
                    x += sw + 6;
                }
                GUI.color = new Color(0.93f, 0.9f, 0.82f);
                GUI.Label(new Rect(x, y, lines.xMax - x, 18), text, _label);
            }
            GUI.color = Color.white;

            float tabWidth = (r.width - 24) / Tabs.Length;
            for (int t = 0; t < Tabs.Length; t++)
            {
                var tr = new Rect(r.x + 12 + t * tabWidth, r.yMax - 34, tabWidth - 6, 24);
                if (GUI.Button(tr, Tabs[t], t == _tab ? _tabActive : _tabStyle))
                    _tab = t;
            }
        }

        private void DrawPanel(Rect r, string title)
        {
            GUI.DrawTexture(r, _border);
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), _panel);
            if (title != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.35f);
                GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, 24), _white);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 12, r.y + 3, r.width - 24, 22), title, _title);
            }
        }

        private static void DrawOutlined(Rect r, string text, GUIStyle style, Color color)
        {
            // Four shadow copies instead of eight: IMGUI labels are not free, and dozens of names draw every frame.
            Color saved = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 1, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x - 1, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + 1, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y - 1, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = saved;
        }

        private void EnsureResources()
        {
            if (_white != null)
                return;

            _white = Own(UnityTextures.Solid(Color.white));
            _border = Own(UnityTextures.Solid(new Color(0.42f, 0.33f, 0.2f, 0.95f)));
            _panel = Own(Parchment());
            _hits = Own(Gradient(new Color(0.85f, 0.18f, 0.14f), new Color(0.45f, 0.05f, 0.04f)));
            _mana = Own(Gradient(new Color(0.22f, 0.42f, 0.95f), new Color(0.06f, 0.14f, 0.5f)));
            _stamina = Own(Gradient(new Color(0.95f, 0.78f, 0.2f), new Color(0.55f, 0.4f, 0.04f)));
            _vignette = Own(VignetteTexture());
            _ring = Own(RingTexture(MinimapSize + 8));
            _arrow = Own(ArrowTexture(32));

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = false, clipping = TextClipping.Clip };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 12 };
            _small.normal.textColor = new Color(0.8f, 0.78f, 0.72f);
            _title = new GUIStyle(_label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.96f, 0.88f, 0.68f);
            _bar = new GUIStyle(_label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _name = new GUIStyle(_label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _overhead = new GUIStyle(_label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter, wordWrap = true };
            _tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _tabStyle.normal.background = Own(UnityTextures.Solid(new Color(0.16f, 0.13f, 0.1f, 0.95f)));
            _tabStyle.hover.background = Own(UnityTextures.Solid(new Color(0.24f, 0.19f, 0.13f, 0.95f)));
            _tabStyle.normal.textColor = _tabStyle.hover.textColor = new Color(0.9f, 0.85f, 0.74f);
            _tabActive = new GUIStyle(_tabStyle);
            _tabActive.normal.background = _tabActive.hover.background = Own(UnityTextures.Solid(new Color(0.38f, 0.29f, 0.16f, 0.95f)));
            _tabActive.normal.textColor = _tabActive.hover.textColor = new Color(1f, 0.92f, 0.7f);
        }

        private Texture2D Own(Texture2D t)
        {
            _owned.Add(t);
            return t;
        }

        private static Texture2D Gradient(Color top, Color bottom)
        {
            var t = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
                t.SetPixel(0, y, Color.Lerp(bottom, top, y / 31f));
            t.Apply();
            return t;
        }

        /// <summary>Dark, slightly mottled panel fill.</summary>
        private static Texture2D Parchment()
        {
            const int size = 64;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(7);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = (float)rng.NextDouble() * 0.035f + Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.04f;
                    t.SetPixel(x, y, new Color(0.11f + n, 0.095f + n * 0.9f, 0.075f + n * 0.7f, 0.9f));
                }
            t.Apply();
            return t;
        }

        private static Texture2D VignetteTexture()
        {
            const int size = 128;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0.35f, 1.0f, d);
                    t.SetPixel(x, y, new Color(0.02f, 0.015f, 0.03f, a));
                }
            t.Apply();
            return t;
        }

        private static Texture2D RingTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float outer = Mathf.Clamp01(c - d), inner = Mathf.Clamp01(d - (c - 6));
                    float a = outer * inner;
                    float shade = 0.75f + 0.25f * Mathf.Clamp01((y / (float)size));
                    t.SetPixel(x, y, new Color(0.5f * shade, 0.4f * shade, 0.25f * shade, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>A white arrowhead pointing right (+x) with a dark rim, tinted when drawn.</summary>
        private static Texture2D ArrowTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Inside the triangle (2, 2) - (size - 2, c) - (2, size - 2), measured as distance to its edges.
                    float px = x + 0.5f, py = y + 0.5f;
                    float half = (size - 2 - px) / (size - 4) * (c - 2);
                    float edge = Mathf.Min(px - 2, half - Mathf.Abs(py - c));
                    float a = Mathf.Clamp01(edge + 0.5f);
                    float shade = Mathf.Clamp01(edge / 2.5f);
                    t.SetPixel(x, y, new Color(0.15f + 0.85f * shade, 0.12f + 0.88f * shade, 0.08f + 0.92f * shade, a));
                }
            t.Apply();
            return t;
        }

        private static Color32 ToColor(ushort c)
        {
            uint argb = Color16.ToArgb32(c);
            return new Color32((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, 255);
        }

        public void Dispose()
        {
            foreach (UnityEngine.Object o in _owned)
                if (o != null)
                    UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
