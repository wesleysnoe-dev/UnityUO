using System;
using System.Collections.Generic;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// An animation as a run of pictures (Resources/Hud/anim/NAME/00.png, 01.png ...), made with ComfyUI's video model
    /// (E:/UOOverhaul/tools/anim_art.py) and played back here: the orbs' liquid, the sparkles. Missing frames mean
    /// no flipbook (null), and whoever asked draws what it drew before.
    /// </summary>
    public sealed class FlipBook
    {
        private static readonly Dictionary<string, FlipBook> Loaded = new Dictionary<string, FlipBook>();

        public readonly Texture2D[] Frames;
        public readonly float Fps;

        private FlipBook(Texture2D[] frames, float fps)
        {
            Frames = frames;
            Fps = fps;
        }

        /// <summary>The flipbook of that name, or null when its frames are not in the build.</summary>
        public static FlipBook Get(string name, float fps = 16f)
        {
            if (Loaded.TryGetValue(name, out FlipBook f))
                return f;
            Texture2D[] frames = Resources.LoadAll<Texture2D>("Hud/anim/" + name);
            if (frames == null || frames.Length == 0)
                return Loaded[name] = null;
            Array.Sort(frames, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (Texture2D t in frames)
                t.wrapMode = TextureWrapMode.Clamp;
            return Loaded[name] = new FlipBook(frames, fps);
        }

        public float Duration => Frames.Length / Fps;

        /// <summary>The frame at a time: round and round for a loop, else held on the last.</summary>
        public Texture2D At(float seconds, bool loop)
        {
            int i = Mathf.FloorToInt(Mathf.Max(0f, seconds) * Fps);
            i = loop ? i % Frames.Length : Mathf.Min(i, Frames.Length - 1);
            return Frames[i];
        }
    }

    /// <summary>
    /// One-shot flipbooks played over the UI where something happened (a slot used, an orb filling): each runs once at
    /// its place and goes. Drawn by whoever owns the place, in its own coordinates.
    /// </summary>
    public sealed class FlipBursts
    {
        private readonly List<(string Name, Rect At, float Start)> _running = new List<(string, Rect, float)>();

        public void Play(string name, Rect at)
        {
            if (FlipBook.Get(name) != null)
                _running.Add((name, at, Time.unscaledTime));
        }

        /// <summary>Draws the running ones (call on every event; they are dropped on repaint once done).</summary>
        public void Draw()
        {
            float now = Time.unscaledTime;
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                var (name, at, start) = _running[i];
                FlipBook f = FlipBook.Get(name);
                if (f == null || now - start >= f.Duration)
                {
                    if (Event.current.type == EventType.Repaint)
                        _running.RemoveAt(i);
                    continue;
                }
                GUI.DrawTexture(at, f.At(now - start, false));
            }
        }
    }
}
