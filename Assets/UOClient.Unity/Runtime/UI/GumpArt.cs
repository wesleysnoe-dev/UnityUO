using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.UI
{
    /// <summary>A decoded piece of UI art: the texture to draw and the bitmap for pixel-exact hit tests.</summary>
    public sealed class UiImage
    {
        public Texture2D Texture;
        public Bitmap32 Bitmap;
        public int Width => Bitmap.Width;
        public int Height => Bitmap.Height;

        /// <summary>True if the pixel at (x, y) from the top-left is opaque.</summary>
        public bool HitTest(int x, int y) =>
            x >= 0 && y >= 0 && x < Bitmap.Width && y < Bitmap.Height && (Bitmap[x, y] >> 24) != 0;
    }

    /// <summary>
    /// A client-drawn gump label (status values, a health bar's name): unicode font 1 in a client hue, no border, remade
    /// only when its text or hue changes. TazUO draws these with the ASCII fonts.mul font 1; we have no fonts.mul reader,
    /// so this is the nearest font we draw. The owner calls <see cref="Release"/> when it closes.
    /// </summary>
    public sealed class CachedLabel
    {
        private string _text;
        private int _hue;
        private UiImage _img;

        public UiImage Get(GumpArt art, string text, int clientHue)
        {
            if (_img != null && _text == text && _hue == clientHue)
                return _img;
            Release();
            _text = text;
            _hue = clientHue;
            if (!string.IsNullOrEmpty(text))
                _img = GumpArt.TextImage(art.Text.Generate(text, ServerGumpWindow.GumpFont,
                    ServerGumpWindow.LabelArgb(art.Data.Hues.Entries, clientHue), 0, UniTextAlign.Left, UniTextStyle.None));
            return _img;
        }

        public void Release()
        {
            GumpArt.Release(_img);
            _img = null;
        }
    }

    /// <summary>
    /// Gump art and item art for the UI, decoded on demand, hued, and cached as textures. UI art is small
    /// and only decoded when a window first shows it, so this runs on the main thread.
    /// </summary>
    public sealed class GumpArt : IDisposable
    {
        private readonly UOData _data;
        private readonly Dictionary<(int, int, bool), UiImage> _gumps = new Dictionary<(int, int, bool), UiImage>();
        private readonly Dictionary<(int, int), UiImage> _items = new Dictionary<(int, int), UiImage>();
        private readonly Dictionary<int, Color> _hueColors = new Dictionary<int, Color>();

        public GumpArt(UOData data)
        {
            _data = data;
            Text = new UniText(data.GetUniFont);
        }

        public UOData Data => _data;

        /// <summary>TazUO's unicode text renderer over the client's unifont files (server gump labels).</summary>
        public UniText Text { get; }

        /// <summary>
        /// A rendered text as UI art, point-sampled at its native size like the gumps around it; null when nothing was
        /// drawn. Not cached here: the window that asks owns the texture and destroys it (<see cref="Release"/>).
        /// </summary>
        public static UiImage TextImage(UniTextImage text)
        {
            if (text?.Bitmap == null)
                return null;
            return new UiImage { Bitmap = text.Bitmap, Texture = UnityTextures.ToTexture(text.Bitmap, "gump text") };
        }

        public static void Release(UiImage img)
        {
            if (img != null)
                UnityEngine.Object.Destroy(img.Texture);
        }

        /// <summary>A gump image, optionally hued (partial = only gray pixels). Null if the gump does not exist.</summary>
        public UiImage Gump(int id, int hue = 0, bool partial = false)
        {
            hue &= 0x3FFF;
            if (_gumps.TryGetValue((id, hue, partial), out UiImage img))
                return img;
            Bitmap32 bmp = id >= 0 && id < 0x10000 ? _data.GetGump(id) : null;
            // An HD gump from the client's art is drawn at the stock size (Bitmap) with its full detail (Texture).
            Bitmap32 hd = bmp == null ? null : _data.GetGumpHd(id).Hd;
            if (bmp != null && hue > 0 && hue <= _data.Hues.Entries.Length)
            {
                bmp = HueApplier.Apply(bmp, _data.Hues.Entries[hue - 1], partial);
                if (hd != null)
                    hd = HueApplier.Apply(hd, _data.Hues.Entries[hue - 1], partial);
            }
            img = bmp == null ? null : new UiImage { Bitmap = bmp, Texture = Texture(bmp, hd, $"gump 0x{id:X4}") };
            _gumps[(id, hue, partial)] = img;
            return img;
        }

        /// <summary>An item's (static) art at its native size, hued as the item is (partly, where the tiledata says so).</summary>
        public UiImage Item(int graphic, int hue = 0)
        {
            hue &= 0x3FFF;
            if (_items.TryGetValue((graphic, hue), out UiImage img))
                return img;
            Bitmap32 bmp = _data.GetStatic(graphic);
            Bitmap32 hd = null;
            for (int s = 2; s <= 4 && hd == null && bmp != null; s++)
                hd = _data.GetStaticHd(graphic, s);
            if (bmp != null && hue > 0 && hue <= _data.Hues.Entries.Length)
            {
                bool partial = (_data.TileData.GetStatic(graphic).Flags & TileFlags.PartialHue) != 0;
                bmp = HueApplier.Apply(bmp, _data.Hues.Entries[hue - 1], partial);
                if (hd != null)
                    hd = HueApplier.Apply(hd, _data.Hues.Entries[hue - 1], partial);
            }
            img = bmp == null ? null : new UiImage { Bitmap = bmp, Texture = Texture(bmp, hd, $"item 0x{graphic:X4}") };
            _items[(graphic, hue)] = img;
            return img;
        }

        private readonly Dictionary<int, UiImage> _bodies = new Dictionary<int, UiImage>();

        /// <summary>
        /// A body's first standing frame facing file direction 1, as TazUO's ShopGump draws a mobile on sale
        /// (GetAnimationFrames(body, Stand, 1)): in the Body.def stand-in hue, partial by the static tile of the same
        /// number, as TazUO reads it. Null when the body has no art.
        /// </summary>
        public UiImage Body(int body)
        {
            if (_bodies.TryGetValue(body, out UiImage img))
                return img;
            AnimationLibrary anims = _data.AnimationLibrary;
            AnimationFrame[] frames = anims.Read(body, anims.Actions(body).Stand, 1);
            Bitmap32 bmp = frames.Length > 0 ? frames[0].Image : null;
            int hue = anims.ReplacementHue(body);
            if (bmp != null && hue > 0 && hue <= _data.Hues.Entries.Length)
                bmp = HueApplier.Apply(bmp, _data.Hues.Entries[hue - 1], (_data.TileData.GetStatic(body).Flags & TileFlags.PartialHue) != 0);
            img = bmp == null ? null : new UiImage { Bitmap = bmp, Texture = UnityTextures.ToTexture(bmp, $"body 0x{body:X4}") };
            _bodies[body] = img;
            return img;
        }

        private readonly Dictionary<UiImage, UiImage> _outlines = new Dictionary<UiImage, UiImage>();

        /// <summary>
        /// The mouse-over outline of a picture (<see cref="ObjectHighlight.Outline"/>, one stock pixel thick): one pixel
        /// larger on each side, so it is drawn at (x - 1, y - 1) for art drawn at (x, y). Cached with the picture.
        /// </summary>
        public UiImage Outline(UiImage img)
        {
            if (img == null)
                return null;
            if (_outlines.TryGetValue(img, out UiImage o))
                return o;
            Bitmap32 bmp = ObjectHighlight.Outline(img.Bitmap, 1, ObjectHighlight.OutlineArgb);
            o = new UiImage { Bitmap = bmp, Texture = UnityTextures.ToTexture(bmp, "outline") };
            _outlines[img] = o;
            return o;
        }

        private readonly Dictionary<(UiImage, int, int, bool), Texture2D> _dragCursors = new Dictionary<(UiImage, int, int, bool), Texture2D>();

        /// <summary>
        /// A picture as a cursor <paramref name="width"/> x <paramref name="height"/> display pixels (the stock art,
        /// point-scaled to the UI scale as windows draw it), kept readable as Cursor.SetCursor needs. Cached per size.
        /// <paramref name="twice"/>: a held stack's two copies (<see cref="ItemStack.Doubled"/>).
        /// </summary>
        public Texture2D CursorTexture(UiImage img, int width, int height, bool twice = false)
        {
            if (_dragCursors.TryGetValue((img, width, height, twice), out Texture2D tex) && tex != null)
                return tex;
            Bitmap32 src = twice ? ItemStack.Doubled(img.Bitmap) : img.Bitmap;
            tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "drag cursor", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    uint p = src[Math.Min(src.Width - 1, x * src.Width / width), Math.Min(src.Height - 1, y * src.Height / height)];
                    colors[(height - 1 - y) * width + x] = new Color32((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >> 24));
                }
            tex.SetPixels32(colors);
            tex.Apply(false, false);
            _dragCursors[(img, width, height, twice)] = tex;
            return tex;
        }

        /// <summary>The stock-size picture, or its HD version (drawn at the stock size, so smoothed rather than point-sampled).</summary>
        private static Texture2D Texture(Bitmap32 bmp, Bitmap32 hd, string name)
        {
            Texture2D tex = UnityTextures.ToTexture(hd ?? bmp, name);
            if (hd != null)
                tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// <summary>The display color of a text hue (a bright shade of it); hue 0 is the caller's default.</summary>
        public Color HueColor(int hue, Color fallback)
        {
            hue &= 0x3FFF;
            if (hue <= 0 || hue > _data.Hues.Entries.Length)
                return fallback;
            if (!_hueColors.TryGetValue(hue, out Color c))
            {
                uint argb = Color16.ToArgb32(_data.Hues.Entries[hue - 1].Colors[24]);
                c = new Color(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, 1f);
                _hueColors[hue] = c;
            }
            return c;
        }

        /// <summary>A server gump label's colour in client hue <paramref name="hue"/>: TazUO's unicode text shade (ServerGumpWindow.TextArgb).</summary>
        public Color GumpTextColor(int hue) => ToColor(GumpTextArgb(hue));

        public uint GumpTextArgb(int hue) => ServerGumpWindow.TextArgb(_data.Hues.Entries, hue);

        public static Color ToColor(uint argb) =>
            new Color(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, 1f);

        public void Dispose()
        {
            foreach (UiImage i in _gumps.Values)
                if (i != null)
                    UnityEngine.Object.Destroy(i.Texture);
            foreach (UiImage i in _items.Values)
                if (i != null)
                    UnityEngine.Object.Destroy(i.Texture);
            foreach (UiImage i in _bodies.Values)
                if (i != null)
                    UnityEngine.Object.Destroy(i.Texture);
            foreach (UiImage i in _outlines.Values)
                UnityEngine.Object.Destroy(i.Texture);
            foreach (Texture2D t in _dragCursors.Values)
                UnityEngine.Object.Destroy(t);
            _dragCursors.Clear();
            _gumps.Clear();
            _items.Clear();
            _bodies.Clear();
            _outlines.Clear();
        }
    }
}
