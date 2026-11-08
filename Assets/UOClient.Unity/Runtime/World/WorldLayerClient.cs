using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.WorldLayer;

namespace UOClient.Unity
{
    /// <summary>
    /// The world editor's client side (WORLD_EDITOR.md), on with <c>-worldeditor</c>: says hello to the server, asks
    /// for the painted regions around the player, keeps them (<see cref="Region"/>, read by the terrain's chunk worker),
    /// overlays the tiles derived from them on this client's map (so walking and sprites agree with the server), and
    /// rebuilds the chunks they cover. Step 2: the brushes (<see cref="Brush"/>) apply each stroke here at once with the
    /// same shared code the server runs (so painting is live), send it to the server, which stays authoritative, and
    /// take the server's copy once a drag ends.
    /// </summary>
    public sealed class WorldLayerClient : IDisposable
    {
        /// <summary>The client in use (null when the editor is off); the terrain emitter reads regions through it.</summary>
        public static WorldLayerClient Current { get; private set; }

        /// <summary>Whether the command line turns the world editor on.</summary>
        public static bool Requested()
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-worldeditor", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private const int Reach = 1;            // regions around the player's to keep
        private const float AskEvery = 1.5f;    // seconds between checks
        private const float RebuildEvery = 0.2f; // seconds between redraws of painted ground while painting

        private readonly GameContext _ctx;
        private readonly GameSession _session;
        private readonly ConcurrentDictionary<(int, int, int), WorldRegion> _regions = new ConcurrentDictionary<(int, int, int), WorldRegion>();
        private readonly Dictionary<(int, int, int), int> _asked = new Dictionary<(int, int, int), int>(); // server version asked with (-1 none)
        private readonly Dictionary<(int, int, int), int> _known = new Dictionary<(int, int, int), int>(); // server's latest version
        private readonly HashSet<(int, int, int)> _serverPainted = new HashSet<(int, int, int)>();
        private WorldStore _store;
        private int _storeMap = -1;
        private int[][] _plain;
        private float _nextAsk, _nextRebuild, _quietUntil;
        private bool _hello;
        private RectInt? _dirty;

        public WorldLayerClient(GameContext ctx, GameSession session)
        {
            _ctx = ctx;
            _session = session;
            _session.WorldEditorReady += OnReady;
            _session.WorldRegionReceived += OnRegion;
            _session.WorldRegionChanged += OnChanged;
            _session.WorldEditResult += OnResult;
            Current = this;
        }

        /// <summary>Whether the server lets this account edit (after the hello's answer).</summary>
        public bool CanEdit { get; private set; }

        /// <summary>The brush settings the editor panel changes.</summary>
        public readonly WorldBrushSettings Brush = new WorldBrushSettings();

        /// <summary>The last message from the server or the editor, for the panel.</summary>
        public string Status { get; private set; } = "";

        /// <summary>A region the terrain should draw from (painted, or made here to paint on), or null. Worker threads too.</summary>
        public WorldRegion Region(int map, int rx, int ry) => _regions.TryGetValue((map, rx, ry), out WorldRegion r) ? r : null;

        // ---- the local copy ----

        private WorldStore Store()
        {
            int map = _session.World.Map;
            if (_store == null || _storeMap != map)
            {
                _store = new WorldStore(map, (rx, ry) => Create(map, rx, ry));
                foreach (KeyValuePair<(int, int, int), WorldRegion> kv in _regions)
                    if (kv.Key.Item1 == map)
                        _store.Put(kv.Value);
                _storeMap = map;
            }
            return _store;
        }

        /// <summary>A region nobody painted yet, made here from the original map exactly as the server makes it.</summary>
        private WorldRegion Create(int map, int rx, int ry)
        {
            MapFile file = _ctx.Data?.GetMap(map);
            if (file == null || rx < 0 || ry < 0 || rx * WorldRegion.Tiles >= file.Width || ry * WorldRegion.Tiles >= file.Height)
                return null;
            LandTileData[] land = _ctx.Data.TileData.Land;
            LandCell Original(int x, int y)
            {
                x = Math.Max(0, Math.Min(file.Width - 1, x));
                y = Math.Max(0, Math.Min(file.Height - 1, y));
                return file.ReadOriginalLandBlock(x >> 3, y >> 3)[((y & 7) << 3) | (x & 7)];
            }
            WorldRegion region = WorldInit.FromMap(map, rx, ry, (x, y) => Original(x, y).Z,
                (x, y) => WorldMaterials.Classify(Original(x, y).Graphic < land.Length ? land[Original(x, y).Graphic].Name : ""));
            _regions[(map, rx, ry)] = region;
            return region;
        }

