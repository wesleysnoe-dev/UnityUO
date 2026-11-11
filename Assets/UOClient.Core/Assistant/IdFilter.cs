using System.Collections.Generic;
using System.Globalization;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// The sound and music filters' lists (TazUO's SoundFilterManager): ids that are not played, kept in order without
    /// repeats, and the ids heard lately, newest first, offered for adding.
    /// </summary>
    public static class IdFilter
    {
        /// <summary>An id as typed: a number, or hex with 0x in front. -1 when it is neither or outside 0..<paramref name="max"/>.</summary>
        public static int Parse(string text, int max)
        {
            text = (text ?? "").Trim();
            bool hex = text.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase);
            if (hex)
                text = text.Substring(2);
            if (text.Length == 0)
                return -1;
            return int.TryParse(text, hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                   && id >= 0 && id <= max ? id : -1;
        }

        /// <summary>Adds an id, keeping the list sorted; false when it is already there or negative.</summary>
        public static bool Add(List<int> ids, int id)
        {
            if (ids == null || id < 0)
                return false;
            int at = ids.BinarySearch(id);
            if (at >= 0)
                return false;
            ids.Insert(~at, id);
            return true;
        }

        /// <summary>A saved list made fit to search: sorted, no repeats, no negatives (a hand-edited file may have any).</summary>
        public static void Tidy(List<int> ids)
        {
            if (ids == null)
                return;
            ids.RemoveAll(i => i < 0);
            ids.Sort();
            for (int i = ids.Count - 1; i > 0; i--)
                if (ids[i] == ids[i - 1])
                    ids.RemoveAt(i);
        }

        /// <summary>Whether a tidy (sorted) list holds the id.</summary>
        public static bool Contains(List<int> ids, int id) => ids != null && ids.BinarySearch(id) >= 0;

        /// <summary>Notes an id as just heard: it moves to the front, and the list keeps the newest <paramref name="keep"/>.</summary>
        public static void Heard(List<int> recent, int id, int keep)
        {
            if (recent == null)
                return;
            if (recent.Count > 0 && recent[0] == id)
                return; // the common case (footsteps): nothing to move
            recent.Remove(id);
            recent.Insert(0, id);
            if (recent.Count > keep)
                recent.RemoveRange(keep, recent.Count - keep);
        }
    }
}
