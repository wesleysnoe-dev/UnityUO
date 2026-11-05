using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity.UI
{
    /// <summary>
    /// ClassicUO's SplitMenuGump: shift-dragging a stack asks how many to lift (all of it to start with). A slider and a
    /// number box choose it; OK or Enter lifts that many (0x07) onto the cursor, Escape or a right-click lifts nothing.
    /// </summary>
    public sealed class SplitStackWindow : UiWindow
    {
        private readonly DraggedItem _item;
        private int _amount;
        private string _text;
        private bool _focused;

        public SplitStackWindow(DraggedItem item, Vector2 at)
        {
            _item = item;
            _amount = item.Amount;
            _text = _amount.ToString();
            Rect = new Rect(at.x - 80, at.y - 40, 160, 70); // under the mouse, as ClassicUO places it
        }

        public override string Key => "split";

        // The stack went away (picked up by someone else, the container closed): nothing left to split.
        public override bool StillValid =>
            _item.FromGround ? Session.World.Items.ContainsKey(_item.Serial) : Session.World.Contained.ContainsKey(_item.Serial);

        public override void OnOpened()
        {
            UiImage bg = Art.Gump(0x085C);
            if (bg != null)
                Rect.size = new Vector2(bg.Width, bg.Height);
        }

        public override void Draw()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                Finish(false);
                return;
            }
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            UiImage bg = Art.Gump(0x085C);
            if (bg != null)
                DrawImage(bg, 0, 0);
            else
                DrawResizePic(9200, 0, 0, Rect.width, Rect.height);

            int slid = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(29, 16, 105, 14), _amount, 1, _item.Amount));
            if (slid != _amount)
            {
                _amount = slid;
                _text = _amount.ToString();
            }
            GUI.SetNextControlName("split-amount");
            string typed = GUI.TextField(new Rect(29, 42, 60, 20), _text, 5);
            if (typed != _text)
            {
                _amount = GameSession.ParseSplitAmount(typed, _item.Amount);
                _text = typed.Length == 0 ? "" : _amount.ToString();
            }
            if (!_focused)
            {
                GUI.FocusControl("split-amount");
                _focused = true;
            }

            bool ok = Art.Gump(0x085D) != null ? GumpButton(0x085D, 0x085E, 102, 37) : GUI.Button(new Rect(102, 37, 40, 24), "OK");
            if (ok || enter)
            {
                if (enter)
                    e.Use();
                Finish(true);
            }
        }

        /// <summary>Lifts the chosen amount (none for a cancel), closes, and gives the keyboard back to the world.</summary>
        private void Finish(bool lift)
        {
            GUIUtility.keyboardControl = 0;
            Close();
            if (lift && _amount > 0)
            {
                _item.Amount = GameSession.SplitAmount(_amount, _item.Amount);
                Ui.BeginDrag(_item);
            }
        }
    }
}
