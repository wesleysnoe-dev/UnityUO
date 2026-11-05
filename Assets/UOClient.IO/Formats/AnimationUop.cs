using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// The newer mobile animations: AnimationFrame1..4.uop hold one entry per (body, action), named
    /// "build/animationlegacyframe/{body:D6}/{action:D2}.bin", with all five stored directions inside it.
    /// AnimationSequence.uop swaps some actions for others. Formats as read by TazUO's
    /// <c>AnimationsLoader.ReadUOPAnimationFrames</c> and <c>LoadUop</c> (ClassicUO.Assets/AnimationsLoader.cs:835-1013, 1308-1507).
    /// </summary>
    public static class AnimationUop
    {
        /// <summary>Actions per UOP body ("gargoyle is like 78", AnimationsLoader.cs:40).</summary>
        public const int MaxActions = 80;

        public const int Directions = 5;

        public static string EntryName(int body, int action) => $"build/animationlegacyframe/{body:D6}/{action:D2}.bin";

        /// <summary>
        /// The frames of one stored direction (0..4) from a decompressed action entry. Frame ids missing in the
        /// middle are kept as empty frames; each direction gets round(frames / 5) slots, at least 10 for equipment.
        /// </summary>
        public static AnimationFrame[] Decode(byte[] data, int fileDirection, bool equipment = false)
        {
            if (data == null || data.Length < 40 || (uint)fileDirection >= Directions)
                return Array.Empty<AnimationFrame>();
            ReadOnlySpan<byte> d = data;

            int count = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(32));
            long start = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(36));
            if (count <= 0 || start + (long)count * 16 > d.Length)
                return Array.Empty<AnimationFrame>();

            // Headers: u16 group, u16 frame id (from 1), 8 unknown bytes, u32 offset of the palette from this header.
            var headers = new List<(int Position, int FrameId, uint PixelOffset)>(count);
            int lastFrameId = 1;
            for (int i = 0; i < count; i++)
            {
                int pos = (int)start + i * 16;
                int frameId = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(pos + 2));
                uint pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos + 12));
                while (frameId - lastFrameId > 1)
                    headers.Add((0, ++lastFrameId, 0)); // a gap: an empty frame that still takes its slot
                headers.Add((pos, frameId, pixelOffset));
                lastFrameId = frameId;
            }

            int perDirection = (int)Math.Round(headers.Count / (float)Directions);
            if (equipment)
                perDirection = Math.Max(10, perDirection);
            if (perDirection <= 0)
                return Array.Empty<AnimationFrame>();

            var frames = new AnimationFrame[perDirection];
            foreach (var h in headers)
            {
                int dir = (h.FrameId - 1) / perDirection;
                if (dir < fileDirection)
                    continue;
                if (dir > fileDirection)
                    break;
                int slot = (h.FrameId - 1) % perDirection;
                if (slot < 0 || h.Position == 0)
                    continue;
                long pixels = h.Position + (long)h.PixelOffset;
                if (pixels + 512 > d.Length)
                    continue;
                uint[] palette = AnimationFile.Palette(d.Slice((int)pixels, 512));
                frames[slot] = AnimationFile.DecodeSprite(d, (int)pixels + 512, palette);
            }

            for (int i = 0; i < frames.Length; i++)
                if (frames[i] == null)
                    frames[i] = new AnimationFrame { Image = new Bitmap32(1, 1) };
            return frames;
        }

        /// <summary>
        /// One AnimationSequence.uop entry: the body it describes and, for each action, the action actually drawn.
        /// Only replacements with a frame count of 0 count. Null if the entry is too short.
        /// </summary>
        public static (int Body, int[] Actions)? ParseSequence(byte[] data)
        {
            if (data == null || data.Length < 56)
                return null;
            ReadOnlySpan<byte> d = data;
            int body = (int)BinaryPrimitives.ReadUInt32LittleEndian(d);
            int replaces = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(52));

            var actions = new int[MaxActions];
            for (int i = 0; i < actions.Length; i++)
                actions[i] = i;

            // TazUO skips the list when its count is 48 or 68 (AnimationsLoader.cs:992).
            if (replaces != 48 && replaces != 68)
            {
                int pos = 56;
                for (int k = 0; k < replaces && pos + 72 <= d.Length; k++, pos += 72)
                {
                    int oldGroup = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(pos));
                    uint frameCount = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos + 4));
                    int newGroup = BinaryPrimitives.ReadInt32LittleEndian(d.Slice(pos + 8));
                    if (frameCount == 0 && oldGroup >= 0 && oldGroup < MaxActions)
                        actions[oldGroup] = newGroup;
                }
            }
            return (body, actions);
        }
    }
}
