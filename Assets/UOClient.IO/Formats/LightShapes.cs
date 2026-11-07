using System;
using System.IO;

namespace UOClient.IO.Formats
{
    /// <summary>One light.mul shape: its size, its brightest texel (0-31) and how far from its centre it reaches.</summary>
    public readonly struct LightShape
    {
        public readonly int Width, Height, Peak;
        public readonly float Radius;

        public LightShape(int width, int height, int peak, float radius)
        {
            Width = width;
            Height = height;
            Peak = peak;
            Radius = radius;
        }

        public bool Exists => Width > 0 && Height > 0 && Peak > 0;
    }

    /// <summary>
    /// light.mul / lightidx.mul: the light shapes the classic client draws for a light id (TazUO LightsLoader.GetLight). The
    /// index's extra field holds width (high 16 bits) and height (low 16); each texel is a byte, 0-31, a value above 0x1F
    /// stored bit-inverted. Read once; only the summary each shape needs is kept.
    /// </summary>
    public sealed class LightShapes
    {
        private readonly LightShape[] _shapes;
        private readonly byte[][] _texels;

        public LightShapes(string mulPath, string idxPath)
        {
            if (!File.Exists(mulPath) || !File.Exists(idxPath))
            {
                _shapes = Array.Empty<LightShape>();
                _texels = Array.Empty<byte[]>();
                return;
            }
            byte[] mul = File.ReadAllBytes(mulPath), idx = File.ReadAllBytes(idxPath);
            int count = idx.Length / 12;
            _shapes = new LightShape[count];
            _texels = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                int offset = BitConverter.ToInt32(idx, i * 12), length = BitConverter.ToInt32(idx, i * 12 + 4);
                int extra = BitConverter.ToInt32(idx, i * 12 + 8);
                int w = (extra >> 16) & 0xFFFF, h = extra & 0xFFFF;
                if (offset < 0 || length <= 0 || w == 0 || h == 0 || offset + w * h > mul.Length)
                    continue;
                var t = new byte[w * h];
                int peak = 0;
                float cx = (w - 1) / 2f, cy = (h - 1) / 2f, reach = 0;
                for (int k = 0; k < t.Length; k++)
                {
                    int v = mul[offset + k];
                    if (v > 0x1F)
                        v = ~v & 0x1F;
                    t[k] = (byte)v;
                    if (v == 0)
                        continue;
                    peak = Math.Max(peak, v);
                    float dx = k % w - cx, dy = k / w - cy;
                    reach = Math.Max(reach, (float)Math.Sqrt(dx * dx + dy * dy));
                }
                _texels[i] = t;
                _shapes[i] = new LightShape(w, h, peak, reach + 0.5f);
            }
        }

        public int Count => _shapes.Length;

        public LightShape Get(int id) => id >= 0 && id < _shapes.Length ? _shapes[id] : default;

        /// <summary>A texel (0-31) of shape <paramref name="id"/>, from its top-left; 0 outside.</summary>
        public int Texel(int id, int x, int y)
        {
            LightShape s = Get(id);
            return !s.Exists || x < 0 || y < 0 || x >= s.Width || y >= s.Height ? 0 : _texels[id][y * s.Width + x];
        }
    }
}
