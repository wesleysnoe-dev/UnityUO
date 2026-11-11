using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Auto sell (TazUO's sell agent): when a vendor's sell list arrives (said "vendor sell", or chose Sell from the
    /// vendor's menu), the listed items it offers to take are sold at once and the shop window is not shown. Each item
    /// has a most-per-visit and an amount to keep. A list with nothing of ours to sell is left to the shop window.
    /// </summary>
    public sealed class AutoSellAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            public List<ItemRule> rules = new List<ItemRule>();
            /// <summary>Per rule, in the rules' order: leave this many in the backpack (0 = sell them all).</summary>
            public List<int> keep = new List<int>();
            /// <summary>Most items in one order, and most different entries sold in one (0 = no cap).</summary>
            public int maxItems, maxKinds;
        }

        private const string File = "autosell";
        private Settings _s = new Settings();

        public override string Name => "Auto Sell";
        public override bool Active => _s.enabled;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            _s.rules ??= new List<ItemRule>();
            _s.keep ??= new List<int>();
            Host.Session.ShopOpened += OnShop;
        }

        public override void Detach()
        {
            if (Host.Session != null)
                Host.Session.ShopOpened -= OnShop;
        }

        private void OnShop(ShopList list)
        {
            if (!_s.enabled || list == null || !list.Selling || list.Vendor == 0)
                return;
            Pad();
            uint pack = Host.Backpack;
            // The vendor offers for what lies in bags too, so what we hold is counted through them.
            ShopOrder order = ShopPlanner.Sell(list, _s.rules, _s.keep,
                rule => ShopPlanner.Held(Host.Session.World.Contained.Values, pack, rule.graphic, rule.hue, true),
                _s.maxItems, _s.maxKinds);
            if (order.Count == 0)
                return;
            Host.Session.AnswerShop(list, order.Reply());
            Host.Log($"Auto Sell: sold {order.Describe()} for {order.Gold} gold.");
        }

        /// <summary>A rule added by targeting arrives outside the tab's draw: the per-rule numbers grow to meet it.</summary>
        private void Pad()
        {
            while (_s.keep.Count < _s.rules.Count)
                _s.keep.Add(0);
        }

        public override void DrawTab(float width)
        {
            Pad();
            AssistantGui.Header("Auto Sell");
            AssistantGui.Note("When a vendor shows what it will buy from you, the listed items are sold at once and no shop window opens. "
                              + "If you carry nothing you list, the shop window opens as usual.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Sell to vendors automatically");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Most items per sale", 170);
            changed |= AssistantGui.Int(ref _s.maxItems, 60, 1, 60000, 0, "");
            GUILayout.Space(16);
            AssistantGui.Cell("Most different entries", 170);
            changed |= AssistantGui.Int(ref _s.maxKinds, 60, 1, 1000, 0, "");
            GUILayout.EndHorizontal();
            AssistantGui.Note("Blank = no cap.");

            AssistantGui.Header("Items to sell");
            AssistantGui.Note("Hue blank = any colour. Words: part of the name the vendor gives the item. "
                              + "Max: the most to sell in one visit (blank = all of them).");
            ItemRuleEditor.Added = Save;
            var before = new List<ItemRule>(_s.rules);
            if (ItemRuleEditor.Draw(Host, _s.rules, width))
            {
                ShopPlanner.Align(_s.keep, before, _s.rules); // a row may be gone: each rule keeps its own number
                changed = true;
            }

            if (_s.rules.Count > 0)
            {
                AssistantGui.Header("Keep");
                AssistantGui.Note("Keep: leave that many in the backpack (bags in it counted) and sell only what is over (needs a graphic). Blank = sell them all.");
                float nameW = Mathf.Max(90f, width - 160f);
                GUILayout.BeginHorizontal();
                AssistantGui.Cell("", 26);
                AssistantGui.Cell("Name", nameW);
                AssistantGui.Cell("Keep", 90);
                GUILayout.EndHorizontal();
                for (int i = 0; i < _s.rules.Count; i++)
                {
                    ItemRule r = _s.rules[i];
                    GUILayout.BeginHorizontal();
                    AssistantGui.Icon(Host.Ui?.Art, r.graphic, r.hue, 24);
                    AssistantGui.Cell(string.IsNullOrWhiteSpace(r.name) ? "(no name)" : r.name, nameW);
                    int keep = _s.keep[i];
                    if (AssistantGui.Int(ref keep, 90, 1, 60000, 0, ""))
                    {
                        _s.keep[i] = keep;
                        changed = true;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            if (changed)
                Save();
        }

        private void Save()
        {
            Pad();
            Host.Save(File, _s);
        }
    }
}
