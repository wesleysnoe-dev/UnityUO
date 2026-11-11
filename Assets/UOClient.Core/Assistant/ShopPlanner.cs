using System;
using System.Collections.Generic;
using System.Text;

namespace UOClient.Core.Assistant
{
    /// <summary>What to answer a vendor's list with: the lines chosen and how many of each, and what it comes to.</summary>
    public sealed class ShopOrder
    {
        public readonly List<(ShopItem Item, int Amount)> Lines = new List<(ShopItem, int)>();

        /// <summary>Items in the order, over all lines.</summary>
        public int Count { get; private set; }

        /// <summary>The gold it costs (buying) or brings (selling), by the vendor's own prices.</summary>
        public long Gold { get; private set; }

        /// <summary>How many of a line the order already holds (two rules can mean the same line).</summary>
        public int Taken(ShopItem item)
        {
            foreach ((ShopItem line, int amount) in Lines)
                if (ReferenceEquals(line, item))
                    return amount;
            return 0;
        }

        public void Add(ShopItem item, int amount)
        {
            if (item == null || amount <= 0)
                return;
            int at = Lines.FindIndex(l => ReferenceEquals(l.Item, item));
            if (at >= 0)
                Lines[at] = (item, Lines[at].Amount + amount); // one reply line per vendor line: the server reads each serial once
            else
                Lines.Add((item, amount));
            Count += amount;
            Gold += (long)item.Price * amount;
        }

        /// <summary>The (serial, amount) pairs the buy and sell replies carry.</summary>
        public List<(uint Serial, int Amount)> Reply()
        {
            var reply = new List<(uint, int)>(Lines.Count);
            foreach ((ShopItem item, int amount) in Lines)
                reply.Add((item.Serial, amount));
            return reply;
        }

        /// <summary>"20 Black pearl, 20 Ginseng and 3 more kinds": the order for the player's log, like names added up.</summary>
        public string Describe(int most = 4)
        {
            var names = new List<string>();
            var amounts = new List<int>();
            foreach ((ShopItem item, int amount) in Lines)
            {
                string name = string.IsNullOrWhiteSpace(item.Name) ? $"0x{item.Graphic:X4}" : item.Name.Trim();
                int at = names.FindIndex(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (at >= 0)
                    amounts[at] += amount;
                else
                {
                    names.Add(name);
                    amounts.Add(amount);
                }
            }
            var text = new StringBuilder();
            for (int i = 0; i < names.Count && i < most; i++)
                text.Append(i > 0 ? ", " : "").Append(amounts[i]).Append(' ').Append(names[i]);
            if (names.Count > most)
                text.Append(" and ").Append(names.Count - most).Append(names.Count - most == 1 ? " more kind" : " more kinds");
            return text.ToString();
        }
    }

    /// <summary>
    /// The auto buy and auto sell decisions (TazUO's BuySellAgent): given a vendor's list, the player's rules and what
    /// the backpack holds, which lines to order and how many. Rules are taken in their order; a rule's lines in the
    /// vendor's order. Per-rule numbers beside <see cref="ItemRule.max"/> come as lists that run parallel to the rules
    /// (0 or missing = not set).
    /// </summary>
    public static class ShopPlanner
    {
        /// <summary>The most one reply line can carry (a 16-bit amount).</summary>
        public const int LineMost = 0xFFFF;

        /// <summary>
        /// What to buy. A rule's max is the most to buy of it in one visit (0 = all the vendor has).
        /// <paramref name="restock"/>: buy only up to this many held (by <paramref name="held"/>), nothing when already
        /// there. <paramref name="maxPrice"/>: pass over lines dearer than this each. <paramref name="maxItems"/> and
        /// <paramref name="maxKinds"/> cap the whole order (0 = no cap): items in all, and rules that bought something.
        /// <paramref name="gold"/>: never order more than this pays for (below 0 = do not look at gold).
        /// </summary>
        public static ShopOrder Buy(ShopList list, IReadOnlyList<ItemRule> rules, IReadOnlyList<int> restock, IReadOnlyList<int> maxPrice,
            Func<ItemRule, int> held, int maxItems, int maxKinds, long gold)
        {
            var order = new ShopOrder();
            if (list == null || rules == null)
                return order;
            int kinds = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                ItemRule rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;
                if (maxKinds > 0 && kinds >= maxKinds)
                    break;
                int want = rule.max > 0 ? rule.max : int.MaxValue;
                int upTo = At(restock, i);
                if (upTo > 0)
                {
                    int have = held != null ? held(rule) : 0;
                    if (have >= upTo)
                        continue; // stocked already
                    want = Math.Min(want, upTo - have);
                }
                int dearest = At(maxPrice, i);
                int got = 0;
                foreach (ShopItem item in list.Items)
                {
                    if (got >= want || maxItems > 0 && order.Count >= maxItems)
                        break;
                    if (!rule.Matches(item.Graphic, item.Hue, item.Name) || dearest > 0 && item.Price > dearest)
                        continue;
                    int taken = order.Taken(item);
                    long n = Math.Min(Math.Min(item.Amount, LineMost) - taken, want - got);
                    if (maxItems > 0)
                        n = Math.Min(n, maxItems - order.Count);
                    if (gold >= 0 && item.Price > 0)
                        n = Math.Min(n, (gold - order.Gold) / item.Price);
                    if (n <= 0)
                        continue;
                    order.Add(item, (int)n);
                    got += (int)n;
                }
                if (got > 0)
                    kinds++;
            }
            return order;
        }

