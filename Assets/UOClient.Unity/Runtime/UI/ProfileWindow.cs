using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// A character's profile (0xB8): the title line, the text they wrote, and the server's note under it, written on UO's
    /// expandable scroll as TazUO's ProfileGump (ExpandableScroll 0x0820).
    /// </summary>
    public sealed class ProfileWindow : UiWindow
    {
        private CharacterProfile _profile;
        private GUIStyle _wrap;
        private Vector2 _scroll;

        public ProfileWindow(CharacterProfile profile)
        {
            _profile = profile;
            Rect = new Rect(360, 80, ScrollArtWidth, 380);
        }

        public override string Key => $"profile:{_profile.Serial}";

        /// <summary>The window is UO's scroll: the opening unrolls exactly over it.</summary>
        public override bool IsScroll => true;

        /// <summary>A newer copy of the same profile (asked for again while open).</summary>
        public void SetProfile(CharacterProfile profile) => _profile = profile;

        public override void Draw()
        {
            if (!DrawScroll(Rect.height))
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);
            // The parchment runs from x 18 to 281 between the rolls (37 tall at the top, 34 at the foot).
            const float left = 34f, right = ScrollArtWidth - 34f;
            Text(new Rect(left, 42, right - left, 22), string.IsNullOrEmpty(_profile.Header) ? "Profile" : _profile.Header,
                Ink, 15, TextAnchor.UpperCenter, true);

            // Its own style: changing GUI.skin.label would make every label in the game wrap from now on.
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 13, normal = { textColor = Ink } };
            bool footer = !string.IsNullOrEmpty(_profile.Footer);
            var view = new Rect(left, 68, right - left, Rect.height - (footer ? 136 : 112));
            string body = string.IsNullOrEmpty(_profile.Body) ? "(no profile)" : _profile.Body;
            float height = _wrap.CalcHeight(new GUIContent(body), view.width - 8);
            // No scrollbars on the parchment: the wheel scrolls it.
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 8, Mathf.Max(height, view.height)),
                false, false, GUIStyle.none, GUIStyle.none);
            GUI.Label(new Rect(0, 0, view.width - 8, height), body, _wrap);
            GUI.EndScrollView();

            if (footer)
                Text(new Rect(left, Rect.height - 64, right - left, 20), _profile.Footer, InkFaded, 12, TextAnchor.UpperCenter);
        }
    }
}
