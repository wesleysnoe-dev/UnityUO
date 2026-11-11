using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// One line of an assistant list (auto loot, scavenger, organizer, buy, sell): which items it means. An item matches
    /// when its graphic and hue are the rule's (-1 = any) and, if the rule has text, its name or tooltip contains it
    /// (any case). Public fields: saved with JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class ItemRule
    {
        public string name = "";
        public int graphic = -1;
        public int hue = -1;
        public string text = "";
        public bool enabled = true;

        /// <summary>Stop once this many are held where they go (0 = no limit): reagents to 100, say.</summary>
        public int max;

        public bool Matches(int itemGraphic, int itemHue, string itemText)
        {
            if (!enabled)
                return false;
            if (graphic >= 0 && graphic != itemGraphic)
                return false;
            if (hue >= 0 && hue != itemHue)
                return false;
            if (!string.IsNullOrWhiteSpace(text))
                return itemText != null && itemText.IndexOf(text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            return graphic >= 0 || hue >= 0; // an empty rule means nothing, not everything
        }

        /// <summary>Whether the rule can be decided without the item's tooltip.</summary>
        public bool NeedsText => !string.IsNullOrWhiteSpace(text);

        /// <summary>The first enabled rule the item matches, or null.</summary>
        public static ItemRule First(IEnumerable<ItemRule> rules, int itemGraphic, int itemHue, string itemText)
        {
            if (rules != null)
                foreach (ItemRule r in rules)
                    if (r != null && r.Matches(itemGraphic, itemHue, itemText))
                        return r;
            return null;
        }

        /// <summary>
        /// How many more of a rule's item may be taken: everything offered when the rule has no limit, else what is still
        /// missing from <paramref name="held"/> (never below 0).
        /// </summary>
        public static int Wanted(int max, int held, int offered) =>
            max <= 0 ? Math.Max(0, offered) : Math.Max(0, Math.Min(offered, max - held));

        /// <summary>Chebyshev distance in tiles (UO's range).</summary>
        public static int Distance(int x1, int y1, int x2, int y2) => Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
    }
}