        /// <summary>
        /// What to sell. A rule's max is the most to sell of it in one visit (0 = all the vendor will take).
        /// <paramref name="keep"/>: leave this many held (by <paramref name="held"/>); nothing is sold at or under it.
        /// <paramref name="maxItems"/> and <paramref name="maxKinds"/> cap the whole order as for buying.
        /// </summary>
        public static ShopOrder Sell(ShopList list, IReadOnlyList<ItemRule> rules, IReadOnlyList<int> keep, Func<ItemRule, int> held,
            int maxItems, int maxKinds)
        {
            var order = new ShopOrder();
            if (list == null || rules == null)
                return order;
            int kinds = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                ItemRule rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;
                if (maxKinds > 0 && kinds >= maxKinds)
                    break;
                int want = rule.max > 0 ? rule.max : int.MaxValue;
                int least = At(keep, i);
                if (least > 0)
                {
                    int have = held != null ? held(rule) : 0;
                    if (have <= least)
                        continue; // none to spare
                    want = Math.Min(want, have - least);
                }
                int got = 0;
                foreach (ShopItem item in list.Items)
                {
                    if (got >= want || maxItems > 0 && order.Count >= maxItems)
                        break;
                    if (!rule.Matches(item.Graphic, item.Hue, item.Name))
                        continue;
                    int n = Math.Min(Math.Min(item.Amount, LineMost) - order.Taken(item), want - got);
                    if (maxItems > 0)
                        n = Math.Min(n, maxItems - order.Count);
                    if (n <= 0)
                        continue;
                    order.Add(item, n);
                    got += n;
                }
                if (got > 0)
                    kinds++;
            }
            return order;
        }

        /// <summary>
        /// How many of an item (by graphic and hue; -1 = any hue) a container holds, stacks counted by amount;
        /// <paramref name="deep"/> also counts inside the bags in it, as far as their contents are known.
        /// </summary>
        public static int Held(IEnumerable<ContainedItem> all, uint container, int graphic, int hue, bool deep)
        {
            if (all == null || container == 0 || graphic < 0)
                return 0;
            var items = new List<ContainedItem>(all);
            var inside = new HashSet<uint> { container };
            // Bags in bags: widen the set of containers until a pass adds none (a few passes: packs are shallow).
            for (int pass = 0; deep && pass < 16; pass++)
            {
                bool grew = false;
                foreach (ContainedItem c in items)
                    if (inside.Contains(c.Container) && inside.Add(c.Serial))
                        grew = true;
                if (!grew)
                    break;
            }
            long n = 0;
            foreach (ContainedItem c in items)
                if (inside.Contains(c.Container) && c.Graphic == graphic && (hue < 0 || c.Hue == hue))
                    n += Math.Max(1, c.Amount);
            return (int)Math.Min(n, int.MaxValue);
        }

        /// <summary>
        /// Keeps a parallel list of per-rule numbers in step with the rules after the list was edited: each rule still
        /// in <paramref name="now"/> keeps the number it had in <paramref name="before"/>, a new one starts at 0.
        /// </summary>
        public static void Align(List<int> numbers, IReadOnlyList<ItemRule> before, IReadOnlyList<ItemRule> now)
        {
            var old = new List<int>(numbers);
            numbers.Clear();
            foreach (ItemRule rule in now)
            {
                int was = -1;
                for (int i = 0; i < before.Count && was < 0; i++)
                    if (ReferenceEquals(before[i], rule))
                        was = i;
                numbers.Add(was >= 0 && was < old.Count ? old[was] : 0);
            }
        }

        private static int At(IReadOnlyList<int> numbers, int index) =>
            numbers != null && index < numbers.Count ? numbers[index] : 0;
    }
}
