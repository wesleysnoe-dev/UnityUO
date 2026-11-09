using System.Collections.Generic;
using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// The item, static and multi-part lights near the player, as shader globals (UOItemLights.cginc) for UOClient/WorldSprite
    /// and the TEST Terrain. Each frame the candidates (ground items, multi parts, the loaded map's light statics) are
    /// gathered, the <see cref="Max"/> nearest within <see cref="RangeTiles"/> kept, and each placed at its tile's centre
    /// (TazUO adds a light at the object's draw position + 22, 22) with its light.mul shape's radius and peak and its
    /// LightColors colour. Keyword UO_ITEM_LIGHTS is on only while at least one light shows, so with none (daylight, Night
    /// Sight above the overall level, no light sources) the shaders run exactly their old code.
    /// </summary>
    public sealed class ItemLights
    {
        public const int Max = 32, RangeTiles = 18;

        /// <summary>The window around a light whose tiles it may reach past walls (UOItemLights.cginc's UO_LIGHT_VIS_TILES).</summary>
        public const int VisTiles = 24;

        /// <summary>New lights whose walls are traced per frame (the rest show unblocked until their turn).</summary>
        public const int VisBudget = 2;

        /// <summary>The walls a light is stopped by (set each frame by its owner); null: light passes everything.</summary>
        public Walkability Walk { get; set; }
        public const string Keyword = "UO_ITEM_LIGHTS";

        private struct Candidate
        {
            public int X, Y, Z, Shape, Color, Graphic;
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<Candidate> _chosen = new List<Candidate>();
        private readonly Vector4[] _positions = new Vector4[Max];
        private readonly Vector4[] _colors = new Vector4[Max];
        private bool _keywordOn;

        private static readonly int LightsId = Shader.PropertyToID("_UOItemLights");
        private static readonly int ColorsId = Shader.PropertyToID("_UOItemLightColors");
        private static readonly int CountId = Shader.PropertyToID("_UOItemLightCount");
        private static readonly int TilesId = Shader.PropertyToID("_UOItemLightTiles"), VisId = Shader.PropertyToID("_UOLightVis");
        private static readonly int WindowGlowId = Shader.PropertyToID("_UOWindowGlow");

        // Walls block light: each light's window of tiles (VisTiles square, 255 reached, 0 behind a wall), cached by the
        // light's tile, Z and graphic (lamps do not move), and the windows of the lights drawn this frame packed 8 x 4.
        private readonly Dictionary<long, byte[]> _visCache = new Dictionary<long, byte[]>();
        private readonly long[] _slotKey = new long[Max];
        private readonly Vector4[] _tiles = new Vector4[Max];
        private readonly byte[] _visPixels = new byte[8 * VisTiles * 4 * VisTiles];
        private Texture2D _vis;
        private bool _visDirty;

        /// <summary>Candidates last frame, lights drawn, and the gather-to-upload cost (ms).</summary>
        public int Candidates { get; private set; }
        public int Count { get; private set; }
        public double LastMs { get; private set; }

        public void Begin() => _candidates.Clear();

        /// <summary>A light source at a tile with its light byte / tiledata Layer (<see cref="LightSources.Resolve"/>).</summary>
        public void Add(int x, int y, int z, int graphic, int id)
        {
            if (LightSources.Resolve(graphic, id) is (int shape, int color))
                _candidates.Add(new Candidate { X = x, Y = y, Z = z, Shape = shape, Color = color, Graphic = graphic });
        }

        private static readonly System.Func<Candidate, (int, int)> At = c => (c.X, c.Y);

        /// <summary>Every gathered light source (tile and graphic), e.g. for the flames (Fx.FlameFx).</summary>
        public void ForEachSource(System.Action<int, int, int, int> f)
        {
            foreach (Candidate c in _candidates)
                f(c.X, c.Y, c.Z, c.Graphic);
        }

        /// <summary>
        /// Effect lights this frame (Fx: a fireball lights the ground it flies over, an explosion flashes): a fractional
        /// tile position, a colour and a radius in tiles. They take the slots left after the item lights and show by day
        /// too (where the white cap makes them all but vanish).
        /// </summary>
        public static readonly List<(Vector3 Tile, UnityEngine.Color Color, float Radius)> Glows =
            new List<(Vector3, UnityEngine.Color, float)>();

        /// <summary>
        /// Picks and uploads the lights. <paramref name="strength"/> is how dark it is (<see cref="Lighting.LocalLightScale"/>,
        /// 0 by day or while the personal light is at the overall level): 0 turns them all off.
        /// </summary>
        public void Apply(IWorldSpace world, LightShapes shapes, int scale, int playerX, int playerY, float strength)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Candidates = _candidates.Count;
            int n = 0;
            int budget = VisBudget;
            if (strength > 0f && world != null)
            {
                LightSources.Nearest(_candidates, At, playerX, playerY, RangeTiles, Max, _chosen);
                foreach (Candidate c in _chosen)
                {
                    IO.Formats.LightShape s = shapes.Get(c.Shape);
                    if (!s.Exists)
                        continue;
                    SetVisibility(n, c, ref budget);
                    Vector2 at = world.ToUnity(new Vector2((c.X - c.Y) * 22f, (c.X + c.Y) * 22f + 22f - c.Z * 4f) * scale);
                    (float r, float g, float b) = LightSources.Rgb(c.Color);
                    float peak = s.Peak / 31f;
                    if (Fx.FxCatalog.Enabled && Fx.FxCatalog.Flickers(c.Graphic)) // fire breathes: brightness and reach waver
                    {
                        float t = Time.time * 7f + (c.X * 13 + c.Y * 7) * 0.37f;
                        float f = Mathf.PerlinNoise(t, c.X * 0.1f) * 0.7f + Mathf.PerlinNoise(t * 2.3f, c.Y * 0.1f) * 0.3f;
                        peak *= 0.78f + 0.3f * f;
                    }
                    _positions[n] = new Vector4(at.x, at.y, s.Radius * scale, Mathf.Min(1f, peak));
                    _colors[n] = new Vector4(r * strength, g * strength, b * strength, 0);
                    n++;
                }
            }
            if (world != null)
                foreach (var gl in Glows)
                {
                    if (n >= Max)
                        break;
                    Vector2 at = world.ToUnity(new Vector2((gl.Tile.x - gl.Tile.y) * 22f, (gl.Tile.x + gl.Tile.y) * 22f + 22f - gl.Tile.z * 4f) * scale);
                    float k = Mathf.Max(strength, 0.35f);
                    _tiles[n] = Vector4.zero; // an effect's light is not traced (it moves)
                    _slotKey[n] = 0;
                    _positions[n] = new Vector4(at.x, at.y, gl.Radius * 22f * scale, 1f);
                    _colors[n] = new Vector4(gl.Color.r * k, gl.Color.g * k, gl.Color.b * k, 0);
                    n++;
                }
            Count = n;
            if (n > 0)
            {
                Shader.SetGlobalVectorArray(LightsId, _positions);
                Shader.SetGlobalVectorArray(ColorsId, _colors);
                Shader.SetGlobalVectorArray(TilesId, _tiles);
                UploadVisibility();
            }
            // Lit windows (WorldSprite): their panes glow as the night deepens.
            Shader.SetGlobalFloat(WindowGlowId, strength);
            Shader.SetGlobalFloat(CountId, n);
            SetKeyword(n > 0);
            LastMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private static long Key(in Candidate c) =>
            ((long)(c.X & 0xFFFF) << 40) | ((long)(c.Y & 0xFFFF) << 24) | ((long)(c.Z & 0xFF) << 16) | (long)(c.Graphic & 0xFFFF);

        /// <summary>Slot <paramref name="slot"/>'s window: from the cache, traced now if the budget allows, else none yet (lit).</summary>
        private void SetVisibility(int slot, in Candidate c, ref int budget)
        {
            long key = Key(c);
            if (Walk == null)
            {
                _tiles[slot] = Vector4.zero;
                _slotKey[slot] = 0;
                return;
            }
            if (!_visCache.TryGetValue(key, out byte[] vis))
            {
                if (budget <= 0)
                {
                    _tiles[slot] = Vector4.zero;
                    _slotKey[slot] = 0;
                    return;
                }
                budget--;
                vis = Trace(Walk, c.X, c.Y, c.Z);
                if (_visCache.Count > 2048)
                    _visCache.Clear(); // a long trip: start over rather than grow without end
                _visCache[key] = vis;
            }
            int half = VisTiles / 2;
            _tiles[slot] = new Vector4(c.X - half, c.Y - half, 1f, 0f);
            if (_slotKey[slot] == key)
                return;
            _slotKey[slot] = key;
            int bx = slot % 8 * VisTiles, by = slot / 8 * VisTiles, w = 8 * VisTiles;
            for (int j = 0; j < VisTiles; j++)
                System.Buffer.BlockCopy(vis, j * VisTiles, _visPixels, (by + j) * w + bx, VisTiles);
            _visDirty = true;
        }

        /// <summary>
        /// Which tiles of the window around a light on (lx, ly) at Z it reaches: a straight line from its tile to each
        /// crosses no wall or door spanning its height (windows let light through; the light's own tile is left out, as
        /// wall sconces hang on wall tiles, and so is the target, so a wall facing the light is lit and what is behind it not).
        /// </summary>
        public static byte[] Trace(Walkability walk, int lx, int ly, int z)
        {
            var vis = new byte[VisTiles * VisTiles];
            int half = VisTiles / 2;
            TileData td = walk.TileData;
            var blocks = new Dictionary<(int, int), bool>();
            bool Blocks(int x, int y)
            {
                if (blocks.TryGetValue((x, y), out bool b))
                    return b;
                b = false;
                foreach (StaticCell s in walk.StaticsOn(x, y))
                {
                    StaticTileData t = td.GetStatic(s.Graphic);
                    if ((t.Flags & (TileFlags.Wall | TileFlags.Door)) != 0 && (t.Flags & TileFlags.Window) == 0
                        && s.Z <= z + 14 && s.Z + Mathf.Max(1, t.Height) >= z + 2)
                    {
                        b = true;
                        break;
                    }
                }
                blocks[(x, y)] = b;
                return b;
            }
            for (int j = 0; j < VisTiles; j++)
                for (int i = 0; i < VisTiles; i++)
                {
                    int x = lx - half + i, y = ly - half + j;
                    int dx = Mathf.Abs(x - lx), dy = Mathf.Abs(y - ly), sx = lx < x ? 1 : -1, sy = ly < y ? 1 : -1, err = dx - dy;
                    int cx = lx, cy = ly;
                    bool open = true;
                    while (open)
                    {
                        int e2 = 2 * err;
                        if (e2 > -dy) { err -= dy; cx += sx; }
                        if (e2 < dx) { err += dx; cy += sy; }
                        if (cx == x && cy == y)
                            break;
                        if (Blocks(cx, cy))
                            open = false;
                    }
                    vis[j * VisTiles + i] = open ? (byte)255 : (byte)0;
                }
            return vis;
        }

        private void UploadVisibility()
        {
            if (_vis == null)
            {
                _vis = new Texture2D(8 * VisTiles, 4 * VisTiles, TextureFormat.R8, false, true)
                {
                    name = "UO light visibility", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                _visDirty = true;
            }
            if (_visDirty)
            {
                _vis.SetPixelData(_visPixels, 0);
                _vis.Apply(false);
                _visDirty = false;
            }
            Shader.SetGlobalTexture(VisId, _vis);
        }

        /// <summary>No lights: the keyword goes off (the shaders' old path).</summary>
        public void Clear()
        {
            _candidates.Clear();
            Count = 0;
            Shader.SetGlobalFloat(CountId, 0);
            Shader.SetGlobalFloat(WindowGlowId, 0f);
            SetKeyword(false);
        }

        private void SetKeyword(bool on)
        {
            if (on == _keywordOn)
                return;
            _keywordOn = on;
            if (on)
                Shader.EnableKeyword(Keyword);
            else
                Shader.DisableKeyword(Keyword);
        }
    }
}
