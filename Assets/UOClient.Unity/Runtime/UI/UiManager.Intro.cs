using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>How a window makes its entrance (<see cref="UiManager.OpenWithIntro{T}"/>).</summary>
    public enum WindowIntro
    {
        /// <summary>The book flies up from where it was used, its cover swings open and pages turn, then the window shows.</summary>
        Book,

        /// <summary>A rolled scroll flies up, spreads along the window's top edge and unrolls downward.</summary>
        Scroll
    }

    public sealed partial class UiManager
    {
        /// <summary>Opening animations on (the options' "Animate books and scrolls opening"); off, windows appear at once.</summary>
        public static bool OpenAnimations { get; set; } = true;

        /// <summary>How long an opening animation takes, seconds.</summary>
        public const float IntroSeconds = 2.5f;

        /// <summary>The share of the animation spent flying to the window; the rest opens or unrolls it.</summary>
        private const float FlightShare = 0.35f;

        /// <summary>Of a book's opening: the cover turns over [0, CoverEnd], each page takes PageTime, the window fades in from FadeStart.</summary>
        private const float Settle = 0.14f, CoverEnd = 0.44f, PageTime = 0.22f, FadeStart = 0.86f;

        private static readonly float[] PageStarts = { 0.38f, 0.49f, 0.6f };

        /// <summary>A rolled scroll's art (tiledata "scroll"): what party, profile and vendor windows fly in as.</summary>
        public const int ScrollGraphic = 0x14ED;

        /// <summary>A plain book's art, when the used item can't be found (a book opened by the server alone).</summary>
        public const int BookGraphic = 0x0FF2;

        private sealed class Intro
        {
            public WindowIntro Kind;
            public float Start;
            public Vector2 From;     // UI coordinates: where it was used
            public UiImage Art;
            public UiImage Pages;    // the book's own window art (two pages), or null: plain parchment
            public Color CoverTint = Color.white; // the book's own colour (its art's mean), the leather tinted to it
            public readonly int OverlayId = _nextOverlayId++;
        }

        private static int _nextOverlayId = 0x7A000;
        private readonly Dictionary<UiWindow, Intro> _intros = new Dictionary<UiWindow, Intro>();
        private Vector2 _lastPress;
        private float _lastPressAt = -100f;

        /// <summary>
        /// Opens a window as <see cref="Open{T}"/> does, and, if it was not open already and animations are on, plays its
        /// entrance from where the mouse last pressed (the book or button that asked for it; within a few seconds, else
        /// the bottom of the screen). <paramref name="graphic"/>: the art that flies (a book's own).
        /// </summary>
        public T OpenWithIntro<T>(T window, WindowIntro kind, int graphic) where T : UiWindow
        {
            bool wasOpen = Find(window.Key) != null;
            T opened = Open(window);
            if (wasOpen || !OpenAnimations || !ReferenceEquals(opened, window))
                return opened;
            UiImage art = Art.Item(graphic) ?? Art.Item(kind == WindowIntro.Book ? BookGraphic : ScrollGraphic);
            if (art == null)
                return opened;
            float now = Time.unscaledTime;
            Vector2 from = now - _lastPressAt < 4f
                ? _lastPress
                : new Vector2(Screen.width / Scale / 2f, Screen.height / Scale - 40f);
            _intros[window] = new Intro
            {
                Kind = kind, Start = now, From = from, Art = art,
                Pages = kind == WindowIntro.Book ? window.IntroPages : null,
                CoverTint = kind == WindowIntro.Book ? MeanColour(art) : Color.white
            };
            return opened;
        }

        /// <summary>The mean colour of an image's opaque pixels, brightened to tint a pale leather (a red book, a black one...).</summary>
        private static Color MeanColour(UiImage img)
        {
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                {
                    uint p = img.Bitmap[x, y];
                    if ((p >> 24) < 128)
                        continue;
                    r += (p >> 16) & 0xFF;
                    g += (p >> 8) & 0xFF;
                    b += p & 0xFF;
                    n++;
                }
            if (n == 0)
                return Color.white;
            var c = new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0));
            return c * 1.9f; // the leather is a mid brown: this brings it to about the art's own colour
        }

        /// <summary>The art of an item the player can see (on the ground, in a container, worn), or 0.</summary>
        public int GraphicOf(uint serial)
        {
            if (Session == null)
                return 0;
            if (Session.World.Items.TryGetValue(serial, out Core.WorldItem w))
                return w.Graphic;
            if (Session.World.Contained.TryGetValue(serial, out Core.ContainedItem c))
                return c.Graphic;
            foreach (Core.Mobile m in Session.World.Mobiles.Values)
                foreach (Core.EquippedItem e in m.Equipment.Values)
                    if (e.Serial == serial)
                        return e.Graphic;
            return 0;
        }

        /// <summary>Every event (before the UI's matrix): remember where the mouse last pressed, in UI coordinates.</summary>
        private void NoteIntroPress(Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _lastPress = e.mousePosition / Scale;
                _lastPressAt = Time.unscaledTime;
            }
        }

        /// <summary>A window's animation progress 0..1, or -1 when it has none (finished ones are dropped).</summary>
        private float IntroT(UiWindow w)
        {
            if (!_intros.TryGetValue(w, out Intro intro))
                return -1f;
            float t = (Time.unscaledTime - intro.Start) / IntroSeconds;
            if (t < 1f && OpenAnimations)
                return t;
            _intros.Remove(w);
            return -1f;
        }

        private static float OpenPhase(float t) => Mathf.Clamp01((t - FlightShare) / (1f - FlightShare));

        /// <summary>Whether the window itself is not drawn yet: in flight, or (a book) while its cover and pages turn.</summary>
        private bool IntroHidesWindow(UiWindow w)
        {
            float t = IntroT(w);
            if (t < 0f)
                return false;
            if (t < FlightShare)
                return true;
            return _intros[w].Kind == WindowIntro.Book && OpenPhase(t) < FadeStart;
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>The flights and the books opening, drawn over the UI (main OnGUI pass; windows are drawn after it).</summary>
        private void DrawIntroFlights()
        {
            foreach (KeyValuePair<UiWindow, Intro> kv in new List<KeyValuePair<UiWindow, Intro>>(_intros))
            {
                float t = IntroT(kv.Key);
                if (t < 0f)
                    continue;
                Intro intro = kv.Value;
                if (t >= FlightShare)
                {
                    if (intro.Kind == WindowIntro.Book && Event.current.type == EventType.Repaint)
                        DrawBookOpening(BookRect(kv.Key, intro), intro, OpenPhase(t));
                    continue;
                }
                Rect target = FlightTarget(BookRect(kv.Key, intro), intro.Kind, intro.Art);
                float k = Ease(t / FlightShare);
                var startSize = new Vector2(intro.Art.Width, intro.Art.Height);
                Vector2 size = Vector2.Lerp(startSize, target.size, k);
                Vector2 centre = Vector2.Lerp(intro.From, target.center, k);
                centre.y -= Mathf.Sin(k * Mathf.PI) * 70f; // an arc toward the viewer
                var r = new Rect(centre - size / 2f, size);
                UiImage art = intro.Art;
                GUI.Window(intro.OverlayId, r, _ =>
                {
                    GUI.DrawTexture(new Rect(0, 0, r.width, r.height), art.Texture, ScaleMode.StretchToFill);
                }, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(intro.OverlayId);
            }
        }

        /// <summary>The book's two-page area: its own art's size at the window's corner, else the window.</summary>
        private static Rect BookRect(UiWindow w, Intro intro) =>
            intro.Pages != null ? new Rect(w.Rect.x, w.Rect.y, intro.Pages.Width, intro.Pages.Height) : w.Rect;

        /// <summary>Where the flight ends, the art at twice its size: on the right page's middle (the book), or the top's (the scroll).</summary>
        private static Rect FlightTarget(Rect window, WindowIntro kind, UiImage art)
        {
            var size = new Vector2(art.Width, art.Height) * 2f;
            Vector2 centre = kind == WindowIntro.Book
                ? new Vector2(window.x + window.width * 0.75f, window.y + window.height / 2f)
                : new Vector2(window.x + window.width / 2f, window.y);
            return new Rect(centre - size / 2f, size);
        }

        // ---- the book opening: leaves turning about the spine, drawn with GL ----

        private static Material _quadMaterial;
        private static Texture2D _leather, _parchment;

        private static readonly Color Gilt = new Color(0.83f, 0.66f, 0.3f), Parchment = new Color(0.86f, 0.76f, 0.56f),
            ParchmentDark = new Color(0.55f, 0.42f, 0.26f), Endpaper = new Color(0.62f, 0.5f, 0.36f);

        private static Material QuadMaterial
        {
            get
            {
                if (_quadMaterial == null)
                {
                    Shader s = Resources.Load<Shader>("UiQuad");
                    if (s != null && s.isSupported)
                        _quadMaterial = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                }
                return _quadMaterial;
            }
        }

        /// <summary>A leather cover: deep red-brown with a grain, a gilt rule inset from its edges.</summary>
        private static Texture2D Leather
        {
            get
            {
                if (_leather != null)
                    return _leather;
                const int w = 96, h = 128;
                _leather = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var px = new Color32[w * h];
                var rng = new System.Random(7);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float n = (float)rng.NextDouble() * 0.12f + Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.1f;
                        bool rule = (x == 6 || x == w - 7) && y >= 6 && y < h - 6 || (y == 6 || y == h - 7) && x >= 6 && x < w - 6;
                        Color c = rule ? Gilt : new Color(0.34f + n, 0.13f + n * 0.5f, 0.08f + n * 0.3f);
                        px[y * w + x] = c;
                    }
                _leather.SetPixels32(px);
                _leather.Apply(false);
                return _leather;
            }
        }

        /// <summary>Plain parchment, for a book whose window art isn't known.</summary>
        private static Texture2D PlainParchment
        {
            get
            {
                if (_parchment != null)
                    return _parchment;
                const int s = 64;
                _parchment = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[s * s];
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float n = Mathf.PerlinNoise(x * 0.2f, y * 0.2f) * 0.12f;
                        px[y * s + x] = new Color(Parchment.r - n, Parchment.g - n, Parchment.b - n);
                    }
                _parchment.SetPixels32(px);
                _parchment.Apply(false);
                return _parchment;
            }
        }

        /// <summary>
        /// A book of rect <paramref name="book"/> (UI units) opening, at <paramref name="o"/> 0..1: the right page lies open
        /// under the cover; the cover turns over the spine and lands on the left; three pages turn after it, each landing
        /// on the stack; at the end the drawing fades as the real window fades in over it.
        /// </summary>
        private void DrawBookOpening(Rect book, Intro intro, float o)
        {
            Material m = QuadMaterial;
            if (m == null)
                return;
            float alpha = o < FadeStart ? 1f : 1f - (o - FadeStart) / (1f - FadeStart);
            Texture pages = intro.Pages?.Texture ?? PlainParchment;
            float s = Scale;
            float sx = (book.x + book.width / 2f) * s, half = book.width / 2f * s, y0 = book.y * s, y1 = book.yMax * s;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

            // The right page, lying open (under the cover at first).
            Flat(m, pages, sx, sx + half, y0, y1, 0.5f, 1f, alpha);

            // The left side's stack: the cover's inside once it has landed, then a page once one has.
            // The closed book settles first (its art growing to fill the cover), then the cover turns.
            float cover = Ease(Mathf.Clamp01((o - Settle) / (CoverEnd - Settle)));
            int landed = 0;
            foreach (float start in PageStarts)
                if (o >= start + PageTime)
                    landed++;
            // The cover's inside is the left page (the book's own art), so once it lands the left page is there.
            if (cover >= 1f || landed > 0)
                Flat(m, pages, sx - half, sx, y0, y1, 0f, 0.5f, alpha);

            // The pages turning (under the cover while it still turns, so it is drawn last).
            foreach (float start in PageStarts)
            {
                float p = (o - start) / PageTime;
                if (p > 0f && p < 1f)
                    Leaf(m, pages, Ease(p) * Mathf.PI, sx, half, y0, y1, alpha, false, null);
            }
            if (cover < 1f)
                Leaf(m, Leather, cover * Mathf.PI, sx, half, y0, y1, alpha, true, intro.Art, pages, intro.CoverTint, Mathf.Clamp01(o / Settle));

            GL.PopMatrix();
        }

        /// <summary>A flat textured page from x0 to x1, the texture's u from u0 to u1.</summary>
        private static void Flat(Material m, Texture tex, float x0, float x1, float y0, float y1, float u0, float u1, float a)
        {
            m.mainTexture = tex;
            m.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(1f, 1f, 1f, a));
            GL.TexCoord2(u0, 1f); GL.Vertex3(x0, y0, 0);
            GL.TexCoord2(u1, 1f); GL.Vertex3(x1, y0, 0);
            GL.TexCoord2(u1, 0f); GL.Vertex3(x1, y1, 0);
            GL.TexCoord2(u0, 0f); GL.Vertex3(x0, y1, 0);
            GL.End();
        }

        private static void FlatColour(Material m, float x0, float x1, float y0, float y1, Color c)
        {
            m.mainTexture = Texture2D.whiteTexture;
            m.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(c);
            GL.TexCoord2(0, 1); GL.Vertex3(x0, y0, 0);
            GL.TexCoord2(1, 1); GL.Vertex3(x1, y0, 0);
            GL.TexCoord2(1, 0); GL.Vertex3(x1, y1, 0);
            GL.TexCoord2(0, 0); GL.Vertex3(x0, y1, 0);
            GL.End();
        }

        /// <summary>
        /// One leaf turning about the spine at angle <paramref name="theta"/> (0 lying on the right, pi on the left), in
        /// perspective: its free edge rises toward the viewer (taller) mid-turn. Its front shows while it faces right (a
        /// page's right half, or the cover with the book's art); its back after (the page's left half, or the endpaper).
        /// </summary>
        private static void Leaf(Material m, Texture front, float theta, float sx, float half, float y0, float y1, float a,
                                 bool isCover, UiImage art, Texture back = null, Color? coverTint = null, float coverOpen = 0f)
        {
            float c = Mathf.Cos(theta), lift = (y1 - y0) * 0.07f * Mathf.Sin(theta);
            float ex = sx + half * c, ey0 = y0 - lift, ey1 = y1 + lift;
            bool facingFront = c >= 0f;
            float shade = 0.55f + 0.45f * Mathf.Abs(c);
            var tint = new Color(shade, shade, shade, a);
            Color face = coverTint ?? Color.white;
            if (isCover && facingFront)
                tint = new Color(Mathf.Min(1.4f, face.r) * shade, Mathf.Min(1.4f, face.g) * shade, Mathf.Min(1.4f, face.b) * shade, a);
            // The cover's back is the left page; a page's back is the same left page (the book's art, as it lands).
            bool showPageBack = !facingFront;
            m.mainTexture = isCover && showPageBack ? back ?? PlainParchment : front;
            m.SetPass(0);
            // Texture u at the spine and at the free edge: the cover's leather whole; a page's right half on the front,
            // the left half on the back.
            float uSpine = isCover && !showPageBack ? 0f : 0.5f, uEdge = isCover && !showPageBack ? 1f : facingFront ? 1f : 0f;
            GL.Begin(GL.QUADS);
            GL.Color(tint);
            GL.TexCoord2(uSpine, 1f); GL.Vertex3(sx, y0, 0);
            GL.TexCoord2(uEdge, 1f); GL.Vertex3(ex, ey0, 0);
            GL.TexCoord2(uEdge, 0f); GL.Vertex3(ex, ey1, 0);
            GL.TexCoord2(uSpine, 0f); GL.Vertex3(sx, y1, 0);
            GL.End();

            // The book's own art on the cover's face, mapped onto the turning leaf (the flight lands it there, at 2x).
            if (isCover && facingFront && art != null)
            {
                // The art on the cover's face: from twice its size (where the flight lands it) to most of the cover,
                // keeping its shape; half its share of the leaf, across and down.
                float grow = coverOpen, pageH = y1 - y0;
                float wantW = Mathf.Lerp(art.Width * 2f, half * 0.8f, Ease(grow));
                float wantH = wantW * art.Height / Mathf.Max(1f, art.Width);
                if (wantH > pageH * 0.8f)
                {
                    wantW *= pageH * 0.8f / wantH;
                    wantH = pageH * 0.8f;
                }
                float aw = Mathf.Min(wantW / half, 1f) * 0.5f, ah = Mathf.Min(wantH / pageH, 1f) * 0.5f;
                var artTint = new Color(shade, shade, shade, a); // the art in its own colours, not the leather's tint
                Vector2 P(float u, float v) // u 0 spine .. 1 edge, v 0 top .. 1 bottom
                {
                    var top = Vector2.Lerp(new Vector2(sx, y0), new Vector2(ex, ey0), u);
                    var bottom = Vector2.Lerp(new Vector2(sx, y1), new Vector2(ex, ey1), u);
                    return Vector2.Lerp(top, bottom, v);
                }
                m.mainTexture = art.Texture;
                m.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(artTint);
                Vector2 a0 = P(0.5f - aw, 0.5f - ah), a1 = P(0.5f + aw, 0.5f - ah), a2 = P(0.5f + aw, 0.5f + ah), a3 = P(0.5f - aw, 0.5f + ah);
                GL.TexCoord2(0, 1); GL.Vertex3(a0.x, a0.y, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(a1.x, a1.y, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(a2.x, a2.y, 0);
                GL.TexCoord2(0, 0); GL.Vertex3(a3.x, a3.y, 0);
                GL.End();
            }
        }

        // ---- the window itself during its opening ----

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>A rolled-up length of parchment lying across <paramref name="r"/> (a cylinder: dark edges, light middle).</summary>
        private static void Roll(Rect r)
        {
            Fill(r, ParchmentDark);
            Fill(new Rect(r.x + 2f, r.y + r.height * 0.2f, r.width - 4f, r.height * 0.6f), Parchment);
            Fill(new Rect(r.x + 2f, r.y + r.height * 0.32f, r.width - 4f, r.height * 0.12f), new Color(1f, 0.95f, 0.82f));
            Fill(new Rect(r.x, r.y, 6f, r.height), new Color(0.4f, 0.28f, 0.16f)); // the ends
            Fill(new Rect(r.xMax - 6f, r.y, 6f, r.height), new Color(0.4f, 0.28f, 0.16f));
        }

        /// <summary>
        /// Draws a window during its opening (window-local, in DrawWindow): a book fading in over its turned pages, or a
        /// scroll revealed top-down behind its roll. Returns false when the window has no animation running.
        /// </summary>
        private bool DrawIntroWindow(UiWindow w)
        {
            float t = IntroT(w);
            if (t < 0f)
                return false;
            Intro intro = _intros[w];
            float open = OpenPhase(t);
            bool was = GUI.enabled;
            Color colour = GUI.color;
            GUI.enabled = false; // nothing takes a click until it is open
            if (intro.Kind == WindowIntro.Book)
            {
                GUI.color = new Color(colour.r, colour.g, colour.b, Mathf.Clamp01((open - FadeStart) / (1f - FadeStart)));
                w.Draw();
            }
            else
            {
                float W = w.Rect.width, H = w.Rect.height;
                // First the roll spreads to the window's width along its top, then it unrolls downward.
                float spread = Mathf.Clamp01(open / 0.2f), down = Ease(Mathf.Clamp01((open - 0.2f) / 0.8f));
                float shown = H * down;
                Clipped(w, new Rect(0, 0, W, shown));
                float rw = Mathf.Lerp(intro.Art.Width * 2f, W + 8f, spread);
                Roll(new Rect(W / 2f - rw / 2f, shown - 12f, rw, 24f));
            }
            GUI.color = colour;
            GUI.enabled = was;
            return true;
        }

        /// <summary>The window's own drawing, showing only <paramref name="visible"/> (window-local) of it.</summary>
        private static void Clipped(UiWindow w, Rect visible)
        {
            if (visible.width <= 0f || visible.height <= 0f)
                return;
            GUI.BeginGroup(visible);
            GUI.BeginGroup(new Rect(-visible.x, -visible.y, w.Rect.width, w.Rect.height));
            w.Draw();
            GUI.EndGroup();
            GUI.EndGroup();
        }

        private void ForgetIntro(UiWindow w) => _intros.Remove(w);
    }
}
