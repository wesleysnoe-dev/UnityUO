using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// Localized strings (Cliloc.enu): a 6-byte header, then entries of int32 number, byte flag,
    /// uint16 length and UTF-8 text. Arguments substitute "~1_NAME~" style tokens, tab-separated.
    /// </summary>
    public sealed class Cliloc
    {
        private readonly Dictionary<int, string> _entries = new Dictionary<int, string>();

        public Cliloc(string path)
        {
            byte[] data = File.ReadAllBytes(path);

            // Plain files start with header 2 (int32) and 1 (int16). Newer clients ship the file
            // compressed with the same move-to-front/BWT scheme as UOP gumps, without the zlib step.
            bool plain = data.Length >= 6 && BinaryPrimitives.ReadInt32LittleEndian(data) == 2;
            if (!plain)
                data = Files.MythicDecompress.Decompress(data);

            int pos = 6;
            while (pos + 7 <= data.Length)
            {
                int number = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos));
                int length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 5));
                pos += 7;
                if (pos + length > data.Length)
                    break;
                _entries[number] = Encoding.UTF8.GetString(data, pos, length);
                pos += length;
            }
        }

        public int Count => _entries.Count;

        public string Get(int number) => _entries.TryGetValue(number, out string s) ? s : null;

        /// <summary>Formats an entry with tab-separated arguments; "#1234" arguments are themselves cliloc numbers.</summary>
        public string Format(int number, string args)
        {
            string text = Get(number);
            if (text == null)
                return $"[cliloc {number}] {args}".TrimEnd();
            if (string.IsNullOrEmpty(args))
                return StripTokens(text);

            string[] parts = args.Split('\t');
            var sb = new StringBuilder();
            int argIndex = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '~')
                {
                    int end = text.IndexOf('~', i + 1);
                    if (end > i)
                    {
                        string arg = argIndex < parts.Length ? parts[argIndex++] : "";
                        if (arg.StartsWith("#") && int.TryParse(arg.Substring(1), out int nested))
                            arg = Get(nested) ?? arg;
                        sb.Append(arg);
                        i = end;
                        continue;
                    }
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private static string StripTokens(string text)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                int end;
                if (text[i] == '~' && (end = text.IndexOf('~', i + 1)) > i)
                {
                    i = end;
                    continue;
                }
                sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }
}
