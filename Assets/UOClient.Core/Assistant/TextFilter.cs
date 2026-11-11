using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// The journal filter's rule (TazUO's JournalFilterManager.IgnoreMessage): a line is hidden when it contains any of
    /// the listed texts, case ignored. A blank entry hides nothing (it would otherwise match every line).
    /// </summary>
    public static class TextFilter
    {
        public const int MaxLength = 120;

        public static bool Hides(IList<string> filters, string message)
        {
            if (filters == null || string.IsNullOrEmpty(message))
                return false;
            for (int i = 0; i < filters.Count; i++)
            {
                string f = filters[i];
                if (!string.IsNullOrWhiteSpace(f) && message.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Adds a text to the list as typed, less the spaces around it; false when it is blank or already there (case
        /// ignored: the match ignores case, so a second spelling adds nothing).
        /// </summary>
        public static bool Add(List<string> filters, string text)
        {
            text = (text ?? "").Trim();
            if (filters == null || text.Length == 0)
                return false;
            if (text.Length > MaxLength)
                text = text.Substring(0, MaxLength);
            foreach (string f in filters)
                if (string.Equals(f, text, StringComparison.OrdinalIgnoreCase))
                    return false;
            filters.Add(text);
            return true;
        }
    }
}
