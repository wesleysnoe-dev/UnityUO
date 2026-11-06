using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;
using Debug = UnityEngine.Debug;

namespace UOClient.Unity
{
    public sealed class MobileFrame
    {
        public AtlasSlot Slot;
        public int AnchorX, AnchorY; // scaled pixels from the unmirrored frame's top-left
    }

    /// <summary>A ground item's sprite at the art scale, plus its original (1x) size for placement.</summary>
    public sealed class ItemSprite
    {
        public AtlasSlot Slot;
        public int OriginalWidth, OriginalHeight;
    }

    /// <summary>
    /// Shared character frames and ground-item sprites, built on a background thread. Frames are
    /// composited per (look, action, direction) and items per (graphic, hue), hued and upscaled to the
    /// art scale; the main thread only copies finished pixels into one growing atlas, within a time
    /// budget per frame. Until a request is ready, callers get null and keep showing what they had.
    /// </summary>
    public sealed class MobileArt : IDisposable
    {
        private readonly UOData _data;
        private readonly int _scale;
        private readonly DynamicAtlas _atlas = new DynamicAtlas(1024);

        // Main thread only.
        private readonly Dictionary<(string, int, int, int), MobileFrame[]> _frames = new Dictionary<(string, int, int, int), MobileFrame[]>();
        private readonly HashSet<(string, int, int, int)> _pendingFrames = new HashSet<(string, int, int, int)>();
        private readonly Dictionary<(int, int), ItemSprite> _items = new Dictionary<(int, int), ItemSprite>();
        private readonly HashSet<(int, int)> _pendingItems = new HashSet<(int, int)>();

        // Worker side.
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private readonly ConcurrentQueue<Result> _results = new ConcurrentQueue<Result>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _worker;
        private readonly Dictionary<(int, int, int), AnimationFrame[]> _raw = new Dictionary<(int, int, int), AnimationFrame[]>(); // worker only
        private volatile bool _stopping;

        public MobileArt(UOData data, int scale)
        {
            _data = data;
            _scale = scale;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "UO mobile art" };
            _worker.Start();
        }

        public DynamicAtlas Atlas => _atlas;

        /// <summary>Main-thread milliseconds per frame spent copying finished art into the atlas.</summary>
        public float IntegrateBudgetMs { get; set; } = 2f;

        public int PendingRequests => _pendingFrames.Count + _pendingItems.Count;
        public double WorkerMs { get; private set; }
        public int Built { get; private set; }

        /// <summary>
        /// Walk/run/stand action numbers differ for humans, animals and monsters, and for UOP creatures in war mode;
        /// riders keep the mounted actions and the dead the peace ones (see <see cref="MobileActions.Select"/>).
        /// </summary>
        public static (int Walk, int Run, int Stand) Actions(MobileLook look, bool warMode = false, bool dead = false) =>
            MobileActions.Select(look.Mounted, look.BodyActions, look.WarActions, warMode, dead);

        // Human actions while riding.
        public const int MountedWalk = MobileActions.MountedWalk, MountedRun = MobileActions.MountedRun, MountedStand = MobileActions.MountedStand;

        /// <summary>What a mount plays while its rider plays <paramref name="riderAction"/>.</summary>
        private static int MountAction(MobileLook look, int riderAction)
        {
            var (walk, run, stand) = look.MountActions;
            return riderAction == MountedWalk ? walk : riderAction == MountedRun ? run : stand;
        }

        /// <summary>
        /// Frames for a look/action/direction: null while being built (requested on first call),
        /// an empty array if the body has no animation in anim.mul. <paramref name="layerDir"/> is the facing whose
        /// <see cref="MobileLook.UsedLayers"/> order stacks the worn layers (facings 0 and 6 share a file direction but
        /// not an order).
        /// </summary>
        public MobileFrame[] Get(MobileLook look, string key, int action, int fileDir, int layerDir)
        {
            int order = MobileLook.OrderClass(layerDir);
            if (_frames.TryGetValue((key, action, fileDir, order), out MobileFrame[] frames))
                return frames;
            if (_pendingFrames.Add((key, action, fileDir, order)))
                Enqueue(new Job { Look = look, Key = key, Action = action, FileDir = fileDir, Order = order });
            return null;
        }

        /// <summary>Asks for a direction's stand, walk and run frames ahead of need (the war set too when in war mode).</summary>
        public void Prefetch(MobileLook look, string key, int fileDir, int layerDir, bool warMode = false, bool dead = false)
        {
            var (walk, run, stand) = Actions(look, warMode, dead);
            Get(look, key, stand, fileDir, layerDir);
            Get(look, key, walk, fileDir, layerDir);
            Get(look, key, run, fileDir, layerDir);
            var peace = Actions(look);
            if (peace != (walk, run, stand))
            {
                // The fallbacks for war actions a body lacks.
                Get(look, key, peace.Stand, fileDir, layerDir);
                Get(look, key, peace.Walk, fileDir, layerDir);
                Get(look, key, peace.Run, fileDir, layerDir);
            }
        }

