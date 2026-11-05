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

        /// <summary>
        /// Ids of every pattern the text matches, ascending and without repeats (ModernUO drops repeats anyway, and
        /// rejects the whole line past 50 ids). As in ClassicUO's SpeechesLoader.GetKeywords: the text is trimmed of
        /// spaces, a leading/trailing '*' allows any prefix/suffix, and a keyword only counts as a whole word.
        /// </summary>
        public List<int> Match(string text)
        {
            var ids = new List<int>();
            string input = (text ?? "").Trim(' ');
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
            string[] words = pattern.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
                _entries.Add((id, words, pattern.StartsWith("*", StringComparison.Ordinal), pattern.EndsWith("*", StringComparison.Ordinal)));
        }

        // ClassicUO's SpeechesLoader.IsMatch: case-insensitive (invariant culture), and the hit must not run into a
        // letter on either side, so "*bank*" tags "bank please" but not "banker", and "*or*" never tags "vendor".
        private const StringComparison Compare = StringComparison.InvariantCultureIgnoreCase;

        private static bool IsMatch(string input, string[] words, bool anyStart, bool anyEnd)
        {
            foreach (string w in words)
            {
                if (w.Length > input.Length)
                    continue;
                if (!anyStart && input.IndexOf(w, 0, w.Length, Compare) == -1)
                    continue;
                if (!anyEnd && input.IndexOf(w, input.Length - w.Length, Compare) == -1)
                    continue;
                for (int at = input.IndexOf(w, Compare); at >= 0; at = input.IndexOf(w, at + 1, Compare))
                {
                    if (at - 1 >= 0 && !char.IsWhiteSpace(input[at - 1]) && char.IsLetter(input[at - 1]))
                        continue;
                    int after = at + w.Length;
                    if (after >= input.Length || char.IsWhiteSpace(input[after]) || !char.IsLetter(input[after]))
                        return true;
                }
            }
            return false;
        }
    }
}
