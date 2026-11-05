using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using UOClient.IO.Formats;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// Minimal PNG codec for the art pipeline (no engine or image-library dependency).
    /// Reads 8/16-bit grayscale, gray+alpha, RGB and RGBA, non-interlaced, which covers ComfyUI output.
    /// Writes 8-bit RGBA.
    /// </summary>
    public static class Png
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static void Write(string path, Bitmap32 bmp)
        {
            using (FileStream fs = File.Create(path))
                Write(fs, bmp);
        }

        public static void Write(Stream s, Bitmap32 bmp)
        {
            int stride = bmp.Width * 4 + 1;
            var raw = new byte[stride * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
            {
                int o = y * stride;
                raw[o++] = 0; // filter: none
                for (int x = 0; x < bmp.Width; x++)
                {
                    uint p = bmp.Pixels[y * bmp.Width + x];
                    raw[o++] = (byte)(p >> 16);
                    raw[o++] = (byte)(p >> 8);
                    raw[o++] = (byte)p;
                    raw[o++] = (byte)(p >> 24);
                }
            }

            s.Write(Signature, 0, Signature.Length);

            var ihdr = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr, bmp.Width);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), bmp.Height);
            ihdr[8] = 8; // bit depth
            ihdr[9] = 6; // RGBA
            WriteChunk(s, "IHDR", ihdr);
            WriteChunk(s, "IDAT", ZlibCompress(raw));
            WriteChunk(s, "IEND", Array.Empty<byte>());
        }

        public static Bitmap32 Read(string path)
        {
            return Read(File.ReadAllBytes(path));
        }

        public static Bitmap32 Read(byte[] file)
        {
            if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
                throw new InvalidDataException("Not a PNG file.");

            int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
            var idat = new MemoryStream();
            int pos = 8;

            while (pos + 8 <= file.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(pos));
                string type = Encoding.ASCII.GetString(file, pos + 4, 4);
                int data = pos + 8;
                if (data + length > file.Length)
                    throw new InvalidDataException("Truncated PNG chunk.");

                if (type == "IHDR")
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(data));
                    height = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(data + 4));
                    bitDepth = file[data + 8];
                    colorType = file[data + 9];
                    interlace = file[data + 12];
                }
                else if (type == "IDAT")
                {
                    idat.Write(file, data, length);
                }
                else if (type == "IEND")
                {
                    break;
                }

                pos = data + length + 4; // skip CRC
            }

            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new NotSupportedException($"PNG color type {colorType} is not supported (save as RGB or RGBA).");
            }
            if (bitDepth != 8 && bitDepth != 16)
                throw new NotSupportedException($"PNG bit depth {bitDepth} is not supported.");
            if (interlace != 0)
                throw new NotSupportedException("Interlaced PNGs are not supported.");

            int sampleBytes = bitDepth / 8;
            int bpp = channels * sampleBytes;
            int stride = width * bpp;
            byte[] raw = ZlibDecompress(idat.ToArray(), (stride + 1) * height);

            var bmp = new Bitmap32(width, height);
            var prev = new byte[stride];
            var row = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                int o = y * (stride + 1);
                byte filter = raw[o];
                Buffer.BlockCopy(raw, o + 1, row, 0, stride);
                Unfilter(filter, row, prev, bpp);

                for (int x = 0; x < width; x++)
                {
                    int p = x * bpp;
                    byte Sample(int c) => row[p + c * sampleBytes]; // high byte of 16-bit samples
                    byte r, g, b, a;
                    switch (channels)
                    {
                        case 1: r = g = b = Sample(0); a = 255; break;
                        case 2: r = g = b = Sample(0); a = Sample(1); break;
                        case 3: r = Sample(0); g = Sample(1); b = Sample(2); a = 255; break;
                        default: r = Sample(0); g = Sample(1); b = Sample(2); a = Sample(3); break;
                    }
                    bmp.Pixels[y * width + x] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                }

                byte[] t = prev;
                prev = row;
                row = t;
            }

            return bmp;
        }

        private static void Unfilter(byte filter, byte[] row, byte[] prev, int bpp)
        {
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= bpp ? row[i - bpp] : 0;
                int b = prev[i];
                int c = i >= bpp ? prev[i - bpp] : 0;
                switch (filter)
                {
                    case 0: break;
                    case 1: row[i] = (byte)(row[i] + a); break;
                    case 2: row[i] = (byte)(row[i] + b); break;
                    case 3: row[i] = (byte)(row[i] + ((a + b) >> 1)); break;
                    case 4:
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        row[i] = (byte)(row[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                        break;
                    default: throw new InvalidDataException($"Bad PNG filter {filter}.");
                }
            }
        }

        private static byte[] ZlibCompress(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x01);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true))
                    deflate.Write(raw, 0, raw.Length);

                uint adler = Adler32(raw);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        private static byte[] ZlibDecompress(byte[] data, int expected)
        {
            var output = new byte[expected];
            using (var input = new MemoryStream(data, 2, data.Length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int total = 0;
                while (total < expected)
                {
                    int n = deflate.Read(output, total, expected - total);
                    if (n == 0)
                        throw new InvalidDataException("PNG image data ended early.");
                    total += n;
                }
            }
            return output;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var header = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, header, 4);
            s.Write(header, 0, 8);
            s.Write(data, 0, data.Length);

            uint crc = Crc(Crc(0xFFFFFFFF, header.AsSpan(4, 4)), data) ^ 0xFFFFFFFF;
            var c = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(c, crc);
            s.Write(c, 0, 4);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