        /// <summary>The painted ground's height (UO Z) at a world point in tile units, or null where nothing is painted or made.</summary>
        public float? HeightAt(float x, float y)
        {
            const int s = WorldRegion.SamplesPerTile;
            int sx = (int)Math.Round(x * s), sy = (int)Math.Round(y * s);
            (int rx, int ry) = WorldRegion.RegionOfSample(sx, sy);
            WorldRegion r = Region(_session.World.Map, rx, ry);
            if (r == null)
                return null;
            return r.Height[WorldRegion.Index(sx - r.SampleX0, sy - r.SampleY0)] / (float)WorldRegion.ZScale;
        }

        // ---- painting ----

        /// <summary>One brush application at a world point (tile units): applied here at once, sent to the server.</summary>
        public void Stroke(float x, float y)
        {
            if (!CanEdit || _session.State != SessionState.InWorld)
                return;
            BrushStroke stroke = Brush.StrokeAt(x, y).Clamped();
            EditStep step = WorldBrush.Apply(Store(), stroke);
            _session.SendWorldStroke(_session.World.Map, stroke);
            _quietUntil = Time.unscaledTime + 1.5f; // our own edits: the server's copy is taken once the drag ends
            if (step == null)
                return;
            OverlayTiles(step.Tiles);
            int r = (int)Math.Ceiling(stroke.Radius) + 1;
            MarkDirty(new RectInt((int)stroke.X - r, (int)stroke.Y - r, 2 * r + 1, 2 * r + 1));
        }

        /// <summary>The mouse let go: one undo step on the server, and its copy of the regions is taken.</summary>
        public void EndStroke()
        {
            _session.EndWorldStroke();
            _quietUntil = Time.unscaledTime + 0.5f;
            AskAgainSoon();
        }

        public void Undo(bool redo)
        {
            if (!CanEdit)
                return;
            _session.UndoWorld(redo);
            AskAgainSoon();
        }

        private void AskAgainSoon()
        {
            // Ask for every region we hold again (the server answers only where its version moved).
            foreach (var key in new List<(int, int, int)>(_asked.Keys))
                _asked[key] = int.MinValue;
            _nextAsk = Time.unscaledTime + 0.6f;
        }

        private void MarkDirty(RectInt rect)
        {
            if (_dirty is RectInt d)
            {
                int x0 = Math.Min(d.xMin, rect.xMin), y0 = Math.Min(d.yMin, rect.yMin);
                int x1 = Math.Max(d.xMax, rect.xMax), y1 = Math.Max(d.yMax, rect.yMax);
                _dirty = new RectInt(x0, y0, x1 - x0, y1 - y0);
            }
            else
                _dirty = rect;
        }

        /// <summary>Every frame (main thread).</summary>
        public void Update()
        {
            if (_session.State != SessionState.InWorld || _ctx.Player == null)
                return;
            if (!_hello)
            {
                _hello = true;
                _session.WorldHello();
            }
            if (_dirty is RectInt d && Time.unscaledTime >= _nextRebuild)
            {
                _nextRebuild = Time.unscaledTime + RebuildEvery;
                _dirty = null;
                _ctx.World?.RebuildArea(d.xMin, d.yMin, d.xMax, d.yMax);
            }
            if (Time.unscaledTime < _nextAsk || Time.unscaledTime < _quietUntil)
                return;
            _nextAsk = Time.unscaledTime + AskEvery;
            int map = _session.World.Map;
            (int prx, int pry) = WorldRegion.RegionOfTile(_ctx.Player.X, _ctx.Player.Y);
            for (int ry = pry - Reach; ry <= pry + Reach; ry++)
                for (int rx = prx - Reach; rx <= prx + Reach; rx++)
                {
                    if (rx < 0 || ry < 0)
                        continue;
                    var key = (map, rx, ry);
                    int have = _serverPainted.Contains(key) && _known.TryGetValue(key, out int v) ? v : -1;
                    int latest = _known.TryGetValue(key, out int k) ? k : int.MinValue;
                    // Ask once per region, and again whenever the server says it moved past what we have.
                    if (_asked.TryGetValue(key, out int askedWith) && askedWith == have && latest <= have)
                        continue;
                    _asked[key] = have;
                    _session.RequestWorldRegion(map, rx, ry, have);
                }
        }

        private void OnReady(bool canEdit)
        {
            CanEdit = canEdit;
            Status = canEdit ? "Connected. You can edit (F5 opens the editor)." : "Connected (view only).";
            _ctx.Hud?.Log(null, "World editor: " + Status);
        }

        private void OnResult(bool ok, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            Status = message;
            _ctx.Hud?.Log(null, "World editor: " + message);
        }

