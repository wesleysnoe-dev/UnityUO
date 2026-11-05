using System;
using UnityEditor;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Editor
{
    /// <summary>Browse land, static and gump art straight from the UO install to check decoding by eye.</summary>
    public sealed class ArtBrowserWindow : EditorWindow
    {
        // v2: the default moved to the game's data setting; an older saved EA path is not reused.
        private const string PathPrefKey = "UOClient.UOPath.v2";
        private static string DefaultPath => UOClient.Unity.ClientSettingsStore.ReadSettingsFile().ResolvedDataPath;

        private enum ArtKind { Land, Static, Gump }

        private string _uoPath;
        private ArtKind _kind = ArtKind.Static;
        private int _id = 0x0FAF;
        private int _zoom = 2;
        private UOData _data;
        private Texture2D _texture;
        private string _status = "";

        [MenuItem("UOClient/Art Browser")]
        private static void Open()
        {
            GetWindow<ArtBrowserWindow>("UO Art Browser");
        }

        private void OnEnable()
        {
            _uoPath = EditorPrefs.GetString(PathPrefKey, DefaultPath);
        }

        private void OnDisable()
        {
            ClearTexture();
            _data?.Dispose();
            _data = null;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            _uoPath = EditorGUILayout.TextField("UO install", _uoPath);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString(PathPrefKey, _uoPath);
                _data?.Dispose();
                _data = null;
            }

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _kind = (ArtKind)EditorGUILayout.EnumPopup(_kind, GUILayout.Width(80));
                _id = EditorGUILayout.IntField(_id, GUILayout.Width(80));
                GUILayout.Label($"0x{_id:X4}", GUILayout.Width(60));
                if (GUILayout.Button("â—€", GUILayout.Width(28))) Step(-1, false);
                if (GUILayout.Button("â–¶", GUILayout.Width(28))) Step(1, false);
                if (GUILayout.Button("Next art â–¶â–¶", GUILayout.Width(90))) Step(1, true);
                _zoom = EditorGUILayout.IntSlider(_zoom, 1, 6);
            }
            if (EditorGUI.EndChangeCheck() || (_texture == null && string.IsNullOrEmpty(_status)))
                Load();

            EditorGUILayout.HelpBox(_status, MessageType.None);

            if (_texture != null)
            {
                Rect r = GUILayoutUtility.GetRect(_texture.width * _zoom, _texture.height * _zoom, GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f));
                GUI.DrawTexture(r, _texture, ScaleMode.ScaleToFit, true);
            }
        }

        /// <summary>Moves the id; with <paramref name="skipEmpty"/>, keeps going until an index that has art.</summary>
        private void Step(int dir, bool skipEmpty)
        {
            int max = _kind == ArtKind.Land ? UOData.StaticOffset : _kind == ArtKind.Static ? UOData.ArtCount - UOData.StaticOffset : UOData.GumpCount;
            for (int tries = 0; tries < max; tries++)
            {
                _id = (_id + dir + max) % max;
                if (!skipEmpty || HasArt())
                    break;
            }
            Load();
        }

        private bool HasArt()
        {
            if (!EnsureData())
                return false;
            return _kind == ArtKind.Gump
                ? _data.GumpFile.TryGetEntry(_id, out _)
                : _data.ArtFile.TryGetEntry(_kind == ArtKind.Land ? _id : UOData.StaticOffset + _id, out _);
        }

        private bool EnsureData()
        {
            if (_data != null)
                return true;
            try
            {
                _data = new UOData(_uoPath);
                UOClient.Unity.ClientSettingsStore.ApplyOverrides(_data, UOClient.Unity.ClientSettingsStore.ReadSettingsFile()); // the shard's custom art, as in the game
                return true;
            }
            catch (Exception e)
            {
                _status = e.Message;
                return false;
            }
        }

        private void Load()
        {
            ClearTexture();
            if (!EnsureData())
                return;

            try
            {
                Bitmap32 bmp = _kind == ArtKind.Land ? _data.GetLand(_id)
                    : _kind == ArtKind.Static ? _data.GetStatic(_id)
                    : _data.GetGump(_id);

                if (bmp == null)
                {
                    _status = $"{_kind} 0x{_id:X4}: empty";
                    return;
                }

                _texture = TextureUtil.ToTexture(bmp);
                string name = _kind == ArtKind.Static ? _data.TileData.GetStatic(_id).Name
                    : _kind == ArtKind.Land && _id < _data.TileData.Land.Length ? _data.TileData.Land[_id].Name
                    : "";
                _status = $"{_kind} 0x{_id:X4}  {bmp.Width}x{bmp.Height}  {name}";
            }
            catch (Exception e)
            {
                _status = $"{_kind} 0x{_id:X4}: {e.GetType().Name}: {e.Message}";
            }
        }

        private void ClearTexture()
        {
            if (_texture != null)
                DestroyImmediate(_texture);
            _texture = null;
        }
    }
}
