using System;
using System.IO;

namespace UOClient.IO.Formats
{
    /// <summary>One animated item's frames: offsets added to the base graphic, and the time per frame.</summary>
    public readonly struct AnimDataEntry
    {
        public readonly sbyte[] Offsets;
        public readonly int Interval;      // in 100 ms ticks, as the classic client counts them
        public readonly int StartInterval;

        public AnimDataEntry(sbyte[] offsets, int interval, int startInterval)
        {
            Offsets = offsets;
            Interval = interval;
            StartInterval = startInterval;
        }

        public bool IsAnimated => Offsets != null && Offsets.Length > 1;
    }

    /// <summary>
    /// animdata.mul: animated item art (fire, water wheels, spell effects). Blocks of eight entries, each
    /// block a 4-byte header then 8 × (64 frame offsets, unknown byte, frame count, interval, start interval).
    /// </summary>
    public sealed class AnimData
    {
        private const int EntrySize = 68, BlockSize = 4 + 8 * EntrySize;
        private readonly byte[] _data;

        public AnimData(string path)
        {
            _data = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
        }

        public AnimDataEntry Get(int graphic)
        {
            int offset = (graphic >> 3) * BlockSize + 4 + (graphic & 7) * EntrySize;
            if (graphic < 0 || offset + EntrySize > _data.Length)
                return default;
            int count = Math.Min(64, (int)_data[offset + 65]);
            if (count == 0)
                return default;
            var frames = new sbyte[count];
            for (int i = 0; i < count; i++)
                frames[i] = (sbyte)_data[offset + i];
            return new AnimDataEntry(frames, _data[offset + 66], _data[offset + 67]);
        }
    }
}
