using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// Speech keyword table (speech.mul): entries of big-endian uint16 id, uint16 length and UTF-8
    /// pattern such as "*bank*". The client tags outgoing speech with matching ids; NPCs like bankers
    /// and stablemasters react to those ids, not to the raw text.
    /// </summary>
    public sealed class SpeechKeywords
    {
        private readonly List<(int Id, string[] Words, bool AnyStart, bool AnyEnd)> _entries = new List<(int, string[], bool, bool)>();

        public SpeechKeywords(string path) : this(File.ReadAllBytes(path)) { }

        public SpeechKeywords(byte[] data)
        {
            int pos = 0;
            while (pos + 4 <= data.Length)
            {
                int id = (data[pos] << 8) | data[pos + 1];
                int length = (data[pos + 2] << 8) | data[pos + 3];
                pos += 4;
                if (pos + length > data.Length)
                    break;
                if (length > 0)
                    Add(id, Encoding.UTF8.GetString(data, pos, length));
                pos += length;
            }
        }

        public SpeechKeywords(IEnumerable<(int Id, string Pattern)> entries)
        {
            foreach (var (id, pattern) in entries)
                Add(id, pattern);
        }

        public int Count => _entries.Count;

        /// <summary>Ids of every pattern the text matches, ascending. A leading/trailing '*' allows any prefix/suffix.</summary>
        public List<int> Match(string text)
        {
            var ids = new List<int>();
            string input = (text ?? "").ToLowerInvariant();
            foreach (var e in _entries)
            {
                if (ids.Contains(e.Id) || !IsMatch(input, e.Words, e.AnyStart, e.AnyEnd))
                    continue;
                ids.Add(e.Id);
            }
            ids.Sort();
            return ids;
        }

        private void Add(int id, string pattern)
        {
            pattern = pattern.Trim().ToLowerInvariant();
            string[] words = pattern.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
                _entries.Add((id, words, pattern.StartsWith("*", StringComparison.Ordinal), pattern.EndsWith("*", StringComparison.Ordinal)));
        }

        private static bool IsMatch(string input, string[] words, bool anyStart, bool anyEnd)
        {
            foreach (string w in words)
            {
                if (w.Length > input.Length)
                    continue;
                if (!anyStart && !input.StartsWith(w, StringComparison.Ordinal))
                    continue;
                if (!anyEnd && !input.EndsWith(w, StringComparison.Ordinal))
                    continue;
                if (input.IndexOf(w, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }
    }
}
