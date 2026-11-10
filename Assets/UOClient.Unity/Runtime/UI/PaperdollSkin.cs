using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// The modern paperdoll's look: painted pieces from Resources/Paperdoll when they are there (frame_corner, frame_top,
    /// frame_side, background, doll_backdrop, slot, icon_ring, button, plaque, icon_*), else stand-ins drawn here in
    /// the same colours (dark iron, gilt rules, parchment), so the window works before the art does.
    /// </summary>
    public static class PaperdollSkin
    {
        public static readonly Color Gold = new Color(0.86f, 0.71f, 0.42f), GoldFaded = new Color(0.72f, 0.62f, 0.45f),
            GoldDim = new Color(0.55f, 0.43f, 0.24f), Iron = new Color(0.12f, 0.11f, 0.1f), Deep = new Color(0.06f, 0.055f, 0.05f),
            InkDark = new Color(0.2f, 0.13f, 0.07f);

        private static readonly Dictionary<string, Texture2D> Loaded = new Dictionary<string, Texture2D>();
        private static Texture2D _iron, _deep, _parchment, _vignette, _disc;
        private static GUIStyle _serif;

        /// <summary>A painted piece (Resources/Paperdoll/name), or null.</summary>
        public static Texture2D Piece(string name)
        {
            if (Loaded.TryGetValue(name, out Texture2D t))
                return t;
            t = Resources.Load<Texture2D>("Paperdoll/" + name);
            if (t != null)
                t.wrapMode = TextureWrapMode.Clamp;
            return Loaded[name] = t;
        }

        // ---- the frame ----

        /// <summary>The window's frame and background: <paramref name="border"/> thick, its corners ornamented.</summary>
        public static void Frame(Rect r, float border, float cornerScale = 3f)
        {
            Texture2D bg = Piece("background");
            var inner = new Rect(r.x + border, r.y + border, r.width - border * 2f, r.height - border * 2f);
            if (bg != null)
                Tile(bg, inner, 256f);
            else
                Tile(DeepTex, inner, 64f);

            Texture2D top = Piece("frame_top"), side = Piece("frame_side"), corner = Piece("frame_corner");
            if (top != null && side != null)
            {
                float tLen = top.width * border / top.height, sLen = side.height * border / side.width;
                Strip(top, new Rect(r.x, r.y, r.width, border), tLen, true, false);
                Strip(top, new Rect(r.x, r.yMax - border, r.width, border), tLen, true, true);
                Strip(side, new Rect(r.x, r.y, border, r.height), sLen, false, false);
                Strip(side, new Rect(r.xMax - border, r.y, border, r.height), sLen, false, true);
            }
            else
            {
                Tile(IronTex, new Rect(r.x, r.y, r.width, border), 64f);
                Tile(IronTex, new Rect(r.x, r.yMax - border, r.width, border), 64f);
                Tile(IronTex, new Rect(r.x, r.y + border, border, r.height - border * 2f), 64f);
                Tile(IronTex, new Rect(r.xMax - border, r.y + border, border, r.height - border * 2f), 64f);
                Box(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), GoldDim, 1f);
                Box(new Rect(r.x + border - 3f, r.y + border - 3f, r.width - border * 2f + 6f, r.height - border * 2f + 6f), Gold, 1f);
            }

            float c = border * cornerScale;
            if (corner != null)
            {
                Flipped(corner, new Rect(r.x - 2f, r.y - 2f, c, c), false, false);
                Flipped(corner, new Rect(r.xMax - c + 2f, r.y - 2f, c, c), true, false);
                Flipped(corner, new Rect(r.x - 2f, r.yMax - c + 2f, c, c), false, true);
                Flipped(corner, new Rect(r.xMax - c + 2f, r.yMax - c + 2f, c, c), true, true);
            }
            else
            {
                foreach (var p in new[] { new Vector2(r.x, r.y), new Vector2(r.xMax - border * 1.6f, r.y),
                                          new Vector2(r.x, r.yMax - border * 1.6f), new Vector2(r.xMax - border * 1.6f, r.yMax - border * 1.6f) })
                {
                    var q = new Rect(p.x, p.y, border * 1.6f, border * 1.6f);
                    Fill(q, Iron * 1.25f);
                    Box(q, Gold, 1f);
                    Box(new Rect(q.x + 4f, q.y + 4f, q.width - 8f, q.height - 8f), GoldDim, 1f);
                }
            }
        }

        /// <summary>The dark niche the character stands in.</summary>
        public static void Niche(Rect r)
        {
            Texture2D art = Piece("doll_backdrop");
            if (art != null)
            {
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                return;
            }
            GUI.DrawTexture(r, VignetteTex, ScaleMode.StretchToFill);
            Box(r, GoldDim, 1f);
        }

        /// <summary>A worn piece's slot; lit while it (or its piece on the body) is under the mouse.</summary>
        public static void Slot(Rect r, bool lit)
        {
            Texture2D art = Piece("slot");
            Color was = GUI.color;
            if (art != null)
            {
                GUI.color = lit ? new Color(1.25f, 1.15f, 0.9f) : Color.white;
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                GUI.color = was;
                return;
            }
            Fill(r, new Color(0.03f, 0.03f, 0.03f, 0.85f));
            Box(r, lit ? Gold : GoldDim, lit ? 2f : 1f);
        }

        /// <summary>A round button of the top row: its ring, its icon (or the word's first letter), brighter when hot.</summary>
        public static void IconButton(Rect r, string icon, string label, bool hot)
        {
            Color was = GUI.color;
            GUI.color = hot ? new Color(1.2f, 1.12f, 0.95f) : Color.white;
            Texture2D ring = Piece("icon_ring"), pic = Piece("icon_" + icon);
            if (ring != null)
                GUI.DrawTexture(r, ring, ScaleMode.StretchToFill);
            else
                GUI.DrawTexture(r, DiscTex, ScaleMode.StretchToFill);
            if (pic != null)
            {
                float k = ring != null ? 0.62f : 0.72f;
                GUI.DrawTexture(new Rect(r.center.x - r.width * k / 2f, r.center.y - r.height * k / 2f, r.width * k, r.height * k), pic, ScaleMode.ScaleToFit);
            }
            else
                Title(r, label.Substring(0, 1), 16, hot ? Color.white : Gold, false);
            GUI.color = was;
        }

        /// <summary>A scroll button of the right column: the scroll, its icon at the left, the word in ink.</summary>
        public static void ScrollButton(Rect r, string icon, string label, bool hot, bool down)
        {
            if (down)
                r.y += 1f;
            Color was = GUI.color;
            GUI.color = hot ? new Color(1.1f, 1.06f, 0.96f) : Color.white;
            Texture2D art = Piece("button"), pic = Piece("icon_" + icon);
            float iconX;
            if (art != null)
            {
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                iconX = r.x + r.height * 0.55f;
            }
            else
            {
                // A parchment strip between two wooden rollers.
                float cap = 7f;
                GUI.DrawTexture(new Rect(r.x + cap, r.y + 3f, r.width - cap * 2f, r.height - 6f), ParchmentTex, ScaleMode.StretchToFill);
                Box(new Rect(r.x + cap, r.y + 3f, r.width - cap * 2f, r.height - 6f), new Color(0.45f, 0.33f, 0.18f), 1f);
                Fill(new Rect(r.x, r.y, cap, r.height), new Color(0.3f, 0.19f, 0.1f));
                Fill(new Rect(r.xMax - cap, r.y, cap, r.height), new Color(0.3f, 0.19f, 0.1f));
                Box(new Rect(r.x, r.y, cap, r.height), GoldDim, 1f);
                Box(new Rect(r.xMax - cap, r.y, cap, r.height), GoldDim, 1f);
                iconX = r.x + cap + 6f;
            }
            float isz = r.height * 0.72f;
            if (pic != null)
                GUI.DrawTexture(new Rect(iconX, r.center.y - isz / 2f, isz, isz), pic, ScaleMode.ScaleToFit);
            float textX = iconX + isz + 6f;
            Title(new Rect(textX, r.y, r.xMax - textX - 14f, r.height), label, 16, hot ? new Color(0.45f, 0.08f, 0.04f) : InkDark, false, TextAnchor.MiddleLeft);
            GUI.color = was;
        }

        /// <summary>The name plaque along the foot.</summary>
        public static void Plaque(Rect r)
        {
            Texture2D art = Piece("plaque");
            if (art != null)
            {
                GUI.DrawTexture(r, art, ScaleMode.StretchToFill);
                return;
            }
            Fill(r, new Color(0.035f, 0.032f, 0.03f));
            Box(r, Gold, 1f);
            Box(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), GoldDim, 1f);
        }

        /// <summary>The serif faces tried, in order: the first one installed is used (else the UI's own font).</summary>
        private static readonly string[] SerifFaces = { "Palatino Linotype", "Book Antiqua", "Georgia", "Cambria", "Times New Roman" };

        private static Font _serifFont;
        private static bool _serifLooked;

        /// <summary>
        /// The first SerifFaces face installed, or null (the UI's own font then). A face that is not installed loads
        /// nothing (and logs every frame), so only an installed one is asked for.
        /// </summary>
        public static Font SerifFont
        {
            get
            {
                if (_serifLooked)
                    return _serifFont;
                _serifLooked = true;
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (string face in SerifFaces)
                    if (installed.Contains(face))
                        return _serifFont = Font.CreateDynamicFontFromOSFont(face, 16);
                return null;
            }
        }

        /// <summary>Words in the serif face (SerifFaces), centred unless told, with a soft shadow.</summary>
        public static void Title(Rect r, string text, int size, Color color, bool shadow = true, TextAnchor align = TextAnchor.MiddleCenter)
        {
            if (_serif == null)
            {
                _serif = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(), richText = false };
                if (SerifFont != null)
                    _serif.font = SerifFont;
            }
            _serif.fontSize = size;
            _serif.alignment = align;
            // Too long for the rect (a long name and title): smaller, down to 9.
            var content = new GUIContent(text);
            while (_serif.fontSize > 9 && _serif.CalcSize(content).x > r.width)
                _serif.fontSize--;
            if (shadow)
            {
                _serif.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _serif);
            }
            _serif.normal.textColor = color;
            GUI.Label(r, text, _serif);
        }

        // ---- drawing helpers ----

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        private static void Box(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y + t, t, r.height - t * 2f), c);
            Fill(new Rect(r.xMax - t, r.y + t, t, r.height - t * 2f), c);
        }

        /// <summary>A texture repeated over a rect, each copy <paramref name="size"/> UI units square.</summary>
        private static void Tile(Texture2D tex, Rect r, float size)
        {
            float th = size * tex.height / tex.width;
            tex.wrapMode = TextureWrapMode.Repeat;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(0, 0, r.width / size, r.height / th));
        }

        /// <summary>A strip along a frame edge, its art repeated every <paramref name="len"/> along it; flipped for the far edge.</summary>
        private static void Strip(Texture2D tex, Rect r, float len, bool horizontal, bool flip)
        {
            tex.wrapMode = TextureWrapMode.Repeat;
            Rect uv = horizontal
                ? new Rect(0, flip ? 1 : 0, r.width / len, flip ? -1 : 1)
                : new Rect(flip ? 1 : 0, 0, flip ? -1 : 1, r.height / len);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
        }

        private static void Flipped(Texture2D tex, Rect r, bool flipX, bool flipY) =>
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(flipX ? 1 : 0, flipY ? 1 : 0, flipX ? -1 : 1, flipY ? -1 : 1));

        // ---- stand-in textures ----

        private static Texture2D Noise(ref Texture2D cache, int size, Color baseColour, float amount, int seed)
        {
            if (cache != null)
                return cache;
            cache = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            var rng = new System.Random(seed);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = (Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f) - 0.5f) * amount + ((float)rng.NextDouble() - 0.5f) * amount * 0.4f;
                    px[y * size + x] = new Color(baseColour.r + n, baseColour.g + n, baseColour.b + n, 1f);
                }
            cache.SetPixels(px);
            cache.Apply(false);
            return cache;
        }

        private static Texture2D IronTex => Noise(ref _iron, 64, Iron, 0.07f, 3);
        private static Texture2D DeepTex => Noise(ref _deep, 64, Deep, 0.04f, 5);
        private static Texture2D ParchmentTex => Noise(ref _parchment, 64, new Color(0.86f, 0.77f, 0.58f), 0.12f, 9);

        /// <summary>A soft light in the middle of the niche, falling to near black at its edges.</summary>
        private static Texture2D VignetteTex
        {
            get
            {
                if (_vignette != null)
                    return _vignette;
                const int w = 64, h = 64;
                _vignette = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w - 0.5f, dy = (y + 0.5f) / h - 0.55f;
                        float d = Mathf.Sqrt(dx * dx * 1.6f + dy * dy) * 1.9f;
                        float v = Mathf.Lerp(0.2f, 0.035f, Mathf.Clamp01(d));
                        px[y * w + x] = new Color(v * 1.05f, v, v * 0.92f, 1f);
                    }
                _vignette.SetPixels(px);
                _vignette.Apply(false);
                return _vignette;
            }
        }

        /// <summary>A dark disc with a gilt ring (the top row's buttons before their art).</summary>
        private static Texture2D DiscTex
        {
            get
            {
                if (_disc != null)
                    return _disc;
                const int s = 64;
                _disc = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                var px = new Color[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f)) / (s / 2f);
                        float a = Mathf.Clamp01((1f - d) * s / 2f);
                        Color c = d > 0.86f ? Gold : d > 0.8f ? GoldDim : new Color(0.09f, 0.085f, 0.08f);
                        c.a = a;
                        px[y * s + x] = c;
                    }
                _disc.SetPixels(px);
                _disc.Apply(false);
                return _disc;
            }
        }
    }
}
