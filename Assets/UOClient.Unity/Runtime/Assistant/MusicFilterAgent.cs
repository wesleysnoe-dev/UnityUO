using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core.Assistant;

namespace UOClient.Unity.Assistant
{
    /// <summary>
    /// The music filter (TazUO's SoundFilterManager, its music list): a track on the list is not started when the server
    /// asks for it; whatever was playing carries on. The tab lists the tracks asked for lately.
    /// </summary>
    public sealed class MusicFilterAgent : AssistantAgent
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            public List<int> ids = new List<int>();
        }

        private const string File = "musicfilter";
        private const int MaxId = Audio.StopMusicId - 1, Remembered = 12;
        private Settings _s = new Settings();
        private readonly List<int> _recent = new List<int>();
        private readonly IdFilterEditor.State _editor = new IdFilterEditor.State();
        private Func<int, bool> _hook;
        private bool _testing;  // a track played from the tab plays even when filtered
        private int _asked = -1; // the track the server last asked for
        private Audio _audio;

        public override string Name => "Music Filter";
        public override string Group => "Filters";
        public override bool Active => _s.enabled && _s.ids.Count > 0;

        public override void Attach()
        {
            _s = Host.Load<Settings>(File);
            IdFilter.Tidy(_s.ids);
            _hook = Silenced;
            Audio.MusicFilter = _hook;
        }

        public override void Detach()
        {
            if (Audio.MusicFilter == _hook)
                Audio.MusicFilter = null;
        }

        /// <summary>Asked for every track the server wants played: notes it and says whether it is left unplayed.</summary>
        private bool Silenced(int id)
        {
            if (_testing)
                return false;
            _asked = id;
            IdFilter.Heard(_recent, id, Remembered);
            return Filtered(id);
        }

        private bool Filtered(int id) => _s.enabled && IdFilter.Contains(_s.ids, id);

        private Audio Sound => _audio != null ? _audio : _audio = UnityEngine.Object.FindFirstObjectByType<Audio>();

        private void Play(int id)
        {
            if (Sound == null || id < 0)
                return;
            _testing = true;
            try { Sound.PlayMusic(id); }
            finally { _testing = false; }
        }

        public override void DrawTab(float width)
        {
            AssistantGui.Header("Music Filter");
            AssistantGui.Note("Tracks on this list are not started when the server asks for them; the music that was playing goes on. "
                              + "Play starts one, filtered or not, until the server next changes the music.");
            bool changed = AssistantGui.Toggle(ref _s.enabled, "Skip the tracks listed below");
            changed |= IdFilterEditor.Draw(_editor, _s.ids, _recent, MaxId, "track", width, Play);
            if (!changed)
                return;
            Host.Save(File, _s);
            // The list changed under the region's own track: one just filtered stops now rather than at the next
            // region, and one just let through starts (the same track already playing is left alone by the player).
            if (_asked < 0 || Sound == null)
                return;
            if (Filtered(_asked))
                Sound.PlayMusic(Audio.StopMusicId);
            else
                Play(_asked);
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
