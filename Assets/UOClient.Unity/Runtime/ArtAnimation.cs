using System;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Animated world art (fire, braziers, moongates, spell fields, lanterns) as TazUO's AnimatedStaticsManager runs it: every
    /// static graphic with the tiledata Animation flag and an animdata.mul entry steps through that entry's frame offsets on
    /// one clock per graphic (so all copies show the same frame), a frame lasting Interval x 100 ms + 1 ms, or 100 ms when the
    /// interval is 0 (AnimatedStaticsManager.cs:67-111, ITEM_EFFECT_ANIMATION_DELAY 50 x 2). The art drawn is the base
    /// graphic plus the frame's offset (View.DrawStaticAnimated, View.cs:394-396), for map statics, multi parts and ground
    /// items alike. Starts at frame 0 when the clock does.
    /// </summary>
    public static class ArtAnimation
    {
        /// <summary>TazUO's ITEM_EFFECT_ANIMATION_DELAY x 2, in ms.</summary>
        public const int TickMs = 100;

        /// <summary>The graphic animates: Animation flag and at least one animdata frame (AnimatedStaticsManager.Initialize).</summary>
        public static bool Animates(TileFlags flags, in AnimDataEntry entry) =>
            (flags & TileFlags.Animation) != 0 && entry.Offsets != null && entry.Offsets.Length > 0;

        /// <summary>One frame's length in ms.</summary>
        public static int PeriodMs(in AnimDataEntry entry) => entry.Interval > 0 ? entry.Interval * TickMs + 1 : TickMs;

        /// <summary>The frame index at <paramref name="ms"/> on the clock.</summary>
        public static int Frame(in AnimDataEntry entry, double ms)
        {
            int count = entry.Offsets?.Length ?? 0;
            if (count == 0 || ms <= 0)
                return 0;
            return (int)((long)Math.Floor(ms / PeriodMs(entry)) % count);
        }

        /// <summary>The art drawn at <paramref name="ms"/>: the base graphic plus the frame's offset.</summary>
        public static int Graphic(int graphic, in AnimDataEntry entry, double ms) =>
            entry.Offsets == null || entry.Offsets.Length == 0 ? graphic : (graphic + entry.Offsets[Frame(entry, ms)]) & 0xFFFF;
    }
}
