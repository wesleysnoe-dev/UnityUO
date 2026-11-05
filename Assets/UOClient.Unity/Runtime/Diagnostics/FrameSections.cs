using System;
using System.Collections.Generic;
using System.Linq;

namespace UOClient.Unity
{
    /// <summary>
    /// Times named parts of Update; when a frame turns out slow (seen at the start of the next one),
    /// records what that frame spent, including garbage collections, so hitches can be attributed.
    /// </summary>
    public sealed class FrameSections
    {
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private readonly List<(string Name, double Ms)> _current = new List<(string, double)>();

        // Unity's own main-thread phases, to attribute time spent outside our Update.
        private static readonly (string Label, global::Unity.Profiling.ProfilerCategory Category, string Marker)[] Markers =
        {
            ("player loop", global::Unity.Profiling.ProfilerCategory.Internal, "PlayerLoop"),
            ("editor loop", global::Unity.Profiling.ProfilerCategory.Internal, "EditorLoop"),
            ("render", global::Unity.Profiling.ProfilerCategory.Render, "Camera.Render"),
            ("GPU wait", global::Unity.Profiling.ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            ("IMGUI", global::Unity.Profiling.ProfilerCategory.Gui, "GUI.Repaint"),
            ("GC", global::Unity.Profiling.ProfilerCategory.Memory, "GC.Collect")
        };
        private readonly global::Unity.Profiling.ProfilerRecorder[] _recorders = Array.ConvertAll(Markers,
            m => global::Unity.Profiling.ProfilerRecorder.StartNew(m.Category, m.Marker));
        private string _open;
        private double _openedAt;
        private int _gcAtStart = GC.CollectionCount(0), _gc2AtStart = GC.CollectionCount(2);

        public readonly List<string> Hitches = new List<string>();
        public float HitchThresholdMs = 33f;

        public void Begin(string name)
        {
            if (!_watch.IsRunning)
                _watch.Start();
            double now = _watch.Elapsed.TotalMilliseconds;
            if (_open != null)
                _current.Add((_open, now - _openedAt));
            _open = name;
            _openedAt = now;
        }

        /// <summary>Call first thing in Update with the last frame's duration.</summary>
        public void EndFrame(float lastFrameSeconds)
        {
            Begin(null);
            double total = lastFrameSeconds * 1000.0;
            int gc0 = GC.CollectionCount(0) - _gcAtStart, gc2 = GC.CollectionCount(2) - _gc2AtStart;
            if (total > HitchThresholdMs && Hitches.Count < 40)
            {
                double inUpdate = _current.Sum(s => s.Ms);
                string parts = string.Join(", ", _current.Where(s => s.Ms >= 0.5).Select(s => $"{s.Name} {s.Ms:F1}"));
                var unity = new List<string>();
                for (int i = 0; i < Markers.Length; i++)
                {
                    double ms = _recorders[i].Valid ? _recorders[i].LastValue / 1e6 : -1;
                    if (ms >= 0.5)
                        unity.Add($"{Markers[i].Label} {ms:F1}");
                }
                Hitches.Add($"{total:F0} ms frame: Update {inUpdate:F1} ms [{parts}], outside Update {total - inUpdate:F0} ms, " +
                            $"GC gen0 {gc0} gen2 {gc2}; Unity: {string.Join(", ", unity)}");
            }
            _current.Clear();
            _watch.Restart();
            _gcAtStart = GC.CollectionCount(0);
            _gc2AtStart = GC.CollectionCount(2);
        }
    }
}
