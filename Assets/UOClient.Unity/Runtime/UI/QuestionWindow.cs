using System;
using UnityEngine;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// TazUO's QuestionGump: a yes / no question over the whole screen, which it dims, so nothing in the world or another
    /// window under it takes a click until it is answered (IsModal; it cannot be moved). Okay answers true; Cancel, a
    /// right-click, Esc or the window closing any other way answer false. The answer is given exactly once.
    /// </summary>
    public sealed partial class QuestionWindow : UiWindow
    {
        private readonly string _key, _text;
        private Action<bool> _result;
        private readonly CachedLabel _label = new CachedLabel();

        /// <summary><paramref name="key"/> names the question (TazUO's QuestionType): "attack" and "target" for the criminal queries.</summary>
        public QuestionWindow(string key, string text, Action<bool> result)
        {
            _key = key;
            _text = text;
            _result = result;
        }

        public override string Key => KeyFor(_key);

        public static string KeyFor(string key) => "question:" + key;

        public override bool Movable => false;

        public override bool StepsAsideOnOpen => false;

        public override void OnOpened() => FitScreen();

        public override void OnClosed()
        {
            _label.Release();
            Answer(false);
        }

        private void FitScreen() => Rect = new Rect(0, 0, Screen.width / Ui.Scale, Screen.height / Ui.Scale);

        private void Answer(bool yes)
        {
            Action<bool> result = _result;
            _result = null;
            result?.Invoke(yes);
        }

        public override void Draw()
        {
            if (Mathf.Abs(Rect.width - Screen.width / Ui.Scale) > 0.5f || Mathf.Abs(Rect.height - Screen.height / Ui.Scale) > 0.5f)
                FitScreen();

            Color saved = GUI.color;
            GUI.color = new Color(0, 0, 0, DimAlpha);
            GUI.DrawTexture(new Rect(0, 0, Rect.width, Rect.height), Texture2D.whiteTexture);
            GUI.color = saved;

            UiImage bg = Art.Gump(Background);
            (int x, int y) = Centered((int)Rect.width, (int)Rect.height, bg?.Width ?? FallbackWidth, bg?.Height ?? FallbackHeight);
            DrawImage(bg, x, y);
            UiImage text = _label.Get(Art, _text, LabelHue);
            if (text != null)
            {
                GUI.BeginGroup(new Rect(x + LabelX, y + LabelY, LabelWidth, text.Height));
                DrawImage(text, 0, 0);
                GUI.EndGroup();
            }

            if (GumpButton(CancelNormal, CancelPressed, x + CancelX, y + ButtonY, CancelOver))
            {
                Close();
                return;
            }
            if (GumpButton(OkNormal, OkPressed, x + OkX, y + ButtonY, OkOver))
            {
                Answer(true);
                Close();
            }
        }
    }
}
