using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;
using UOClient.IO.Formats;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Dress agent (TazUO's DressAgentManager): named outfits, each a list of the very items (by serial) and the layers
    /// they are worn on. "Dress" takes off whatever else is in the outfit's way and puts its pieces on; "Undress" moves
    /// the outfit's worn pieces to the backpack or a chosen bag. Every piece is one lift at the server's action pace.
    /// Hair, beard, backpack, mount and bank are never touched. Items are remembered by serial, so the outfits are
    /// kept per character.
    /// </summary>
    public sealed class DressAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public List<DressOutfit> outfits = new List<DressOutfit>();
        }

        private Settings _s = new Settings();
        private string _file; // null until we know which character this is
        private int _selected;
        private bool _confirmDelete;

        // The run under way.
        private DressOutfit _running;
        private bool _dressing;
        private int _steps;
        private readonly HashSet<uint> _done = new HashSet<uint>(); // items already asked for in this run
        private float _nextAt;

        public override string Name => "Dress Agent";
        public override bool Active => _running != null;

        public override void Detach() => _running = null;

        /// <summary>Loads this character's outfits the first time the character is known.</summary>
        private bool EnsureLoaded()
        {
            if (_file != null)
                return true;
            uint me = Host.Session?.World.PlayerSerial ?? 0;
            if (me == 0)
                return false;
            _file = "dress_" + me.ToString("X8");
            _s = Host.Load<Settings>(_file);
            _s.outfits.RemoveAll(o => o == null);
            foreach (DressOutfit o in _s.outfits)
                o.items.RemoveAll(i => i == null);
            return true;
        }

        private void Save()
        {
            if (_file != null)
                Host.Save(_file, _s);
        }

        /// <summary>Starts putting an outfit on (<paramref name="dress"/>) or taking it off; the moves are made in <see cref="Tick"/>.</summary>
        public void Run(DressOutfit outfit, bool dress)
        {
            _running = outfit;
            _dressing = dress;
            _steps = 0;
            _done.Clear();
        }

        public override void Tick(float now)
        {
            if (_running == null)
                return;
            GameSession s = Host.Session;
            if (now < _nextAt || s.QueuedActions > 0 || s.Holding != 0)
                return;
            Mobile me = s.World.Player;
            if (me.IsDead)
            {
                _running = null;
                return;
            }
            DressStep step = _dressing
                ? DressPlan.NextDress(_running, me.Equipment, _done)
                : DressPlan.NextUndress(_running, me.Equipment, _done);
            if (step.Kind == DressStepKind.None)
            {
                if (_steps == 0)
                    Host.Log(_dressing ? $"'{_running.name}' is already on." : $"Nothing of '{_running.name}' is worn.");
                _running = null;
                return;
            }
            if (_steps == 0)
                Host.Log(_dressing ? $"Dressing: {_running.name}" : $"Undressing: {_running.name}");
            _steps++;
            // Asked for once per run: the server's answer comes a moment later, and a refused item is not retried.
            _done.Add(step.Serial);
            if (step.Kind == DressStepKind.PutOn)
                s.MoveToLayer(step.Serial, step.Layer);
            else
            {
                uint bag = _running.undressBag != 0 ? _running.undressBag : Host.Backpack;
                if (bag == 0)
                {
                    Host.Log("Dress agent: cannot find your backpack.");
                    _running = null;
                    return;
                }
                s.MoveItem(step.Serial, 1, bag);
            }
            _nextAt = now + 0.1f; // the action queue itself spaces the lifts
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Dress Agent");
            if (!EnsureLoaded() || Host.Session.World.Player == null)
            {
                AssistantGui.Note("Outfits are kept per character: they show once you are in the world.");
                return;
            }
            AssistantGui.Note("An outfit remembers the exact items you wear. Dress puts them on (taking off what is in their way); "
                              + "Undress moves them to your backpack or the bag you choose.");

            if (_s.outfits.Count > 0)
            {
                var names = new string[_s.outfits.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = string.IsNullOrWhiteSpace(_s.outfits[i].name) ? "(no name)" : _s.outfits[i].name;
                _selected = Mathf.Clamp(_selected, 0, names.Length - 1);
                int picked = GUILayout.SelectionGrid(_selected, names, 3, GUILayout.Width(width));
                if (picked != _selected)
                {
                    _selected = picked;
                    _confirmDelete = false;
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button("New outfit from what I wear", GUILayout.Width(210)))
            {
                var made = new DressOutfit { name = "Outfit " + (_s.outfits.Count + 1) };
                AddWorn(made);
                _s.outfits.Add(made);
                _selected = _s.outfits.Count - 1;
                _confirmDelete = false;
                Save();
            }
            if (_s.outfits.Count == 0)
                return;

            DressOutfit o = _s.outfits[_selected];
            bool changed = false;
            AssistantGui.Header(string.IsNullOrWhiteSpace(o.name) ? "(no name)" : o.name);
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Name", 80);
            changed |= AssistantGui.Text(ref o.name, 220);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Undress to", 80);
            AssistantGui.Cell(BagLabel(o.undressBag), Mathf.Max(100f, width - 80f - 70f - 90f - 16f));
            if (GUILayout.Button("Target", GUILayout.Width(70)))
            {
                Host.Log("Target the bag this outfit is taken off into.");
                Host.Session.BeginLocalTarget(picked =>
                {
                    if (!IsContainer(picked))
                    {
                        Host.Log("That is not a container.");
                        return;
                    }
                    o.undressBag = picked;
                    Save();
                });
            }
            if (GUILayout.Button("Backpack", GUILayout.Width(90)) && o.undressBag != 0)
            {
                o.undressBag = 0;
                changed = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (ReferenceEquals(_running, o))
            {
                if (GUILayout.Button("Stop", GUILayout.Width(184)))
                    _running = null;
            }
            else
            {
                if (GUILayout.Button("Dress", GUILayout.Width(90)))
                    Run(o, true);
                if (GUILayout.Button("Undress", GUILayout.Width(90)))
                    Run(o, false);
            }
            // Deleting takes a second press: the outfit's list is gone with it.
            if (GUILayout.Button(_confirmDelete ? "Really delete?" : "Delete", GUILayout.Width(110)))
            {
                if (_confirmDelete)
                {
                    if (ReferenceEquals(_running, o))
                        _running = null;
                    _s.outfits.Remove(o);
                    _selected = Mathf.Max(0, _selected - 1);
                    Save();
                    _confirmDelete = false;
                    GUILayout.EndHorizontal();
                    return;
                }
                _confirmDelete = true;
            }
            GUILayout.EndHorizontal();

            AssistantGui.Header("Items");
            Mobile me = Host.Session.World.Player;
            float nameW = Mathf.Max(100f, width - 26f - 100f - 60f - 28f - 20f);
            int remove = -1;
            for (int i = 0; i < o.items.Count; i++)
            {
                DressItem piece = o.items[i];
                GUILayout.BeginHorizontal();
                AssistantGui.Icon(Host.Ui?.Art, piece.graphic, piece.hue, 24);
                AssistantGui.Cell(DressPlan.LayerName(piece.layer), 100);
                AssistantGui.Cell(piece.name, nameW);
                AssistantGui.Cell(Host.Session.World.Worn(me, (byte)piece.layer) == piece.serial ? "worn" : "", 60);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (o.items.Count == 0)
                AssistantGui.Note("This outfit is empty.");
            if (remove >= 0)
            {
                o.items.RemoveAt(remove);
                changed = true;
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add what I wear now", GUILayout.Width(160)))
            {
                AddWorn(o);
                changed = true;
            }
            if (GUILayout.Button("Add by targeting an item", GUILayout.Width(190)))
            {
                Host.Log("Target the item to add to the outfit.");
                Host.Session.BeginLocalTarget(picked =>
                {
                    if (AddTargeted(o, picked))
                        Save();
                });
            }
            if (GUILayout.Button("Clear", GUILayout.Width(70)) && o.items.Count > 0)
            {
                o.items.Clear();
                changed = true;
            }
            GUILayout.EndHorizontal();
            if (changed)
                Save();
        }

        /// <summary>Adds the clothing the character wears now; a piece replaces the outfit's earlier one on the same layer.</summary>
        private void AddWorn(DressOutfit o)
        {
            Mobile me = Host.Session.World.Player;
            if (me == null)
                return;
            foreach (DressItem piece in DressPlan.Capture(me.Equipment))
                Put(o, piece);
        }

        private void Put(DressOutfit o, DressItem piece)
        {
            piece.name = Host.ItemName(piece.serial, piece.graphic);
            // One item per layer: two could never be worn together.
            o.items.RemoveAll(i => i.layer == piece.layer || i.serial == piece.serial);
            o.items.Add(piece);
            o.items.Sort((a, b) => a.layer.CompareTo(b.layer));
        }

        /// <summary>Adds a pointed-at item: on the layer it is worn on, or else the layer its art says it belongs to.</summary>
        private bool AddTargeted(DressOutfit o, uint serial)
        {
            Mobile me = Host.Session.World.Player;
            if (me != null)
                foreach (EquippedItem e in me.Equipment.Values)
                    if (e.Serial == serial)
                    {
                        if (!DressPlan.IsClothingLayer(e.Layer))
                            break;
                        Put(o, new DressItem { serial = serial, layer = e.Layer, graphic = e.Graphic, hue = e.Hue });
                        return true;
                    }
            if (Host.Data != null
                && (Host.Session.World.Contained.ContainsKey(serial) || Host.Session.World.Items.ContainsKey(serial))
                && ItemRuleEditor.Describe(Host, serial, out int graphic, out int hue))
            {
                StaticTileData tile = Host.Data.TileData.GetStatic(graphic);
                if ((tile.Flags & TileFlags.Wearable) != 0 && DressPlan.IsClothingLayer(tile.Layer))
                {
                    Put(o, new DressItem { serial = serial, layer = tile.Layer, graphic = graphic, hue = hue });
                    return true;
                }
            }
            Host.Log("That cannot be worn.");
            return false;
        }

        private bool IsContainer(uint serial)
        {
            if (!ItemRuleEditor.Describe(Host, serial, out int graphic, out _))
                return false;
            // Without tiledata we cannot tell; the server refuses the drop if it is not one.
            return Host.Data == null || (Host.Data.TileData.GetStatic(graphic).Flags & TileFlags.Container) != 0;
        }

        private string BagLabel(uint serial)
        {
            if (serial == 0)
                return "Backpack";
            return ItemRuleEditor.Describe(Host, serial, out int graphic, out _)
                ? $"{Host.ItemName(serial, graphic)} (0x{serial:X8})"
                : $"0x{serial:X8} (not in sight)";
        }

        /// <summary>Self-tests: an outfit made from what is worn, and a run, without the window.</summary>
        public DressOutfit CaptureForTest()
        {
            var o = new DressOutfit();
            AddWorn(o);
            return o;
        }

        public bool RunningForTest => _running != null;
    }
}
