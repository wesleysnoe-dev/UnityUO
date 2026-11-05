using System;
using System.Collections.Generic;
using System.Linq;

namespace UOClient.Unity
{
    /// <summary>Frame-time statistics: average, 99th percentile, worst, and hitches over 33 / 50 ms.</summary>
    public sealed class FrameStats
    {
        private readonly List<float> _ms = new List<float>(4096);

        public void Add(float seconds)
        {
            if (_ms.Count >= 20000)
                _ms.RemoveRange(0, 10000);
            _ms.Add(seconds * 1000f);
        }

        public void Reset() => _ms.Clear();

        public string Summary()
        {
            if (_ms.Count == 0)
                return "no frames";
            var sorted = new List<float>(_ms);
            sorted.Sort();
            float avg = sorted.Average();
            float p99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            int over33 = sorted.Count(v => v > 33.4f), over50 = sorted.Count(v => v > 50f);
            return $"{sorted.Count} frames, avg {avg:F1} ms ({1000f / avg:F0} fps), p99 {p99:F1} ms, worst {sorted[sorted.Count - 1]:F1} ms, {over33} over 33 ms, {over50} over 50 ms";
        }
    }
}
