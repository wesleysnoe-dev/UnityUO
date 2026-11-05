using System;
using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>Speech, labels and damage numbers over whoever they belong to.</summary>
    public sealed class OverheadText
    {
        private readonly List<Overhead> _overheads = new List<Overhead>();

        private sealed class Overhead
        {
            public uint Serial;
            public string Text;
            public Color Color;
            public int Size;
            public float Born, Life;
            public bool Rises; // damage numbers float upward
        }

        /// <summary>Lines alive right now.</summary>
        public int Count => _overheads.Count;

        public void Add(uint serial, string text, Color color, int size, float life, bool rises)
        {
            // Classic clients keep a few lines per speaker; older ones scroll off.
            int count = 0;
            for (int i = _overheads.Count - 1; i >= 0; i--)
                if (_overheads[i].Serial == serial && !_overheads[i].Rises && ++count >= 3)
                    _overheads.RemoveAt(i);
            _overheads.Add(new Overhead { Serial = serial, Text = text, Color = color, Size = size, Born = Time.realtimeSinceStartup, Life = life, Rises = rises });
        }

        /// <summary>
        /// Hands the live lines to the HUD. <paramref name="drawnRect"/> gives a serial's drawn rectangle (world units),
        /// or null if it's not drawn.
        /// </summary>
        public void Update(DemoHud hud, Func<uint, Rect?> drawnRect, Camera camera)
        {
            hud.Overheads.Clear();
            float now = Time.realtimeSinceStartup;
            _overheads.RemoveAll(o => now - o.Born > o.Life);
            // Newest first: the HUD stacks each speaker's lines upward from the head as it measures them.
            for (int i = _overheads.Count - 1; i >= 0; i--)
            {
                Overhead o = _overheads[i];
                Vector2? anchor = Anchor(drawnRect(o.Serial), camera);
                if (anchor == null)
                    continue;
                Vector2 p = anchor.Value;
                if (o.Rises)
                    p.y -= (now - o.Born) * 40f;
                float fade = Mathf.Clamp01((o.Life - (now - o.Born)) / 0.5f);
                Color c = o.Color;
                c.a = fade;
                hud.Overheads.Add((o.Text, p, c, o.Size, o.Rises ? 0u : o.Serial));
            }
        }

        /// <summary>Where overhead text goes on screen (top-left origin) over a drawn rectangle, or null if it's not drawn.</summary>
        private static Vector2? Anchor(Rect? drawn, Camera camera)
        {
            if (drawn == null)
                return null;
            Rect r = drawn.Value;
            if (r.width <= 0)
                return null;
            Vector3 s = camera.WorldToScreenPoint(new Vector3(r.center.x, r.yMax, 0));
            return new Vector2(s.x, Screen.height - s.y - 18);
        }
    }
}