        /// <summary>
        /// Whether a world point (Unity units) is on an opaque pixel of a sprite drawn over <paramref name="drawn"/> (mirrored
        /// or not), as TazUO's pick tests the art's pixels; atlas pages stay readable on the CPU.
        /// </summary>
        public bool HitTest(Rect drawn, AtlasSlot slot, bool mirror, Vector2 p)
        {
            if (slot.Width <= 0 || slot.Height <= 0)
                return false;
            var px = ObjectHighlight.SpritePixel(p.x, p.y, drawn.xMin, drawn.yMin, drawn.width, drawn.height, slot.Width, slot.Height, mirror);
            return px.HasValue && _atlas.Opaque(slot, px.Value.X, px.Value.Y);
        }

        // Outlines of sprites already in the atlas, by the sprite's place there (main thread only).
        private readonly Dictionary<(int, int, int), AtlasSlot> _outlines = new Dictionary<(int, int, int), AtlasSlot>();

        /// <summary>The outline thickness in atlas pixels: one pixel of the original art.</summary>
        public int OutlineThickness => _scale;

        /// <summary>
        /// The outline (<see cref="ObjectHighlight.Outline"/>) of a sprite in the atlas, built from its pixels on first
        /// use: a character's frame is all its worn layers composited, so this is the whole equipped silhouette. It is
        /// <see cref="OutlineThickness"/> larger on each side than <paramref name="slot"/>.
        /// </summary>
        public AtlasSlot Outline(AtlasSlot slot)
        {
            if (_outlines.TryGetValue((slot.Page, slot.X, slot.Y), out AtlasSlot o))
                return o;
            Bitmap32 mask = _atlas.Read(slot);
            o = _atlas.Add(ObjectHighlight.Outline(mask, OutlineThickness, ObjectHighlight.OutlineArgb));
            _atlas.Apply();
            _outlines[(slot.Page, slot.X, slot.Y)] = o;
            return o;
        }

        /// <summary>A ground item's sprite: null while being built (requested on first call).</summary>
        public ItemSprite GetItem(int graphic, int hue)
        {
            if (_items.TryGetValue((graphic, hue), out ItemSprite sprite))
                return sprite;
            if (_pendingItems.Add((graphic, hue)))
                Enqueue(new Job { IsItem = true, Graphic = graphic, Hue = hue });
            return null;
        }

        /// <summary>Copies finished art into the atlas (within the budget) and uploads it; call once per frame before rendering.</summary>
        public void Flush()
        {
            var watch = Stopwatch.StartNew();
            int n = 0;
            while ((n == 0 || watch.Elapsed.TotalMilliseconds < IntegrateBudgetMs) && _results.TryDequeue(out Result r))
            {
                n++;
                WorkerMs += r.BuildMs;
                Built++;
                if (r.IsItem)
                {
                    _pendingItems.Remove((r.Graphic, r.Hue));
                    _items[(r.Graphic, r.Hue)] = r.Pixels == null ? null : new ItemSprite
                    {
                        Slot = _atlas.Add(r.Width, r.Height, r.Pixels),
                        OriginalWidth = r.Width / _scale,
                        OriginalHeight = r.Height / _scale
                    };
                    continue;
                }

                var frames = new MobileFrame[r.Frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    BuiltFrame f = r.Frames[i];
                    frames[i] = new MobileFrame { Slot = _atlas.Add(f.Width, f.Height, f.Pixels), AnchorX = f.AnchorX, AnchorY = f.AnchorY };
                }
                _pendingFrames.Remove((r.Key, r.Action, r.FileDir, r.Order));
                _frames[(r.Key, r.Action, r.FileDir, r.Order)] = frames;
            }
            _atlas.Apply();
        }

        private void Enqueue(Job job)
        {
            _jobs.Enqueue(job);
            _wake.Set();
        }

        // ---- worker ----

        private void WorkerLoop()
        {
            while (!_stopping)
            {
                if (!_jobs.TryDequeue(out Job job))
                {
                    _wake.WaitOne(250);
                    continue;
                }
                var watch = Stopwatch.StartNew();
                Result result;
                try
                {
                    result = job.IsItem ? BuildItem(job) : BuildFrames(job);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    result = job.IsItem
                        ? new Result { IsItem = true, Graphic = job.Graphic, Hue = job.Hue }
                        : new Result { Key = job.Key, Action = job.Action, FileDir = job.FileDir, Order = job.Order, Frames = Array.Empty<BuiltFrame>() };
                }
                result.BuildMs = watch.Elapsed.TotalMilliseconds;
                _results.Enqueue(result);
            }
        }