        private void OnChanged(int map, int rx, int ry, int version)
        {
            _known[(map, rx, ry)] = version;
            if (Time.unscaledTime >= _quietUntil)
                _nextAsk = 0f; // someone else's edit (or an undo): ask on the next frame
        }

        private void OnRegion(int map, int rx, int ry, int version, byte[] data)
        {
            var key = (map, rx, ry);
            _known[key] = version;
            if (data == null)
            {
                _asked[key] = -1;
                return; // nobody painted it (any region we made locally stays: it is the same as the server's start)
            }
            WorldRegion region;
            try
            {
                region = WorldRegion.Deserialize(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient world editor: region {map}:{rx},{ry} unreadable: {e.Message}");
                return;
            }
            _serverPainted.Add(key);
            _asked[key] = region.Version;
            bool same = _regions.TryGetValue(key, out WorldRegion mine) && Same(mine, region);
            _regions[key] = region;
            if (_store != null && _storeMap == map)
                _store.Put(region);
            if (same)
                return; // what we painted here already: nothing to redraw
            var tiles = new List<(int, int)>();
            for (int i = 0; i < region.Edited.Length; i++)
                if (region.Edited[i])
                    tiles.Add((region.TileX0 + i % WorldRegion.Tiles, region.TileY0 + i / WorldRegion.Tiles));
            OverlayTiles(tiles);
            MarkDirty(new RectInt(region.TileX0 - 1, region.TileY0 - 1, WorldRegion.Tiles + 2, WorldRegion.Tiles + 2));
        }

        private static bool Same(WorldRegion a, WorldRegion b)
        {
            for (int i = 0; i < a.Height.Length; i++)
                if (a.Height[i] != b.Height[i])
                    return false;
            for (int i = 0; i < a.MatW.Length; i++)
                if (a.MatW[i] != b.MatW[i] || a.MatId[i] != b.MatId[i])
                    return false;
            return true;
        }

        /// <summary>The server's tiles for edited tiles, put on this client's map (same rules, same code).</summary>
        private void OverlayTiles(IEnumerable<(int X, int Y)> tiles)
        {
            int mapIndex = _session.World.Map;
            MapFile map = _ctx.Data?.GetMap(mapIndex);
            if (map == null)
                return;
            LandTileData[] land = _ctx.Data.TileData.Land;
            _plain ??= WorldMaterials.Plain(g => g < land.Length ? land[g].Name : "", land.Length);
            WorldStore store = Store();
            foreach ((int x, int y) in tiles)
            {
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                    continue;
                LandCell original = map.ReadOriginalLandBlock(x >> 3, y >> 3)[((y & 7) << 3) | (x & 7)];
                string name = original.Graphic < land.Length ? land[original.Graphic].Name : "";
                (int graphic, int z) = WorldDerive.Tile(store, x, y, original.Graphic, WorldMaterials.Classify(name), _plain);
                map.SetLandOverride(x, y, new LandCell { Graphic = (ushort)graphic, Z = (sbyte)z });
            }
        }

        public void Dispose()
        {
            _session.WorldEditorReady -= OnReady;
            _session.WorldRegionReceived -= OnRegion;
            _session.WorldRegionChanged -= OnChanged;
            _session.WorldEditResult -= OnResult;
            if (Current == this)
                Current = null;
        }
    }

    /// <summary>What the editor panel sets: the mode and the brush (WORLD_EDITOR.md, the owner's mockup).</summary>
    public sealed class WorldBrushSettings
    {
        public enum Mode { Raise, Lower, SetZ, Soft, Paint, Look }

        public Mode Current = Mode.Raise;

        /// <summary>Brush radius in tiles.</summary>
        public float Size = 3f;

        /// <summary>0..1: how much of the radius fades out.</summary>
        public float Softness = 0.6f;

        /// <summary>Strokes per second while the mouse is held.</summary>
        public float Flow = 10f;

        /// <summary>Strength of each stroke (0..1).</summary>
        public float Strength = 0.35f;

        public byte Material = WorldMaterials.Grass;

        /// <summary>Set Z's target height (UO Z); Look picks it from the ground.</summary>
        public float TargetZ;

        public BrushStroke StrokeAt(float x, float y) => new BrushStroke
        {
            Tool = Current switch
            {
                Mode.Lower => BrushTool.Lower,
                Mode.SetZ => BrushTool.Flatten,
                Mode.Soft => BrushTool.Smooth,
                Mode.Paint => BrushTool.Paint,
                _ => BrushTool.Raise
            },
            X = x,
            Y = y,
            Radius = Size,
            Softness = Softness,
            Strength = Strength,
            Material = Material,
            TargetZ = TargetZ
        };
    }
}
