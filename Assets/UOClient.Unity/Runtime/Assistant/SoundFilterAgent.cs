using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The sound filter (TazUO's SoundFilterManager): sound effects on the list are not played. The tab also lists the
    /// sounds heard lately, so an annoying one can be picked out by ear rather than by number.
    /// </summary>
    public sealed class SoundFilterAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            public List<int> ids = new List<int>();
        }

        private const string File = "soundfilter";
        private const int MaxId = 0xFFFF, Remembered = 12;
        private Settings _s = new Settings();
        private readonly List<int> _recent = new List<int>();
        private readonly IdFilterEditor.State _editor = new IdFilterEditor.State();
        private Func<int, bool> _hook;
        private bool _testing; // a sound played from the tab is heard even when filtered
        private Audio _audio;

        public override string Name => "Sound Filter";
        public override string Group => "Filters";
        public override bool Active => _s.enabled && _s.ids.Count > 0;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            IdFilter.Tidy(_s.ids);
            _hook = Silenced;
            Audio.SoundFilter = _hook;
        }

        public override void Detach()
        {
            if (Audio.SoundFilter == _hook)
                Audio.SoundFilter = null;
        }

        /// <summary>Asked for every sound in earshot: notes it as heard and says whether it stays silent.</summary>
        private bool Silenced(int id)
        {
            if (_testing)
                return false;
            IdFilter.Heard(_recent, id, Remembered);
            return _s.enabled && IdFilter.Contains(_s.ids, id);
        }

        private void Play(int id)
        {
            if (_audio == null)
                _audio = UnityEngine.Object.FindFirstObjectByType<Audio>();
            if (_audio == null || id < 0)
                return;
            _testing = true;
            try { _audio.PlaySound(id, 0, 0); }
            finally { _testing = false; }
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Sound Filter");
            AssistantGui.Note("Sounds on this list are not played. Play lets you hear one, filtered or not.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Silence the sounds listed below");
            changed |= IdFilterEditor.Draw(_editor, _s.ids, _recent, MaxId, "sound", width, Play);
            if (changed)
                Host.Save(File, _s);
        }

        /// <summary>Self-tests: the list replaced and switched on, without the window.</summary>
        public void SetForTest(List<int> ids)
        {
            _s.enabled = true;
            _s.ids = ids;
            IdFilter.Tidy(_s.ids);
        }
    }
}
