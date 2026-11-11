using System;
using System.Collections.Generic;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Scavenger (TazUO's ScavengerManager): while switched on, every listed item lying on the ground within 2 tiles of
    /// the character is moved to the backpack, the nearest first, one at a time at the server's action pace. Corpses,
    /// houses and things the server marks as not movable (locked down, part of the world) are left alone. Nothing is
    /// picked up while an item rides the cursor, other actions are still queued, or the character is dead.
    /// </summary>
    public sealed class ScavengerAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            public List<ItemRule> rules = new List<ItemRule>();
        }

        /// <summary>How long an item we asked for is left alone before it may be tried again (TazUO's retry delay).</summary>
        private const float RetryAfter = 5f;

        private const string File = "scavenger";
        private Settings _s = new Settings();
        private readonly Dictionary<uint, float> _tried = new Dictionary<uint, float>(); // item: when it may be tried again
        private readonly List<uint> _expired = new List<uint>();
        private float _nextAt;

        public override string Name => "Scavenger";
        public override bool Active => _s.enabled;

        public override void Attach() => _s = Host.Load<Settings>(File);

        public override void Tick(float now)
        {
            if (!_s.enabled)
            {
                _tried.Clear();
                return;
            }
            GameSession s = Host.Session;
            if (now < _nextAt || s.QueuedActions > 0 || s.Holding != 0)
                return;
            // The ground is looked over a few times a second, not every frame: there can be thousands of items in view.
            _nextAt = now + 0.25f;
            Mobile me = s.World.Player;
            uint pack = Host.Backpack;
            if (me.IsDead || pack == 0 || _s.rules.Count == 0)
                return;

            _expired.Clear();
            foreach (KeyValuePair<uint, float> t in _tried)
                if (now >= t.Value)
                    _expired.Add(t.Key);
            foreach (uint serial in _expired)
                _tried.Remove(serial);

            // Only a rule with words needs the tooltip; asking for it also requests it from the server when we lack
            // it, and the item is then judged on a later pass.
            Func<WorldItem, string> text = null;
            if (_s.rules.Exists(r => r.enabled && r.NeedsText))
                text = i => Host.ItemText(i.Serial, i.Graphic);

            (WorldItem item, ItemRule rule) = ScavengerRules.Next(s.World.Items.Values, me.X, me.Y, _s.rules, _tried.ContainsKey, text);
            if (item == null)
                return;
            // Taken or not (too heavy, out of sight, someone else was faster), it is not asked for again straight away.
            _tried[item.Serial] = now + RetryAfter;
            int amount = ItemRule.Wanted(rule.max, Host.CountIn(pack, item.Graphic, rule.hue), Math.Max(1, item.Amount));
            if (amount <= 0)
                return;
            s.MoveItem(item.Serial, amount, pack);
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Scavenger");
            AssistantGui.Note("Picks the listed items up from the ground into your backpack when you stand within 2 tiles of them. "
                              + "Items that are locked down or cannot be moved are left where they are.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Pick up items from the ground automatically");
            AssistantGui.Header("Items to pick up");
            AssistantGui.Note("Hue blank = any colour. Words: part of the item's name or tooltip. Max: stop once the backpack holds that many.");
            ItemRuleEditor.Added = Save;
            changed |= ItemRuleEditor.Draw(Host, _s.rules, width);
            if (changed)
                Save();
        }

        private void Save() => Host.Save(File, _s);

        /// <summary>Self-tests: the list replaced and switched on, without the window.</summary>
        public void SetForTest(List<ItemRule> rules)
        {
            _s.enabled = true;
            _s.rules = rules;
        }
    }
}
