using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UOClient.IO;

namespace UOClient.Unity
{
    /// <summary>
    /// Sound effects (PCM from the sound file, quieter with distance, a few voices at once) and region
    /// music (Music/Digital/*.mp3, chosen by Config.txt, faded between tracks).
    /// </summary>
    public sealed class Audio : MonoBehaviour
    {
        private const int Voices = 10;
        private const float HearingRange = 18f;

        private UOData _data;
        private readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
        private readonly List<AudioSource> _voices = new List<AudioSource>();
        private AudioSource _music, _musicOld;
        private Dictionary<int, (string File, bool Loop)> _tracks;
        private int _musicId = -1;

        public float SoundVolume { get; set; } = 0.8f;

        public int SoundsPlayed { get; private set; }

        /// <summary>The music track playing (file name), or null.</summary>
        public string MusicPlaying => _music != null && _music.isPlaying && _music.clip != null ? _musicName : null;

        private string _musicName;

        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Mathf.Clamp01(value);
                if (_music != null && _music.clip != null)
                    _music.volume = _musicVolume; // a fade in progress takes it from here
            }
        }

        private float _musicVolume = 0.45f;

        public void Init(UOData data)
        {
            _data = data;
            for (int i = 0; i < Voices; i++)
                _voices.Add(NewSource());
            _music = NewSource();
            _musicOld = NewSource();
            AudioConfiguration cfg = AudioSettings.GetConfiguration();
            Debug.Log($"UOClient audio: output {cfg.sampleRate} Hz, {cfg.speakerMode}, dsp {cfg.dspBufferSize}; listener volume {AudioListener.volume}, paused {AudioListener.pause}; " +
                      $"listeners {UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}; sound {SoundVolume}, music {_musicVolume}");
        }

        private int _soundLogs;

        private AudioSource NewSource()
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f; // 2D: the client pans and fades by tile distance itself
            return s;
        }

        /// <summary>
        /// The assistant's sound and music filters: asked with the id of each sound in earshot, and of each track the
        /// server asks for; true keeps it silent. Null unless a filter is in use.
        /// </summary>
        public static Func<int, bool> SoundFilter, MusicFilter;

        /// <summary>Plays a sound heard from <paramref name="dx"/>, <paramref name="dy"/> tiles away from the player.</summary>
        public void PlaySound(int id, int dx, int dy)
        {
            if (_data == null || SoundVolume <= 0)
                return;
            float distance = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            if (distance > HearingRange)
                return;
            if (SoundFilter != null && SoundFilter(id))
                return;
            AudioClip clip = Clip(id);
            if (_soundLogs < 5)
            {
                _soundLogs++;
                Debug.Log($"UOClient audio: sound {id} at {dx},{dy}: {(clip == null ? "no data" : $"{clip.length:0.00} s")}, volume {SoundVolume}, listener {AudioListener.volume}");
            }
            if (clip == null)
                return;
            SoundsPlayed++;
            AudioSource voice = _voices.Find(v => !v.isPlaying) ?? _voices[0];
            voice.clip = clip;
            voice.volume = SoundVolume * Mathf.Lerp(1f, 0.15f, distance / HearingRange);
            voice.panStereo = Mathf.Clamp((dx - dy) / 12f, -0.8f, 0.8f); // screen-right is +x, -y
            voice.Play();
        }

        private AudioClip Clip(int id)
        {
            if (_clips.TryGetValue(id, out AudioClip clip))
                return clip;
            short[] pcm = _data.GetSound(id);
            if (pcm != null && pcm.Length > 0)
            {
                var samples = new float[pcm.Length];
                for (int i = 0; i < pcm.Length; i++)
                    samples[i] = pcm[i] / 32768f;
                clip = AudioClip.Create($"sound {id}", samples.Length, 1, 22050, false);
                clip.SetData(samples, 0);
            }
            _clips[id] = clip;
            return clip;
        }

        /// <summary>Switches to a region's music (fading the old track out); the same id keeps playing.</summary>
        public void PlayMusic(int id)
        {
            // A filtered track is as if the server never asked for it: what was playing goes on (as TazUO's PlayMusic).
            if (id != StopMusicId && MusicFilter != null && MusicFilter(id))
                return;
            _regionMusic = id;
            if (!_combat)
                Switch(id);
        }

        // The music the server last asked for (the region's): what plays again when the combat music ends.
        private int _regionMusic = -1;
        private bool _combat;

        /// <summary>
        /// The combat music (TazUO's EnableCombatMusic): on, <paramref name="track"/> plays over the region's music; off,
        /// the region's comes back (or silence, if the server asked for none).
        /// </summary>
        public void SetCombatMusic(bool on, int track)
        {
            if (on == _combat)
                return;
            _combat = on;
            Switch(on ? track : _regionMusic >= 0 ? _regionMusic : StopMusicId);
        }

        /// <summary>
        /// A combat track at random: the tracks of Config.txt whose file is named for combat (GenericCombat, ...); this
        /// data has none at ClassicUO's ids 38-40, which are only the fallback.
        /// </summary>
        public int RandomCombatTrack()
        {
            if (_data == null)
                return GameOptions.FirstCombatMusic;
            _tracks ??= ReadTracks(Path.Combine(_data.Directory, "Music", "Digital", "Config.txt"));
            List<int> ids = GameOptions.CombatTracks(_tracks.Select(t => (t.Key, t.Value.File)));
            return ids[UnityEngine.Random.Range(0, ids.Count)];
        }

        private void Switch(int id)
        {
            if (_data == null || id == _musicId)
                return;
            _musicId = id;
            int request = ++_musicRequest;
            // 0x1FFF is ModernUO's "stop the music".
            if (id == StopMusicId)
            {
                StartCoroutine(Crossfade(request, null, null, false));
                return;
            }
            _tracks ??= ReadTracks(Path.Combine(_data.Directory, "Music", "Digital", "Config.txt"));
            if (!_tracks.TryGetValue(id, out var track))
            {
                Debug.Log($"UOClient audio: music {id}: not in Config.txt ({_tracks.Count} tracks)");
                return;
            }
            string path = LoginMediaRules.MusicFile(Path.Combine(_data.Directory, "Music", "Digital"), track.File);
            if (!File.Exists(path))
            {
                Debug.Log($"UOClient audio: music {id}: {path} missing");
                return;
            }
            Debug.Log($"UOClient audio: music {id} -> {Path.GetFileName(path)} (volume {_musicVolume})");
            StartCoroutine(LoadAndFade(request, path, track.Loop));
        }

        public const int StopMusicId = 0x1FFF;
        private int _musicRequest;

        // Each request runs to completion (so its web request is always disposed); a newer request makes it
        // drop what it loaded instead of playing it.
        private IEnumerator LoadAndFade(int request, string path, bool loop)
        {
            AudioClip clip;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"UOClient: music {Path.GetFileName(path)} failed: {req.error}");
                    yield break;
                }
                clip = DownloadHandlerAudioClip.GetContent(req);
            }
            Debug.Log($"UOClient audio: loaded {Path.GetFileName(path)}: {(clip == null ? "null" : $"{clip.length:0.0} s, {clip.loadState}")}");
            if (request != _musicRequest)
            {
                Destroy(clip); // overtaken by a newer track while loading
                yield break;
            }
            yield return Crossfade(request, clip, Path.GetFileNameWithoutExtension(path), loop);
        }

        /// <summary>Fades the playing track out and <paramref name="clip"/> in (null: just out), then frees the old clip.</summary>
        private IEnumerator Crossfade(int request, AudioClip clip, string name, bool loop)
        {
            (_music, _musicOld) = (_musicOld, _music);
            // The source taking the new track may still hold one from an interrupted fade: let it go first.
            if (_music.clip != null && _music.clip != clip && _music.clip != _musicOld.clip)
            {
                AudioClip stale = _music.clip;
                _music.Stop();
                _music.clip = null;
                Destroy(stale);
            }
            _musicName = name;
            _music.clip = clip;
            _music.loop = loop;
            _music.volume = 0;
            if (clip != null)
                _music.Play();
            float startOld = _musicOld.volume;
            for (float t = 0; t < 1.5f && request == _musicRequest; t += Time.unscaledDeltaTime)
            {
                float k = t / 1.5f;
                _music.volume = clip != null ? _musicVolume * k : 0;
                _musicOld.volume = startOld * (1 - k);
                yield return null;
            }
            if (request != _musicRequest)
                yield break; // a newer fade takes over from here
            _music.volume = clip != null ? _musicVolume : 0;
            AudioClip old = _musicOld.clip;
            _musicOld.Stop();
            _musicOld.clip = null;
            if (old != null && old != clip)
                Destroy(old);
        }

        /// <summary>Config.txt: "id name[,loop]" per line.</summary>
        private static Dictionary<int, (string File, bool Loop)> ReadTracks(string path) =>
            File.Exists(path) ? LoginMediaRules.ParseMusicConfig(File.ReadAllLines(path)) : new Dictionary<int, (string, bool)>();

        private void OnDestroy()
        {
            foreach (AudioClip c in _clips.Values)
                if (c != null)
                    Destroy(c);
        }
    }
}
