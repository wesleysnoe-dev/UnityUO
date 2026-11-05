using System;
using System.Buffers.Binary;

namespace UOClient.IO.Files
{
    /// <summary>
    /// Second stage of UOP compression flag 3, applied after zlib: a move-to-front decode
    /// followed by an inverse block-sorting transform.
    /// Ported from ClassicUO's BwtDecompress (BSD-2-Clause, https://github.com/ClassicUO/ClassicUO).
    /// </summary>
    public static class MythicDecompress
    {
        public static byte[] Decompress(byte[] buffer)
        {
            if (buffer.Length < 5)
                return Array.Empty<byte>();

            // Move-to-front over 256 symbols. Bytes after a 4-byte header; the final byte is never emitted.
            var mtf = new byte[256];
            for (int i = 0; i < 256; i++)
                mtf[i] = (byte)i;

            var list = new byte[buffer.Length - 4];
            int pos = 4;
            byte current = buffer[pos++];
            int n = 0;
            while (pos <= buffer.Length - 1)
            {
                byte value = mtf[current];
                for (int c = current; c > 0; c--)
                    mtf[c] = mtf[c - 1];
                mtf[0] = value;
                list[n++] = value;
                current = buffer[pos++];
            }

            return InverseTransform(list);
        }

        private static byte[] InverseTransform(byte[] input)
        {
            if (input.Length < 1024)
                return Array.Empty<byte>();

            var symbols = new int[256];
            for (int i = 0; i < 256; i++)
                symbols[i] = i;

            // [0..255] symbol counts, [256..511] read cursor per symbol, [512..767] end per symbol.
            var table = new int[768];
            for (int i = 0; i < 256; i++)
                table[i] = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(i * 4));

            long sum = 0;
            int nonZero = 0;
            for (int i = 0; i < 256; i++)
            {
                sum += table[i];
                if (table[i] != 0)
                    nonZero++;
            }

            if (sum <= 0 || sum > int.MaxValue)
                return Array.Empty<byte>();

            int length = (int)sum;
            var output = new byte[length];
            int[] byFrequency = SymbolsByFrequency(table);

            for (int i = 0, m = 0; i < nonZero; i++)
            {
                int symbol = byFrequency[i];
                symbols[input[m + 1024]] = symbol;
                table[symbol + 256] = m + 1;
                m += table[symbol];
                table[symbol + 512] = m;
            }

            byte val = (byte)symbols[0];
            for (int count = 0; count < length; count++)
            {
                output[count] = val;
                int cursor = table[val + 256];

                if (cursor >= table[val + 512])
                {
                    if (nonZero-- > 0)
                    {
                        ShiftLeft(symbols, nonZero);
                        val = (byte)symbols[0];
                    }
                }
                else
                {
                    int idx = input[cursor + 1024];
                    table[val + 256] = cursor + 1;

                    if (idx != 0)
                    {
                        ShiftLeft(symbols, idx);
                        symbols[idx] = val;
                        val = (byte)symbols[0];
                    }
                }
            }

            return output;
        }

        /// <summary>Symbols with a non-zero count, most frequent first; ties keep the lower symbol first.</summary>
        private static int[] SymbolsByFrequency(int[] counts)
        {
            var tmp = new int[256];
            Array.Copy(counts, tmp, 256);
            var result = new int[256];

            for (int i = 0; i < 256; i++)
            {
                int best = 0, bestValue = 0;
                for (int j = 0; j < 256; j++)
                {
                    if (tmp[j] > bestValue)
                    {
                        best = j;
                        bestValue = tmp[j];
                    }
                }

                if (bestValue == 0)
                    break;

                result[i] = best;
                tmp[best] = 0;
            }
            return result;
        }

        private static void ShiftLeft(int[] values, int max)
        {
            for (int i = 0; i < max; i++)
                values[i] = values[i + 1];
        }
    }
}
