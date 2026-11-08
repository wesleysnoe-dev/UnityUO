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
    /// The world editor's client side (WORLD_EDITOR.md, step 1), on with <c>-worldeditor</c>: says hello to the server,
    /// asks for the painted regions around the player, keeps them (<see cref="Region"/>, read by the terrain's chunk
    /// worker), overlays the tiles the server derived from them on this client's map (so walking and sprites agree with
    /// the server), and rebuilds the chunks a region covers when it arrives or changes. Unpainted ground is untouched.
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

        private const int Reach = 1;          // regions around the player's to keep
        private const float AskEvery = 1.5f;  // seconds between checks

        private readonly GameContext _ctx;
        private readonly GameSession _session;
        private readonly ConcurrentDictionary<(int, int, int), WorldRegion> _regions = new ConcurrentDictionary<(int, int, int), WorldRegion>();
        private readonly Dictionary<(int, int, int), int> _asked = new Dictionary<(int, int, int), int>(); // version asked with (-1 none)
        private readonly Dictionary<(int, int, int), int> _known = new Dictionary<(int, int, int), int>(); // server's latest version
        private int[][] _plain;
        private float _nextAsk;
        private bool _hello;

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

        /// <summary>A painted region, or null (worker threads too).</summary>
        public WorldRegion Region(int map, int rx, int ry) => _regions.TryGetValue((map, rx, ry), out WorldRegion r) ? r : null;

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
            if (Time.unscaledTime < _nextAsk)
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
                    int have = _regions.TryGetValue(key, out WorldRegion r) ? r.Version : -1;
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
            _ctx.Hud?.Log(null, canEdit ? "World editor: connected, you can edit." : "World editor: connected (view only).");
        }

        private void OnResult(bool ok, string message)
        {
            if (!string.IsNullOrEmpty(message))
                _ctx.Hud?.Log(null, "World editor: " + message);
        }

        private void OnChanged(int map, int rx, int ry, int version)
        {
            _known[(map, rx, ry)] = version;
            _nextAsk = 0f; // ask on the next frame
        }

        private void OnRegion(int map, int rx, int ry, int version, byte[] data)
        {
            var key = (map, rx, ry);
            _known[key] = version;
            if (data == null)
            {
                _asked[key] = -1;
                return; // nobody painted it
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
            _regions[key] = region;
            _asked[key] = region.Version;
            OverlayTiles(region);
            // The chunks under it (and one tile around, whose corners it shares) draw again.
            _ctx.World?.RebuildArea(region.TileX0 - 1, region.TileY0 - 1, region.TileX0 + WorldRegion.Tiles, region.TileY0 + WorldRegion.Tiles);
        }

        /// <summary>The server's tiles for the region's edited tiles, put on this client's map (same rules, same code).</summary>
        private void OverlayTiles(WorldRegion region)
        {
            MapFile map = _ctx.Data?.GetMap(region.Map);
            if (map == null)
                return;
            LandTileData[] land = _ctx.Data.TileData.Land;
            _plain ??= WorldMaterials.Plain(g => g < land.Length ? land[g].Name : "", land.Length);
            var store = new WorldStore(region.Map, (rx, ry) => Region(region.Map, rx, ry));
            for (int i = 0; i < region.Edited.Length; i++)
            {
                if (!region.Edited[i])
                    continue;
                int x = region.TileX0 + i % WorldRegion.Tiles, y = region.TileY0 + i / WorldRegion.Tiles;
                if (x >= map.Width || y >= map.Height)
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
}
