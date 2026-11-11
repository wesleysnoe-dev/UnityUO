using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Auto loot (TazUO's AutoLootManager): when a corpse's contents arrive (it was opened, by hand or by the options'
    /// corpse auto-open), every item on the list is moved to the backpack, one at a time at the server's action pace,
    /// while the corpse is within reach. A rule's limit stops it once the backpack holds that many. Nothing is looted
    /// while an item rides the cursor or other actions are still queued.
    /// </summary>
    public sealed class AutoLootAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            public List<ItemRule> rules = new List<ItemRule>();
        }

        /// <summary>How far a corpse may be looted from (ModernUO's corpse reach).</summary>
        public const int Reach = 2;

        private const string File = "autoloot";
        private Settings _s = new Settings();
        private readonly List<(uint Item, uint Corpse)> _queue = new List<(uint, uint)>();
        private readonly HashSet<uint> _seen = new HashSet<uint>();         // items already queued or passed over
        private readonly Dictionary<uint, float> _watch = new Dictionary<uint, float>(); // corpses waiting on tooltips: until when
        private float _nextAt, _rescanAt;

        public override string Name => "Auto Loot";
        public override bool Active => _s.enabled;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            Host.Session.World.ContainerOpened += OnOpened;
            Host.Session.World.ContainerChanged += Scan;
        }

        public override void Detach()
        {
            if (Host.Session == null)
                return;
            Host.Session.World.ContainerOpened -= OnOpened;
            Host.Session.World.ContainerChanged -= Scan;
        }

        private void OnOpened(OpenContainer c) => Scan(c.Serial);

        /// <summary>Queues what the list wants from a corpse.</summary>
        private void Scan(uint corpse)
        {
            if (!_s.enabled || !Host.Session.World.Items.TryGetValue(corpse, out WorldItem body) || !body.IsCorpse)
                return;
            bool anyText = _s.rules.Exists(r => r.enabled && r.NeedsText);
            bool waiting = false;
            foreach (ContainedItem item in Host.ItemsIn(corpse))
            {
                if (_seen.Contains(item.Serial))
                    continue;
                string text = null;
                if (anyText)
                {
                    text = Host.ItemText(item.Serial, item.Graphic); // asks for the tooltip when we lack it
                    if (!Host.Session.Properties.ContainsKey(item.Serial))
                        waiting = true; // decided when it arrives (the rescan below)
                }
                ItemRule rule = ItemRule.First(_s.rules, item.Graphic, item.Hue, text);
                if (rule == null)
                    continue;
                _seen.Add(item.Serial);
                _queue.Add((item.Serial, corpse));
            }
            if (waiting)
                _watch[corpse] = Time.unscaledTime + 5f;
        }

        public override void Tick(float now)
        {
            if (!_s.enabled)
            {
                _queue.Clear();
                _watch.Clear();
                return;
            }
            // Corpses whose items' tooltips were still on their way.
            if (_watch.Count > 0 && now >= _rescanAt)
            {
                _rescanAt = now + 0.5f;
                foreach (uint watched in new List<uint>(_watch.Keys))
                {
                    if (now > _watch[watched])
                        _watch.Remove(watched);
                    else
                    {
                        float until = _watch[watched];
                        Scan(watched);
                        _watch[watched] = until; // Scan may have pushed it on: keep the first deadline
                    }
                }
            }
            if (_queue.Count == 0 || now < _nextAt || Host.Session.QueuedActions > 0 || Host.Session.Holding != 0)
                return;

            (uint serial, uint corpse) = _queue[0];
            _queue.RemoveAt(0);
            GameSession s = Host.Session;
            // Still there, and the corpse within reach? (Someone else took it; we walked on.)
            if (!s.World.Contained.TryGetValue(serial, out ContainedItem item) || item.Container != corpse)
                return;
            if (Host.TileOf(corpse) is not (int cx, int cy) || ItemRule.Distance(cx, cy, s.World.Player.X, s.World.Player.Y) > Reach)
            {
                _seen.Remove(serial); // looted if we come back and the corpse is opened again
                return;
            }
            uint pack = Host.Backpack;
            if (pack == 0)
                return;
            ItemRule rule = ItemRule.First(_s.rules, item.Graphic, item.Hue, Host.ItemText(serial, item.Graphic));
            if (rule == null)
                return;
            int amount = ItemRule.Wanted(rule.max, Host.CountIn(pack, item.Graphic, rule.hue), Math.Max(1, item.Amount));
            if (amount <= 0)
                return;
            s.MoveItem(serial, amount, pack);
            _nextAt = now + 0.1f; // the action queue itself spaces the lifts
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Auto Loot");
            AssistantGui.Note("Takes the listed items from a corpse when it is opened, while you stand within 2 tiles of it. "
                              + "Turn on Options > Containers > auto-open corpses to have corpses open by themselves.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Loot corpses automatically");
            AssistantGui.Header("Items to loot");
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

        public int QueuedForTest => _queue.Count;
    }
}
