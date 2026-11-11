using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;
using UOClient.IO.Formats;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// Organizer (TazUO's OrganizerAgent): named organizers, each with a container to take from, a container to put
    /// into and a list of items. "Run" moves every listed item from the one to the other, one at a time at the
    /// server's action pace; an item's amount (the list's Max column) stops it once the destination holds that many.
    /// Containers are remembered by serial, so the organizers are kept per character.
    /// </summary>
    public sealed class OrganizerAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public List<Organizer> organizers = new List<Organizer>();
        }

        private Settings _s = new Settings();
        private string _file; // null until we know which character this is
        private int _selected;
        private bool _confirmDelete;

        // The run under way.
        private Organizer _running;
        private uint _from, _to;
        private List<(uint Serial, int Amount)> _moves;
        private float _planAt, _nextAt;

        public override string Name => "Organizer";
        public override bool Active => _running != null;

        public override void Detach() => _running = null;

        /// <summary>Loads this character's organizers the first time the character is known.</summary>
        private bool EnsureLoaded()
        {
            if (_file != null)
                return true;
            uint me = Host.Session?.World.PlayerSerial ?? 0;
            if (me == 0)
                return false;
            _file = "organizer_" + me.ToString("X8");
            _s = Host.Load<Settings>(_file);
            _s.organizers.RemoveAll(o => o == null);
            return true;
        }

        private void Save()
        {
            if (_file != null)
                Host.Save(_file, _s);
        }

        private bool AnyWords(Organizer o) => o.rules.Exists(r => r != null && r.enabled && r.NeedsText);

        /// <summary>Starts an organizer; the moves themselves are made in <see cref="Tick"/>.</summary>
        public void Run(Organizer o)
        {
            uint pack = Host.Backpack;
            _from = o.source != 0 ? o.source : pack;
            _to = o.destination != 0 ? o.destination : pack;
            if (_from == 0 || _to == 0)
            {
                Host.Log("Organizer: cannot find your backpack.");
                return;
            }
            if (_from == _to)
            {
                Host.Log($"Organizer '{o.name}': it takes from and puts into the same container.");
                return;
            }
            _running = o;
            _moves = null;
            _planAt = 0f;
            // Rules that match on words need the items' tooltips: ask for the ones we lack and give them a moment to arrive.
            if (AnyWords(o))
            {
                foreach (uint container in new[] { _from, _to })
                    foreach (ContainedItem item in Host.ItemsIn(container))
                    {
                        Host.ItemText(item.Serial, item.Graphic);
                        if (!Host.Session.Properties.ContainsKey(item.Serial))
                            _planAt = Time.unscaledTime + 1f;
                    }
            }
        }

        public override void Tick(float now)
        {
            if (_running == null || now < _planAt)
                return;
            GameSession s = Host.Session;
            if (_moves == null)
            {
                Func<ContainedItem, string> text = null;
                if (AnyWords(_running))
                    text = i => Host.ItemText(i.Serial, i.Graphic);
                _moves = OrganizerPlan.Build(Host.ItemsIn(_from), Host.ItemsIn(_to), _running.rules, text);
                if (_moves.Count == 0)
                {
                    bool seen = false;
                    foreach (ContainedItem _ in Host.ItemsIn(_from))
                    {
                        seen = true;
                        break;
                    }
                    Host.Log(seen
                        ? $"No items were organized by '{_running.name}'."
                        : $"Organizer '{_running.name}': the container to take from is empty or has not been opened yet.");
                    _running = null;
                    return;
                }
                Host.Log($"Organizing {_moves.Count} items with '{_running.name}'...");
            }
            if (now < _nextAt || s.QueuedActions > 0 || s.Holding != 0)
                return;
            if (_moves.Count == 0)
            {
                Host.Log($"Organizer '{_running.name}' is done.");
                _running = null;
                return;
            }
            (uint serial, int amount) = _moves[0];
            _moves.RemoveAt(0);
            // Still where the plan saw it? (Moved by hand meanwhile, or used up.)
            if (!s.World.Contained.TryGetValue(serial, out ContainedItem item) || item.Container != _from)
                return;
            s.MoveItem(serial, Math.Min(amount, Math.Max(1, item.Amount)), _to);
            _nextAt = now + 0.1f; // the action queue itself spaces the lifts
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Organizer");
            if (!EnsureLoaded())
            {
                AssistantGui.Note("Organizers are kept per character: they show once you are in the world.");
                return;
            }
            AssistantGui.Note("Each organizer moves its listed items from one container to another when you press Run. "
                              + "The container to take from must have been opened so its contents are known.");

            if (_s.organizers.Count > 0)
            {
                var names = new string[_s.organizers.Count];
                for (int i = 0; i < names.Length; i++)
                    names[i] = string.IsNullOrWhiteSpace(_s.organizers[i].name) ? "(no name)" : _s.organizers[i].name;
                _selected = Mathf.Clamp(_selected, 0, names.Length - 1);
                int picked = GUILayout.SelectionGrid(_selected, names, 3, GUILayout.Width(width));
                if (picked != _selected)
                {
                    _selected = picked;
                    _confirmDelete = false;
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button("New organizer", GUILayout.Width(130)))
            {
                _s.organizers.Add(new Organizer { name = "Organizer " + (_s.organizers.Count + 1) });
                _selected = _s.organizers.Count - 1;
                _confirmDelete = false;
                Save();
            }
            if (_s.organizers.Count == 0)
                return;

            Organizer o = _s.organizers[_selected];
            bool changed = false;
            AssistantGui.Header(string.IsNullOrWhiteSpace(o.name) ? "(no name)" : o.name);
            GUILayout.BeginHorizontal();
            AssistantGui.Cell("Name", 80);
            changed |= AssistantGui.Text(ref o.name, 220);
            GUILayout.EndHorizontal();
            ContainerRow("Take from", o, true, width);
            ContainerRow("Put into", o, false, width);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (ReferenceEquals(_running, o))
            {
                if (GUILayout.Button("Stop", GUILayout.Width(90)))
                    _running = null;
            }
            else if (GUILayout.Button("Run", GUILayout.Width(90)))
                Run(o);
            if (GUILayout.Button("Duplicate", GUILayout.Width(90)))
            {
                var copy = new Organizer { name = o.name + " copy", source = o.source, destination = o.destination };
                foreach (ItemRule r in o.rules)
                    copy.rules.Add(new ItemRule { name = r.name, graphic = r.graphic, hue = r.hue, text = r.text, enabled = r.enabled, max = r.max });
                _s.organizers.Add(copy);
                _selected = _s.organizers.Count - 1;
                changed = true;
            }
            // Deleting takes a second press: the list of items is gone with it.
            if (GUILayout.Button(_confirmDelete ? "Really delete?" : "Delete", GUILayout.Width(110)))
            {
                if (_confirmDelete)
                {
                    if (ReferenceEquals(_running, o))
                        _running = null;
                    _s.organizers.Remove(o);
                    _selected = Mathf.Max(0, _selected - 1);
                    Save();
                    _confirmDelete = false;
                    GUILayout.EndHorizontal();
                    return;
                }
                _confirmDelete = true;
            }
            GUILayout.EndHorizontal();

            AssistantGui.Header("Items to move");
            AssistantGui.Note("Hue blank = any colour. Words: part of the item's name or tooltip. "
                              + "Max: blank moves all of them; a number moves them only until the destination holds that many.");
            ItemRuleEditor.Added = Save;
            changed |= ItemRuleEditor.Draw(Host, o.rules, width);
            if (changed)
                Save();
        }

        /// <summary>One of an organizer's two containers: what it is now, and buttons to point at another or go back to the backpack.</summary>
        private void ContainerRow(string label, Organizer o, bool source, float width)
        {
            uint serial = source ? o.source : o.destination;
            GUILayout.BeginHorizontal();
            AssistantGui.Cell(label, 80);
            AssistantGui.Cell(ContainerLabel(serial), Mathf.Max(100f, width - 80f - 70f - 90f - 16f));
            if (GUILayout.Button("Target", GUILayout.Width(70)))
            {
                Host.Log(source ? "Target the container to take from." : "Target the container to put into.");
                Host.Session.BeginLocalTarget(picked =>
                {
                    if (!IsContainer(picked))
                    {
                        Host.Log("That is not a container.");
                        return;
                    }
                    if (source)
                        o.source = picked;
                    else
                        o.destination = picked;
                    Save();
                });
            }
            if (GUILayout.Button("Backpack", GUILayout.Width(90)) && serial != 0)
            {
                if (source)
                    o.source = 0;
                else
                    o.destination = 0;
                Save();
            }
            GUILayout.EndHorizontal();
        }

        private bool IsContainer(uint serial)
        {
            if (!ItemRuleEditor.Describe(Host, serial, out int graphic, out _))
                return false;
            // Without tiledata we cannot tell; the server refuses the drop if it is not one.
            return Host.Data == null || (Host.Data.TileData.GetStatic(graphic).Flags & TileFlags.Container) != 0;
        }

        private string ContainerLabel(uint serial)
        {
            if (serial == 0)
                return "Backpack";
            return ItemRuleEditor.Describe(Host, serial, out int graphic, out _)
                ? $"{Host.ItemName(serial, graphic)} (0x{serial:X8})"
                : $"0x{serial:X8} (not in sight)";
        }

        /// <summary>Self-tests: an organizer run without the window.</summary>
        public void RunForTest(Organizer o) => Run(o);

        public bool RunningForTest => _running != null;
    }
}
