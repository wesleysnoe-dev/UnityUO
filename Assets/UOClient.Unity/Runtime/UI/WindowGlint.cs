using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A window's glint as it opens: a band of light runs along its frame (the top left to right, the bottom right to
    /// left) and a four-rayed twinkle flares at the corner each reaches. Drawn over the window, in its own coordinates,
    /// for <see cref="Duration"/> seconds after it opens. Pictures painted here, once.
    /// </summary>
    public static class WindowGlint
    {
        public const float Duration = 1.0f;

        private static Texture2D _band, _star;
        private static readonly Color Light = new Color(1f, 0.93f, 0.72f);

        /// <summary>
        /// Draws the glint <paramref name="age"/> seconds into it over a window of size <paramref name="size"/> whose frame
        /// is <paramref name="border"/> thick. Nothing outside 0..Duration.
        /// </summary>
        public static void Draw(Vector2 size, float age, float border)
        {
            if (age < 0f || age >= Duration || Event.current.type != EventType.Repaint)
                return;
            float t = age / Duration;
            float band = Mathf.Clamp(size.x * 0.22f, 40f, 120f);
            float strip = Mathf.Max(6f, border);
            // The sweeps: out along the edges over the first 70%, fading at their ends.
            float run = Mathf.Clamp01(t / 0.7f);
            float fade = Mathf.Sin(run * Mathf.PI);
            if (run < 1f)
            {
                float x = Mathf.Lerp(-band, size.x, Ease(run));
                Sweep(new Rect(0, 0, size.x, strip), x, band, fade);
                Sweep(new Rect(0, size.y - strip, size.x, strip), size.x - band - x, band, fade);
            }
            // The twinkles: where each sweep ends, flaring and fading as it arrives.
            float star = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.3f, 44f, 96f);
            Twinkle(new Vector2(size.x - strip * 0.5f, strip * 0.5f), star, (t - 0.45f) / 0.5f);
            Twinkle(new Vector2(strip * 0.5f, size.y - strip * 0.5f), star, (t - 0.5f) / 0.5f);
        }

        private static float Ease(float x) => 1f - (1f - x) * (1f - x);

        private static void Sweep(Rect strip, float x, float width, float alpha)
        {
            GUI.BeginGroup(strip);
            Color was = GUI.color;
            GUI.color = new Color(Light.r, Light.g, Light.b, alpha);
            GUI.DrawTexture(new Rect(x, 0, width, strip.height), Band);
            GUI.color = was;
            GUI.EndGroup();
        }

        /// <summary>A twinkle <paramref name="p"/> of the way through (0..1): it swells, turns a little, and shrinks away.</summary>
        private static void Twinkle(Vector2 at, float size, float p)
        {
            if (p <= 0f || p >= 1f)
                return;
            float s = Mathf.Sin(p * Mathf.PI);
            float d = size * (0.35f + 0.65f * s);
            Matrix4x4 was = GUI.matrix;
            GUIUtility.RotateAroundPivot(p * 40f, at);
            Color c = GUI.color;
            GUI.color = new Color(Light.r, Light.g, Light.b, s);
            GUI.DrawTexture(new Rect(at.x - d / 2f, at.y - d / 2f, d, d), Star);
            GUI.color = c;
            GUI.matrix = was;
        }

        /// <summary>A soft vertical band of light, clear at both sides.</summary>
        private static Texture2D Band => _band ??= Paint(64, 8, (x, y) =>
        {
            float a = Mathf.Clamp01(1f - Mathf.Abs(x));
            return a * a;
        });

        /// <summary>A four-rayed star: a soft core, long thin rays across and down, shorter fainter ones on the diagonals.</summary>
        private static Texture2D Star => _star ??= Paint(128, 128, (x, y) =>
        {
            float r2 = x * x + y * y;
            float core = Mathf.Exp(-r2 / (2f * 0.07f * 0.07f));
            float across = Mathf.Exp(-(y * y) / (2f * 0.018f * 0.018f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(x)), 2f);
            float down = Mathf.Exp(-(x * x) / (2f * 0.018f * 0.018f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(y)), 2f);
            float u = (x + y) * 0.7071f, v = (x - y) * 0.7071f;
            float diag = 0.45f * (Mathf.Exp(-(v * v) / (2f * 0.012f * 0.012f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u) * 1.8f), 2f)
                                  + Mathf.Exp(-(u * u) / (2f * 0.012f * 0.012f)) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v) * 1.8f), 2f));
            return Mathf.Clamp01(Mathf.Max(core, Mathf.Max(across, Mathf.Max(down, diag))));
        });

        /// <summary>A white picture whose alpha is <paramref name="alpha"/>(x, y), x and y running -1..1.</summary>
        private static Texture2D Paint(int w, int h, System.Func<float, float, float> alpha)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear
            };
            var px = new Color[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i + 0.5f) / w * 2f - 1f, y = (j + 0.5f) / h * 2f - 1f;
                    px[j * w + i] = new Color(1f, 1f, 1f, alpha(x, y));
                }
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }
    }
}
