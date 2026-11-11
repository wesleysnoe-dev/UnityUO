using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// What the scavenger may pick up from the ground and which item comes next: something the server lets be moved,
    /// not a corpse or a house, within reach of where we stand, that a rule on the list means.
    /// </summary>
    public static class ScavengerRules
    {
        /// <summary>How far away the server lets an item be lifted from (ModernUO: 2 tiles).</summary>
        public const int Reach = 2;

        /// <summary>Whether a ground item could be picked up from (<paramref name="px"/>, <paramref name="py"/>) at all.</summary>
        public static bool CanTake(WorldItem item, int px, int py) =>
            item != null && item.Movable && !item.IsCorpse && !item.IsMulti
            && ItemRule.Distance(item.X, item.Y, px, py) <= Reach;

        /// <summary>
        /// The item to pick up next and the rule that asked for it: the nearest one in reach that the list means, so
        /// what lies underfoot goes before what we are about to walk away from. <paramref name="skip"/> leaves out
        /// items already tried; <paramref name="text"/> gives an item's name and tooltip for rules that match on words
        /// (null when no rule needs it).
        /// </summary>
        public static (WorldItem Item, ItemRule Rule) Next(IEnumerable<WorldItem> ground, int px, int py, IList<ItemRule> rules,
            Func<uint, bool> skip = null, Func<WorldItem, string> text = null)
        {
            WorldItem best = null;
            ItemRule bestRule = null;
            int bestDistance = int.MaxValue;
            if (ground == null || rules == null || rules.Count == 0)
                return (null, null);
            foreach (WorldItem item in ground)
            {
                if (!CanTake(item, px, py) || (skip != null && skip(item.Serial)))
                    continue;
                int d = ItemRule.Distance(item.X, item.Y, px, py);
                // Ties go to the lower serial so the choice does not depend on the order the world lists its items in.
                if (d > bestDistance || (d == bestDistance && best != null && item.Serial > best.Serial))
                    continue;
                ItemRule rule = ItemRule.First(rules, item.Graphic, item.Hue, text?.Invoke(item));
                if (rule == null)
                    continue;
                best = item;
                bestRule = rule;
                bestDistance = d;
            }
            return (best, bestRule);
        }
    }
}
