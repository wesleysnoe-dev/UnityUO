using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The journal filter (TazUO's JournalFilterManager): a line that contains one of the listed texts (any case) is
    /// dropped as it arrives, the way the options' ignore list drops what a named character says: it reaches neither
    /// the journal, the chat log nor the text over heads.
    /// </summary>
    public sealed class JournalFilterAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            public List<string> filters = new List<string>();
        }

        private const string File = "journalfilter";
        private Settings _s = new Settings();
        private string _typed = "";
        private World _world;

        // What the session's packet handling reads: replaced whole when the list changes, never edited in place, so a
        // line arriving while the tab is being edited sees one list or the other.
        private string[] _live = Array.Empty<string>();
        private Func<JournalEntry, bool> _hook;

        public override string Name => "Journal Filter";
        public override string Group => "Filters";
        public override bool Active => _s.enabled && _s.filters.Count > 0;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            _s.filters.RemoveAll(string.IsNullOrWhiteSpace);
            _world = Host.Session.World;
            _hook = e => TextFilter.Hides(_live, e.Text);
            Apply();
        }

        public override void Detach()
        {
            if (_world != null && _world.JournalFilter == _hook)
                _world.JournalFilter = null;
            _world = null;
        }

        /// <summary>The list as it now is goes live; with nothing to filter the session is left without a filter at all.</summary>
        private void Apply()
        {
            _live = _s.filters.ToArray();
            if (_world == null)
                return;
            if (Active)
                _world.JournalFilter = _hook;
            else if (_world.JournalFilter == _hook)
                _world.JournalFilter = null;
        }

        private void Changed()
        {
            Apply();
            Host.Save(File, _s);
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Journal Filter");
            AssistantGui.Note("A message that contains one of these texts is not shown: not in the journal, not in the chat log, "
                              + "not over anyone's head. Capitals do not matter. It applies to messages that arrive from now on.");
            if (AssistantGui.Toggle(ref _s.enabled, "Hide the messages listed below"))
                Changed();

            AssistantGui.Header("Hidden texts");
            if (_s.filters.Count == 0)
                AssistantGui.Note("Nothing is filtered.");
            float textW = Mathf.Max(120f, width - 40f);
            int remove = -1;
            for (int i = 0; i < _s.filters.Count; i++)
            {
                GUILayout.BeginHorizontal();
                AssistantGui.Cell(_s.filters[i], textW);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                    remove = i;
                GUILayout.EndHorizontal();
            }
            if (remove >= 0)
            {
                _s.filters.RemoveAt(remove);
                Changed();
            }

            AssistantGui.Header("Add a text");
            GUILayout.BeginHorizontal();
            AssistantGui.Text(ref _typed, Mathf.Max(120f, width - 80f), TextFilter.MaxLength);
            if (GUILayout.Button("Add", GUILayout.Width(60)) && TextFilter.Add(_s.filters, _typed))
            {
                _typed = "";
                Changed();
            }
            GUILayout.EndHorizontal();
            AssistantGui.Note("Part of a message is enough: \"You must wait\" hides every line with those words in it.");
        }

        /// <summary>Self-tests: the list replaced and switched on, without the window.</summary>
        public void SetForTest(List<string> filters)
        {
            _s.enabled = true;
            _s.filters = filters;
            Apply();
        }
    }
}
