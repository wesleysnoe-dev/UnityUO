using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// TazUO's ColorPickerGump for a dye tub (0x95): the 0x0906 frame, 10 rows of 20 swatches at (34, 34), a slider of five
    /// graduations under them, the tub at (200, 78) in the chosen hue, Okay at (208, 138). Okay sends the hue once; a
    /// right-click, Esc or the window closing any other way cancels and sends nothing (ModernUO leaves the picker waiting).
    /// </summary>
    public sealed class HuePickerWindow : UiWindow
    {
        /// <summary>Our own window, in the game's look (IronSkin).</summary>
        public override bool Themed => true;

        public const int Background = 0x0906;
        public const int OkNormal = 0x0907, OkPressed = 0x0908, OkOver = 0x0909, OkX = 208, OkY = 138;
        public const int BoxX = 34, BoxY = 34, Cell = 8;
        public const int SliderX = 39, SliderY = 142, SliderWidth = 145;
        public const int TubGraphic = 0x0FAB, TubX = 200, TubY = 78, HueLabelX = 196, HueLabelY = 100;
        public const int FallbackWidth = 250, FallbackHeight = 170;

        private readonly HuePickerRequest _request;
        private int _graduation = GameSession.DyeDefaultGraduation, _selected;

        public HuePickerWindow(HuePickerRequest request)
        {
            _request = request;
            Rect = new Rect(Screen.width / 2f / UiScale.Current - FallbackWidth / 2f, Screen.height / 2f / UiScale.Current - FallbackHeight / 2f,
                            FallbackWidth, FallbackHeight);
        }

        public override string Key => KeyFor(_request.Serial);

        public static string KeyFor(uint serial) => "huepicker:" + serial;

        public uint Serial => _request.Serial;

        public ushort SelectedHue => GameSession.DyeHue(_graduation, _selected);

        public override void OnOpened()
        {
            if (Art.Gump(Background) is UiImage bg)
                Rect = new Rect(Rect.center.x - bg.Width / 2f, Rect.center.y - bg.Height / 2f, bg.Width, bg.Height);
        }

        // Closed without Okay: a cancel, which sends nothing.
        public override void OnClosed() => _request.Answered = true;

        public override void Draw()
        {
            UiImage bg = Art.Gump(Background);
            if (bg != null)
                DrawImage(bg, 0, 0);
            else
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);

            Event e = Event.current;
            var box = new Rect(BoxX, BoxY, GameSession.DyeColumns * Cell, GameSession.DyeRows * Cell);
            for (int row = 0; row < GameSession.DyeRows; row++)
            {
                for (int col = 0; col < GameSession.DyeColumns; col++)
                {
                    int i = row * GameSession.DyeColumns + col;
                    Color saved = GUI.color;
                    GUI.color = Art.HueColor(GameSession.DyeHue(_graduation, i), Color.gray);
                    GUI.DrawTexture(new Rect(box.x + col * Cell, box.y + row * Cell, Cell, Cell), Texture2D.whiteTexture);
                    GUI.color = saved;
                }
            }
            // ColorPickerBox's marker: a 2x2 dot in the middle of the chosen swatch.
            GUI.DrawTexture(new Rect(box.x + (_selected % GameSession.DyeColumns + 0.5f) * Cell - 1,
                                     box.y + (_selected / GameSession.DyeColumns + 0.5f) * Cell - 1, 2, 2), Texture2D.whiteTexture);
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && box.Contains(e.mousePosition) && MouseIsOver)
            {
                int col = Mathf.Clamp((int)((e.mousePosition.x - box.x) / Cell), 0, GameSession.DyeColumns - 1);
                int row = Mathf.Clamp((int)((e.mousePosition.y - box.y) / Cell), 0, GameSession.DyeRows - 1);
                _selected = row * GameSession.DyeColumns + col;
                e.Use();
            }

            _graduation = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(SliderX, SliderY, SliderWidth, 12), _graduation, 0, GameSession.DyeGraduations - 1));

            ushort hue = SelectedHue;
            DrawImage(Art.Item(TubGraphic, hue), TubX, TubY);
            Text(new Rect(HueLabelX, HueLabelY, 60, 16), $"0x{hue:X4}", Color.white, 12);

            if (GumpButton(OkNormal, OkPressed, OkX, OkY, OkOver) || (Art.Gump(OkNormal) == null && GUI.Button(new Rect(OkX, OkY, 36, 20), "OK")))
            {
                Session.AnswerHuePicker(_request, hue);
                Close();
            }
        }
    }
}