        private Result BuildItem(Job job)
        {
            var result = new Result { IsItem = true, Graphic = job.Graphic, Hue = job.Hue };
            // The client's HD art at exactly our scale is used as is; otherwise the 1x art is upscaled.
            Bitmap32 hd = _data.GetStaticHd(job.Graphic, _scale);
            Bitmap32 art = hd ?? _data.GetStatic(job.Graphic);
            if (art == null)
                return result;
            int hue = job.Hue & 0x3FFF;
            if (hue != 0 && hue <= _data.Hues.Entries.Length)
            {
                // The highlight hues the whole sprite (ObjectHighlight.WholeArt), whatever the tiledata says.
                bool partial = !ObjectHighlight.IsWholeArt(job.Hue) && (_data.TileData.GetStatic(job.Graphic).Flags & TileFlags.PartialHue) != 0;
                art = HueApplier.Apply(art, _data.Hues.Entries[hue - 1], partial);
            }
            Bitmap32 scaled = hd != null ? art : Resample.Nearest(art, _scale);
            result.Width = scaled.Width;
            result.Height = scaled.Height;
            result.Pixels = DynamicAtlas.ToColors(scaled);
            return result;
        }

        private Result BuildFrames(Job job)
        {
            var result = new Result { Key = job.Key, Action = job.Action, FileDir = job.FileDir, Order = job.Order, Frames = Array.Empty<BuiltFrame>() };
            var layerFrames = new List<(AnimationFrame[] Frames, MobileLook.Layer Layer)>();
            bool sawBody = false;
            foreach (MobileLook.Layer layer in job.Look.DrawOrder(job.Order))
            {
                int action = layer.IsMount ? MountAction(job.Look, job.Action) : job.Action;
                AnimationFrame[] f = Raw(layer.Body, action, job.FileDir);
                if (f.Length > 0)
                    layerFrames.Add((f, layer));
                else if (!layer.IsMount && !sawBody)
                    return result; // no body animation anywhere: draw nothing
                if (!layer.IsMount)
                    sawBody = true;
            }
            if (layerFrames.Count == 0)
                return result;

            // The rider's (or creature's) own animation sets the frame count; a mount loops alongside it.
            int count = layerFrames[layerFrames[0].Layer.IsMount && layerFrames.Count > 1 ? 1 : 0].Frames.Length;
            var built = new BuiltFrame[count];
            for (int i = 0; i < count; i++)
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var (frames, _) in layerFrames)
                {
                    AnimationFrame f = frames[i % frames.Length];
                    int l = -f.CenterX, t = -(f.CenterY + f.Image.Height);
                    minX = Math.Min(minX, l);
                    minY = Math.Min(minY, t);
                    maxX = Math.Max(maxX, l + f.Image.Width);
                    maxY = Math.Max(maxY, t + f.Image.Height);
                }

