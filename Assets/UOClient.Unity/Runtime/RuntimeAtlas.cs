using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    public struct AtlasSlot
    {
        public int Page, X, Y, Width, Height;
    }

    /// <summary>
    /// Packs decoded bitmaps into texture pages (shelf packing) and uploads them. The same bitmap
    /// instance is stored once, so shared art (every copy of a graphic) costs one slot.
    /// </summary>
    public sealed class RuntimeAtlas : IDisposable
    {
        private const int Padding = 1;

        private readonly int _pageSize;
        private readonly List<Bitmap32> _pages = new List<Bitmap32>();
        private readonly Dictionary<Bitmap32, AtlasSlot> _slots = new Dictionary<Bitmap32, AtlasSlot>();
        private int _x, _y, _shelf;

        public RuntimeAtlas(int pageSize = 2048)
        {
            _pageSize = pageSize;
        }

        public int PageSize => _pageSize;
        public Texture2D[] Textures { get; private set; } = Array.Empty<Texture2D>();

        public AtlasSlot Add(Bitmap32 bmp)
        {
            if (_slots.TryGetValue(bmp, out AtlasSlot slot))
                return slot;

            int w = bmp.Width + Padding, h = bmp.Height + Padding;
            if (w > _pageSize || h > _pageSize)
                throw new InvalidOperationException($"Sprite {bmp.Width}x{bmp.Height} does not fit a {_pageSize} page.");

            if (_pages.Count == 0)
                _pages.Add(new Bitmap32(_pageSize, _pageSize));
            if (_x + w > _pageSize)
            {
                _x = 0;
                _y += _shelf;
                _shelf = 0;
            }
            if (_y + h > _pageSize)
            {
                _pages.Add(new Bitmap32(_pageSize, _pageSize));
                _x = _y = _shelf = 0;
            }

            slot = new AtlasSlot { Page = _pages.Count - 1, X = _x, Y = _y, Width = bmp.Width, Height = bmp.Height };
            Bitmap32 page = _pages[slot.Page];
            for (int row = 0; row < bmp.Height; row++)
                Array.Copy(bmp.Pixels, row * bmp.Width, page.Pixels, (_y + row) * _pageSize + _x, bmp.Width);

            _x += w;
            _shelf = Math.Max(_shelf, h);
            _slots[bmp] = slot;
            return slot;
        }

        /// <summary>Uploads all pages and frees the CPU copies.</summary>
        public Texture2D[] Upload()
        {
            Textures = new Texture2D[_pages.Count];
            for (int i = 0; i < _pages.Count; i++)
                Textures[i] = UnityTextures.ToTexture(_pages[i], $"UO atlas {i}");
            _pages.Clear();
            return Textures;
        }

        /// <summary>UVs (Unity's bottom-left origin) for a slot: x = left, y = bottom, z = right, w = top.</summary>
        public Vector4 Uv(AtlasSlot s)
        {
            float size = _pageSize;
            return new Vector4(s.X / size, 1f - (s.Y + s.Height) / size, (s.X + s.Width) / size, 1f - s.Y / size);
        }

        public void Dispose()
        {
            foreach (Texture2D t in Textures)
                if (t != null)
                    UnityEngine.Object.Destroy(t);
            Textures = Array.Empty<Texture2D>();
        }
    }

    public static class UnityTextures
    {
        /// <summary>Uploads a decoded bitmap, flipping rows (Bitmap32 is top-left origin, Unity is bottom-left).</summary>
        public static Texture2D ToTexture(Bitmap32 bmp, string name = null)
        {
            var tex = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, false)
            {
                name = name ?? "UO texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var colors = new Color32[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                int src = y * bmp.Width;
                int dst = (bmp.Height - 1 - y) * bmp.Width;
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint p = bmp.Pixels[src + x];
                    colors[dst + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
                }
            }

            tex.SetPixels32(colors);
            tex.Apply(false, true);
            return tex;
        }

        public static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
