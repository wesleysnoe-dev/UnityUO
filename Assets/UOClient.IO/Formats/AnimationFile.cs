using System;
using System.Buffers.Binary;
using UOClient.IO.Files;

namespace UOClient.IO.Formats
{
    public sealed class AnimationFrame
    {
        public Bitmap32 Image;

        /// <summary>The frame's anchor: the mobile's draw point sits at (CenterX, Height + CenterY) from the frame's top-left.</summary>
        public int CenterX;
        public int CenterY;
    }

    /// <summary>
    /// Classic anim.mul / anim.idx mobile animations. Each (body, action, direction) entry holds a
    /// 256-color palette and run-length frames. Five directions are stored; the other three are mirrors.
    /// Format details follow ClassicUO's MUL animation reader (BSD-2-Clause).
    /// </summary>
    public sealed class AnimationFile : IDisposable
    {
        private const uint FrameEnd = 0x7FFF7FFF;
        private const uint DoubleXor = (0x200u << 22) | (0x200u << 12);

        private readonly MulFile _file;

        public AnimationFile(string dataPath, string indexPath)
        {
            _file = new MulFile(dataPath, indexPath);
        }

        /// <summary>Entry index in anim.mul: monsters (body &lt; 200) have 22 actions, animals (&lt; 400) 13, humans and equipment 35.</summary>
        public static int Index(int body, int action, int fileDirection)
        {
            int baseIndex = body < 200 ? body * 110
                : body < 400 ? 22000 + (body - 200) * 65
                : 35000 + (body - 400) * 175;
            return baseIndex + action * 5 + fileDirection;
        }

        /// <summary>
        /// Maps a UO direction (0 = north/up-right ... 7 = up) to the stored direction and whether to mirror it.
        /// </summary>
        public static int FileDirection(int direction, out bool mirror)
        {
            mirror = false;
            switch (direction & 7)
            {
                case 2: mirror = true; return 1;
                case 4: return 1;
                case 1: mirror = true; return 2;
                case 5: return 2;
                case 0: mirror = true; return 3;
                case 6: return 3;
                case 3: return 0;
                default: return 4; // 7
            }
        }

        public AnimationFrame[] Read(int body, int action, int fileDirection)
        {
            byte[] data = _file.Read(Index(body, action, fileDirection));
            if (data == null || data.Length < 512 + 4)
                return Array.Empty<AnimationFrame>();
            return Decode(data);
        }

        public static AnimationFrame[] Decode(byte[] data)
        {
            ReadOnlySpan<byte> d = data;
            var palette = new uint[256];
            for (int i = 0; i < 256; i++)
            {
                ushort c = BinaryPrimitives.ReadUInt16LittleEndian(d.Slice(i * 2));
                palette[i] = c == 0 ? 0 : Color16.ToArgb32(c);
            }

            int dataStart = 512;
            int frameCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(dataStart));
            if (frameCount <= 0 || frameCount > 256)
                return Array.Empty<AnimationFrame>();

            var frames = new AnimationFrame[frameCount];
            for (int f = 0; f < frameCount; f++)
            {
                int pos = dataStart + (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(dataStart + 4 + f * 4));
                if (pos + 8 > d.Length)
                    return Array.Empty<AnimationFrame>();

                short centerX = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(pos));
                short centerY = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(pos + 2));
                short width = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(pos + 4));
                short height = BinaryPrimitives.ReadInt16LittleEndian(d.Slice(pos + 6));
                pos += 8;

                var frame = new AnimationFrame { CenterX = centerX, CenterY = centerY };
                if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                {
                    frame.Image = new Bitmap32(1, 1);
                    frames[f] = frame;
                    continue;
                }

                var bmp = new Bitmap32(width, height);
                while (pos + 4 <= d.Length)
                {
                    uint header = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos));
                    pos += 4;
                    if (header == FrameEnd)
                        break;

                    header ^= DoubleXor;
                    int x = (int)((header >> 22) & 0x3FF) + centerX - 0x200;
                    int y = (int)((header >> 12) & 0x3FF) + centerY + height - 0x200;
                    int run = (int)(header & 0xFFF);

                    for (int k = 0; k < run && pos < d.Length; k++, pos++)
                    {
                        int px = x + k;
                        if ((uint)px < (uint)width && (uint)y < (uint)height)
                            bmp.Pixels[y * width + px] = palette[d[pos]];
                    }
                }

                frame.Image = bmp;
                frames[f] = frame;
            }

            return frames;
        }

        public void Dispose() => _file.Dispose();
    }
}
