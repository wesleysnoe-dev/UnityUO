using System;

namespace UOClient.ArtPipeline
{
    /// <summary>
    /// Per-channel mean/std of original vs restyled pixels, for a simple color transfer:
    /// art that was never visible is shifted to look like the restyled pixels around it.
    /// </summary>
    internal sealed class ColorTransfer
    {
        private readonly double[] _sumO = new double[3], _sumO2 = new double[3];
        private readonly double[] _sumR = new double[3], _sumR2 = new double[3];
        private int _n;

        public int Samples => _n;

        public void Add(uint original, uint restyled)
        {
            for (int c = 0; c < 3; c++)
            {
                double o = (original >> (16 - c * 8)) & 0xFF;
                double r = (restyled >> (16 - c * 8)) & 0xFF;
                _sumO[c] += o;
                _sumO2[c] += o * o;
                _sumR[c] += r;
                _sumR2[c] += r * r;
            }
            _n++;
        }

        public uint Apply(uint original)
        {
            if (_n == 0)
                return original | 0xFF000000;

            uint result = 0xFF000000;
            for (int c = 0; c < 3; c++)
            {
                double meanO = _sumO[c] / _n, meanR = _sumR[c] / _n;
                double stdO = Math.Sqrt(Math.Max(0, _sumO2[c] / _n - meanO * meanO));
                double stdR = Math.Sqrt(Math.Max(0, _sumR2[c] / _n - meanR * meanR));
                double ratio = stdO < 1 ? 1 : Math.Max(0.5, Math.Min(2.0, stdR / stdO));

                double o = (original >> (16 - c * 8)) & 0xFF;
                int v = (int)Math.Round((o - meanO) * ratio + meanR);
                result |= (uint)Math.Max(0, Math.Min(255, v)) << (16 - c * 8);
            }
            return result;
        }
    }
}
