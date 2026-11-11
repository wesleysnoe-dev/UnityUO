using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Graphic replacement (TazUO's GraphicsReplacement): characters with a listed body, and items on the ground with a
    /// listed graphic, are drawn as another, in another hue if one is given. Only the drawing changes: the server, the
    /// tooltips and the other tools still see the thing as it is.
    /// </summary>
    public sealed class GraphicFilterAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            public List<GraphicSwap> swaps = new List<GraphicSwap>();
        }

        private const string File = "graphicfilter";
        private const int MaxGraphic = 0xFFFF, MaxHue = 0x3FFF;
        private Settings _s = new Settings();
        private GraphicSwapTable _table = new GraphicSwapTable(null);
        private Func<int, int, (int, int)> _bodyHook;
        private Func<WorldItem, WorldItem> _itemHook;

        // The line being typed in under the list.
        private int _newKind = GraphicSwap.Mobile;
        private string _newFrom = "", _newTo = "", _newHue = "";

        /// <summary>
        /// Raised when the replacements changed. Characters redraw by themselves; items already on screen keep their old
        /// look until something redraws them, so whoever owns the item views can listen here and rebuild them.
        /// </summary>
        public static event Action Changed;

        public override string Name => "Graphics";
        public override string Group => "Filters";
        public override bool Active => _s.enabled && _table.Count > 0;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            _s.swaps.RemoveAll(s => s == null);
            _bodyHook = Body;
            _itemHook = Item;
            Apply(false);
        }

        public override void Detach()
        {
            if (MobileLook.BodyFilter == _bodyHook)
                MobileLook.BodyFilter = null;
            if (EntityViews.ItemFilter == _itemHook)
                EntityViews.ItemFilter = null;
        }

        private (int, int) Body(int body, int hue)
        {
            _table.Apply(GraphicSwap.Mobile, ref body, ref hue);
            return (body, hue);
        }

        private WorldItem Item(WorldItem item)
        {
            // A corpse's and a house's graphic say what they are, not what art to draw.
            if (item.IsCorpse || item.IsMulti)
                return item;
            int graphic = item.Graphic, hue = item.Hue;
            return _table.Apply(GraphicSwap.Item, ref graphic, ref hue) ? item.DrawnAs((ushort)graphic, (ushort)hue) : item;
        }

        /// <summary>The list as it now is goes live; with nothing to replace the drawing code is left without a filter at all.</summary>
        private void Apply(bool redraw)
        {
            _table = new GraphicSwapTable(_s.enabled ? _s.swaps : null);
            bool on = _table.Count > 0;
            if (on)
            {
                MobileLook.BodyFilter = _bodyHook;
                EntityViews.ItemFilter = _itemHook;
            }
            else
                Detach();
            if (!redraw || Host.Session == null)
                return;
            // A character's look is rebuilt when its appearance version moves.
            foreach (Mobile m in Host.Session.World.Mobiles.Values)
                m.AppearanceVersion++;
            Changed?.Invoke();
        }

        private void Save()
        {
            Apply(true);
            Host.Save(File, _s);
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Graphics");
            AssistantGui.Note("Draws one thing as another: every character with a body, or every item on the ground with a graphic, "
                              + "shows as the replacement, in a new hue if you give one. Characters change at once; an item already "
                              + "on screen changes the next time the server sends it (walk away and back).");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Replace the graphics listed below");

            AssistantGui.Header("Replacements");
            if (_s.swaps.Count == 0)
                AssistantGui.Note("No replacements yet.");
            else
            {
                GUILayout.BeginHorizontal();
                AssistantGui.Cell("", 22);
                AssistantGui.Cell("Kind", 70);
                AssistantGui.Cell("Graphic", 26 + 110);
                AssistantGui.Cell("Drawn as", 26 + 110);
                AssistantGui.Cell("Hue", 60);
                GUILayout.EndHorizontal();
            }
            int remove = -1;
            for (int i = 0; i < _s.swaps.Count; i++)
            {
                GraphicSwap s = _s.swaps[i];
                bool item = s.kind == GraphicSwap.Item;
                GUILayout.BeginHorizontal();
                changed |= AssistantGui.Toggle(ref s.enabled, "", 22);
                AssistantGui.Cell(item ? "Item" : "Character", 70);
                AssistantGui.Icon(Host.Ui?.Art, item ? s.graphic : -1, 0, 24);
                AssistantGui.Cell(Label(s.graphic), 110);
                AssistantGui.Icon(Host.Ui?.Art, item ? s.to : -1, s.hue, 24);
                AssistantGui.Cell(Label(s.to), 110);
                AssistantGui.Cell(s.hue < 0 ? "its own" : s.hue.ToString(), 60);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                _s.swaps.RemoveAt(remove);
                changed = true;
            }

            AssistantGui.Header("Add a replacement");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_newKind == GraphicSwap.Item ? "Item" : "Character", GUILayout.Width(90)))
                _newKind = _newKind == GraphicSwap.Item ? GraphicSwap.Mobile : GraphicSwap.Item;
            AssistantGui.Cell("graphic", 50);
            AssistantGui.Text(ref _newFrom, 70, 8);
            AssistantGui.Cell("drawn as", 58);
            AssistantGui.Text(ref _newTo, 70, 8);
            AssistantGui.Cell("hue", 28);
            AssistantGui.Text(ref _newHue, 56, 6);
            GUILayout.EndHorizontal();

            int from = IdFilter.Parse(_newFrom, MaxGraphic), to = IdFilter.Parse(_newTo, MaxGraphic);
            int hue = string.IsNullOrWhiteSpace(_newHue) ? -1 : IdFilter.Parse(_newHue, MaxHue);
            bool hueOk = hue >= 0 || string.IsNullOrWhiteSpace(_newHue);
            bool taken = from >= 0 && _s.swaps.Exists(s => s.kind == _newKind && s.graphic == from);
            GUILayout.BeginHorizontal();
            GUI.enabled = from >= 0 && to >= 0 && hueOk && !taken;
            if (GUILayout.Button("Add", GUILayout.Width(90)))
            {
                _s.swaps.Add(new GraphicSwap { kind = _newKind, graphic = from, to = to, hue = hue });
                _newFrom = _newTo = _newHue = "";
                changed = true;
            }
            GUI.enabled = true;
            if (GUILayout.Button("Fill in by targeting", GUILayout.Width(160)))
            {
                Host.Log("Target the character or item whose graphic to replace.");
                Host.Session.BeginLocalTarget(FillFrom);
            }
            GUILayout.EndHorizontal();
            if (taken)
                AssistantGui.Note("That graphic already has a replacement; remove it first.");
            AssistantGui.Note("Graphics as numbers, or hex with 0x in front (a human man is 400, or 0x190). Hue blank: its own colour. "
                              + "The kind button switches between characters (bodies) and items (art).");
            if (changed)
                Save();
        }

        private static string Label(int graphic) => graphic < 0 ? "" : $"{graphic}  (0x{graphic:X4})";

        /// <summary>The targeted thing's kind and graphic go into the boxes; the replacement is still to be typed.</summary>
        private void FillFrom(uint serial)
        {
            GameSession s = Host.Session;
            if (s == null)
                return;
            if (s.World.Mobiles.TryGetValue(serial, out Mobile m))
            {
                _newKind = GraphicSwap.Mobile;
                _newFrom = $"0x{m.Body:X4}";
            }
            else if (ItemRuleEditor.Describe(Host, serial, out int graphic, out _))
            {
                _newKind = GraphicSwap.Item;
                _newFrom = $"0x{graphic:X4}";
            }
            else
                Host.Log("That is neither a character nor an item.");
        }

        /// <summary>Self-tests: the list replaced and switched on, without the window.</summary>
        public void SetForTest(List<GraphicSwap> swaps)
        {
            _s.enabled = true;
            _s.swaps = swaps;
            Apply(true);
        }
    }
}
