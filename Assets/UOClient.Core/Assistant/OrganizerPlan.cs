using System;
using System.Collections.Generic;

namespace UOClient.Core.Assistant
{
    /// <summary>
    /// One organizer: move the listed items from one container to another. A container of 0 means the backpack.
    /// A rule's <see cref="ItemRule.max"/> is the amount: 0 moves every matching item, otherwise items are moved only
    /// until the destination holds that many. Public fields: saved with JsonUtility.
    /// </summary>
    [Serializable]
    public sealed class Organizer
    {
        public string name = "Organizer";
        public uint source;
        public uint destination;
        public List<ItemRule> rules = new List<ItemRule>();
    }

    /// <summary>Works out what an organizer has to move.</summary>
    public static class OrganizerPlan
    {
        /// <summary>
        /// The moves, in the order the source lists its items: each matching item and how many of it. An item belongs
        /// to the first rule it matches. For a rule with an amount, what the destination already holds of that rule's
        /// items counts towards it, and so does what earlier moves in the plan will bring, so two stacks in the source
        /// never overshoot it. <paramref name="text"/> gives an item's name and tooltip for rules that match on words
        /// (null when no rule needs it).
        /// </summary>
        public static List<(uint Serial, int Amount)> Build(IEnumerable<ContainedItem> source, IEnumerable<ContainedItem> destination,
            IList<ItemRule> rules, Func<ContainedItem, string> text = null)
        {
            var moves = new List<(uint, int)>();
            if (source == null || rules == null || rules.Count == 0)
                return moves;

            var held = new Dictionary<ItemRule, int>();
            if (destination != null)
                foreach (ContainedItem d in destination)
                {
                    ItemRule rule = ItemRule.First(rules, d.Graphic, d.Hue, text?.Invoke(d));
                    if (rule == null || rule.max <= 0)
                        continue; // only limited rules need the count
                    held.TryGetValue(rule, out int n);
                    held[rule] = n + Math.Max(1, d.Amount);
                }

            foreach (ContainedItem item in source)
            {
                ItemRule rule = ItemRule.First(rules, item.Graphic, item.Hue, text?.Invoke(item));
                if (rule == null)
                    continue;
                held.TryGetValue(rule, out int have);
                int amount = ItemRule.Wanted(rule.max, have, Math.Max(1, item.Amount));
                if (amount <= 0)
                    continue;
                held[rule] = have + amount;
                moves.Add((item.Serial, amount));
            }
            return moves;
        }
    }
}
