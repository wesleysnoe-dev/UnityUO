using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

namespace UOClient.Unity
{
    /// <summary>
    /// The login screen's background video (StreamingAssets/Login, looping, no audio track used) and login music
    /// (the UO data's LoginLoop.mp3, looping on its own source at the options' music volume). See
    /// <see cref="LoginMediaRules"/>. Begin when the login screen shows, End on entering the world, on tearing the
    /// screen down and on quitting; both are safe to repeat. Without the video the login screen keeps its old backdrop.
    /// </summary>
    public sealed class LoginMedia : MonoBehaviour
    {
        private readonly LoginMediaState _state = new LoginMediaState();
        private GameObject _videoObject;
        private VideoPlayer _video;
        private RenderTexture _frame;
        private AudioSource _music;
        private int _musicRequest;
        private float _musicVolume = LoginMediaRules.Volume(GameOptions.Defaults().musicVolume);

        public LoginMediaState State => _state;

        /// <summary>The video's current frame once it is playing, else null (draw the fallback backdrop).</summary>
        public Texture VideoFrame => _video != null && _frame != null && _video.isPlaying ? _frame : null;

        /// <summary>The options' music volume; takes effect at once on the playing login music.</summary>
        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = LoginMediaRules.Volume(value);
                if (_music != null)
                    _music.volume = _musicVolume;
            }
        }

        /// <summary>Starts the video and music unless they already play. <paramref name="dataDirectory"/>: the UO data folder.</summary>
        public void Begin(string dataDirectory)
        {
            if (!_state.Start())
                return;
            if (_state.WantsVideo)
                StartVideo();
            if (_state.WantsMusic)
                StartMusic(dataDirectory);
        }

        /// <summary>Stops the video and music and frees the player, its frame texture and the music clip.</summary>
        public void End()
        {
            if (!_state.Stop())
                return;
            ReleaseVideo();
            ReleaseMusic();
        }

        // ---- video ----

        private void StartVideo()
        {
            string path = LoginMediaRules.VideoPath(Application.streamingAssetsPath);
            if (!Application.streamingAssetsPath.Contains("://") && !File.Exists(path))
            {
                FailVideo($"not found at {path}");
                return;
            }
            _videoObject = new GameObject("Login video");
            _videoObject.transform.SetParent(transform, false);
            _video = _videoObject.AddComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.source = VideoSource.Url;
            _video.url = LoginMediaRules.VideoUrl(Application.streamingAssetsPath);
            _video.isLooping = true; // the file's own baked crossfade hides the join; nothing is layered on top
            _video.audioOutputMode = VideoAudioOutputMode.None;
            _video.renderMode = VideoRenderMode.RenderTexture;
            _video.skipOnDrop = true;
            _video.waitForFirstFrame = true;
            _video.errorReceived += OnVideoError;
            _video.prepareCompleted += OnVideoPrepared;
            _video.Prepare();
        }

        private void OnVideoPrepared(VideoPlayer player)
        {
            if (player != _video)
                return;
            int w = (int)Math.Max(1u, player.width), h = (int)Math.Max(1u, player.height);
            _frame = new RenderTexture(w, h, 0) { name = "Login video frame" };
            _frame.Create();
            player.targetTexture = _frame;
            player.Play();
        }

        private void OnVideoError(VideoPlayer player, string message)
        {
            if (player != _video)
                return;
            FailVideo(message);
            ReleaseVideo();
        }

        private void FailVideo(string why)
        {
            if (_state.FailVideo())
                Debug.LogWarning($"UOClient: login video off ({why}); using the plain login backdrop.");
        }

        private void ReleaseVideo()
        {
            if (_video != null)
            {
                _video.errorReceived -= OnVideoError;
                _video.prepareCompleted -= OnVideoPrepared;
                _video.Stop();
                _video.targetTexture = null;
                _video = null;
            }
            if (_videoObject != null)
            {
                Destroy(_videoObject);
                _videoObject = null;
            }
            if (_frame != null)
            {
                _frame.Release();
                Destroy(_frame);
                _frame = null;
            }
        }

        // ---- music ----

        private void StartMusic(string dataDirectory)
        {
            string path = LoginMediaRules.LoginMusicPath(dataDirectory);
            if (path == null)
            {
                FailMusic($"Music/Digital/{LoginMediaRules.LoginMusicFallbackName}.mp3 (id {LoginMediaRules.LoginMusicId}) not found under {dataDirectory}");
                return;
            }
            if (_music == null)
            {
                _music = gameObject.AddComponent<AudioSource>();
                _music.playOnAwake = false;
                _music.spatialBlend = 0f;
            }
            // Music needs a listener; before the world the session has not added one yet.
            if (FindAnyObjectByType<AudioListener>() == null)
                (Camera.main != null ? Camera.main.gameObject : gameObject).AddComponent<AudioListener>();
            StartCoroutine(LoadMusic(++_musicRequest, path));
        }

        private IEnumerator LoadMusic(int request, string path)
        {
            AudioClip clip;
            using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG))
            {
                ((DownloadHandlerAudioClip)req.downloadHandler).streamAudio = true;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (request == _musicRequest)
                        FailMusic($"{Path.GetFileName(path)}: {req.error}");
                    yield break;
                }
                clip = DownloadHandlerAudioClip.GetContent(req);
            }
            // Ended (entered the world) or begun again while loading: this load is stale.
            if (request != _musicRequest || !_state.WantsMusic || _music == null)
            {
                Destroy(clip);
                yield break;
            }
            _music.clip = clip;
            _music.loop = true;
            _music.volume = _musicVolume;
            _music.Play();
        }

        private void FailMusic(string why)
        {
            if (_state.FailMusic())
                Debug.LogWarning($"UOClient: login music off ({why}).");
        }

        private void ReleaseMusic()
        {
            _musicRequest++; // a load still running drops its clip
            if (_music == null)
                return;
            AudioClip clip = _music.clip;
            _music.Stop();
            _music.clip = null;
            if (clip != null)
                Destroy(clip);
        }

        private void OnDestroy() => End();
    }
}
