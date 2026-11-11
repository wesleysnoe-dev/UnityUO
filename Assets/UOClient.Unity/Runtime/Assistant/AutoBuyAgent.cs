using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Auto buy (TazUO's buy agent): when a vendor's buy list arrives (said "vendor buy", or chose Buy from the vendor's
    /// menu), the listed items are ordered at once and the shop window is not shown. Each item has a most-per-visit, a
    /// restock level (buy only up to that many in the backpack) and a highest price. A list with nothing wanted on it
    /// is left to the shop window as usual.
    /// </summary>
    public sealed class AutoBuyAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled;
            public List<ItemRule> rules = new List<ItemRule>();
            /// <summary>Per rule, in the rules' order: buy up to this many held (0 = no restock level).</summary>
            public List<int> restock = new List<int>();
            /// <summary>Per rule, in the rules' order: pass over lines dearer than this each (0 = any price).</summary>
            public List<int> maxPrice = new List<int>();
            /// <summary>Most items in one order, and most different entries bought in one (0 = no cap).</summary>
            public int maxItems, maxKinds;
            /// <summary>Restock counts what is in bags inside the backpack too.</summary>
            public bool subContainers;
            /// <summary>Never order more than the gold carried pays for.</summary>
            public bool onlyGoldCarried = true;
        }

        private const string File = "autobuy";
        private Settings _s = new Settings();

        public override string Name => "Auto Buy";
        public override bool Active => _s.enabled;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            _s.rules ??= new List<ItemRule>();
            _s.restock ??= new List<int>();
            _s.maxPrice ??= new List<int>();
            Host.Session.ShopOpened += OnShop;
        }

        public override void Detach()
        {
            if (Host.Session != null)
                Host.Session.ShopOpened -= OnShop;
        }

        private void OnShop(ShopList list)
        {
            if (!_s.enabled || list == null || list.Selling || list.Vendor == 0 || Host.Session.World.Player == null)
                return;
            Pad();
            uint pack = Host.Backpack;
            long gold = _s.onlyGoldCarried ? Host.Session.World.Player.Gold : -1;
            ShopOrder order = ShopPlanner.Buy(list, _s.rules, _s.restock, _s.maxPrice,
                rule => ShopPlanner.Held(Host.Session.World.Contained.Values, pack, rule.graphic, rule.hue, _s.subContainers),
                _s.maxItems, _s.maxKinds, gold);
            if (order.Count == 0)
                return;
            Host.Session.AnswerShop(list, order.Reply());
            Host.Log($"Auto Buy: ordered {order.Describe()} for {order.Gold} gold.");
        }

        /// <summary>A rule added by targeting arrives outside the tab's draw: the per-rule numbers grow to meet it.</summary>
        private void Pad()
        {
            while (_s.restock.Count < _s.rules.Count)
                _s.restock.Add(0);
            while (_s.maxPrice.Count < _s.rules.Count)
                _s.maxPrice.Add(0);
        }

        public override void DrawTab(float width)
        {
            Pad();
            AssistantGui.Header("Auto Buy");
            AssistantGui.Note("When a vendor shows what it sells, the listed items are bought at once and no shop window opens. "
                              + "If the vendor has nothing you list, the shop window opens as usual.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Buy from vendors automatically");
            changed |= AssistantGui.Toggle(ref _s.onlyGoldCarried, "Never order more than the gold I carry pays for");
            changed |= AssistantGui.Toggle(ref _s.subContainers, "Restock counts bags inside the backpack too");
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Most items per order", 170);
            changed |= AssistantGui.Int(ref _s.maxItems, 60, 1, 60000, 0, "");
            GUILayout.Space(16);
            AssistantGui.Cell("Most different entries", 170);
            changed |= AssistantGui.Int(ref _s.maxKinds, 60, 1, 1000, 0, "");
            GUILayout.EndHorizontal();
            AssistantGui.Note("Blank = no cap.");

            AssistantGui.Header("Items to buy");
            AssistantGui.Note("Hue blank = any colour. Words: part of the name the vendor gives the item. "
                              + "Max: the most to buy in one visit (blank = all the vendor has).");
            ItemRuleEditor.Added = Save;
            var before = new List<ItemRule>(_s.rules);
            if (ItemRuleEditor.Draw(Host, _s.rules, width))
            {
                // A row may be gone: each rule keeps its own restock level and price.
                ShopPlanner.Align(_s.restock, before, _s.rules);
                ShopPlanner.Align(_s.maxPrice, before, _s.rules);
                changed = true;
            }

            if (_s.rules.Count > 0)
            {
                AssistantGui.Header("Restock and price");
                AssistantGui.Note("Restock to: buy only until the backpack holds that many, and none once it does (needs a graphic). "
                                  + "Highest price: skip the item when the vendor asks more for one. Blank = not used.");
                float nameW = Mathf.Max(90f, width - 260f);
                GUILayout.BeginHorizontal();
                AssistantGui.Cell("", 26);
                AssistantGui.Cell("Name", nameW);
                AssistantGui.Cell("Restock to", 90);
                AssistantGui.Cell("Highest price", 100);
                GUILayout.EndHorizontal();
                for (int i = 0; i < _s.rules.Count; i++)
                {
                    ItemRule r = _s.rules[i];
                    GUILayout.BeginHorizontal();
                    AssistantGui.Icon(Host.Ui?.Art, r.graphic, r.hue, 24);
                    AssistantGui.Cell(string.IsNullOrWhiteSpace(r.name) ? "(no name)" : r.name, nameW);
                    int stock = _s.restock[i], price = _s.maxPrice[i];
                    if (AssistantGui.Int(ref stock, 90, 1, 60000, 0, ""))
                    {
                        _s.restock[i] = stock;
                        changed = true;
                    }
                    if (AssistantGui.Int(ref price, 100, 1, 1000000, 0, ""))
                    {
                        _s.maxPrice[i] = price;
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
