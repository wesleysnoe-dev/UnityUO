using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>How a window makes its entrance (<see cref="UiManager.OpenWithIntro{T}"/>).</summary>
    public enum WindowIntro
    {
        /// <summary>The book flies up from where it was used, grows into a closed book and its cover swings open.</summary>
        Book,

        /// <summary>A rolled scroll flies up, settles at the window's top edge and unrolls downward.</summary>
        Scroll
    }

    public sealed partial class UiManager
    {
        /// <summary>Opening animations on (the options' "Animate books and scrolls"); off, windows appear at once.</summary>
        public static bool OpenAnimations { get; set; } = true;

        /// <summary>How long an opening animation takes, seconds.</summary>
        public const float IntroSeconds = 2.5f;

        /// <summary>The share of the animation spent flying to the window; the rest opens or unrolls it.</summary>
        private const float FlightShare = 0.45f;

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
            _intros[window] = new Intro { Kind = kind, Start = now, From = from, Art = art };
            return opened;
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

        /// <summary>Whether the window is still in flight (not drawn at all yet).</summary>
        private bool IntroHidesWindow(UiWindow w)
        {
            float t = IntroT(w);
            return t >= 0f && t < FlightShare;
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>The flying art for every window in flight, each as a small window of its own above the others.</summary>
        private void DrawIntroFlights()
        {
            foreach (KeyValuePair<UiWindow, Intro> kv in new List<KeyValuePair<UiWindow, Intro>>(_intros))
            {
                float t = IntroT(kv.Key);
                if (t < 0f || t >= FlightShare)
                    continue;
                Intro intro = kv.Value;
                Rect target = FlightTarget(kv.Key.Rect, intro.Kind, intro.Art);
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

        /// <summary>Where the flight ends, the art at twice its size: on the right page's middle (the book), or the top's (the scroll).</summary>
        private static Rect FlightTarget(Rect window, WindowIntro kind, UiImage art)
        {
            var size = new Vector2(art.Width, art.Height) * 2f;
            Vector2 centre = kind == WindowIntro.Book
                ? new Vector2(window.x + window.width * 0.75f, window.y + window.height / 2f)
                : new Vector2(window.x + window.width / 2f, window.y);
            return new Rect(centre - size / 2f, size);
        }

        private static readonly Color Leather = new Color(0.36f, 0.14f, 0.09f), Gilt = new Color(0.83f, 0.66f, 0.3f),
            Parchment = new Color(0.86f, 0.76f, 0.56f), ParchmentDark = new Color(0.55f, 0.42f, 0.26f);

        private static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }

        /// <summary>A closed book's cover over <paramref name="r"/>: leather, a gilt rule, the book's own art in the middle.</summary>
        private static void Cover(Rect r, UiImage art, float alpha)
        {
            if (r.width < 1f)
                return;
            Fill(r, new Color(Leather.r, Leather.g, Leather.b, alpha));
            Fill(new Rect(r.x + 6f, r.y + 6f, Mathf.Max(0f, r.width - 12f), 2f), new Color(Gilt.r, Gilt.g, Gilt.b, alpha));
            Fill(new Rect(r.x + 6f, r.yMax - 8f, Mathf.Max(0f, r.width - 12f), 2f), new Color(Gilt.r, Gilt.g, Gilt.b, alpha));
            float w = Mathf.Min(art.Width * 2f, r.width), h = art.Height * 2f * (w / (art.Width * 2f));
            Color was = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(r.center.x - w / 2f, r.center.y - h / 2f, w, h), art.Texture, ScaleMode.StretchToFill);
            GUI.color = was;
        }

        /// <summary>A rolled-up length of parchment lying across <paramref name="r"/> (a cylinder: dark edges, light middle).</summary>
        private static void Roll(Rect r)
        {
            Fill(r, ParchmentDark);
            Fill(new Rect(r.x + 2f, r.y + r.height * 0.2f, r.width - 4f, r.height * 0.6f), Parchment);
            Fill(new Rect(r.x + 2f, r.y + r.height * 0.32f, r.width - 4f, r.height * 0.12f), new Color(1f, 0.95f, 0.82f));
            Fill(new Rect(r.x, r.y, 6f, r.height), new Color(0.4f, 0.28f, 0.16f));          // the ends
            Fill(new Rect(r.xMax - 6f, r.y, 6f, r.height), new Color(0.4f, 0.28f, 0.16f));
        }

        /// <summary>
        /// Draws a window during its opening (window-local, in DrawWindow): the book's right page under its cover, the
        /// cover swinging over the spine and fading as the left page shows; or the scroll revealed top-down behind its roll.
        /// Returns false when the window has no animation running (draw it normally).
        /// </summary>
        private bool DrawIntroWindow(UiWindow w)
        {
            float t = IntroT(w);
            if (t < 0f)
                return false;
            Intro intro = _intros[w];
            float open = Ease(Mathf.Clamp01((t - FlightShare) / (1f - FlightShare)));
            float W = w.Rect.width, H = w.Rect.height;
            bool was = GUI.enabled;
            GUI.enabled = false; // nothing takes a click until it is open
            if (intro.Kind == WindowIntro.Book)
            {
                // The right page from the start (the closed book lies on it); the left page as the cover leaves it.
                float half = W / 2f;
                float leftShown = open < 0.5f ? 0f : half;
                Clipped(w, new Rect(half - leftShown, 0, half + leftShown, H));
                float angle = open * Mathf.PI;
                float coverW = half * Mathf.Abs(Mathf.Cos(angle));
                if (open < 0.5f)
                    Cover(new Rect(half, 0, coverW, H), intro.Art, 1f); // the cover's front, swinging up off the right page
                else
                    Fill(new Rect(half - coverW, 0, coverW, H), new Color(Leather.r * 1.3f, Leather.g * 1.3f, Leather.b * 1.3f, 1f - (open - 0.5f) * 2f)); // its inside, landing and fading
            }
            else
            {
                // First the roll spreads to the window's width along its top, then it unrolls downward.
                float spread = Mathf.Clamp01(open / 0.2f), down = Ease(Mathf.Clamp01((open - 0.2f) / 0.8f));
                float shown = H * down;
                Clipped(w, new Rect(0, 0, W, shown));
                float rw = Mathf.Lerp(intro.Art.Width * 2f, W + 8f, spread);
                Roll(new Rect(W / 2f - rw / 2f, shown - 12f, rw, 24f));
            }
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
