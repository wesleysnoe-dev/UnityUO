using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// The season filter's rule (TazUO's SeasonFilter.ApplyFilter): for each season the server may send, the season shown
    /// instead. The list has one entry per season, in the enum's order; <see cref="AsSent"/> (or anything that is not a
    /// season, or a missing entry) leaves the server's.
    /// </summary>
    public static class SeasonMap
    {
        public const int AsSent = -1;
        public const int Seasons = 5;

        public static Season Apply(IList<int> showAs, Season sent)
        {
            int i = (int)sent;
            if (showAs == null || i >= showAs.Count)
                return sent;
            int shown = showAs[i];
            return shown >= 0 && shown < Seasons ? (Season)shown : sent;
        }

        /// <summary>A saved list made the right length: one entry per season, bad values as sent.</summary>
        public static void Tidy(List<int> showAs)
        {
            if (showAs == null)
                return;
            while (showAs.Count < Seasons)
                showAs.Add(AsSent);
            if (showAs.Count > Seasons)
                showAs.RemoveRange(Seasons, showAs.Count - Seasons);
            for (int i = 0; i < Seasons; i++)
                if (showAs[i] < 0 || showAs[i] >= Seasons)
                    showAs[i] = AsSent;
        }

        /// <summary>The next choice when a row's button is clicked: as sent, then each season in turn, then as sent again.</summary>
        public static int Next(int shown) => shown < 0 || shown >= Seasons - 1 ? (shown == Seasons - 1 ? AsSent : 0) : shown + 1;

        /// <summary>Whether any season is shown as another.</summary>
        public static bool Any(IList<int> showAs)
        {
            if (showAs != null)
                for (int i = 0; i < showAs.Count && i < Seasons; i++)
                    if (showAs[i] >= 0 && showAs[i] < Seasons && showAs[i] != i)
                        return true;
            return false;
        }
    }
}
