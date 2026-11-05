using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using UnityEngine;
using UOClient.ArtPipeline.Imaging;
using UOClient.IO;
using UOClient.IO.Formats;
using Debug = UnityEngine.Debug;

namespace UOClient.Unity
{
    /// <summary>What a mobile looks like: body, skin hue, and worn items in draw order. Immutable once built.</summary>
    public sealed class MobileLook
    {
        public struct Layer
        {
            public int Body;   // animation body id (the item's tiledata AnimId for equipment)
            public int Hue;    // hues.mul index (entry + 1); 0 = unhued
            public bool Partial;
            public bool IsMount; // the creature being ridden: plays its own walk/run/stand under the rider
        }

        public readonly List<Layer> Layers = new List<Layer>();

        /// <summary>The body's action layout (humans, animals and monsters number their actions differently).</summary>
        public AnimGroup Group = AnimGroup.Human;

        /// <summary>Riding: the rider uses the mounted actions, the mount layer its own.</summary>
        public bool Mounted;

        /// <summary>The mount's action layout, when mounted.</summary>
        public AnimGroup MountGroup = AnimGroup.Animal;

        /// <summary>Identifies the look for sharing frames between identical mobiles.</summary>
        public string Key
        {
            get
            {
                var sb = new StringBuilder();
                foreach (Layer l in Layers)
                    sb.Append(l.IsMount ? "m" : "").Append(l.Body).Append(':').Append(l.Hue).Append(l.Partial ? "p" : "").Append(';');
                return sb.ToString();
            }
        }

        public int BaseBody
        {
            get
            {
                foreach (Layer l in Layers)
                    if (!l.IsMount)
                        return l.Body;
                return 0;
            }
        }

        // Paperdoll-style order for front-facing views: what is listed later is drawn on top.
        private static readonly byte[] LayerOrder =
        {
            20, // cloak (behind the body when seen from the front)
            5, 4, 3, 24, 13, 8, 9, 14, 19, 7, 23, 17, 10, 11, 22, 12, 16, 18, 6, 1, 2
        };

        /// <summary>Builds a look from a body and worn items (graphic, hue per layer), resolving each item's animation.</summary>
        public static MobileLook From(UOData data, int body, int hue, IEnumerable<(byte Layer, int Graphic, int Hue)> equipment)
        {
            var look = new MobileLook();
            AnimationLibrary anims = data.AnimationLibrary;
            look.Group = anims.GroupOf(body);
            int skin = hue & 0x3FFF;
            if (skin == 0)
                skin = anims.ReplacementHue(body); // Body.def stand-ins come with their own hue

            var byLayer = new Dictionary<byte, (int Graphic, int Hue)>();
            foreach (var e in equipment)
                byLayer[e.Layer] = (e.Graphic, e.Hue);

            // The mount goes first, so the rider is drawn over it.
            if (look.Group == AnimGroup.Human && byLayer.TryGetValue(25, out var mount))
            {
                int mountBody = AnimationLibrary.MountBody(mount.Graphic);
                if (mountBody == 0)
                    mountBody = data.TileData.GetStatic(mount.Graphic).AnimId;
                if (mountBody != 0)
                {
                    look.Mounted = true;
                    look.MountGroup = anims.GroupOf(mountBody);
                    look.Layers.Add(new Layer { Body = mountBody, Hue = mount.Hue & 0x3FFF, IsMount = true });
                }
            }

            look.Layers.Add(new Layer { Body = body, Hue = skin });
            if (look.Group != AnimGroup.Human)
                return look; // creatures wear nothing visible

            foreach (byte layer in LayerOrder)
            {
                if (!byLayer.TryGetValue(layer, out var item))
                    continue;
                StaticTileData td = data.TileData.GetStatic(item.Graphic);
                if (td.AnimId == 0)
                    continue;
                // Equipconv.def and the shard's EquipConv lines: e.g. a faction tabard drawn as the stock surcoat in its faction hue.
                int anim = td.AnimId, itemHue = item.Hue & 0x3FFF;
                if (data.TryEquipConv(body, anim, out EquipConvEntry conv))
                {
                    anim = conv.Anim;
                    if (itemHue == 0)
                        itemHue = conv.Hue & 0x3FFF;
                }
                look.Layers.Add(new Layer
                {
                    Body = anim,
                    Hue = itemHue,
                    Partial = (td.Flags & TileFlags.PartialHue) != 0 || (item.Hue & 0x8000) != 0
                });
            }
            return look;
        }
    }

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
        private readonly Dictionary<(string, int, int), MobileFrame[]> _frames = new Dictionary<(string, int, int), MobileFrame[]>();
        private readonly HashSet<(string, int, int)> _pendingFrames = new HashSet<(string, int, int)>();
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

        /// <summary>Walk/run/stand action numbers differ for humans, animals and monsters.</summary>
        public static (int Walk, int Run, int Stand) Actions(MobileLook look)
        {
            if (look.Mounted) return (MountedWalk, MountedRun, MountedStand);
            return Actions(look.Group);
        }

        public static (int Walk, int Run, int Stand) Actions(AnimGroup group)
        {
            switch (group)
            {
                case AnimGroup.Human: return (0, 2, 4);
                case AnimGroup.Animal: return (0, 1, 2);
                default: return (0, 0, 1);
            }
        }

        // Human actions while riding.
        public const int MountedWalk = 23, MountedRun = 24, MountedStand = 25;

        /// <summary>What a mount plays while its rider plays <paramref name="riderAction"/>.</summary>
        private static int MountAction(MobileLook look, int riderAction)
        {
            var (walk, run, stand) = Actions(look.MountGroup);
            return riderAction == MountedWalk ? walk : riderAction == MountedRun ? run : stand;
        }

        /// <summary>
        /// Frames for a look/action/direction: null while being built (requested on first call),
        /// an empty array if the body has no animation in anim.mul.
        /// </summary>
        public MobileFrame[] Get(MobileLook look, string key, int action, int fileDir)
        {
            if (_frames.TryGetValue((key, action, fileDir), out MobileFrame[] frames))
                return frames;
            if (_pendingFrames.Add((key, action, fileDir)))
                Enqueue(new Job { Look = look, Key = key, Action = action, FileDir = fileDir });
            return null;
        }

        /// <summary>Asks for a direction's stand, walk and run frames ahead of need.</summary>
        public void Prefetch(MobileLook look, string key, int fileDir)
        {
            var (walk, run, stand) = Actions(look);
            Get(look, key, stand, fileDir);
            Get(look, key, walk, fileDir);
            Get(look, key, run, fileDir);
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
                _pendingFrames.Remove((r.Key, r.Action, r.FileDir));
                _frames[(r.Key, r.Action, r.FileDir)] = frames;
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
                        : new Result { Key = job.Key, Action = job.Action, FileDir = job.FileDir, Frames = Array.Empty<BuiltFrame>() };
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
                bool partial = (_data.TileData.GetStatic(job.Graphic).Flags & TileFlags.PartialHue) != 0;
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
            var result = new Result { Key = job.Key, Action = job.Action, FileDir = job.FileDir, Frames = Array.Empty<BuiltFrame>() };
            var layerFrames = new List<(AnimationFrame[] Frames, MobileLook.Layer Layer)>();
            bool sawBody = false;
            foreach (MobileLook.Layer layer in job.Look.Layers)
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
            public int Action, FileDir;
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
            public int Action, FileDir;
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
