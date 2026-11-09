using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.ArtPipeline;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Opt-in replacement for LAND emission only (LiveGroundCompare, TEST host). Statics, mobiles and roofs never
    /// pass through it; without an emitter the streamer draws land exactly as before.
    /// </summary>
    public interface ILandEmitter
    {
        /// <summary>Worker thread: whether this placement leaves the original sprite path.</summary>
        bool Diverts(in DrawItem item, bool baked);

        /// <summary>Worker thread, no Unity API: the chunk's diverted land (with production depth). Null draws nothing for it.</summary>
        ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land);
    }

    /// <summary>A chunk's replacement land; owned and disposed by the chunk.</summary>
    public interface ILandChunk : IDisposable
    {
        /// <summary>Main thread: creates the land objects under the chunk (its transform carries the depth origin).</summary>
        void Attach(Transform chunk, Shader shader, IWorldSpace world);
    }

    /// <summary>
    /// LiveGroundCompare's controller as WorldDemo sees it. Created by type name only under -liveground (this assembly
    /// does not reference LiveGroundCompare), so without the flag nothing of it runs.
    /// </summary>
    public interface ILandBackendSwitch : IDisposable
    {
        /// <summary>A world was created for a session on <paramref name="host"/>; returns a status line for the journal.</summary>
        string Attach(WorldStreamer world, string host);

        /// <summary>The world is going away.</summary>
        void Detach();

        /// <summary>Every frame: handles the toggle key; returns a status line when the backend changed, else null.</summary>
        string Tick();
    }

    /// <summary>One diverted land placement: the compositor's item and art (texmap when stretched) and its sort depth.</summary>
    public struct LandPlacement
    {
        public DrawItem Item;
        public Bitmap32 Art;
        public bool Texmap;
        public float Depth;
    }

    /// <summary>
    /// Streams the map around the player in 16x16-tile chunks. A worker thread composes each chunk
    /// (draw list, sloped-land rasterizing, pixel conversion and packing); the main thread only
    /// uploads finished textures and builds meshes, a bounded amount per frame, so walking never
    /// stalls. Chunks far behind the player are unloaded with their textures.
    /// Shared art (every copy of a graphic) lives in one growing atlas; art unique to a placement
    /// (sloped land, baked placements) lives in the chunk's own texture and goes away with it.
    /// </summary>
    public sealed class WorldStreamer : IWorldSpace, IDisposable
    {
        public const int ChunkSize = 16;

        private readonly UOData _data;
        private readonly Transform _root;
        private readonly Shader _shader;
        private readonly int _scale;
        private readonly Func<IsoCompositor> _compositorFactory;
        private readonly Vector2Int _originPixels;

        private readonly Dictionary<(int, int), Chunk> _chunks = new Dictionary<(int, int), Chunk>();
        private readonly HashSet<(int, int)> _requested = new HashSet<(int, int)>();
        private readonly List<(int, int)> _queue = new List<(int, int)>(); // guarded by itself
        private readonly ConcurrentQueue<ChunkBuild> _results = new ConcurrentQueue<ChunkBuild>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _worker;
        private volatile bool _stopping;
        private volatile int _focusX, _focusY;
        private int _map;
        private int _generation;

        // Shared sprites: the worker claims each bitmap once and sends its pixels; the main thread places it.
        private readonly DynamicAtlas _shared = new DynamicAtlas(1024);
        private readonly HashSet<Bitmap32> _claimed = new HashSet<Bitmap32>(); // worker only
        private readonly Dictionary<Bitmap32, AtlasSlot> _sharedSlots = new Dictionary<Bitmap32, AtlasSlot>();
        private readonly Dictionary<int, Material> _sharedMaterials = new Dictionary<int, Material>();

        private int _originDiagonal;
        private bool _roofsVisible = true;
        private ILandEmitter _landEmitter; // guarded by _queue

        public WorldStreamer(UOData data, Transform parent, Shader shader, int scale, Func<IsoCompositor> compositorFactory, int map, int originX, int originY)
        {
            _data = data;
            _shader = shader;
            _scale = scale;
            _compositorFactory = compositorFactory;
            _map = map;
            _originPixels = new Vector2Int((originX - originY) * 22 * scale, (originX + originY) * 22 * scale);
            _originDiagonal = originX + originY;
            _root = new GameObject("World").transform;
            _root.SetParent(parent, false);
            _focusX = originX;
            _focusY = originY;

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "UO chunk builder" };
            _worker.Start();
        }

        /// <summary>Tiles around the player kept loaded (rounded out to whole chunks).</summary>
        public int LoadRadius { get; set; } = 40;

        /// <summary>Main-thread milliseconds per frame spent integrating finished chunks.</summary>
        public float IntegrateBudgetMs { get; set; } = 4f;

        public int Map => _map;

        /// <summary>The data files this streamer composes from (tiledata overrides included). Read-only view.</summary>
        public UOData Data => _data;

        public int LoadedChunks => _chunks.Count;
        public int PendingChunks => _requested.Count;
        public bool RoofsVisible => _roofsVisible;

        /// <summary>Total time the worker spent building, and chunks built (for diagnostics).</summary>
        public double WorkerMs { get; private set; }
        public int ChunksBuilt { get; private set; }

        /// <summary>Unity units are output pixels at the art scale, measured from the session origin; screen Y flipped up.</summary>
        public Rect ToUnityRect(float left, float top, float width, float height)
        {
            float x = left - _originPixels.x;
            float yTop = -(top - _originPixels.y);
            return new Rect(x, yTop - height, width, height);
        }

        public Vector2 ToUnity(Vector2 worldPixels) => new Vector2(worldPixels.x - _originPixels.x, -(worldPixels.y - _originPixels.y));

        public Vector2 FromUnity(Vector2 unity) => new Vector2(unity.x + _originPixels.x, _originPixels.y - unity.y);

        /// <summary>The map tile under a world-screen pixel, assuming ground at height <paramref name="z"/>.</summary>
        public Vector2Int TileAt(Vector2 worldPixels, int z)
        {
            float d = worldPixels.x / (22f * _scale);                       // x - y
            float sum = (worldPixels.y / _scale - 22f + z * 4f) / 22f;       // x + y
            return new Vector2Int(Mathf.RoundToInt((sum + d) / 2f), Mathf.RoundToInt((sum - d) / 2f));
        }

        public float DepthFor(DrawItem item) => DepthKey.Relative(item, _originDiagonal, 0);

        /// <summary>True when the chunks around (x, y) are loaded, i.e. the player can be shown there.</summary>
        public bool IsReadyAround(int x, int y)
        {
            int cx = Math.Max(0, x) / ChunkSize, cy = Math.Max(0, y) / ChunkSize;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!_chunks.ContainsKey((cx + dx, cy + dy)) && cx + dx >= 0 && cy + dy >= 0)
                        return false;
            return true;
        }

        /// <summary>Switches facets: everything is unloaded and rebuilt for the new map.</summary>
        public void SetMap(int map)
        {
            if (map == _map)
                return;
            _map = map;
            Rebuild();
        }

        /// <summary>LiveGroundCompare: swaps how land is emitted (null = original) and rebuilds the chunks; nothing else moves.</summary>
        public ILandEmitter LandEmitter
        {
            get => _landEmitter;
            set
            {
                if (value == _landEmitter)
                    return;
                lock (_queue)
                    _landEmitter = value;
                Rebuild();
            }
        }

        // Per-chunk rebuild counts (RebuildArea), read by the worker under the queue lock.
        private readonly Dictionary<(int, int), int> _chunkSeq = new Dictionary<(int, int), int>();

        /// <summary>
        /// Rebuilds the chunks covering tiles (x0, y0) .. (x1, y1) inclusive (the world editor changed the ground there):
        /// a loaded chunk stays on screen and is swapped for its new build in one frame when that is ready (no gap);
        /// builds already running for them are discarded.
        /// </summary>
        public void RebuildArea(int x0, int y0, int x1, int y1)
        {
            bool added = false;
            for (int cy = Math.Max(0, y0 / ChunkSize); cy <= y1 / ChunkSize; cy++)
                for (int cx = Math.Max(0, x0 / ChunkSize); cx <= x1 / ChunkSize; cx++)
                {
                    var key = (cx, cy);
                    bool loaded = _chunks.ContainsKey(key);
                    lock (_queue)
                    {
                        _chunkSeq[key] = (_chunkSeq.TryGetValue(key, out int s) ? s : 0) + 1;
                        _queue.Remove(key);
                        if (loaded)
                            _queue.Add(key); // a loaded chunk is not requested by Update: ask for its new build here
                    }
                    if (loaded)
                    {
                        _replacing.Add(key);
                        _requested.Add(key);
                        added = true;
                    }
                    else
                        _requested.Remove(key);
                }
            if (added)
                _wake.Set();
            if (added && EditProfile.On)
            {
                _batchStarted = Time.realtimeSinceStartup;
                Debug.Log($"[editprofile] rebuild batch: {_replacing.Count} chunk(s) for tiles {x0},{y0}..{x1},{y1}");
            }
        }

        private float _batchStarted;

        // Loaded chunks whose new build (RebuildArea) replaces them when it is ready.
        private readonly HashSet<(int, int)> _replacing = new HashSet<(int, int)>();

        private void Rebuild()
        {
            _generation++;
            foreach (Chunk c in _chunks.Values)
                c.Dispose();
            _chunks.Clear();
            _requested.Clear();
            _replacing.Clear();
            foreach (ChunkBuild b in _held.Values)
                b.Land?.Dispose();
            _held.Clear();
            lock (_queue)
                _queue.Clear();
        }

        public void SetRoofsVisible(bool visible)
        {
            if (visible == _roofsVisible)
                return;
            _roofsVisible = visible;
            foreach (Chunk c in _chunks.Values)
                c.SetRoofsVisible(visible);
        }

        /// <summary>Call every frame with the player's tile: requests, integrates and unloads chunks.</summary>
        public void Update(int playerX, int playerY)
        {
            _focusX = playerX;
            _focusY = playerY;

            // Keep depth values small: re-base the depth origin as the player travels.
            if (Math.Abs(playerX + playerY - _originDiagonal) > 64)
            {
                _originDiagonal = playerX + playerY;
                foreach (Chunk c in _chunks.Values)
                    c.SetDepthOrigin(_originDiagonal);
            }

            int r = (LoadRadius + ChunkSize - 1) / ChunkSize;
            int pcx = playerX / ChunkSize, pcy = playerY / ChunkSize;

            // Request missing chunks (the worker always takes the one nearest the player).
            bool added = false;
            for (int cy = pcy - r; cy <= pcy + r; cy++)
                for (int cx = pcx - r; cx <= pcx + r; cx++)
                {
                    if (cx < 0 || cy < 0 || _chunks.ContainsKey((cx, cy)) || !_requested.Add((cx, cy)))
                        continue;
                    lock (_queue)
                        _queue.Add((cx, cy));
                    added = true;
                }
            if (added)
                _wake.Set();

            // Integrate finished chunks within the frame budget (always at least one).
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int integrated = 0;
            while ((integrated == 0 || watch.Elapsed.TotalMilliseconds < IntegrateBudgetMs) && _results.TryDequeue(out ChunkBuild build))
            {
                Integrate(build, pcx, pcy, r);
                integrated++;
                EditProfile.Add("worker.build", build.BuildMs);
                if (EditProfile.On && build.BuildMs > 100 && _replacing.Contains((build.ChunkX, build.ChunkY)))
                    Debug.Log($"[editprofile] chunk {build.ChunkX},{build.ChunkY} built in {build.BuildMs:0} ms: draw list {build.DrawListMs:0}, " +
                              $"quads {build.QuadsMs:0}, land {build.LandMs:0}, entries {build.Quads.Count}");
            }
            if (integrated > 0)
                EditProfile.Add("integrate", watch.Elapsed.TotalMilliseconds, integrated);
            _shared.Apply();
            Animate();

            // Unload chunks well outside the radius (a margin avoids load/unload churn at the edge).
            List<(int, int)> unload = null;
            foreach (KeyValuePair<(int, int), Chunk> kv in _chunks)
                if (Math.Max(Math.Abs(kv.Key.Item1 - pcx), Math.Abs(kv.Key.Item2 - pcy)) > r + 1)
                    (unload ??= new List<(int, int)>()).Add(kv.Key);
            if (unload != null)
                foreach ((int, int) key in unload)
                {
                    _chunks[key].Dispose();
                    _chunks.Remove(key);
                    _replacing.Remove(key);
                    if (_held.Remove(key, out ChunkBuild heldBuild))
                        heldBuild.Land?.Dispose();
                }
            if (unload != null && _replacing.Count > 0)
                FlushBatchIfReady();
        }

        private void Integrate(ChunkBuild build, int pcx, int pcy, int r)
        {
            // Shared sprites are always placed, even for a discarded chunk: later chunks may reuse them.
            foreach (SharedSprite s in build.NewShared)
                _sharedSlots[s.Bitmap] = _shared.Add(s.Width, s.Height, s.Colors);

            _requested.Remove((build.ChunkX, build.ChunkY));
            WorkerMs += build.BuildMs;
            ChunksBuilt++;
            int currentSeq;
            lock (_queue)
                currentSeq = _chunkSeq.TryGetValue((build.ChunkX, build.ChunkY), out int cs) ? cs : 0;
            var chunkKey = (build.ChunkX, build.ChunkY);
            bool replacing = _replacing.Contains(chunkKey);
            bool stale = build.Generation != _generation
                         || build.Seq != currentSeq
                         || Math.Max(Math.Abs(build.ChunkX - pcx), Math.Abs(build.ChunkY - pcy)) > r + 1
                         || _chunks.ContainsKey(chunkKey) && !replacing;
            if (stale)
            {
                build.Land?.Dispose();
                // A batch member that will not come (out of range now, or the world moved on): the old chunk stays.
                if (replacing && build.Seq == currentSeq)
                {
                    _replacing.Remove(chunkKey);
                    FlushBatchIfReady();
                }
                return;
            }
            // RebuildArea: the old chunks stay on screen until every new build of the batch is in; then they all swap in
            // this frame, so neighbouring chunks never show a crack between old and new edges.
            if (replacing)
            {
                _held[chunkKey] = build;
                FlushBatchIfReady();
                return;
            }
            Attach(build);
        }

        // Rebuilt chunks waiting for the rest of their batch (RebuildArea).
        private readonly Dictionary<(int, int), ChunkBuild> _held = new Dictionary<(int, int), ChunkBuild>();

        /// <summary>Whether a RebuildArea batch is still being built (the world editor waits before sending the next).</summary>
        public bool Rebuilding => _replacing.Count > 0;

        private void FlushBatchIfReady()
        {
            foreach ((int, int) k in _replacing)
                if (!_held.ContainsKey(k))
                    return; // still waiting for others
            var ready = new List<ChunkBuild>(_held.Values);
            _held.Clear();
            _replacing.Clear();
            var swapWatch = System.Diagnostics.Stopwatch.StartNew();
            foreach (ChunkBuild b in ready)
            {
                var bk = (b.ChunkX, b.ChunkY);
                if (_chunks.TryGetValue(bk, out Chunk old))
                {
                    old.Dispose();
                    _chunks.Remove(bk);
                }
                Attach(b);
            }
            EditProfile.Add("batch.swap", swapWatch.Elapsed.TotalMilliseconds, ready.Count);
            if (EditProfile.On && ready.Count > 0)
                Debug.Log($"[editprofile] rebuild batch swapped in: {ready.Count} chunk(s), {swapWatch.Elapsed.TotalMilliseconds:0} ms, " +
                          $"{(Time.realtimeSinceStartup - _batchStarted) * 1000f:0} ms after it started");
        }

        /// <summary>Puts a finished, current build on screen as its chunk.</summary>
        private void Attach(ChunkBuild build)
        {
            var chunk = new Chunk(_root, build.ChunkX, build.ChunkY, build.BaseDiagonal);
            Texture2D unique = null;
            if (build.UniqueColors != null)
            {
                unique = new Texture2D(build.UniqueWidth, build.UniqueHeight, TextureFormat.RGBA32, false)
                {
                    name = $"UO chunk {build.ChunkX},{build.ChunkY}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                unique.SetPixels32(build.UniqueColors);
                unique.Apply(false, true);
                chunk.UniqueTexture = unique;
            }

            // Group quads by texture (shared page or the chunk's own) and roof.
            var groups = new Dictionary<(int, bool), MeshBuilder>();
            foreach (ChunkQuad q in build.Quads)
            {
                int texture;
                Vector4 uv;
                if (q.Unique)
                {
                    texture = -1;
                    float w = build.UniqueWidth, h = build.UniqueHeight;
                    uv = new Vector4(q.U / w, 1f - (q.V + q.Height) / h, (q.U + q.Width) / w, 1f - q.V / h);
                }
                else
                {
                    if (!_sharedSlots.TryGetValue(q.Bitmap, out AtlasSlot slot))
                        continue;
                    texture = slot.Page;
                    uv = _shared.Uv(slot);
                }

                var key = (texture, q.Roof);
                if (!groups.TryGetValue(key, out MeshBuilder mb))
                    groups[key] = mb = new MeshBuilder();
                Rect rect = ToUnityRect(q.Left, q.Top, q.Width, q.Height);
                if (MeshBuilder.FootMetadata) // LiveGroundCompare sprite shadow (TEST); false by default
                {
                    Vector2 foot = ToUnity(new Vector2(q.FootX, q.FootY));
                    mb.AddQuad(rect, uv, q.Depth, false, q.CutHeight, new Vector4(foot.x, foot.y, q.FootZ, q.FootKind));
                }
                else
                    mb.AddQuad(rect, uv, q.Depth, false, q.CutHeight);
            }

            AttachShadows(build, chunk, unique);

            foreach (KeyValuePair<(int, bool), MeshBuilder> g in groups)
            {
                Material material;
                if (g.Key.Item1 < 0)
                {
                    material = new Material(_shader) { mainTexture = unique, name = $"UO chunk {build.ChunkX},{build.ChunkY}" };
                    chunk.OwnedMaterials.Add(material);
                }
                else if (!_sharedMaterials.TryGetValue(g.Key.Item1, out material))
                {
                    material = new Material(_shader) { mainTexture = _shared.Page(g.Key.Item1), name = $"UO shared page {g.Key.Item1}" };
                    _sharedMaterials[g.Key.Item1] = material;
                }
                chunk.AddMesh(g.Value.Build($"Chunk {build.ChunkX},{build.ChunkY}"), material, g.Key.Item2);
            }
            chunk.Lights = build.Lights.ToArray();
            double now = ItemView.AnimationClockMs;
            foreach (AnimatedBuild a in build.Animated)
            {
                AnimatedQuad q = chunk.AddAnimated(a, _shader);
                ShowFrame(q, ArtAnimation.Frame(a.Entry, now));
            }
            if (build.Land != null)
            {
                chunk.Land = build.Land;
                build.Land.Attach(chunk.Transform, _shader, this);
            }

            chunk.SetDepthOrigin(_originDiagonal);
            chunk.SetRoofsVisible(_roofsVisible);
            _chunks[(build.ChunkX, build.ChunkY)] = chunk;
        }

        // Free-standing map objects' outline shadows (CharacterShadow): read by the worker, fixed for the session.
        private readonly bool _outlineShadows = CharacterShadow.ShadowShader != null;
        private readonly Dictionary<int, Material> _sharedShadowMaterials = new Dictionary<int, Material>();

        /// <summary>The chunk's outline shadows: each casting static's art laid on the ground from its base, one mesh per texture.</summary>
        private void AttachShadows(ChunkBuild build, Chunk chunk, Texture2D unique)
        {
            Shader shader = CharacterShadow.ShadowShader;
            if (shader == null)
                return;
            Vector2 perPixel = CharacterShadow.PerPixel();
            var groups = new Dictionary<int, (List<Vector3> V, List<Vector2> Uv, List<int> I)>();
            foreach (ChunkQuad q in build.Quads)
            {
                if (!q.Shadow)
                    continue;
                int texture;
                Vector4 uv;
                if (q.Unique)
                {
                    texture = -1;
                    float w = build.UniqueWidth, h = build.UniqueHeight;
                    uv = new Vector4(q.U / w, 1f - (q.V + q.Height) / h, (q.U + q.Width) / w, 1f - q.V / h);
                }
                else
                {
                    if (!_sharedSlots.TryGetValue(q.Bitmap, out AtlasSlot slot))
                        continue;
                    texture = slot.Page;
                    uv = _shared.Uv(slot);
                }
                if (!groups.TryGetValue(texture, out var g))
                    groups[texture] = g = (new List<Vector3>(), new List<Vector2>(), new List<int>());
                // An object stands on the bottom of its art (statics are drawn with their foot on the tile's lower corner).
                CharacterShadow.AddQuad(g.V, g.Uv, g.I, this, perPixel, uv, false, q.Left, q.Top, q.Width, q.Height, q.Top + q.Height, q.Depth);
            }
            foreach (KeyValuePair<int, (List<Vector3> V, List<Vector2> Uv, List<int> I)> g in groups)
            {
                Material material;
                if (g.Key < 0)
                {
                    if (unique == null)
                        continue;
                    material = new Material(shader) { mainTexture = unique, name = $"UO chunk shadows {build.ChunkX},{build.ChunkY}" };
                    chunk.OwnedMaterials.Add(material);
                }
                else if (!_sharedShadowMaterials.TryGetValue(g.Key, out material))
                {
                    material = new Material(shader) { mainTexture = _shared.Page(g.Key), name = $"UO shared page {g.Key} shadows" };
                    _sharedShadowMaterials[g.Key] = material;
                }
                var mesh = new Mesh { name = $"Chunk {build.ChunkX},{build.ChunkY} shadows", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(g.Value.V);
                mesh.SetUVs(0, g.Value.Uv);
                mesh.SetTriangles(g.Value.I, 0);
                mesh.RecalculateBounds();
                chunk.AddMesh(mesh, material, false);
            }
        }

        // ---- worker ----

        private void WorkerLoop()
        {
            IsoCompositor compositor = null;
            TileData tileData = null;
            while (!_stopping)
            {
                (int, int) next;
                int generation, map, seq;
                ILandEmitter landEmitter;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                        next = (-1, -1);
                    else
                    {
                        // Nearest to the player first.
                        int best = 0, bestDistance = int.MaxValue, fx = _focusX / ChunkSize, fy = _focusY / ChunkSize;
                        for (int i = 0; i < _queue.Count; i++)
                        {
                            int d = Math.Max(Math.Abs(_queue[i].Item1 - fx), Math.Abs(_queue[i].Item2 - fy));
                            if (d < bestDistance)
                            {
                                best = i;
                                bestDistance = d;
                            }
                        }
                        next = _queue[best];
                        _queue.RemoveAt(best);
                    }
                    generation = _generation;
                    map = _map;
                    landEmitter = _landEmitter;
                    seq = _chunkSeq.TryGetValue(next, out int s) ? s : 0;
                }

                if (next.Item1 < 0)
                {
                    _wake.WaitOne(250);
                    continue;
                }

                try
                {
                    compositor ??= _compositorFactory();
                    tileData ??= _data.TileData;
                    ChunkBuild built = BuildChunk(compositor, tileData, map, next.Item1, next.Item2, generation, landEmitter);
                    built.Seq = seq;
                    _results.Enqueue(built);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _results.Enqueue(new ChunkBuild { ChunkX = next.Item1, ChunkY = next.Item2, Generation = -1 });
                }
            }
        }

        private ChunkBuild BuildChunk(IsoCompositor compositor, TileData tileData, int map, int cx, int cy, int generation, ILandEmitter landEmitter)
        {
            List<LandPlacement> land = landEmitter != null ? new List<LandPlacement>() : null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int x0 = cx * ChunkSize, y0 = cy * ChunkSize;
            List<IsoCompositor.DrawEntry> entries = compositor.BuildDrawList(map, x0, y0, ChunkSize, ChunkSize);
            var build = new ChunkBuild { ChunkX = cx, ChunkY = cy, Generation = generation, BaseDiagonal = x0 + y0 };
            build.DrawListMs = watch.Elapsed.TotalMilliseconds;

            // Unique art (sloped land, baked placements) is packed into the chunk's own texture.
            var uniques = new List<(Bitmap32 Bitmap, int Quad)>();
            int tie = 0, lastDiagonal = int.MinValue, lastPriority = int.MinValue;
            DrawKind lastKind = DrawKind.Land;
            foreach (IsoCompositor.DrawEntry e in entries)
            {
                if (e.Sprite == null)
                    continue;
                DrawItem it = e.Item;
                bool unique = e.Baked || it.Stretched;
                bool divert = land != null && landEmitter.Diverts(it, e.Baked); // LiveGroundCompare; false by default
                Bitmap32 bmp = it.Stretched && !e.Baked && !divert ? compositor.RenderPlacementAlone(it) : e.Sprite;
                if (bmp == null)
                    continue;

                int diagonal = it.X + it.Y;
                tie = diagonal == lastDiagonal && it.PriorityZ == lastPriority && it.Kind == lastKind ? tie + 1 : 0;
                lastDiagonal = diagonal;
                lastPriority = it.PriorityZ;
                lastKind = it.Kind;

                // LiveGroundCompare: diverted land keeps the exact depth it would have had as a sprite quad.
                if (divert)
                {
                    land.Add(new LandPlacement { Item = it, Art = e.Sprite, Texmap = it.Stretched, Depth = DepthKey.Relative(it, build.BaseDiagonal, tie) });
                    continue;
                }

                // A light source (LightSources): a map static lights by its tiledata Layer (ItemLights picks the nearest).
                if (it.Kind == DrawKind.Static && LightSources.IsLight(tileData.GetStatic(it.Graphic).Flags))
                    build.Lights.Add(new StaticLight { X = it.X, Y = it.Y, Z = it.Z, Graphic = it.Graphic, Id = tileData.GetStatic(it.Graphic).Layer });

                // Animated art (ArtAnimation): out of the baked mesh into its own quad, every frame's sprite sent now, so
                // the main thread only swaps one quad's rect and UV when its frame changes. Baked (HD placement) art stays.
                if (it.Kind == DrawKind.Static && !unique && !divert)
                {
                    StaticTileData std = tileData.GetStatic(it.Graphic);
                    AnimDataEntry ae = (std.Flags & TileFlags.Animation) != 0 ? _data.AnimData.Get(it.Graphic) : default;
                    if (ArtAnimation.Animates(std.Flags, ae))
                    {
                        build.Animated.Add(AnimatedStatic(compositor, build, it, bmp, ae, DepthKey.Relative(it, build.BaseDiagonal, tie), std.IsRoof));
                        continue;
                    }
                }

                var quad = new ChunkQuad
                {
                    Left = it.Left,
                    Top = it.Top,
                    Width = it.Width,
                    Height = it.Height,
                    Depth = DepthKey.Relative(it, build.BaseDiagonal, tie),
                    Roof = it.Kind == DrawKind.Static && tileData.GetStatic(it.Graphic).IsRoof,
                    // Statics are cut away above the floor overhead when indoors; land always draws.
                    CutHeight = it.Kind == DrawKind.Static ? MeshBuilder.CutHeight(it.Z) : MeshBuilder.NeverCut,
                    Unique = unique,
                    Bitmap = unique ? null : bmp,
                    // LiveGroundCompare sprite shadow: the tile centre the sprite stands on (used only while MeshBuilder.FootMetadata).
                    FootX = (it.X - it.Y) * 22f * _scale,
                    FootY = ((it.X + it.Y) * 22f + 22f - it.Z * 4f) * _scale,
                    FootZ = it.Z,
                    FootKind = it.Kind == DrawKind.Static && tileData.GetStatic(it.Graphic).Height > 1 ? 1 : 2, // upright : ground
                    Shadow = _outlineShadows && it.Kind == DrawKind.Static && ShadowRules.CastsOutline(tileData.GetStatic(it.Graphic), (int)(it.Height / _scale))
                };
                if (unique)
                    uniques.Add((bmp, build.Quads.Count));
                else if (_claimed.Add(bmp))
                    build.NewShared.Add(new SharedSprite { Bitmap = bmp, Width = bmp.Width, Height = bmp.Height, Colors = DynamicAtlas.ToColors(bmp) });
                build.Quads.Add(quad);
            }

            build.QuadsMs = watch.Elapsed.TotalMilliseconds - build.DrawListMs;
            if (uniques.Count > 0)
                PackUniques(build, uniques);
            double beforeLand = watch.Elapsed.TotalMilliseconds;
            if (land != null && land.Count > 0)
                build.Land = landEmitter.BuildLand(cx, cy, land);
            build.LandMs = watch.Elapsed.TotalMilliseconds - beforeLand;

            build.BuildMs = watch.Elapsed.TotalMilliseconds;
            return build;
        }

        // Worker: one animated static, each animdata frame's art (claimed into the shared atlas) and rect.
        private AnimatedBuild AnimatedStatic(IsoCompositor compositor, ChunkBuild build, DrawItem it, Bitmap32 baseArt, AnimDataEntry entry, float depth, bool roof)
        {
            var frames = new AnimatedFrame[entry.Offsets.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                DrawItem f = it;
                f.Graphic = (ushort)((it.Graphic + entry.Offsets[i]) & 0xFFFF);
                Bitmap32 art = compositor.StaticSprite(f.Graphic) ?? baseArt;
                if (_claimed.Add(art))
                    build.NewShared.Add(new SharedSprite { Bitmap = art, Width = art.Width, Height = art.Height, Colors = DynamicAtlas.ToColors(art) });
                (f.Left, f.Top, f.Width, f.Height) = IsoCompositor.PlacementRect(f, _scale, art.Width / _scale, art.Height / _scale);
                frames[i] = new AnimatedFrame { Bitmap = art, Graphic = f.Graphic, Left = f.Left, Top = f.Top, Width = f.Width, Height = f.Height };
            }
            return new AnimatedBuild
            {
                Entry = entry, Frames = frames, Depth = depth, Roof = roof, CutHeight = MeshBuilder.CutHeight(it.Z),
                Foot = new Vector4((it.X - it.Y) * 22f * _scale, ((it.X + it.Y) * 22f + 22f - it.Z * 4f) * _scale, it.Z,
                    _data.TileData.GetStatic(it.Graphic).Height > 1 ? 1 : 2)
            };
        }

        /// <summary>Animated statics in loaded chunks, frame swaps in the last <see cref="Update"/>, and its cost (ms).</summary>
        public int AnimatedStatics { get; private set; }
        public int AnimatedSwaps { get; private set; }
        public double AnimateMs { get; private set; }

        // Main thread, every frame: each animated static whose frame changed takes that frame's rect and UV. O(animated
        // statics loaded), a few integer compares each; mesh writes only on a change (a frame lasts 100 ms or more).
        private void Animate()
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            double now = ItemView.AnimationClockMs;
            int count = 0, swaps = 0;
            foreach (Chunk c in _chunks.Values)
                foreach (AnimatedQuad a in c.Animated)
                {
                    count++;
                    int frame = ArtAnimation.Frame(a.Entry, now);
                    if (frame == a.Shown)
                        continue;
                    ShowFrame(a, frame);
                    swaps++;
                }
            AnimatedStatics = count;
            AnimatedSwaps = swaps;
            AnimateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private void ShowFrame(AnimatedQuad a, int frame)
        {
            AnimatedFrame f = a.Frames[frame];
            if (!_sharedSlots.TryGetValue(f.Bitmap, out AtlasSlot slot))
                return; // its art is not placed yet (another chunk's build claimed it): keep what is shown, try again next frame
            a.Shown = frame;
            Rect rect = ToUnityRect(f.Left, f.Top, f.Width, f.Height);
            Vector2 foot = ToUnity(new Vector2(a.Foot.x, a.Foot.y));
            MeshBuilder.SetQuad(a.Mesh, rect, _shared.Uv(slot), a.Depth, false, a.CutHeight, new Vector4(foot.x, foot.y, a.Foot.z, a.Foot.w));
            a.Renderer.sharedMaterial = SharedMaterial(slot.Page);
            a.Renderer.enabled = true;
        }

        private Material SharedMaterial(int page)
        {
            if (!_sharedMaterials.TryGetValue(page, out Material material))
            {
                material = new Material(_shader) { mainTexture = _shared.Page(page), name = $"UO shared page {page}" };
                _sharedMaterials[page] = material;
            }
            return material;
        }

        /// <summary>Shelf-packs the chunk's unique sprites into one texture (1024 wide) and converts it to Unity colors.</summary>
        private static void PackUniques(ChunkBuild build, List<(Bitmap32 Bitmap, int Quad)> uniques)
        {
            const int width = 1024, pad = 1;
            int x = 0, y = 0, shelf = 0;
            var placed = new (int X, int Y)[uniques.Count];
            for (int i = 0; i < uniques.Count; i++)
            {
                Bitmap32 b = uniques[i].Bitmap;
                if (x + b.Width + pad > width)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                placed[i] = (x, y);
                x += b.Width + pad;
                shelf = Math.Max(shelf, b.Height + pad);
            }
            int height = Math.Max(1, y + shelf);
            height = (height + 63) / 64 * 64;

            var colors = new Color32[width * height];
            for (int i = 0; i < uniques.Count; i++)
            {
                Bitmap32 b = uniques[i].Bitmap;
                (int px, int py) = placed[i];
                for (int row = 0; row < b.Height; row++)
                {
                    int dst = (height - 1 - (py + row)) * width + px;
                    int src = row * b.Width;
                    for (int col = 0; col < b.Width; col++)
                    {
                        uint p = b.Pixels[src + col];
                        colors[dst + col] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
                    }
                }
                ChunkQuad q = build.Quads[uniques[i].Quad];
                q.U = px;
                q.V = py;
                build.Quads[uniques[i].Quad] = q;
            }
            build.UniqueColors = colors;
            build.UniqueWidth = width;
            build.UniqueHeight = height;
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            _worker.Join(1000);
            foreach (Chunk c in _chunks.Values)
                c.Dispose();
            _chunks.Clear();
            foreach (Material m in _sharedMaterials.Values)
                UnityEngine.Object.Destroy(m);
            foreach (Material m in _sharedShadowMaterials.Values)
                UnityEngine.Object.Destroy(m);
            _shared.Dispose();
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }

        private struct ChunkQuad
        {
            public float Left, Top, Width, Height, Depth, CutHeight;
            public bool Roof, Unique;
            public Bitmap32 Bitmap; // shared art (key into the shared atlas)
            public int U, V;        // unique art position in the chunk texture
            public float FootX, FootY, FootZ, FootKind; // foot anchor (world-screen pixels, UO Z, kind) for the TEST sprite shadow
            public bool Shadow; // casts its own outline as a sun shadow (ShadowRules, CharacterShadow)
        }

        private struct SharedSprite
        {
            public Bitmap32 Bitmap;
            public int Width, Height;
            public Color32[] Colors;
        }

        private struct AnimatedFrame
        {
            public Bitmap32 Bitmap; // shared art (key into the shared atlas)
            public int Graphic, Left, Top, Width, Height;
        }

        /// <summary>Worker output: an animated static's frames and placement.</summary>
        private sealed class AnimatedBuild
        {
            public AnimDataEntry Entry;
            public AnimatedFrame[] Frames;
            public float Depth, CutHeight;
            public bool Roof;
            public Vector4 Foot; // world-screen x, y; UO Z; kind (TEST sprite shadow only)
        }

        /// <summary>A loaded animated static: its own quad, and the frame it shows (-1 before the first).</summary>
        private sealed class AnimatedQuad
        {
            public AnimDataEntry Entry;
            public AnimatedFrame[] Frames;
            public float Depth, CutHeight;
            public Vector4 Foot;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public int Shown = -1;
        }

        private struct StaticLight
        {
            public int X, Y, Z, Graphic, Id;
        }

        /// <summary>The light-source statics of the loaded chunks, for <see cref="ItemLights"/> (main thread).</summary>
        public void AddStaticLights(ItemLights lights)
        {
            foreach (Chunk c in _chunks.Values)
                foreach (StaticLight l in c.Lights)
                    lights.Add(l.X, l.Y, l.Z, l.Graphic, l.Id);
        }

        private sealed class ChunkBuild
        {
            public int ChunkX, ChunkY, Generation, BaseDiagonal;

            /// <summary>The chunk's rebuild count when this build started (<see cref="RebuildArea"/>); older: stale.</summary>
            public int Seq;
            public readonly List<ChunkQuad> Quads = new List<ChunkQuad>();
            public readonly List<StaticLight> Lights = new List<StaticLight>();
            public readonly List<AnimatedBuild> Animated = new List<AnimatedBuild>();
            public readonly List<SharedSprite> NewShared = new List<SharedSprite>();
            public Color32[] UniqueColors;
            public int UniqueWidth, UniqueHeight;
            public double BuildMs, DrawListMs, QuadsMs, LandMs;
            public ILandChunk Land; // LiveGroundCompare only
        }

        private sealed class Chunk : IDisposable
        {
            private readonly GameObject _go;
            private readonly List<GameObject> _roofs = new List<GameObject>();
            private readonly List<Mesh> _meshes = new List<Mesh>();
            private readonly int _baseDiagonal;

            public readonly List<Material> OwnedMaterials = new List<Material>();
            public Texture2D UniqueTexture;
            public ILandChunk Land;
            public readonly List<AnimatedQuad> Animated = new List<AnimatedQuad>();
            public StaticLight[] Lights = Array.Empty<StaticLight>();

            /// <summary>An animated static's quad under the chunk (so it takes the chunk's depth origin), hidden until a frame shows.</summary>
            public AnimatedQuad AddAnimated(AnimatedBuild a, Shader shader)
            {
                var go = new GameObject($"Animated 0x{a.Frames[0].Graphic:X4}");
                go.transform.SetParent(_go.transform, false);
                var mesh = new Mesh();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                _meshes.Add(mesh);
                if (a.Roof)
                    _roofs.Add(go);
                var q = new AnimatedQuad { Entry = a.Entry, Frames = a.Frames, Depth = a.Depth, CutHeight = a.CutHeight, Foot = a.Foot, Mesh = mesh, Renderer = r };
                Animated.Add(q);
                return q;
            }

            public Transform Transform => _go.transform;

            public Chunk(Transform parent, int cx, int cy, int baseDiagonal)
            {
                _go = new GameObject($"Chunk {cx},{cy}");
                _go.transform.SetParent(parent, false);
                _baseDiagonal = baseDiagonal;
            }

            public void AddMesh(Mesh mesh, Material material, bool roof)
            {
                var go = new GameObject(roof ? "Roofs" : "Ground");
                go.transform.SetParent(_go.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _meshes.Add(mesh);
                if (roof)
                    _roofs.Add(go);
            }

            public void SetDepthOrigin(int originDiagonal)
            {
                _go.transform.localPosition = new Vector3(0, 0, DepthKey.ChunkOffset(_baseDiagonal, originDiagonal));
            }

            public void SetRoofsVisible(bool visible)
            {
                foreach (GameObject r in _roofs)
                    r.SetActive(visible);
            }

            public void Dispose()
            {
                foreach (Mesh m in _meshes)
                    UnityEngine.Object.Destroy(m);
                foreach (Material m in OwnedMaterials)
                    UnityEngine.Object.Destroy(m);
                if (UniqueTexture != null)
                    UnityEngine.Object.Destroy(UniqueTexture);
                Land?.Dispose();
                UnityEngine.Object.Destroy(_go);
            }
        }
    }
}
