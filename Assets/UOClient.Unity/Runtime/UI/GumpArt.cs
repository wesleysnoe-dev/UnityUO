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

        private readonly Dictionary<int, UiImage> _detailed = new Dictionary<int, UiImage>();

        /// <summary>
        /// An item's art for drawing large (a book's cover in its opening): the HD pack's when there is one, else the stock
        /// art upscaled 4x by Scale2x twice (pixel edges kept crisp, not blurred), drawn with bilinear filtering. The
        /// bitmap stays the stock one, so sizes and shapes are as the plain art.
        /// </summary>
        public UiImage ItemDetailed(int graphic)
        {
            if (_detailed.TryGetValue(graphic, out UiImage img))
                return img;
            Bitmap32 bmp = _data.GetStatic(graphic);
            if (bmp == null)
                return _detailed[graphic] = null;
            Bitmap32 big = null;
            for (int s = 4; s >= 2 && big == null; s--)
                big = _data.GetStaticHd(graphic, s);
            big ??= Scale2x(Scale2x(bmp));
            Texture2D tex = UnityTextures.ToTexture(big, $"item 0x{graphic:X4} detailed");
            tex.filterMode = FilterMode.Bilinear;
            return _detailed[graphic] = new UiImage { Bitmap = bmp, Texture = tex };
        }

        private readonly Dictionary<int, UiImage> _gumpsDetailed = new Dictionary<int, UiImage>();

        /// <summary>A gump for drawing large (the scroll opening's rolls and parchment): the HD one, else Scale2x x2, filtered.</summary>
        public UiImage GumpDetailed(int id)
        {
            if (_gumpsDetailed.TryGetValue(id, out UiImage img))
                return img;
            Bitmap32 bmp = _data.GetGump(id);
            if (bmp == null)
                return _gumpsDetailed[id] = null;
            Bitmap32 big = _data.GetGumpHd(id).Hd ?? Scale2x(Scale2x(bmp));
            Texture2D tex = UnityTextures.ToTexture(big, $"gump 0x{id:X4} detailed");
            tex.filterMode = FilterMode.Bilinear;
            return _gumpsDetailed[id] = new UiImage { Bitmap = bmp, Texture = tex };
        }

        private readonly Dictionary<(int, RectInt), UiImage> _cutouts = new Dictionary<(int, RectInt), UiImage>();

        /// <summary>
        /// The ink of a part of a gump (lettering, rules, seals) without the parchment it was painted on, to lay on other
        /// parchment: each pixel's opacity is how far it is from the part's paper colour (the median of its pixels), with
        /// a soft ramp so ink edges blend; then sharpened as <see cref="GumpDetailed"/> (Scale2x x2, filtered).
        /// </summary>
        public UiImage GumpCutout(int id, RectInt part)
        {
            if (_cutouts.TryGetValue((id, part), out UiImage img))
                return img;
            Bitmap32 src = _data.GetGump(id);
            if (src == null)
                return _cutouts[(id, part)] = null;
            var cut = new Bitmap32(part.width, part.height);
            var lums = new List<int>(part.width * part.height);
            for (int y = 0; y < part.height; y++)
                for (int x = 0; x < part.width; x++)
                {
                    int sx = part.x + x, sy = part.y + y;
                    uint p = sx < src.Width && sy < src.Height ? src[sx, sy] : 0u;
                    cut[x, y] = p;
                    if ((p >> 24) != 0)
                        lums.Add((int)(((p >> 16) & 0xFF) * 3 + ((p >> 8) & 0xFF) * 6 + (p & 0xFF)) / 10);
                }
            // The paper: the median opaque pixel's colour (most of a part is paper).
            lums.Sort();
            int paperLum = lums.Count > 0 ? lums[lums.Count / 2] : 200;
            double pr = 0, pg = 0, pb = 0;
            int pn = 0;
            for (int i = 0; i < cut.Pixels.Length; i++)
            {
                uint p = cut.Pixels[i];
                int lum = (int)(((p >> 16) & 0xFF) * 3 + ((p >> 8) & 0xFF) * 6 + (p & 0xFF)) / 10;
                if ((p >> 24) != 0 && Mathf.Abs(lum - paperLum) < 12)
                {
                    pr += (p >> 16) & 0xFF; pg += (p >> 8) & 0xFF; pb += p & 0xFF; pn++;
                }
            }
            if (pn > 0)
            {
                pr /= pn; pg /= pn; pb /= pn;
            }
            for (int i = 0; i < cut.Pixels.Length; i++)
            {
                uint p = cut.Pixels[i];
                if ((p >> 24) == 0)
                    continue;
                double dr = ((p >> 16) & 0xFF) - pr, dg = ((p >> 8) & 0xFF) - pg, db = (p & 0xFF) - pb;
                double dist = System.Math.Sqrt(dr * dr + dg * dg + db * db);
                double a = System.Math.Max(0.0, System.Math.Min(1.0, (dist - 28.0) / 44.0)); // paper 0, ink 1, a soft edge
                cut.Pixels[i] = ((uint)(a * 255.0) << 24) | (p & 0xFFFFFFu);
            }
            Texture2D tex = UnityTextures.ToTexture(Scale2x(Scale2x(cut)), $"gump 0x{id:X4} cutout");
            tex.filterMode = FilterMode.Bilinear;
            return _cutouts[(id, part)] = new UiImage { Bitmap = cut, Texture = tex };
        }

        /// <summary>Scale2x (EPX): each pixel becomes four, taking a neighbour's colour along a clean edge.</summary>
        public static Bitmap32 Scale2x(Bitmap32 src)
        {
            int w = src.Width, h = src.Height;
            var dst = new Bitmap32(w * 2, h * 2);
            uint At(int x, int y) => src[Mathf.Clamp(x, 0, w - 1), Mathf.Clamp(y, 0, h - 1)];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint e = src[x, y], b = At(x, y - 1), d = At(x - 1, y), f = At(x + 1, y), hh = At(x, y + 1);
                    dst[2 * x, 2 * y] = d == b && b != f && d != hh ? d : e;
                    dst[2 * x + 1, 2 * y] = b == f && b != d && f != hh ? f : e;
                    dst[2 * x, 2 * y + 1] = d == hh && d != b && hh != f ? d : e;
                    dst[2 * x + 1, 2 * y + 1] = hh == f && d != hh && b != f ? f : e;
                }
            return dst;
        }

        private readonly Dictionary<int, UiImage> _lands = new Dictionary<int, UiImage>();

        /// <summary>A land tile's art (the 44x44 diamond, HD when there is one), for the world editor's ground gallery.</summary>
        public UiImage Land(int graphic)
        {
            if (_lands.TryGetValue(graphic, out UiImage img))
                return img;
            Bitmap32 bmp = _data.GetLand(graphic);
            Bitmap32 hd = null;
            for (int s = 2; s <= 4 && hd == null && bmp != null; s++)
                hd = _data.GetLandHd(graphic, s);
            img = bmp == null ? null : new UiImage { Bitmap = bmp, Texture = Texture(bmp, hd, $"land 0x{graphic:X4}") };
            _lands[graphic] = img;
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
