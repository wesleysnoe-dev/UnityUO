using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>A character's profile (0xB8): the title line, the text they wrote, and the server's note under it.</summary>
    public sealed class ProfileWindow : UiWindow
    {
        private CharacterProfile _profile;
        private GUIStyle _wrap;
        private Vector2 _scroll;

        public ProfileWindow(CharacterProfile profile)
        {
            _profile = profile;
            Rect = new Rect(360, 80, 320, 380);
        }

        public override string Key => $"profile:{_profile.Serial}";

        /// <summary>A newer copy of the same profile (asked for again while open).</summary>
        public void SetProfile(CharacterProfile profile) => _profile = profile;

        public override void Draw()
        {
            DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            Text(new Rect(16, 12, Rect.width - 32, 22), string.IsNullOrEmpty(_profile.Header) ? "Profile" : _profile.Header,
                new Color(1f, 0.9f, 0.6f), 15, TextAnchor.UpperCenter, true);

            // Its own style: changing GUI.skin.label would make every label in the game wrap from now on.
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, normal = { textColor = new Color(0.95f, 0.92f, 0.84f) } };
            bool footer = !string.IsNullOrEmpty(_profile.Footer);
            var view = new Rect(16, 42, Rect.width - 32, Rect.height - (footer ? 86 : 58));
            string body = string.IsNullOrEmpty(_profile.Body) ? "(no profile)" : _profile.Body;
            float height = _wrap.CalcHeight(new GUIContent(body), view.width - 18);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, Mathf.Max(height, view.height)));
            GUI.Label(new Rect(0, 0, view.width - 18, height), body, _wrap);
            GUI.EndScrollView();

            if (footer)
                Text(new Rect(16, Rect.height - 38, Rect.width - 32, 20), _profile.Footer, new Color(0.75f, 0.72f, 0.62f), 12, TextAnchor.UpperCenter);
        }
    }
}