                var canvas = new Bitmap32(Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
                foreach (var (frames, layer) in layerFrames)
                {
                    AnimationFrame f = frames[i % frames.Length];
                    Bitmap32 img = layer.Hue > 0 && layer.Hue <= _data.Hues.Entries.Length
                        ? HueApplier.Apply(f.Image, _data.Hues.Entries[layer.Hue - 1], layer.Partial)
                        : f.Image;
                    int ox = -f.CenterX - minX, oy = -(f.CenterY + f.Image.Height) - minY;
                    for (int y = 0; y < img.Height; y++)
                        for (int x = 0; x < img.Width; x++)
                        {
                            uint p = img.Pixels[y * img.Width + x];
                            if ((p >> 24) != 0)
                                canvas.Pixels[(oy + y) * canvas.Width + ox + x] = p;
                        }
                }

                Bitmap32 scaled = Resample.Nearest(canvas, _scale);
                built[i] = new BuiltFrame
                {
                    Width = scaled.Width,
                    Height = scaled.Height,
                    Pixels = DynamicAtlas.ToColors(scaled),
                    AnchorX = -minX * _scale,
                    AnchorY = -minY * _scale
                };
            }
            result.Frames = built;
            return result;
        }

        private AnimationFrame[] Raw(int body, int action, int fileDir)
        {
            if (!_raw.TryGetValue((body, action, fileDir), out AnimationFrame[] frames))
                _raw[(body, action, fileDir)] = frames = _data.AnimationLibrary.Read(body, action, fileDir);
            return frames;
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            _worker.Join(1000);
            _atlas.Dispose();
        }

        private sealed class Job
        {
            public bool IsItem;
            public MobileLook Look;
            public string Key;
            public int Action, FileDir, Order;
            public int Graphic, Hue;
        }

        private struct BuiltFrame
        {
            public int Width, Height, AnchorX, AnchorY;
            public Color32[] Pixels;
        }

        private sealed class Result
        {
            public bool IsItem;
            public string Key;
            public int Action, FileDir, Order;
            public BuiltFrame[] Frames;
            public int Graphic, Hue, Width, Height;
            public Color32[] Pixels;
            public double BuildMs;
        }
    }

    /// <summary>An atlas that grows while running: sprites are written straight into GPU texture pages.</summary>
    public sealed class DynamicAtlas : IDisposable
    {
        private const int Padding = 1;
        private readonly int _pageSize;
        private readonly List<Texture2D> _pages = new List<Texture2D>();
        private readonly HashSet<Texture2D> _dirty = new HashSet<Texture2D>();
        private int _x, _y, _shelf;

        public DynamicAtlas(int pageSize)
        {
            _pageSize = pageSize;
        }

        public Texture2D Page(int index) => _pages[index];
        public int PageCount => _pages.Count;

        public AtlasSlot Add(Bitmap32 bmp) => Add(bmp.Width, bmp.Height, ToColors(bmp));

        /// <summary>Unity-ordered (bottom-up) colors for a bitmap; safe to call off the main thread.</summary>
        public static Color32[] ToColors(Bitmap32 bmp)
        {
            var colors = new Color32[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                int src = y * bmp.Width, dst = (bmp.Height - 1 - y) * bmp.Width;
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint p = bmp.Pixels[src + x];
                    colors[dst + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
                }
            }
            return colors;
        }

        /// <summary>Adds a sprite given as bottom-up colors (see <see cref="ToColors"/>).</summary>
        public AtlasSlot Add(int width, int height, Color32[] colors)
        {
            int w = width + Padding, h = height + Padding;
            if (_pages.Count == 0)
                NewPage();
            if (_x + w > _pageSize)
            {
                _x = 0;
                _y += _shelf;
                _shelf = 0;
            }
            if (_y + h > _pageSize)
                NewPage();

            var slot = new AtlasSlot { Page = _pages.Count - 1, X = _x, Y = _y, Width = width, Height = height };
            Texture2D page = _pages[slot.Page];
            page.SetPixels32(slot.X, _pageSize - slot.Y - height, width, height, colors);
            _dirty.Add(page);

            _x += w;
            _shelf = Math.Max(_shelf, h);
            return slot;
        }

        /// <summary>Whether a slot's pixel (x, y) from its top-left is opaque, read from the page's CPU copy (rows are stored bottom-up).</summary>
        public bool Opaque(AtlasSlot s, int x, int y) => _pages[s.Page].GetPixel(s.X + x, _pageSize - 1 - s.Y - y).a > 0;

        /// <summary>A slot's pixels back as a top-left-origin bitmap, from the page's CPU copy.</summary>
        public Bitmap32 Read(AtlasSlot s)
        {
            var bmp = new Bitmap32(s.Width, s.Height);
            Color[] c = _pages[s.Page].GetPixels(s.X, _pageSize - s.Y - s.Height, s.Width, s.Height); // rows bottom-up
            for (int y = 0; y < s.Height; y++)
            {
                int row = (s.Height - 1 - y) * s.Width;
                for (int x = 0; x < s.Width; x++)
                {
                    Color32 p = c[row + x];
                    bmp.Pixels[y * s.Width + x] = (uint)p.a << 24 | (uint)p.r << 16 | (uint)p.g << 8 | p.b;
                }
            }
            return bmp;
        }

        public Vector4 Uv(AtlasSlot s)
        {
            float size = _pageSize;
            return new Vector4(s.X / size, 1f - (s.Y + s.Height) / size, (s.X + s.Width) / size, 1f - s.Y / size);
        }

        public void Apply()
        {
            foreach (Texture2D t in _dirty)
                t.Apply(false, false);
            _dirty.Clear();
        }

        private void NewPage()
        {
            var tex = new Texture2D(_pageSize, _pageSize, TextureFormat.RGBA32, false)
            {
                name = $"UO mobile atlas {_pages.Count}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(new Color32[_pageSize * _pageSize]);
            _pages.Add(tex);
            _x = _y = _shelf = 0;
        }

        public void Dispose()
        {
            foreach (Texture2D t in _pages)
                UnityEngine.Object.Destroy(t);
            _pages.Clear();
        }
    }
}
